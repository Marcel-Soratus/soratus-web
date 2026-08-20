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
/// </remarks>
/// <param name="alleenBuitenProductie">
/// Zet elke klantrij op "wel agents, maar geen enkele in productie": nul productie-agents, één
/// agent op acceptatie. Zo'n rij komt op rang 0 uit — net als een klant zonder agents — en is
/// het enige geval waarin het scherm <c>StatusVisuals.UnknownNonProductionLabel</c> hoort te
/// tonen in plaats van "Geen agents". Zonder deze stand is dat pad niet te renderen, want de
/// standaardrijen hebben altijd productie-agents.
/// </param>
internal sealed class VastePortaalweergaven(bool alleenBuitenProductie = false) : IPortalViews
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
