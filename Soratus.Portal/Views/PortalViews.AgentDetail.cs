using Soratus.Agents.Contracts;
using Soratus.Portal.Data;
using Soratus.Portal.Security;

namespace Soratus.Portal.Views;

/// <summary>
/// De tabbladen van het agentdetail: Logs, Runs en Configuratie (§3.3).
/// </summary>
/// <remarks>
/// Zelfde klasse als de rest van de viewmodelbouw, apart bestand. Eén implementatie voor twee
/// interfaces, zodat de klok, de sortering en de omzetting van runs niet op twee plekken staan.
/// </remarks>
internal sealed partial class PortalViews : IAgentDetailViews
{
    /// <inheritdoc />
    public async Task<CustomerAgentLogsView?> BuildLogsAsync(
        CustomerScope scope,
        string agentName,
        LogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var registration = await VisibleToCustomerAsync(scope, agentName, cancellationToken)
            .ConfigureAwait(false);

        if (registration is null)
        {
            return null;
        }

        var logs = await LogsAsync(scope, registration.AgentName, query, cancellationToken)
            .ConfigureAwait(false);

        return new CustomerAgentLogsView
        {
            AgentName = logs.AgentName,
            GeneratedAt = logs.GeneratedAt,

            // Hier valt de projectie waar het besluit over extra op neerkomt. Het klanttype heeft
            // het veld niet, dus er is geen @if te vergeten en er reist niets mee over de
            // serialisatiegrens van het logtabblad.
            Lines = [.. logs.Lines.Select(CustomerLogLine.From)],
            Counts = logs.Counts,
            ActiveLevels = logs.ActiveLevels,
            Search = logs.Search,
            RunId = logs.RunId,
            ContinuationToken = logs.ContinuationToken,
            TailFrom = logs.TailFrom,
        };
    }

