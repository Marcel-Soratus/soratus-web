using Soratus.Agents.Contracts;
using Soratus.Portal.Data;
using Soratus.Portal.Security;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// Een <see cref="IAgentTelemetryStore"/> op een lijst in het geheugen, voor de rekentests van het
/// agentdetail.
/// </summary>
/// <remarks>
/// <para><strong>Dit is geen tweede store in het portaal.</strong> De reflectietest in
/// <c>StoreImplementatieTests</c> kijkt naar de assembly van <c>Soratus.Portal</c> en die houdt
/// precies één implementatie over. Deze klasse staat in het <em>testproject</em>, om dezelfde
/// reden als <see cref="VastePortaalweergaven"/>: er is een manier nodig om
/// <c>PortalViews</c> zelf te testen — de zichtbaarheidsregel, de tellingen, de cursor — en dat
/// kan niet met de echte weergavelaag erboven.</para>
///
/// <para>Wat hij nadoet, doet hij zoals de query het doet, en niet gemakkelijker:</para>
/// <list type="bullet">
///   <item><description>
///     <see cref="CountLogLevelsAsync"/> negeert <c>query.Levels</c>. Dat is niet vergeten maar de
///     afspraak: een telling per niveau die zelf op niveau filtert telt alleen zichzelf. Zie de
///     opmerking bij <c>CosmosAgentTelemetryStore.CountLogLevelsAsync</c>.
///   </description></item>
///   <item><description>
///     <see cref="TailLogsAsync"/> gebruikt dezelfde gelijkspelclausule als de SQL —
///     <c>ts &gt; @since OR (ts = @since AND id &gt; @sinceId)</c> — sorteert op tijd én id
///     ordinaal, vraagt één regel meer op dan hij uitlevert, en laat de jongste groep liggen met
///     <em>de productiecode zelf</em> via <see cref="Opslaglaag.TrimJongsteGroep"/>.
///   </description></item>
/// </list>
///
/// <para>Dat laatste is de eerlijke grens van deze fixture: de query bewijst hij niet. Wat hij
/// bewijst is dat de regels die eromheen staan — de cursor die PortalViews doorgeeft en de
/// grensregel op gelijke tijdstempels — samen geen regel dubbel leveren en er geen overslaan. Dat
/// de clausule ook werkelijk in de query staat, controleert een aparte broncodetest.</para>
///
/// <para>De methoden die deze tests niet gebruiken werpen. Een <c>null</c> of een lege lijst
/// teruggeven zou een test die er per ongeluk langskomt groen laten staan op niets.</para>
/// </remarks>
internal sealed class Vastetelemetriestore : IAgentTelemetryStore
{
    private readonly List<LogRecord> _logregels = [];
    private readonly List<RunRecord> _runs = [];

    /// <summary>
    /// Het registratiedocument dat <see cref="GetRegistrationAsync"/> teruggeeft, of <c>null</c>
    /// voor een agent die niet bestaat.
    /// </summary>
    public AgentRegistration? Registratie { get; set; } =
        Testgegevens.Registratie(Testgegevens.Nu - TimeSpan.FromSeconds(14));

    /// <summary>Hoeveel keer er om een registratie is gevraagd.</summary>
    /// <remarks>
    /// Elke methode van <c>IAgentDetailViews</c> doet zijn eigen zichtbaarheidscontrole. Dat is
    /// alleen waar als er ook werkelijk een keer wordt gekeken, en niet één keer voor alles.
    /// </remarks>
    public int Registratieverzoeken { get; private set; }

    /// <summary>De logregels, oudste eerst.</summary>
    public IReadOnlyList<LogRecord> Logregels => _logregels;

    /// <summary>Zet de logregels; de volgorde waarin ze binnenkomen doet niet mee.</summary>
    /// <param name="regels">De regels.</param>
    /// <returns>Deze store, zodat een test hem in één uitdrukking kan opbouwen.</returns>
    public Vastetelemetriestore MetLogregels(IEnumerable<LogRecord> regels)
    {
        ArgumentNullException.ThrowIfNull(regels);

        _logregels.Clear();
        _logregels.AddRange(regels);

        return this;
    }

    /// <summary>Zet de runs; nieuwste eerst is een besluit van de query, niet van de aanroeper.</summary>
    /// <param name="runs">De runs.</param>
    /// <returns>Deze store.</returns>
    public Vastetelemetriestore MetRuns(IEnumerable<RunRecord> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        _runs.Clear();
        _runs.AddRange(runs);

        return this;
    }

