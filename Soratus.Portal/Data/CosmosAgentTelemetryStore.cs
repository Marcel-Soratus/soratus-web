using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Soratus.Agents.Contracts;
using Soratus.Portal.Security;

// Zowel het contract als Microsoft.Extensions.Logging kent een LogLevel; ILogger zit hier in de
// constructor, dus het alias is niet optioneel. In de datalaag bedoelen we altijd het niveau uit
// het contract: drie waarden, zoals ze in Cosmos staan.

namespace Soratus.Portal.Data;

/// <summary>
/// De enige implementatie van <see cref="IAgentTelemetryStore"/>: leest de drie containers in de
/// Cosmos-opslag van elke klant.
/// </summary>
/// <remarks>
/// <para><strong>Waar wordt gelezen.</strong> Nooit uit configuratie en nooit uit een parameter,
/// altijd uit <see cref="CustomerScope.Telemetry"/>. Elke klant krijgt zijn eigen account, en de
/// scope draagt de endpoint; er is dus geen pad waarlangs een leesactie in de verkeerde opslag
/// terechtkomt.</para>
///
/// <para><strong>Tijdstempels.</strong> Elk moment dat als queryparameter meegaat loopt door
/// <see cref="CosmosMoment"/>. Cosmos vergelijkt tijdvelden als string, dus de parameter moet
/// letterlijk dezelfde vorm hebben als wat er in het document staat. Zie de opmerkingen bij die
/// methode: dit is gemeten tegen de echte opslag, niet beredeneerd.</para>
///
/// <para><strong>Kosten.</strong> De laatste afgeronde run wordt per agent apart opgevraagd. Dat
/// zijn evenveel query's als agents. Bewust: één gezamenlijke query met een tijdvenster zou
/// goedkoper zijn, maar dan mist hij de agent wiens laatste run buiten dat venster viel — en juist
/// die agent staat dan ten onrechte op "live" terwijl zijn laatste run mislukte. Correct gaat hier
/// voor goedkoop; de query's lopen parallel en het gaat om tientallen agents, niet duizenden.</para>
/// </remarks>
internal sealed class CosmosAgentTelemetryStore(
    CosmosContainerProvider containers,
    IOptions<PortalTelemetryOptions> options,
    TimeProvider timeProvider,
    ILogger<CosmosAgentTelemetryStore> logger) : IAgentTelemetryStore
{
    /// <summary>Hoeveel "laatste run"-query's binnen één klant tegelijk lopen.</summary>
    private const int MaxParallelRunLookups = 8;

    /// <summary>
    /// Over hoeveel dagpartities de live tail maximaal mag zoeken voordat hij de beperking
    /// loslaat. Een tabblad dat een week open stond hoort geen regels te missen.
    /// </summary>
    private const int MaxTailPartitionDays = 8;

    private readonly PortalTelemetryOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentSnapshot>> GetAgentsAsync(
        CustomerScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var agents = await containers.AgentsAsync(scope.Telemetry, cancellationToken).ConfigureAwait(false);

        var query = new QueryDefinition("SELECT * FROM c WHERE c.customerId = @customerId")
            .WithParameter("@customerId", scope.CustomerId);

        var registrations = await ReadAllAsync<AgentRegistration>(agents, query, cancellationToken)
            .ConfigureAwait(false);

        return await WithLastCompletedRunsAsync(scope, registrations, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AgentSnapshot?> GetAgentAsync(
        CustomerScope scope,
        string agentName,
        CancellationToken cancellationToken = default)
    {
        var registration = await GetRegistrationAsync(scope, agentName, cancellationToken)
            .ConfigureAwait(false);

        if (registration is null)
        {
            return null;
        }

        var lastRun = await LastCompletedRunAsync(scope, registration.AgentName, cancellationToken)
            .ConfigureAwait(false);

        return new AgentSnapshot(registration, lastRun);
    }

    /// <inheritdoc />
    public async Task<AgentRegistration?> GetRegistrationAsync(
        CustomerScope scope,
        string agentName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (string.IsNullOrWhiteSpace(agentName))
        {
            return null;
        }

        var agents = await containers.AgentsAsync(scope.Telemetry, cancellationToken).ConfigureAwait(false);

        AgentRegistration registration;
        try
        {
            // Id en partitiesleutel zijn beide gelijk aan de agentnaam, dus dit is een point read:
            // de goedkoopste leesactie die Cosmos kent.
            var response = await agents
                .ReadItemAsync<AgentRegistration>(
                    agentName,
                    new PartitionKey(agentName),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            registration = response.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        // De scope bewijst dat deze gebruiker déze klant mag lezen. Hij bewijst niet dat de agent
        // van die klant is, en de agentnaam komt uit de URL. Dus alsnog vergelijken — en bij een
        // agent van een andere klant hetzelfde antwoord geven als bij een agent die niet bestaat.
        if (!string.Equals(registration.CustomerId, scope.CustomerId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return registration;
    }

    /// <inheritdoc />
    public async Task<RunPage> GetRunsAsync(
        CustomerScope scope,
        string agentName,
        int? pageSize = null,
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (string.IsNullOrWhiteSpace(agentName))
        {
            return RunPage.Empty;
        }

        var runs = await containers.RunsAsync(scope.Telemetry, cancellationToken).ConfigureAwait(false);

        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.customerId = @customerId AND c.agentName = @agentName " +
                "ORDER BY c.startedAt DESC")
            .WithParameter("@customerId", scope.CustomerId)
            .WithParameter("@agentName", agentName);

        using var iterator = runs.GetItemQueryIterator<RunRecord>(
            query,
            continuationToken,
            new QueryRequestOptions { MaxItemCount = PageSize(pageSize) });

        if (!iterator.HasMoreResults)
        {
            return RunPage.Empty;
        }

        var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
        return new RunPage([.. response], response.ContinuationToken);
    }

    /// <inheritdoc />
    public async Task<LogPage> GetLogsAsync(
        CustomerScope scope,
        string agentName,
        LogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(agentName))
        {
            return LogPage.Empty;
        }

        var logs = await containers.LogsAsync(scope.Telemetry, cancellationToken).ConfigureAwait(false);

        var filter = BuildLogFilter(
            scope,
            agentName,
            query.Levels,
            query.Search,
            query.RunId,
            query.AsOf);

        var definition = Compose($"SELECT * FROM c WHERE {filter.Where} ORDER BY c.ts DESC", filter);

        using var iterator = logs.GetItemQueryIterator<LogRecord>(
            definition,
            query.ContinuationToken,
            new QueryRequestOptions { MaxItemCount = PageSize(query.PageSize) });

        if (!iterator.HasMoreResults)
        {
            return LogPage.Empty;
        }

        var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
        var lines = response.ToArray();

        // De query sorteert aflopend, dus de eerste regel is de nieuwste. Die is de cursor waar de
        // live tail de volgende keer op verder gaat.
        var newest = lines.Length == 0
            ? (LogCursor?)null
            : new LogCursor(lines[0].Timestamp, lines[0].Id);

        return new LogPage(lines, response.ContinuationToken, newest);
    }

    /// <inheritdoc />
    public async Task<LogLevelTally> CountLogLevelsAsync(
        CustomerScope scope,
        string agentName,
        LogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(agentName))
        {
            return LogLevelTally.Empty;
        }

        var logs = await containers.LogsAsync(scope.Telemetry, cancellationToken).ConfigureAwait(false);

        // Levels gaat hier niet mee. Zie IAgentTelemetryStore.CountLogLevelsAsync: een telling per
        // niveau die zelf op niveau filtert telt alleen zichzelf.
        var filter = BuildLogFilter(
            scope,
            agentName,
            levels: null,
            query.Search,
            query.RunId,
            query.AsOf);

        var definition = Compose(
            $"SELECT c.level AS logLevel, COUNT(1) AS lineCount FROM c WHERE {filter.Where} " +
            "GROUP BY c.level",
            filter);

        var buckets = await ReadAllAsync<LevelBucket>(logs, definition, cancellationToken)
            .ConfigureAwait(false);

        var tally = LogLevelTally.Empty;

        foreach (var bucket in buckets)
        {
            tally = tally.Add(bucket.Level, bucket.Count);
        }

        return tally;
    }

    /// <inheritdoc />
    public async Task<LogTail> TailLogsAsync(
        CustomerScope scope,
        string agentName,
        LogTailQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(agentName))
        {
            return LogTail.Nothing(query.Since);
        }

        var logs = await containers.LogsAsync(scope.Telemetry, cancellationToken).ConfigureAwait(false);

        // Geen AsOf: de tail wil juist alles wat er ná de cursor is bijgekomen, ook een regel met
        // een tijdstempel die door klokverschil een fractie in de toekomst ligt.
        var filter = BuildLogFilter(
            scope,
            agentName,
            query.Levels,
            query.Search,
            query.RunId,
            asOf: null);

        var sql = new StringBuilder("SELECT * FROM c WHERE ")
            .Append(filter.Where)
            .Append(" AND (c.ts > @since OR (c.ts = @since AND c.id > @sinceId))");

        var parameters = new List<(string Name, object Value)>(filter.Parameters)
        {
            ("@since", CosmosMoment(query.Since.Timestamp)),
            ("@sinceId", query.Since.Id),
        };

        if (TailPartitionKeys(agentName, query.Since.Timestamp) is { Count: > 0 } partitionKeys)
        {
            var names = new List<string>(partitionKeys.Count);

            for (var index = 0; index < partitionKeys.Count; index++)
            {
                var name = $"@pk{index}";
                names.Add(name);
                parameters.Add((name, partitionKeys[index]));
            }

            sql.Append(CultureInfo.InvariantCulture, $" AND c.pk IN ({string.Join(", ", names)})");
        }

        // Oplopend, en alleen op de tijd. "ORDER BY c.ts ASC, c.id ASC" zou de volgorde binnen
        // dezelfde tijdstempel ook uit de query laten komen, maar dat vraagt een composite index op
        // (ts, id) en die staat niet op de container: gemeten geeft Cosmos er een 400 op. Dus
        // sorteren we hieronder in het geheugen na, en dat kán ook — de pagina is begrensd.
        sql.Append(" ORDER BY c.ts ASC");

        var cap = PageSize(query.MaxLines);

        // Eén regel meer opvragen dan we uitleveren. Die extra regel wordt niet getoond; hij is er
        // om twee dingen te weten. Eén: of er nog meer klaarstaat. Twee: of de grens midden in een
        // groep regels met dezelfde tijdstempel valt. Zonder die ene regel zou de tail elke tik de
        // jongste groep moeten laten liggen — ook als die compleet was — en dus altijd één regel
        // minder leveren dan gevraagd.
        using var iterator = logs.GetItemQueryIterator<LogRecord>(
            Compose(sql.ToString(), parameters),
            requestOptions: new QueryRequestOptions { MaxItemCount = cap + 1 });

        if (!iterator.HasMoreResults)
        {
            return LogTail.Nothing(query.Since);
        }

        var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);

        // Nasorteren in het geheugen, op tijd én ULID. De query kan alleen op tijd sorteren (zie
        // hierboven), en binnen dezelfde tijdstempel is de volgorde uit Cosmos niet vastgelegd.
        // Zonder deze sortering zou de cursor op een willekeurige regel uit die groep landen.
        var lines = response
            .OrderBy(line => line.Timestamp)
            .ThenBy(line => line.Id, StringComparer.Ordinal)
            .ToList();

        if (lines.Count == 0)
        {
            return LogTail.Nothing(query.Since);
        }

        var hasMore = lines.Count > cap || iterator.HasMoreResults;

        if (lines.Count > cap)
        {
            var beyond = lines[cap];
            lines.RemoveRange(cap, lines.Count - cap);

            // Valt de grens midden in een groep met dezelfde tijdstempel, dan weten we van die
            // groep niet of we hem helemaal hebben. Zetten we de cursor er tóch op, dan is een
            // regel met dezelfde tijdstempel en een lagere ULID voorgoed overgeslagen. Die groep
            // blijft dus liggen en komt bij de volgende tik compleet mee.
            if (lines[^1].Timestamp == beyond.Timestamp)
            {
                TrimYoungestGroup(lines);
            }
        }

        if (lines.Count == 0)
        {
            // De hele pagina bestond uit één tijdstempel. Laten liggen kan niet — dan komt de tail
            // nooit meer vooruit — dus leveren we hem uit en is de sortering op ULID het enige wat
            // de volgorde bepaalt. Dat vraagt meer regels binnen dezelfde honderd nanoseconden dan
            // er in een pagina passen; gebeurt dat, dan is er iets anders aan de hand.
            //
            // De cursor is de láátste van de al ordinaal gesorteerde lijst, en niet Max() over de
            // id's. Enumerable.Max op strings gebruikt Comparer<string>.Default en die is
            // cultuurgevoelig: bij nl-NL komt "ab1" ná "AB1" en ordinaal ervoor. Kiest de cursor
            // anders dan de uitlevering sorteert, dan wijst hij niet naar de laatst geleverde regel
            // en raakt de tail stil een regel kwijt. Voor pure ULID's vallen die twee samen, maar
            // een id uit een andere bron hoeft dat niet te doen.
            var alles = response
                .OrderBy(line => line.Timestamp)
                .ThenBy(line => line.Id, StringComparer.Ordinal)
                .ToArray();

            return new LogTail(
                alles,
                new LogCursor(alles[^1].Timestamp, alles[^1].Id),
                true);
        }

        return new LogTail(lines, new LogCursor(lines[^1].Timestamp, lines[^1].Id), hasMore);
    }

    /// <summary>Laat de jongste groep regels met dezelfde tijdstempel vallen.</summary>
    private static void TrimYoungestGroup(List<LogRecord> lines)
    {
        var youngest = lines[^1].Timestamp;
        var keep = lines.Count;

        while (keep > 0 && lines[keep - 1].Timestamp == youngest)
        {
            keep--;
        }

        lines.RemoveRange(keep, lines.Count - keep);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<RunBucket>>> GetRunHistogramAsync(
        CustomerScope scope,
        HistogramWindow window,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(window);

        var runs = await containers.RunsAsync(scope.Telemetry, cancellationToken).ConfigureAwait(false);

        // Groeperen op de eerste dertien tekens van de tijdstempel: "2026-08-19T17". Dat is het
        // hele uur, en het werkt omdat de opslagvorm vast is (ISO-8601 in UTC). Rekenen met datums
        // in de query zou hier niets aan nauwkeurigheid toevoegen en wel aan onleesbaarheid.
        //
        // Geen "pk IN (...)" om partities te beperken. Dat lag voor de hand — 24 uur beslaat twee
        // dagpartities per agent — maar gemeten is het duurder: 5,80 RU met de clausule tegen
        // 5,13 RU zonder. Het filter op customerId doet het werk al, en een IN-lijst van tweemaal
        // het aantal agents kost meer aan planning dan hij aan scan bespaart.
        var query = new QueryDefinition(
                "SELECT c.agentName AS agentName, SUBSTRING(c.startedAt, 0, 13) AS hour, " +
                "COUNT(1) AS runs, SUM(c.result = @failed ? 1 : 0) AS failed " +
                "FROM c WHERE c.customerId = @customerId AND c.startedAt >= @since AND c.startedAt < @until " +
                "GROUP BY c.agentName, SUBSTRING(c.startedAt, 0, 13)")
            .WithParameter("@customerId", scope.CustomerId)
            .WithParameter("@since", CosmosMoment(window.Start))
            .WithParameter("@until", CosmosMoment(window.End))
            .WithParameter("@failed", JsonNameOf(RunResult.Failed));

        var rows = await ReadAllAsync<HistogramRow>(runs, query, cancellationToken).ConfigureAwait(false);

        var histogram = new Dictionary<string, RunBucket[]>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.AgentName) || !TryParseHour(row.Hour, out var hour))
            {
                continue;
            }

            if (window.IndexOf(hour) is not { } index)
            {
                continue;
            }

            if (!histogram.TryGetValue(row.AgentName, out var blocks))
            {
                blocks = new RunBucket[window.BlockCount];
                histogram[row.AgentName] = blocks;
            }

            // Optellen en niet overschrijven: twee uren vallen samen in één blok van twee uur.
            blocks[index] = new RunBucket(
                blocks[index].Runs + row.Runs,
                blocks[index].Failed + row.Failed);
        }

        return histogram.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<RunBucket>)pair.Value,
            StringComparer.Ordinal);
    }

    /// <summary>Leest <c>"2026-08-19T17"</c> terug naar een moment in UTC.</summary>
    private static bool TryParseHour(string? hour, out DateTimeOffset moment)
    {
        moment = default;

        if (!DateTime.TryParseExact(
                hour,
                "yyyy-MM-ddTHH",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return false;
        }

        moment = new DateTimeOffset(parsed, TimeSpan.Zero);
        return true;
    }

    /// <inheritdoc />
    public async Task<RunTally> CountRunsAsync(
        CustomerScope scope,
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var runs = await containers.RunsAsync(scope.Telemetry, cancellationToken).ConfigureAwait(false);

        // De aliassen zijn bewust geen 'result' en 'count': het tweede botst met de functienaam
        // COUNT en dat soort botsingen merk je pas als de query op een andere SDK-versie draait.
        var query = new QueryDefinition(
                "SELECT c.result AS runResult, COUNT(1) AS runCount FROM c " +
                "WHERE c.customerId = @customerId AND c.startedAt >= @since GROUP BY c.result")
            .WithParameter("@customerId", scope.CustomerId)
            .WithParameter("@since", CosmosMoment(since));

        var buckets = await ReadAllAsync<ResultBucket>(runs, query, cancellationToken).ConfigureAwait(false);

        var tally = RunTally.Empty;

        foreach (var bucket in buckets)
        {
            tally = bucket.Result switch
            {
                RunResult.Ok => tally with { Ok = tally.Ok + bucket.Count },
                RunResult.Failed => tally with { Failed = tally.Failed + bucket.Count },
                RunResult.Skipped => tally with { Skipped = tally.Skipped + bucket.Count },
                RunResult.Running => tally with { Running = tally.Running + bucket.Count },
                _ => tally,
            };
        }

        return tally;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CustomerTelemetry>> GetOverviewAsync(
        OperatorScope scope,
        DateTimeOffset todayStartedAt,
        DateTimeOffset last24HoursStartedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var customers = scope.Customers;
        if (customers.Count == 0)
        {
            return [];
        }

        // Eerst verbinden, dan pas klokken. De tijdslimiet hieronder gaat over "deze klant is
        // traag" en mag niet afgaan op de eenmalige opstartkost van het allereerste contact met
        // een Cosmos-account; anders meldt de eerste bezoeker na een herstart dat álle klanten
        // onbereikbaar zijn. Gemeten: koud bijna acht seconden, warm zo'n 200 ms.
        await containers
            .WarmAsync(customers.Select(customer => customer.Telemetry), cancellationToken)
            .ConfigureAwait(false);

        var results = new CustomerTelemetry[customers.Count];

        await Parallel.ForAsync(
            0,
            customers.Count,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, _options.OverviewParallelism),
                CancellationToken = cancellationToken,
            },
            async (index, token) =>
            {
                results[index] = await ReadForOverviewAsync(
                    customers[index],
                    todayStartedAt,
                    last24HoursStartedAt,
                    token).ConfigureAwait(false);
            }).ConfigureAwait(false);

        return results;
    }

    /// <summary>
    /// Leest één klantopslag voor het overzicht, en vangt op als dat niet lukt.
    /// </summary>
    /// <remarks>
    /// Hier staat een brede <c>catch</c>, en dat is met opzet. Elke klant is straks een eigen
    /// account met een eigen netwerkpad en een eigen roltoewijzing; dat er af en toe eentje niet
    /// antwoordt is normaal bedrijf, geen uitzonderlijke situatie. De keuze is dan: het hele
    /// overzicht laten omvallen, de klant weglaten, of hem tonen met de reden erbij. Alleen de
    /// derde is eerlijk.
    ///
    /// Annulering door de aanroeper valt er buiten: als de gebruiker wegnavigeert, is er niets meer
    /// te melden.
    /// </remarks>
    private async Task<CustomerTelemetry> ReadForOverviewAsync(
        CustomerScope scope,
        DateTimeOffset todayStartedAt,
        DateTimeOffset last24HoursStartedAt,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.OverviewTimeoutSeconds)));

        try
        {
            var agents = await GetAgentsAsync(scope, timeout.Token).ConfigureAwait(false);
            var today = await CountRunsAsync(scope, todayStartedAt, timeout.Token).ConfigureAwait(false);
            var last24Hours = await CountRunsAsync(scope, last24HoursStartedAt, timeout.Token)
                .ConfigureAwait(false);

            return new CustomerTelemetry(scope, agents, today, last24Hours, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Telemetrie van klant {CustomerId} op {Endpoint} antwoordde niet binnen {Seconds}s.",
                scope.CustomerId,
                scope.Telemetry.AccountEndpoint,
                _options.OverviewTimeoutSeconds);

            return Unavailable(
                scope,
                $"De opslag van deze klant antwoordde niet binnen {_options.OverviewTimeoutSeconds} seconden.",
                "timeout");
        }
        catch (TelemetryNotProvisionedException exception)
        {
            logger.LogError(
                exception,
                "Telemetrie van klant {CustomerId} is niet ingericht.",
                scope.CustomerId);

            return Unavailable(scope, "De opslag van deze klant is niet ingericht.", exception.Message);
        }
        catch (CosmosException exception)
        {
            logger.LogError(
                exception,
                "Telemetrie van klant {CustomerId} gaf {StatusCode}.",
                scope.CustomerId,
                exception.StatusCode);

            return Unavailable(
                scope,
                "De opslag van deze klant gaf een fout.",
                $"Cosmos {(int)exception.StatusCode} {exception.StatusCode}");
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Telemetrie van klant {CustomerId} kon niet worden gelezen.",
                scope.CustomerId);

            return Unavailable(
                scope,
                "De opslag van deze klant kon niet worden gelezen.",
                exception.GetType().Name);
        }
    }

    private static CustomerTelemetry Unavailable(CustomerScope scope, string reason, string? detail) =>
        new(scope, [], RunTally.Empty, RunTally.Empty, new TelemetryUnavailable(reason, detail));

    /// <summary>
    /// De WHERE-clausule van de logweergave, met de parameters die erbij horen.
    /// </summary>
    /// <param name="Where">De clausule, zonder het woord <c>WHERE</c>.</param>
    /// <param name="Parameters">De parameters, in de volgorde waarin ze in de clausule staan.</param>
    private readonly record struct LogFilter(
        string Where,
        IReadOnlyList<(string Name, object Value)> Parameters);

    /// <summary>
    /// Bouwt het filter dat de lijst, de niveautellingen en de live tail delen.
    /// </summary>
    /// <remarks>
    /// <para>Eén plek, en dat is het punt. De chip "error 3" en de drie regels die het filter
    /// oplevert zijn hetzelfde getal alleen zolang de twee query's hetzelfde filter gebruiken; twee
    /// keer dezelfde clausule uitschrijven is twee keer die kunnen aanpassen.</para>
    ///
    /// <para>Alles gaat als parameter mee. Er wordt nergens een waarde in de querytekst geplakt,
    /// ook niet de zoekterm, want dat is precies waar een injectie in gaat zitten. De enige stukken
    /// die de tekst zelf raken zijn de <em>namen</em> van de parameters, en die maken wij.</para>
    /// </remarks>
    private static LogFilter BuildLogFilter(
        CustomerScope scope,
        string agentName,
        IReadOnlyCollection<LogLevel>? levels,
        string? search,
        string? runId,
        DateTimeOffset? asOf)
    {
        var sql = new StringBuilder("c.customerId = @customerId AND c.agentName = @agentName");

        var parameters = new List<(string Name, object Value)>
        {
            ("@customerId", scope.CustomerId),
            ("@agentName", agentName),
        };

        // Alle niveaus aan is hetzelfde als geen niveaufilter, en dan hoort er ook geen clausule te
        // staan. Anders krijgt de lezer die alle chips weer aanzet een andere query dan de lezer die
        // er nooit aan zat, met een andere prijs en een ander queryplan voor precies dezelfde vraag.
        var selected = levels?.Distinct().ToArray() ?? [];

        if (selected.Length > 0 && selected.Length < Enum.GetValues<LogLevel>().Length)
        {
            var names = new List<string>(selected.Length);
            var index = 0;

            foreach (var level in selected)
            {
                var name = $"@level{index++}";
                names.Add(name);
                parameters.Add((name, JsonNameOf(level)));
            }

            sql.Append(CultureInfo.InvariantCulture, $" AND c.level IN ({string.Join(", ", names)})");
        }

        if (!string.IsNullOrWhiteSpace(runId))
        {
            sql.Append(" AND c.runId = @runId");
            parameters.Add(("@runId", runId));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // De derde parameter van CONTAINS is "negeer hoofdletters". Zonder die vlag zou de
            // lezer moeten weten hoe de bouwer zijn events heeft gespeld.
            //
            // Regels zonder runId zijn geen probleem: CONTAINS op een ontbrekend veld levert
            // undefined, en undefined in een OR laat de andere twee gewoon hun werk doen. Gemeten
            // op de echte opslag, waar heartbeat- en config-regels geen runId hebben.
            sql.Append(
                " AND (CONTAINS(c.event, @search, true) OR CONTAINS(c.msg, @search, true)" +
                " OR CONTAINS(c.runId, @search, true))");
            parameters.Add(("@search", search.Trim()));
        }

        if (asOf is { } upperBound)
        {
            sql.Append(" AND c.ts <= @asOf");
            parameters.Add(("@asOf", CosmosMoment(upperBound)));
        }

        return new LogFilter(sql.ToString(), parameters);
    }

    /// <summary>Zet querytekst en parameters aan elkaar.</summary>
    private static QueryDefinition Compose(
        string sql,
        IEnumerable<(string Name, object Value)> parameters)
    {
        var definition = new QueryDefinition(sql);

        foreach (var (name, value) in parameters)
        {
            definition = definition.WithParameter(name, value);
        }

        return definition;
    }

    /// <summary>Zet querytekst en het filter aan elkaar.</summary>
    private static QueryDefinition Compose(string sql, LogFilter filter) =>
        Compose(sql, filter.Parameters);

    /// <summary>
    /// De dagpartities waarin de live tail kan zoeken, van de cursor tot vandaag.
    /// </summary>
    /// <returns>
    /// De partitiesleutels, of een lege lijst als de cursor te oud is om de query zinnig te
    /// begrenzen — dan zoekt hij over alle partities, wat duurder is maar niets mist.
    /// </returns>
    private List<string> TailPartitionKeys(string agentName, DateTimeOffset since)
    {
        var today = timeProvider.GetUtcNow().UtcDateTime.Date;
        var first = since.UtcDateTime.Date;

        if (first > today || (today - first).TotalDays >= MaxTailPartitionDays)
        {
            return [];
        }

        var keys = new List<string>();

        for (var day = first; day <= today; day = day.AddDays(1))
        {
            keys.Add(LogRecord.BuildPartitionKey(agentName, new DateTimeOffset(day, TimeSpan.Zero)));
        }

        return keys;
    }

    /// <summary>
    /// Haalt bij elke registratie zijn laatste afgeronde run op.
    /// </summary>
    private async Task<IReadOnlyList<AgentSnapshot>> WithLastCompletedRunsAsync(
        CustomerScope scope,
        IReadOnlyList<AgentRegistration> registrations,
        CancellationToken cancellationToken)
    {
        if (registrations.Count == 0)
        {
            return [];
        }

        var lastRuns = new RunRecord?[registrations.Count];

        await Parallel.ForAsync(
            0,
            registrations.Count,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxParallelRunLookups,
                CancellationToken = cancellationToken,
            },
            async (index, token) =>
            {
                lastRuns[index] = await LastCompletedRunAsync(
                    scope,
                    registrations[index].AgentName,
                    token).ConfigureAwait(false);
            }).ConfigureAwait(false);

        var snapshots = new AgentSnapshot[registrations.Count];

        for (var index = 0; index < registrations.Count; index++)
        {
            snapshots[index] = new AgentSnapshot(registrations[index], lastRuns[index]);
        }

        return snapshots;
    }

    /// <summary>
    /// De laatste run van deze agent die niet meer loopt.
    /// </summary>
    /// <remarks>
    /// Een lopende run zegt nog niets over slagen of falen, dus die telt niet mee — zie de
    /// documentatie bij <see cref="AgentStatusCalculator.Calculate"/>.
    /// </remarks>
    private async Task<RunRecord?> LastCompletedRunAsync(
        CustomerScope scope,
        string agentName,
        CancellationToken cancellationToken)
    {
        var runs = await containers.RunsAsync(scope.Telemetry, cancellationToken).ConfigureAwait(false);

        var query = new QueryDefinition(
                "SELECT TOP 1 * FROM c WHERE c.customerId = @customerId AND c.agentName = @agentName " +
                "AND c.result != @running ORDER BY c.startedAt DESC")
            .WithParameter("@customerId", scope.CustomerId)
            .WithParameter("@agentName", agentName)
            .WithParameter("@running", JsonNameOf(RunResult.Running));

        using var iterator = runs.GetItemQueryIterator<RunRecord>(
            query,
            requestOptions: new QueryRequestOptions { MaxItemCount = 1 });

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);

            foreach (var run in response)
            {
                return run;
            }
        }

        return null;
    }

    private static async Task<IReadOnlyList<T>> ReadAllAsync<T>(
        Container container,
        QueryDefinition query,
        CancellationToken cancellationToken)
    {
        var results = new List<T>();

        using var iterator = container.GetItemQueryIterator<T>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            results.AddRange(response);
        }

        return results;
    }

    /// <summary>
    /// Zet een moment om naar de vorm waarin Cosmos het kan vergelijken met wat er in het document
    /// staat.
    /// </summary>
    /// <param name="moment">Het moment.</param>
    /// <returns>Hetzelfde moment als UTC-<see cref="DateTime"/>.</returns>
    /// <remarks>
    /// <para>Dit is geen cosmetische omzetting en het teruggavetype is geen slordigheid. Cosmos
    /// bewaart tijdvelden als string en vergelijkt ze ordinaal. De documenten bevatten
    /// <c>"2026-08-19T15:24:00.0556102Z"</c> — met een <c>Z</c>. Serialiseer je een
    /// <see cref="DateTimeOffset"/>, dan schrijft System.Text.Json
    /// <c>"2026-08-19T15:24:00.0556102+00:00"</c>, en dat is een andere string.</para>
    ///
    /// <para>Gemeten tegen de echte opslag, met dezelfde tijdstempel als parameter:</para>
    /// <list type="table">
    ///   <item>
    ///     <term><c>DateTimeOffset</c></term>
    ///     <description><c>ts = @cursor</c> vindt 0 rijen, <c>ts &gt; @nieuwste</c> vindt er 1.</description>
    ///   </item>
    ///   <item>
    ///     <term><c>DateTime</c> (UTC)</term>
    ///     <description><c>ts = @cursor</c> vindt 1 rij, <c>ts &gt; @nieuwste</c> vindt er 0.</description>
    ///   </item>
    /// </list>
    ///
    /// <para>Alleen de tweede klopt. Met de eerste zou de live tail bij elke poll zijn nieuwste
    /// regel opnieuw tonen, en zou de gelijkspel-clausule op de ULID nooit afgaan — een fout die
    /// alleen zichtbaar is op de grens en dus vrijwel nooit tijdens het bouwen.</para>
    ///
    /// <para>Bewust <em>niet</em> zelf een string opmaken met <c>"yyyy-MM-ddTHH:mm:ss.fffffffZ"</c>.
    /// System.Text.Json kapt nullen aan het eind af — een moment op <c>.0550000</c> wordt
    /// <c>.055Z</c> — dus een vaste opmaak zou juist op ronde waarden afwijken. Door het als
    /// <see cref="DateTime"/> door dezelfde serializer te laten gaan die het document schreef, komt
    /// er per definitie dezelfde string uit.</para>
    /// </remarks>
    private static DateTime CosmosMoment(DateTimeOffset moment) => moment.UtcDateTime;

    private int PageSize(int? requested) =>
        requested is > 0 and <= 1000 ? requested.Value : _options.DefaultPageSize;

    /// <summary>
    /// De naam waaronder een enumwaarde in het document staat.
    /// </summary>
    /// <remarks>
    /// Uit het contract afgeleid en niet zelf verzonnen: de attributen op <see cref="RunResult"/>
    /// en <see cref="LogLevel"/> bepalen wat er in Cosmos staat, dus die worden hier uitgelezen.
    /// Een handgeschreven tabel met "ok", "failed" en zo zou stilzwijgend uit de pas gaan lopen
    /// zodra iemand een waarde toevoegt.
    /// </remarks>
    private static string JsonNameOf<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var members = typeof(TEnum).GetMember(value.ToString());

        if (members.Length > 0)
        {
            var attributes = members[0]
                .GetCustomAttributes(typeof(JsonStringEnumMemberNameAttribute), inherit: false);

            if (attributes.Length > 0)
            {
                return ((JsonStringEnumMemberNameAttribute)attributes[0]).Name;
            }
        }

        return value.ToString();
    }

    /// <summary>Eén regel uit de histogramquery: één agent, één heel uur.</summary>
    private sealed class HistogramRow
    {
        [JsonPropertyName("agentName")]
        public string? AgentName { get; set; }

        [JsonPropertyName("hour")]
        public string? Hour { get; set; }

        [JsonPropertyName("runs")]
        public int Runs { get; set; }

        [JsonPropertyName("failed")]
        public int Failed { get; set; }
    }

    /// <summary>Eén regel uit de <c>GROUP BY</c>-telling over de logniveaus.</summary>
    /// <remarks>
    /// De aliassen zijn niet <c>level</c> en <c>count</c>, om dezelfde reden als bij
    /// <see cref="ResultBucket"/>: <c>count</c> botst met de functienaam <c>COUNT</c> en dat merk je
    /// pas op een andere SDK-versie.
    /// </remarks>
    private sealed class LevelBucket
    {
        [JsonPropertyName("logLevel")]
        public LogLevel Level { get; set; }

        [JsonPropertyName("lineCount")]
        public int Count { get; set; }
    }

    /// <summary>Eén regel uit de <c>GROUP BY</c>-telling over de runs.</summary>
    private sealed class ResultBucket
    {
        [JsonPropertyName("runResult")]
        public RunResult Result { get; set; }

        [JsonPropertyName("runCount")]
        public int Count { get; set; }
    }
}