    /// <inheritdoc />
    public async Task<OperatorAgentLogsView?> BuildLogsAsync(
        OperatorCustomerScope scope,
        string agentName,
        LogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var registration = await VisibleToOperatorAsync(scope, agentName, cancellationToken)
            .ConfigureAwait(false);

        return registration is null
            ? null
            : await LogsAsync(scope.Customer, registration.AgentName, query, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CustomerAgentLogTail?> TailLogsAsync(
        CustomerScope scope,
        string agentName,
        LogQuery query,
        LogCursor since,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var registration = await VisibleToCustomerAsync(scope, agentName, cancellationToken)
            .ConfigureAwait(false);

        if (registration is null)
        {
            return null;
        }

        var tail = await TailAsync(scope, registration.AgentName, query, since, cancellationToken)
            .ConfigureAwait(false);

        // Dezelfde projectie als in de lijst, en om dezelfde reden: een regel die de tail erin
        // schuift mag niet meer velden dragen dan de regels die er al staan.
        return new CustomerAgentLogTail(
            [.. tail.Lines.Select(CustomerLogLine.From)],
            tail.Cursor,
            tail.HasMore,
            tail.Counts);
    }

    /// <inheritdoc />
    public async Task<OperatorAgentLogTail?> TailLogsAsync(
        OperatorCustomerScope scope,
        string agentName,
        LogQuery query,
        LogCursor since,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var registration = await VisibleToOperatorAsync(scope, agentName, cancellationToken)
            .ConfigureAwait(false);

        return registration is null
            ? null
            : await TailAsync(scope.Customer, registration.AgentName, query, since, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CustomerAgentRunsView?> BuildRunsAsync(
        CustomerScope scope,
        string agentName,
        int? pageSize = null,
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var registration = await VisibleToCustomerAsync(scope, agentName, cancellationToken)
            .ConfigureAwait(false);

        if (registration is null)
        {
            return null;
        }

        var page = await store
            .GetRunsAsync(
                scope,
                registration.AgentName,
                pageSize,
                continuationToken,
                cancellationToken)
            .ConfigureAwait(false);

        return new CustomerAgentRunsView
        {
            AgentName = registration.AgentName,
            GeneratedAt = timeProvider.GetUtcNow(),

            // Hier valt de projectie waar het besluit over errorType op neerkomt, en het is dezelfde
            // vorm als bij een logregel: het klanttype heeft het veld niet, dus er is geen @if te
            // vergeten en geen tooltip die er later per ongeluk bij komt.
            //
            // Beide overloads projecteren rechtstreeks uit RunRecord en niet de één uit de ander.
            // Zou de klantrij uit de operatorrij ontstaan, dan bestaat er een pad van het volle type
            // naar het smalle waarlangs een veld kan meeliften — en dat is precies de weg terug.
            //
            // Geen sortering: de query levert nieuwste eerst en dat is de volgorde van het scherm.
            // Nog eens sorteren zou de paginering doorbreken, want de tweede pagina wordt dan binnen
            // zichzelf gesorteerd en niet ten opzichte van de eerste.
            Runs = [.. page.Runs.Select(CustomerRunRow.From)],
            ContinuationToken = page.ContinuationToken,
        };
    }

    /// <inheritdoc />
    public async Task<OperatorAgentRunsView?> BuildRunsAsync(
        OperatorCustomerScope scope,
        string agentName,
        int? pageSize = null,
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var registration = await VisibleToOperatorAsync(scope, agentName, cancellationToken)
            .ConfigureAwait(false);

        if (registration is null)
        {
            return null;
        }

        var page = await store
            .GetRunsAsync(
                scope.Customer,
                registration.AgentName,
                pageSize,
                continuationToken,
                cancellationToken)
            .ConfigureAwait(false);

        return new OperatorAgentRunsView
        {
            AgentName = registration.AgentName,
            GeneratedAt = timeProvider.GetUtcNow(),
            Runs = [.. page.Runs.Select(OperatorRunRow.From)],
            ContinuationToken = page.ContinuationToken,
        };
    }

    /// <inheritdoc />
    public async Task<CustomerAgentConfigurationView?> BuildConfigurationAsync(
        CustomerScope scope,
        string agentName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var registration = await VisibleToCustomerAsync(scope, agentName, cancellationToken)
            .ConfigureAwait(false);

        if (registration is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();

        return new CustomerAgentConfigurationView
        {
            AgentName = registration.AgentName,
            GeneratedAt = now,
            Version = registration.Version,
            Schedule = registration.Schedule,
            TriggerKind = registration.TriggerKind,
            TriggerDetail = registration.TriggerDetail,
            NextRunAt = registration.NextRunAt,
            StartedAt = registration.StartedAt,
            LastHeartbeatAt = registration.LastHeartbeatAt,
            Silence = AgentStatusCalculator.SilenceFor(registration, now),
            HeartbeatInterval = AgentStatusThresholds.HeartbeatInterval,
            LogRetention = TelemetryRetention.Logs,
            RunRetention = TelemetryRetention.Runs,
            ReadOnlyNotice = AgentConfigurationNotice.ReadOnly,
        };
    }

    /// <inheritdoc />
    public async Task<OperatorAgentConfigurationView?> BuildConfigurationAsync(
        OperatorCustomerScope scope,
        string agentName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var registration = await VisibleToOperatorAsync(scope, agentName, cancellationToken)
            .ConfigureAwait(false);

        if (registration is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var telemetry = scope.Customer.Telemetry;

        return new OperatorAgentConfigurationView
        {
            AgentName = registration.AgentName,
            GeneratedAt = now,
            Version = registration.Version,
            Schedule = registration.Schedule,
            TriggerKind = registration.TriggerKind,
            TriggerDetail = registration.TriggerDetail,
            NextRunAt = registration.NextRunAt,
            StartedAt = registration.StartedAt,
            LastHeartbeatAt = registration.LastHeartbeatAt,
            Silence = AgentStatusCalculator.SilenceFor(registration, now),
            HeartbeatInterval = AgentStatusThresholds.HeartbeatInterval,
            LogRetention = TelemetryRetention.Logs,
            RunRetention = TelemetryRetention.Runs,
            ReadOnlyNotice = AgentConfigurationNotice.ReadOnly,
            IdentityNotice = AgentConfigurationNotice.IdentityElsewhere,
            AgentEnvironment = registration.Environment,
            Lifecycle = registration.Lifecycle,
            ContractVersion = registration.ContractVersion,
            ExpectedContractVersion = AgentRegistration.CurrentContractVersion,
            EnvironmentDetail = scope.EnvironmentDetail,
            TelemetryLocation = $"{telemetry.AccountEndpoint} · {telemetry.Database}",
        };
    }

    /// <summary>
    /// De agent zoals de klant hem mag zien, of <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Hier valt de omgevingsfilter voor de tabbladen, op precies dezelfde grond als in
    /// <see cref="BuildAgentDetailAsync(CustomerScope, string, CancellationToken)"/>: bestaat niet,
    /// andere klant, of niet in productie levert alle drie hetzelfde antwoord. Een klant hoort niet
    /// te kunnen afleiden dat er een acceptatie-agent met deze naam bestaat, en dat mag niet afhangen
    /// van de vraag welk deel van het scherm hij opvraagt.
    /// </remarks>
    private async Task<AgentRegistration?> VisibleToCustomerAsync(
        CustomerScope scope,
        string agentName,
        CancellationToken cancellationToken)
    {
        var registration = await store.GetRegistrationAsync(scope, agentName, cancellationToken)
            .ConfigureAwait(false);

        return registration?.Environment == AgentEnvironment.Production ? registration : null;
    }

    /// <summary>
    /// De agent zoals de operator hem mag zien, of <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Geen omgevingsfilter: de operator hoort ook naar acceptatie te kunnen kijken. Het filter op
    /// klant zit in de store en niet hier — de agentnaam komt uit de URL en de scope bewijst alleen
    /// dat deze gebruiker déze klant mag lezen.
    /// </remarks>
    private Task<AgentRegistration?> VisibleToOperatorAsync(
        OperatorCustomerScope scope,
        string agentName,
        CancellationToken cancellationToken) =>
        store.GetRegistrationAsync(scope.Customer, agentName, cancellationToken);

    /// <summary>
    /// Haalt de logpagina en de niveautellingen op, met dezelfde bovengrens.
    /// </summary>
    /// <remarks>
    /// <para>Twee query's, één moment. De bovengrens komt uit <see cref="LogQuery.AsOf"/> als de
    /// aanroeper er een meegaf en anders uit de klok, en gaat naar beide query's. Zonder die grens
    /// zouden de lijst en de tellingen naar twee verschillende verzamelingen kijken — en dan staat
    /// er op een dag "error 4" op de chip terwijl de tabel er drie toont.</para>
    ///
    /// <para>De twee query's lopen parallel. Ze raken dezelfde container maar zijn beide alleen
    /// lezend, en de latentie van het tabblad is die van de traagste in plaats van de som.</para>
    ///
    /// <para><strong>Deze methode levert de operatorvorm, ook op het klantpad.</strong> De
    /// klantoverload projecteert hem daarna naar <see cref="CustomerAgentLogsView"/>. Dat is bewust:
    /// er is één plek waar de query's staan en de bovengrens wordt gezet, en de rolscheiding valt in
    /// één projectie die je kunt aanwijzen. Het risico dat het besluit over <c>extra</c> adresseert
    /// is de serialisatiegrens naar het scherm, en die haalt deze vorm nooit — hij bestaat een paar
    /// microseconden in het geheugen van dezelfde methode.</para>
    /// </remarks>
    private async Task<OperatorAgentLogsView> LogsAsync(
        CustomerScope scope,
        string agentName,
        LogQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.ContinuationToken is not null && query.AsOf is null)
        {
            throw new ArgumentException(
                "Een vervolgtoken zonder AsOf verschuift de verzameling onder de paginering vandaan. " +
                "Geef CustomerAgentLogsView.GeneratedAt van de eerste pagina terug in LogQuery.AsOf.",
                nameof(query));
        }

        var bounded = query with { AsOf = query.AsOf ?? timeProvider.GetUtcNow() };

        var pageTask = store.GetLogsAsync(scope, agentName, bounded, cancellationToken);
        var tallyTask = store.CountLogLevelsAsync(scope, agentName, bounded, cancellationToken);

        await Task.WhenAll(pageTask, tallyTask).ConfigureAwait(false);

        var page = await pageTask.ConfigureAwait(false);
        var tally = await tallyTask.ConfigureAwait(false);

        // Het moment van deze weergave is de bovengrens en niet "nu": dat is het moment waarop de
        // getallen op dit scherm bij elkaar horen, en het is wat de aanroeper bij "meer laden"
        // moet teruggeven.
        var generatedAt = bounded.AsOf!.Value;

        return new OperatorAgentLogsView
        {
            AgentName = agentName,
            GeneratedAt = generatedAt,
            Lines = page.Lines,
            Counts = Counts(tally),
            ActiveLevels = ActiveLevels(query.Levels),
            Search = Blank(query.Search),
            RunId = Blank(query.RunId),
            ContinuationToken = page.ContinuationToken,

            // Bij een agent zonder logregels is er geen nieuwste regel om op verder te gaan. Dan
            // begint de tail bij het moment van deze weergave, zodat hij vanaf nu meeleest in plaats
            // van dat het scherm zelf een beginpunt moet verzinnen.
            TailFrom = page.Newest ?? LogCursor.From(generatedAt),
        };
    }

    /// <summary>
    /// Haalt op wat er ná de cursor bij is gekomen, met bijgewerkte tellingen.
    /// </summary>
    /// <remarks>
    /// De tellingen worden begrensd op de nieuwe cursor en niet op "nu". Dat is precies de grens van
    /// wat er na deze tik in de tabel staat: alles tot en met de jongste regel die de tail meegeeft.
    /// Zou hier "nu" staan, dan kan een regel die net is weggeschreven maar nog niet is uitgeleverd
    /// wél in de chip en niet in de tabel terechtkomen — en dan spreken de chip en de tabel elkaar
    /// tegen op het ene moment dat de lezer ernaar kijkt.
    /// </remarks>
    private async Task<OperatorAgentLogTail> TailAsync(
        CustomerScope scope,
        string agentName,
        LogQuery query,
        LogCursor since,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var tail = await store.TailLogsAsync(scope, agentName, query.Tail(since), cancellationToken)
            .ConfigureAwait(false);

        var tally = await store.CountLogLevelsAsync(
                scope,
                agentName,
                query with { AsOf = tail.Cursor.Timestamp, ContinuationToken = null },
                cancellationToken)
            .ConfigureAwait(false);

        return new OperatorAgentLogTail(tail.Lines, tail.Cursor, tail.HasMore, Counts(tally));
    }


    /// <summary>
    /// De tellingen als woordenboek, zodat de filterchips ze direct kunnen aflezen.
    /// </summary>
    /// <remarks>
    /// Alle drie de niveaus staan erin, ook die met nul. Een ontbrekende sleutel zou het scherm
    /// dwingen te kiezen tussen "0" en niets, en die keuze hoort niet in de weergave: een chip
    /// zonder getal ziet eruit als een chip die nog aan het laden is.
    /// </remarks>
    private static IReadOnlyDictionary<LogLevel, int> Counts(LogLevelTally tally) =>
        new Dictionary<LogLevel, int>(3)
        {
            [LogLevel.Info] = tally.Info,
            [LogLevel.Warn] = tally.Warn,
            [LogLevel.Error] = tally.Error,
        };

    /// <summary>
    /// De niveaus die aan staan, of <c>null</c> als dat alle drie zijn.
    /// </summary>
    /// <remarks>
    /// Leeg en "alle drie" komen beide als <c>null</c> terug, want ze betekenen op het scherm
    /// hetzelfde: geen niveaufilter. Zouden ze verschillen, dan was er een stand waarin alle chips
    /// uit staan en er tóch alles te zien is.
    /// </remarks>
    private static IReadOnlySet<LogLevel>? ActiveLevels(IReadOnlyCollection<LogLevel>? levels)
    {
        if (levels is null or { Count: 0 })
        {
            return null;
        }

        var set = levels.ToHashSet();
        return set.Count >= 3 ? null : set;
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