    /// <summary>Zet de omgeving van het registratiedocument.</summary>
    /// <param name="omgeving">De omgeving.</param>
    /// <returns>Deze store.</returns>
    public Vastetelemetriestore MetOmgeving(AgentEnvironment omgeving)
    {
        Registratie = Testgegevens.Registratie(
            Testgegevens.Nu - TimeSpan.FromSeconds(14),
            environment: omgeving);

        return this;
    }

    /// <summary>Laat de agent niet bestaan.</summary>
    /// <returns>Deze store.</returns>
    public Vastetelemetriestore ZonderAgent()
    {
        Registratie = null;

        return this;
    }

    /// <inheritdoc />
    public Task<AgentRegistration?> GetRegistrationAsync(
        CustomerScope scope,
        string agentName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        Registratieverzoeken++;

        return Task.FromResult(
            Registratie is { } registratie
            && string.Equals(registratie.AgentName, agentName, StringComparison.OrdinalIgnoreCase)
                ? registratie
                : null);
    }

    /// <inheritdoc />
    public Task<LogPage> GetLogsAsync(
        CustomerScope scope,
        string agentName,
        LogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        // Nieuwste eerst, precies als "ORDER BY c.ts DESC".
        var regels = Gefilterd(query, query.Levels)
            .OrderByDescending(r => r.Timestamp)
            .ThenByDescending(r => r.Id, StringComparer.Ordinal)
            .ToArray();

        var overslaan = Sprong(query.ContinuationToken);
        var omvang = query.PageSize ?? 50;
        var pagina = regels.Skip(overslaan).Take(omvang).ToArray();
        var vervolg = overslaan + pagina.Length < regels.Length
            ? (overslaan + pagina.Length).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;

        var nieuwste = pagina.Length == 0
            ? (LogCursor?)null
            : new LogCursor(pagina[0].Timestamp, pagina[0].Id);

        return Task.FromResult(new LogPage(pagina, vervolg, nieuwste));
    }

    /// <inheritdoc />
    public Task<LogLevelTally> CountLogLevelsAsync(
        CustomerScope scope,
        string agentName,
        LogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        // levels: null. Zie de opmerking bij deze klasse: een telling per niveau die zelf op
        // niveau filtert telt alleen zichzelf.
        var tally = LogLevelTally.Empty;

        foreach (var regel in Gefilterd(query, levels: null))
        {
            tally = tally.Add(regel.Level, 1);
        }

        return Task.FromResult(tally);
    }

