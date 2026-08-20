using Soratus.Agents.Contracts;
using Soratus.Portal.Data;
using Soratus.Portal.Security;
using Soratus.Portal.Views;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// Een vaste <see cref="IPortalViews"/> voor de zichtbaarheidstests: dezelfde agents voor elke
/// klant, met een vast moment.
/// </summary>
/// <remarks>
/// <para>Dit is geen tweede implementatie van de <c>IAgentTelemetryStore</c> — die mag er niet
/// zijn, en de reflectietest in <c>StoreImplementatieTests</c> houdt dat tegen. Dit is een vaste
/// weergavelaag in het <em>testproject</em>, zodat de zichtbaarheidstests een pagina kunnen
/// renderen zonder Cosmos aan te raken. Hij staat hier en niet in <c>Soratus.Portal</c>, want daar
/// zou hij vroeg of laat de plek worden waar demo en werkelijkheid uiteen gaan lopen.</para>
///
/// <para>De gegevens zijn met opzet rijk gevuld: een failed, een degraded, een idle en een live
/// agent. Een lege weergave zou de zichtbaarheidstests laten slagen omdat er niets te zien is, en
/// dat is precies het soort groen waar je niets aan hebt.</para>
///
/// <para>Deze klasse breekt bij het compileren zodra er een <c>required</c> lid bij een viewmodel
/// komt. Dat is geen ongemak maar het punt: een nieuw veld op een klantviewmodel is een beslissing
/// over wat een klant mag zien, en die hoort iemand bewust te nemen in plaats van hem stilzwijgend
/// mee te laten liften.</para>
///
/// <para><strong>Ook <see cref="IAgentDetailViews"/>, en met vijandige inhoud.</strong> Het
/// agentdetail vraagt beide interfaces; een stub die lege lijsten teruggeeft zou het scherm laten
/// renderen zonder er iets op te zetten, en dan bewijst een zichtbaarheidstest niets. De logregels
/// komen daarom uit <see cref="Testlogregels"/> — met een <c>extra</c> zoals de echte agents hem
/// schrijven, koppelingdetails en al. Zie de toelichting daar.</para>
/// </remarks>
/// <param name="alleenBuitenProductie">
/// Zet elke klantrij op "wel agents, maar geen enkele in productie": nul productie-agents, één
/// agent op acceptatie. Zo'n rij komt op rang 0 uit — net als een klant zonder agents — en is
/// het enige geval waarin het scherm <c>StatusVisuals.UnknownNonProductionLabel</c> hoort te
/// tonen in plaats van "Geen agents". Zonder deze stand is dat pad niet te renderen, want de
/// standaardrijen hebben altijd productie-agents.
/// </param>
/// <param name="metLangeBerichten">
/// Zet er twee logregels bij waarvan het <em>bericht</em> lang is, en dat op twee tegengestelde
/// manieren: één meerregelig bericht met een .NET-stacktrace en onze bronpaden erin, en één geldig
/// bericht van 1417 tekens op precies één regel. Ze horen bij elkaar en staan daarom onder één
/// vlag: de knip in <c>CustomerMessage.FirstLine</c> moet de eerste inkorten en de tweede
/// ongemoeid laten, en een fixture die er maar één van kent laat de helft van die eis ongetest.
/// Zie <see cref="Testlogregels.BerichtMetStacktrace"/> en
/// <see cref="Testlogregels.LangBerichtOpEenRegel"/>.
/// </param>
internal sealed class VastePortaalweergaven(
    bool alleenBuitenProductie = false,
    bool metLangeBerichten = false)
    : IPortalViews, IAgentDetailViews
{
    private static readonly DateTimeOffset Nu = Testgegevens.Nu;

    public Task<OperatorOverviewView> BuildOverviewAsync(
        OperatorScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var rijen = scope.Customers.Select((c, i) => new OperatorCustomerRow
        {
            CustomerId = c.CustomerId,
            DisplayName = c.DisplayName,
            IsInternal = c.IsInternal,
            Statuses = alleenBuitenProductie
                ? AgentStatusBreakdown.Empty
                : new AgentStatusBreakdown(2, 1, 1, 1, 0),
            NonProductionStatuses = new AgentStatusBreakdown(1, 0, 0, 0, 0),
            // De ernst gaat over productie, dus een klant zonder productie-agents komt op
            // CustomerSeverity.None uit: rang 0, geen activiteit, nul agents meegerekend.
            Severity = alleenBuitenProductie
                ? CustomerSeverity.None
                : new CustomerSeverity(
                    AgentStatus.Failed,
                    Nu - TimeSpan.FromMinutes(3 + i),
                    5),
            Today = alleenBuitenProductie ? RunTally.Empty : new RunTally(18, 2, 1, 0),
            Last24Hours = alleenBuitenProductie ? RunTally.Empty : new RunTally(96, 4, 3, 1),
        }).ToArray();

        return Task.FromResult(new OperatorOverviewView
        {
            GeneratedAt = Nu,
            Kpis = new OperatorOverviewKpis
            {
                CustomerCount = rijen.Length,
                OnboardingCount = 0,
                NonProductionOnlyCount = alleenBuitenProductie ? rijen.Length : 0,
                UnavailableCount = 0,
                Statuses = alleenBuitenProductie
                    ? AgentStatusBreakdown.Empty
                    : new AgentStatusBreakdown(2 * rijen.Length, rijen.Length, rijen.Length, rijen.Length, 0),
                NonProductionStatuses = new AgentStatusBreakdown(rijen.Length, 0, 0, 0, 0),
                TodayStartedAt = Nu.AddHours(-9),
                Today = alleenBuitenProductie
                    ? RunTally.Empty
                    : new RunTally(18 * rijen.Length, 2 * rijen.Length, rijen.Length, 0),
                Last24Hours = alleenBuitenProductie
                    ? RunTally.Empty
                    : new RunTally(96 * rijen.Length, 4 * rijen.Length, 3 * rijen.Length, rijen.Length),
            },
            Customers = rijen,
        });
    }

    public Task<CustomerAgentsView> BuildAgentsAsync(
        CustomerScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var agents = Klantrijen();

        return Task.FromResult(new CustomerAgentsView
        {
            CustomerId = scope.CustomerId,
            DisplayName = scope.DisplayName,
            Environment = scope.Environment,
            GeneratedAt = Nu,
            Agents = agents,
            Statuses = AgentStatusBreakdown.FromStatuses(agents.Select(a => a.Status)),
        });
    }

    public Task<OperatorCustomerAgentsView> BuildAgentsAsync(
        OperatorCustomerScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var agents = Operatorrijen();

        return Task.FromResult(new OperatorCustomerAgentsView
        {
            CustomerId = scope.CustomerId,
            DisplayName = scope.DisplayName,
            Environment = scope.Environment,
            EnvironmentDetail = scope.EnvironmentDetail,
            IsInternal = scope.IsInternal,
            GeneratedAt = Nu,
            Agents = agents,
            Statuses = AgentStatusBreakdown.FromStatuses(agents.Select(a => a.Status)),
            ProductionStatuses = AgentStatusBreakdown.FromStatuses(
                agents.Where(a => a.AgentEnvironment == AgentEnvironment.Production).Select(a => a.Status)),
        });
    }

    public Task<CustomerAgentDetailView?> BuildAgentDetailAsync(
        CustomerScope scope,
        string agentName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var agent = Klantrijen()[0] with { AgentName = agentName };

        return Task.FromResult<CustomerAgentDetailView?>(new CustomerAgentDetailView
        {
            CustomerId = scope.CustomerId,
            CustomerDisplayName = scope.DisplayName,
            GeneratedAt = Nu,
            Agent = agent,
        });
    }

    public Task<OperatorAgentDetailView?> BuildAgentDetailAsync(
        OperatorCustomerScope scope,
        string agentName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var agent = Operatorrijen()[0] with { AgentName = agentName };

        return Task.FromResult<OperatorAgentDetailView?>(new OperatorAgentDetailView
        {
            CustomerId = scope.CustomerId,
            CustomerDisplayName = scope.DisplayName,
            Environment = scope.Environment,
            GeneratedAt = Nu,
            Agent = agent,
            TelemetryLocation = $"{scope.Customer.Telemetry.AccountEndpoint} · {scope.Customer.Telemetry.Database}",
        });
    }

    // ────────────────────────────────────────────────────────────────────────────────────────
    // IAgentDetailViews: de drie tabbladen van het agentdetail.
    //
    // Elke methode heeft een overload per rol, en bij de logregels is dat een echt
    // typeverschil: de operator krijgt LogRecord met Extra erin, de klant krijgt
    // CustomerLogLine en die héért Extra niet te hebben — hij hééft het niet.
    //
    // Beide overloads leveren dezelfde regels, uit dezelfde bron, met dezelfde vijandige
    // inhoud in Extra. Dat is de hele opzet: zou de fixture het klantpad stilletjes armer
    // vullen, dan zou een zichtbaarheidstest groen staan omdat de fixture al filterde en niet
    // omdat de scheiding werkt.
    // ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>De logregels, oudste eerst. Zie <see cref="Testlogregels"/>.</summary>
    private readonly IReadOnlyList<LogRecord> _logregels = metLangeBerichten
        ?
        [
            .. Testlogregels.Klantregels(),
            Testlogregels.BerichtMetStacktrace(),
            Testlogregels.LangBerichtOpEenRegel(),
        ]
        : Testlogregels.Klantregels();

    public Task<CustomerAgentLogsView?> BuildLogsAsync(
        CustomerScope scope,
        string agentName,
        LogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var (zichtbaar, tellingen) = Filter(query);

        return Task.FromResult<CustomerAgentLogsView?>(new CustomerAgentLogsView
        {
            AgentName = agentName,
            GeneratedAt = Nu,
            Lines = [.. zichtbaar.Select(CustomerLogLine.From)],
            Counts = tellingen,
            ActiveLevels = ActieveNiveaus(query),
            Search = query.Search,
            RunId = query.RunId,
            ContinuationToken = null,
            TailFrom = Testlogregels.Cursor(_logregels),
        });
    }

    public Task<OperatorAgentLogsView?> BuildLogsAsync(
        OperatorCustomerScope scope,
        string agentName,
        LogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var (zichtbaar, tellingen) = Filter(query);

        return Task.FromResult<OperatorAgentLogsView?>(new OperatorAgentLogsView
        {
            AgentName = agentName,
            GeneratedAt = Nu,
            Lines = zichtbaar,
            Counts = tellingen,
            ActiveLevels = ActieveNiveaus(query),
            Search = query.Search,
            RunId = query.RunId,
            ContinuationToken = null,
            TailFrom = Testlogregels.Cursor(_logregels),
        });
    }

    public Task<CustomerAgentLogTail?> TailLogsAsync(
        CustomerScope scope,
        string agentName,
        LogQuery query,
        LogCursor since,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var (nieuw, cursor) = Staart(query, since);

        return Task.FromResult<CustomerAgentLogTail?>(new CustomerAgentLogTail(
            [.. nieuw.Select(CustomerLogLine.From)],
            cursor,
            HasMore: false,
            Testlogregels.Tellingen(_logregels)));
    }

    public Task<OperatorAgentLogTail?> TailLogsAsync(
        OperatorCustomerScope scope,
        string agentName,
        LogQuery query,
        LogCursor since,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var (nieuw, cursor) = Staart(query, since);

        return Task.FromResult<OperatorAgentLogTail?>(new OperatorAgentLogTail(
            nieuw,
            cursor,
            HasMore: false,
            Testlogregels.Tellingen(_logregels)));
    }

    public Task<AgentRunsView?> BuildRunsAsync(
        CustomerScope scope,
        string agentName,
        int? pageSize = null,
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return Task.FromResult<AgentRunsView?>(Runweergave(agentName));
    }

    public Task<AgentRunsView?> BuildRunsAsync(
        OperatorCustomerScope scope,
        string agentName,
        int? pageSize = null,
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return Task.FromResult<AgentRunsView?>(Runweergave(agentName));
    }

    public Task<CustomerAgentConfigurationView?> BuildConfigurationAsync(
        CustomerScope scope,
        string agentName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return Task.FromResult<CustomerAgentConfigurationView?>(new CustomerAgentConfigurationView
        {
            AgentName = agentName,
            GeneratedAt = Nu,
            Version = "1.4.2",
            Schedule = "*/5 * * * *",
            TriggerKind = TriggerKind.Timer,
            TriggerDetail = "Elke 5 minuten",
            NextRunAt = Nu + TimeSpan.FromMinutes(4),
            StartedAt = Nu - TimeSpan.FromHours(6),
            LastHeartbeatAt = Nu - TimeSpan.FromSeconds(14),
            Silence = TimeSpan.FromSeconds(14),
            HeartbeatInterval = AgentStatusThresholds.HeartbeatInterval,
            LogRetention = TelemetryRetention.Logs,
            RunRetention = TelemetryRetention.Runs,
            ReadOnlyNotice = AgentConfigurationNotice.ReadOnly,
        });
    }

    public Task<OperatorAgentConfigurationView?> BuildConfigurationAsync(
        OperatorCustomerScope scope,
        string agentName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return Task.FromResult<OperatorAgentConfigurationView?>(new OperatorAgentConfigurationView
        {
            AgentName = agentName,
            GeneratedAt = Nu,
            Version = "1.4.2",
            Schedule = "*/5 * * * *",
            TriggerKind = TriggerKind.Timer,
            TriggerDetail = "Elke 5 minuten",
            NextRunAt = Nu + TimeSpan.FromMinutes(4),
            StartedAt = Nu - TimeSpan.FromHours(6),
            LastHeartbeatAt = Nu - TimeSpan.FromSeconds(14),
            Silence = TimeSpan.FromSeconds(14),
            HeartbeatInterval = AgentStatusThresholds.HeartbeatInterval,
            LogRetention = TelemetryRetention.Logs,
            RunRetention = TelemetryRetention.Runs,
            ReadOnlyNotice = AgentConfigurationNotice.ReadOnly,
            IdentityNotice = AgentConfigurationNotice.IdentityElsewhere,
            AgentEnvironment = AgentEnvironment.Production,
            Lifecycle = AgentLifecycle.Running,
            ContractVersion = AgentRegistration.CurrentContractVersion,
            ExpectedContractVersion = AgentRegistration.CurrentContractVersion,
            EnvironmentDetail = scope.EnvironmentDetail,
            TelemetryLocation =
                $"{scope.Customer.Telemetry.AccountEndpoint} · {scope.Customer.Telemetry.Database}",
        });
    }

    /// <summary>
    /// De regels die aan het filter voldoen, nieuwste eerst, plus de telling per niveau.
    /// </summary>
    /// <param name="query">Het filter.</param>
    /// <returns>De zichtbare regels en de tellingen.</returns>
    /// <remarks>
    /// De telling gaat over de regels ná de zoekterm maar vóór het niveaufilter, precies zoals
    /// <c>PortalViews</c> het doet: anders zou een uitgezette chip altijd 0 tonen en kon je niet
    /// zien wat je mist. Het filter wordt hier echt toegepast en niet genegeerd, want de test die
    /// de chip-telling tegen het filter afzet moet iets te vergelijken hebben.
    /// </remarks>
    private (IReadOnlyList<LogRecord> Regels, IReadOnlyDictionary<LogLevel, int> Tellingen)
        Filter(LogQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var opZoekterm = _logregels
            .Where(r => query.Search is null
                || r.Message.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || r.Event.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var zichtbaar = opZoekterm
            .Where(r => query.Levels is null or { Count: 0 } || query.Levels.Contains(r.Level))
            .OrderByDescending(r => r.Timestamp)
            .ThenByDescending(r => r.Id, StringComparer.Ordinal)
            .ToArray();

        return (zichtbaar, Testlogregels.Tellingen(opZoekterm));
    }

    /// <summary>De niveaus die het scherm als "filter aan" moet zien.</summary>
    /// <remarks>
    /// Alle niveaus aan is hetzelfde als geen filter, dus dan <c>null</c>. Anders zou het scherm
    /// "gefilterd" melden terwijl er niets is weggelaten.
    /// </remarks>
    private static IReadOnlySet<LogLevel>? ActieveNiveaus(LogQuery query) =>
        query.Levels is { Count: > 0 } niveaus
        && niveaus.Count < Enum.GetValues<LogLevel>().Length
            ? niveaus.ToHashSet()
            : null;

    /// <summary>
    /// Eén tik van de live tail: alles wat strikt ná de cursor komt.
    /// </summary>
    /// <remarks>
    /// De vergelijking is dezelfde als de gelijkspelclausule in de opslaglaag — later, of
    /// gelijktijdig met een hogere id. Zo levert een fixture die twee keer wordt aangeroepen geen
    /// dubbele regels op, en dat is wat een tail hoort te doen.
    /// </remarks>
    private (IReadOnlyList<LogRecord> Nieuw, LogCursor Cursor) Staart(
        LogQuery query,
        LogCursor since)
    {
        ArgumentNullException.ThrowIfNull(query);

        var nieuw = _logregels
            .Where(r => r.Timestamp > since.Timestamp
                || (r.Timestamp == since.Timestamp
                    && string.CompareOrdinal(r.Id, since.Id) > 0))
            .Where(r => query.Levels is null or { Count: 0 } || query.Levels.Contains(r.Level))
            .OrderBy(r => r.Timestamp)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToArray();

        return (nieuw, nieuw.Length == 0
            ? since
            : new LogCursor(nieuw[^1].Timestamp, nieuw[^1].Id));
    }

    /// <summary>
    /// De runweergave: een mislukte run, een lopende run en een geslaagde run.
    /// </summary>
    /// <remarks>
    /// De lopende run staat er met opzet in. Die rij is de enige die de streepjes en de neutrale
    /// badge rendert, en zonder hem is dat pad op het scherm niet te zien.
    /// </remarks>
    private static AgentRunsView Runweergave(string agentName) =>
        new()
        {
            AgentName = agentName,
            GeneratedAt = Nu,
            Runs =
            [
                new AgentRunRow
                {
                    RunId = "r-9a11",
                    StartedAt = Nu - TimeSpan.FromMinutes(1),
                    Version = "1.4.2",
                    Trigger = TriggerKind.Timer,
                },
                new AgentRunRow
                {
                    RunId = "r-8f3c",
                    StartedAt = Nu - TimeSpan.FromMinutes(5),
                    FinishedAt = Nu - TimeSpan.FromMinutes(4),
                    Duration = TimeSpan.FromSeconds(12),
                    Outcome = RunResult.Failed,
                    ItemsProcessed = 14,
                    ItemsFailed = 2,
                    ErrorType = "System.TimeoutException",
                    ErrorMessage = "De bron antwoordde niet binnen 30 seconden.",
                    Version = "1.4.2",
                    Trigger = TriggerKind.Timer,
                },
                new AgentRunRow
                {
                    RunId = "r-77e0",
                    StartedAt = Nu - TimeSpan.FromMinutes(10),
                    FinishedAt = Nu - TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(9),
                    Duration = TimeSpan.FromSeconds(9),
                    Outcome = RunResult.Ok,
                    ItemsProcessed = 31,
                    ItemsFailed = 0,
                    Version = "1.4.2",
                    Trigger = TriggerKind.Timer,
                },
            ],
            ContinuationToken = null,
        };

    private static CustomerAgentRow[] Klantrijen() =>
    [
        Klantrij("factuur-intake", AgentStatus.Failed, RunResult.Failed, TimeSpan.FromSeconds(14)),
        Klantrij("urensync-mcp", AgentStatus.Degraded, RunResult.Ok, TimeSpan.FromMinutes(41)),
        Klantrij("kosten-collector", AgentStatus.Idle, RunResult.Skipped, TimeSpan.FromSeconds(9)),
        Klantrij("storingsmelder", AgentStatus.Live, RunResult.Ok, TimeSpan.FromSeconds(3)),
    ];

    private static OperatorAgentRow[] Operatorrijen() =>
    [
        Operatorrij("factuur-intake", AgentStatus.Failed, AgentEnvironment.Production),
        Operatorrij("urensync-mcp", AgentStatus.Degraded, AgentEnvironment.Production),
        Operatorrij("proefopstelling", AgentStatus.Live, AgentEnvironment.Acceptance),
    ];

    /// <summary>
    /// Twaalf blokken van twee uur, met één mislukking. Bewust niet leeg: een sparkline zonder
    /// runs rendert niets, en dan meet een zichtbaarheidstest op die kolom niets.
    /// </summary>
    private static IReadOnlyList<Soratus.Portal.Components.Shared.SparkBlock> Sparkline() =>
    [
        .. Enumerable.Range(0, 12).Select(i =>
            new Soratus.Portal.Components.Shared.SparkBlock(Runs: 3, Failed: i == 7 ? 1 : 0))
    ];

    private static CustomerAgentRow Klantrij(
        string naam,
        AgentStatus status,
        RunResult afloop,
        TimeSpan stilte) =>
        new()
        {
            AgentName = naam,
            DisplayType = "Document-intake",
            Status = status,
            Version = "1.4.2",
            StartedAt = Nu - TimeSpan.FromHours(6),
            LastHeartbeatAt = Nu - stilte,
            Silence = stilte,
            LastActivityAt = Nu - stilte,
            Schedule = "*/5 * * * *",
            TriggerKind = TriggerKind.Timer,
            TriggerDetail = "Elke 5 minuten",
            NextRunAt = Nu + TimeSpan.FromMinutes(4),
            Runs24Hours = Sparkline(),
            LastRun = new AgentRunSummary
            {
                RunId = "r-8f3c",
                StartedAt = Nu - TimeSpan.FromMinutes(5),
                Result = afloop,
                Version = "1.4.2",
            },
        };

    private static OperatorAgentRow Operatorrij(
        string naam,
        AgentStatus status,
        AgentEnvironment omgeving) =>
        new()
        {
            AgentName = naam,
            DisplayType = "Document-intake",
            Status = status,
            Version = "1.4.2",
            StartedAt = Nu - TimeSpan.FromHours(6),
            LastHeartbeatAt = Nu - TimeSpan.FromSeconds(11),
            Silence = TimeSpan.FromSeconds(11),
            LastActivityAt = Nu - TimeSpan.FromSeconds(11),
            Lifecycle = AgentLifecycle.Running,
            TriggerKind = TriggerKind.Timer,
            AgentEnvironment = omgeving,
            ContractVersion = AgentRegistration.CurrentContractVersion,
            Runs24Hours = Sparkline(),
        };
}