    /// <inheritdoc />
    public Task<LogTail> TailLogsAsync(
        CustomerScope scope,
        string agentName,
        LogTailQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        var cap = query.MaxLines ?? 50;

        // De gelijkspelclausule uit de SQL, letterlijk: later, of gelijktijdig met een hogere id.
        var kandidaten = _logregels
            .Where(r => NaCursor(r, query.Since))
            .Where(r => query.Levels is null or { Count: 0 } || query.Levels.Contains(r.Level))
            .Where(r => query.Search is null
                || r.Message.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || r.Event.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .Where(r => query.RunId is null
                || string.Equals(r.RunId, query.RunId, StringComparison.Ordinal))
            .OrderBy(r => r.Timestamp)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();

        if (kandidaten.Count == 0)
        {
            return Task.FromResult(LogTail.Nothing(query.Since));
        }

        // Eén regel meer opvragen dan uitleveren, net als de query: die extra regel vertelt of er
        // meer klaarstaat én of de grens midden in een groep met dezelfde tijdstempel valt.
        var venster = kandidaten.Take(cap + 1).ToList();
        var meer = venster.Count > cap;

        if (venster.Count > cap)
        {
            var voorbij = venster[cap];
            venster.RemoveRange(cap, venster.Count - cap);

            if (venster[^1].Timestamp == voorbij.Timestamp)
            {
                Opslaglaag.TrimJongsteGroep(venster);
            }
        }

        if (venster.Count == 0)
        {
            // De hele pagina was één tijdstempel. Laten liggen kan niet, dus alles eruit.
            var alles = kandidaten.Take(cap + 1).ToArray();

            return Task.FromResult(new LogTail(
                alles,
                new LogCursor(alles[^1].Timestamp, alles[^1].Id),
                true));
        }

        return Task.FromResult(new LogTail(
            venster,
            new LogCursor(venster[^1].Timestamp, venster[^1].Id),
            meer));
    }

    /// <summary>
    /// Of deze regel ná de cursor komt: later, of gelijktijdig met een hogere id.
    /// </summary>
    /// <param name="regel">De regel.</param>
    /// <param name="cursor">De cursor.</param>
    /// <returns><c>true</c> als de regel nog geleverd moet worden.</returns>
    /// <remarks>
    /// Dit is de clausule die de gelijke tijdstempels afhandelt. Zonder het tweede lid zou een
    /// tail met <c>ts &gt; @since</c> alle regels op de cursortijd overslaan, en met
    /// <c>ts &gt;= @since</c> zou hij ze allemaal opnieuw leveren.
    /// </remarks>
    public static bool NaCursor(LogRecord regel, LogCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(regel);

        return regel.Timestamp > cursor.Timestamp
            || (regel.Timestamp == cursor.Timestamp
                && string.CompareOrdinal(regel.Id, cursor.Id) > 0);
    }

    /// <inheritdoc />
    public Task<RunPage> GetRunsAsync(
        CustomerScope scope,
        string agentName,
        int? pageSize = null,
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var runs = _runs
            .Where(r => string.Equals(r.AgentName, agentName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.StartedAt)
            .ToArray();

        var overslaan = Sprong(continuationToken);
        var pagina = runs.Skip(overslaan).Take(pageSize ?? 50).ToArray();
        var vervolg = overslaan + pagina.Length < runs.Length
            ? (overslaan + pagina.Length).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;

        return Task.FromResult(new RunPage(pagina, vervolg));
    }

    private IEnumerable<LogRecord> Gefilterd(LogQuery query, IReadOnlyCollection<LogLevel>? levels) =>
        _logregels
            .Where(r => query.AsOf is null || r.Timestamp <= query.AsOf)
            .Where(r => levels is null or { Count: 0 } || levels.Contains(r.Level))
            .Where(r => query.Search is null
                || r.Message.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || r.Event.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .Where(r => query.RunId is null
                || string.Equals(r.RunId, query.RunId, StringComparison.Ordinal));

    /// <summary>
    /// Het vervolgtoken van deze fixture is het aantal al geleverde regels. Cosmos gebruikt een
    /// opake tekenreeks; wat een token betekent is aan de opslaglaag, en dit is de eenvoudigste
    /// vorm die zich als een token gedraagt.
    /// </summary>
    private static int Sprong(string? continuationToken) =>
        int.TryParse(
            continuationToken,
            System.Globalization.CultureInfo.InvariantCulture,
            out var waarde)
            ? waarde
            : 0;

    /// <inheritdoc />
    /// <remarks>
    /// Het detail leest de agent via <see cref="AgentSnapshot"/> en niet via het registratie-
    /// document. Dat is dezelfde vraag met een andere vorm, dus het antwoord komt uit dezelfde
    /// bron: één plek in deze fixture bepaalt of de agent bestaat en in welke omgeving hij draait.
    /// </remarks>
    public async Task<AgentSnapshot?> GetAgentAsync(
        CustomerScope scope,
        string agentName,
        CancellationToken cancellationToken = default)
    {
        var registratie = await GetRegistrationAsync(scope, agentName, cancellationToken)
            .ConfigureAwait(false);

        if (registratie is null)
        {
            return null;
        }

        var laatste = _runs
            .Where(r => r.Result != RunResult.Running)
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefault();

        return new AgentSnapshot(registratie, laatste);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Leeg, en dat mag: de weergavelaag vult een agent zonder runs zelf aan tot de twaalf blokken
    /// van het venster. Een verzonnen histogram zou hier alleen ruis toevoegen aan tests die over
    /// zichtbaarheid en tellingen gaan.
    /// </remarks>
    public Task<IReadOnlyDictionary<string, IReadOnlyList<RunBucket>>> GetRunHistogramAsync(
        CustomerScope scope,
        HistogramWindow window,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<RunBucket>>>(
            new Dictionary<string, IReadOnlyList<RunBucket>>(StringComparer.Ordinal));

    public Task<IReadOnlyList<AgentSnapshot>> GetAgentsAsync(
        CustomerScope scope,
        CancellationToken cancellationToken = default) => throw Ongebruikt();

    public Task<RunTally> CountRunsAsync(
        CustomerScope scope,
        DateTimeOffset since,
        CancellationToken cancellationToken = default) => throw Ongebruikt();

    public Task<IReadOnlyList<CustomerTelemetry>> GetOverviewAsync(
        OperatorScope scope,
        DateTimeOffset todayStartedAt,
        DateTimeOffset last24HoursStartedAt,
        CancellationToken cancellationToken = default) => throw Ongebruikt();

    private static NotSupportedException Ongebruikt() =>
        new("Deze fixture bedient alleen het agentdetail: registratie, logs en runs. Komt een " +
            "test hier langs, dan test hij iets anders dan hij denkt — vul de fixture aan in " +
            "plaats van hier een leeg antwoord te laten teruggeven.");
}
