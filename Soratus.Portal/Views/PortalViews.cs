using Soratus.Portal.Components.Shared;
using Soratus.Agents.Contracts;
using Soratus.Portal.Data;
using Soratus.Portal.Security;

namespace Soratus.Portal.Views;

/// <summary>
/// Bouwt de viewmodels op uit de store.
/// </summary>
/// <remarks>
/// Alles wat op het scherm een getal is, ontstaat hier — één keer, uit één lijst. De KPI-rij van
/// het overzicht wordt niet apart geteld maar opgeteld uit precies de rijen die eronder komen te
/// staan, en de statusverdeling van een klant komt uit dezelfde agentlijst als zijn ernstigste
/// status. Zo kunnen twee getallen op hetzelfde scherm elkaar niet tegenspreken, en twee schermen
/// evenmin.
///
/// De klok komt uit <see cref="TimeProvider"/> en wordt één keer per opbouw uitgelezen. Zou elke
/// rij zijn eigen <c>DateTimeOffset.UtcNow</c> nemen, dan kan de agent bovenaan de lijst op een
/// ander moment beoordeeld zijn dan de agent onderaan — meestal onzichtbaar, en precies op de
/// drempel van twee minuten zichtbaar op de verkeerde manier.
/// </remarks>
internal sealed class PortalViews(
    IAgentTelemetryStore store,
    ICustomerDirectory directory,
    TimeProvider timeProvider) : IPortalViews
{
    /// <inheritdoc />
    public async Task<OperatorOverviewView> BuildOverviewAsync(
        OperatorScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var now = timeProvider.GetUtcNow();
        var todayStartedAt = PortalTimeZone.StartOfToday(now);
        var last24HoursStartedAt = now.AddHours(-24);

        var results = await store
            .GetOverviewAsync(scope, todayStartedAt, last24HoursStartedAt, cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<OperatorCustomerRow>(directory.All.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var result in results)
        {
            seen.Add(result.Scope.CustomerId);
            rows.Add(ToRow(result, directory.Find(result.Scope.CustomerId), now));
        }

        // Klanten die wel zijn ingericht maar geen opslag hebben, komen niet uit de store — er valt
        // niets te lezen. Ze horen wél op het scherm: een klant die van het overzicht verdwijnt
        // omdat zijn opslag ontbreekt is precies het geval dat je wil zien.
        foreach (var record in directory.All)
        {
            if (seen.Contains(record.Id))
            {
                continue;
            }

            rows.Add(NotProvisionedRow(record));
        }

        var sorted = CustomerSeverity.Sort(rows, row => row.Severity).ToArray();

        return new OperatorOverviewView
        {
            GeneratedAt = now,
            Kpis = BuildKpis(sorted, todayStartedAt),
            Customers = sorted,
        };
    }

    /// <inheritdoc />
    public async Task<CustomerAgentsView> BuildAgentsAsync(
        CustomerScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var now = timeProvider.GetUtcNow();
        var window = HistogramWindow.Last24Hours(now);

        var snapshots = await store.GetAgentsAsync(scope, cancellationToken).ConfigureAwait(false);
        var histogram = await store.GetRunHistogramAsync(scope, window, cancellationToken).ConfigureAwait(false);

        // Hier valt de omgevingsfilter. De klantweergave toont uitsluitend productie: een
        // acceptatie-agent die omvalt is geen storing voor de klant. Het filteren gebeurt bij het
        // projecteren en niet in de query, zodat de operator dezelfde store-aanroep kan gebruiken
        // en wél alles ziet.
        var rows = snapshots
            .Where(snapshot => snapshot.Registration.Environment == AgentEnvironment.Production)
            .Select(snapshot => ToCustomerRow(snapshot, now, Blocks(histogram, snapshot.AgentName, window)))
            .ToArray();

        var sorted = SortBySeverity(rows, row => (row.Status, row.LastActivityAt));

        return new CustomerAgentsView
        {
            CustomerId = scope.CustomerId,
            DisplayName = scope.DisplayName,
            Environment = scope.Environment,
            GeneratedAt = now,
            Agents = sorted,
            Statuses = AgentStatusBreakdown.FromStatuses(sorted.Select(row => row.Status)),
        };
    }

    /// <inheritdoc />
    public async Task<OperatorCustomerAgentsView> BuildAgentsAsync(
        OperatorCustomerScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var now = timeProvider.GetUtcNow();
        var window = HistogramWindow.Last24Hours(now);

        var snapshots = await store.GetAgentsAsync(scope.Customer, cancellationToken).ConfigureAwait(false);
        var histogram = await store.GetRunHistogramAsync(scope.Customer, window, cancellationToken)
            .ConfigureAwait(false);

        var rows = snapshots
            .Select(snapshot => ToOperatorRow(snapshot, now, Blocks(histogram, snapshot.AgentName, window)))
            .ToArray();
        var sorted = SortBySeverity(rows, row => (row.Status, row.LastActivityAt));

        return new OperatorCustomerAgentsView
        {
            CustomerId = scope.CustomerId,
            DisplayName = scope.DisplayName,
            Environment = scope.Environment,
            EnvironmentDetail = scope.EnvironmentDetail,
            IsInternal = scope.IsInternal,
            GeneratedAt = now,
            Agents = sorted,
            Statuses = AgentStatusBreakdown.FromStatuses(sorted.Select(row => row.Status)),
            ProductionStatuses = AgentStatusBreakdown.FromStatuses(
                sorted
                    .Where(row => row.AgentEnvironment == AgentEnvironment.Production)
                    .Select(row => row.Status)),
        };
    }

    /// <inheritdoc />
    public async Task<CustomerAgentDetailView?> BuildAgentDetailAsync(
        CustomerScope scope,
        string agentName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var now = timeProvider.GetUtcNow();
        var snapshot = await store.GetAgentAsync(scope, agentName, cancellationToken).ConfigureAwait(false);

        // Niet gevonden, van een andere klant, of niet in productie: alle drie hetzelfde antwoord.
        // Een klant hoort niet te kunnen afleiden dat er een acceptatie-agent met deze naam bestaat.
        if (snapshot is null || snapshot.Registration.Environment != AgentEnvironment.Production)
        {
            return null;
        }

        var window = HistogramWindow.Last24Hours(now);
        var histogram = await store.GetRunHistogramAsync(scope, window, cancellationToken).ConfigureAwait(false);

        return new CustomerAgentDetailView
        {
            CustomerId = scope.CustomerId,
            CustomerDisplayName = scope.DisplayName,
            GeneratedAt = now,
            Agent = ToCustomerRow(snapshot, now, Blocks(histogram, snapshot.AgentName, window)),
        };
    }

    /// <inheritdoc />
    public async Task<OperatorAgentDetailView?> BuildAgentDetailAsync(
        OperatorCustomerScope scope,
        string agentName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var now = timeProvider.GetUtcNow();
        var snapshot = await store.GetAgentAsync(scope.Customer, agentName, cancellationToken)
            .ConfigureAwait(false);

        if (snapshot is null)
        {
            return null;
        }

        var telemetry = scope.Customer.Telemetry;
        var window = HistogramWindow.Last24Hours(now);
        var histogram = await store.GetRunHistogramAsync(scope.Customer, window, cancellationToken)
            .ConfigureAwait(false);

        return new OperatorAgentDetailView
        {
            CustomerId = scope.CustomerId,
            CustomerDisplayName = scope.DisplayName,
            Environment = scope.Environment,
            EnvironmentDetail = scope.EnvironmentDetail,
            GeneratedAt = now,
            Agent = ToOperatorRow(snapshot, now, Blocks(histogram, snapshot.AgentName, window)),
            TelemetryLocation = $"{telemetry.AccountEndpoint} · {telemetry.Database}",
        };
    }

    /// <summary>
    /// Telt de KPI-rij op uit precies de rijen die eronder komen te staan.
    /// </summary>
    private static OperatorOverviewKpis BuildKpis(
        IReadOnlyList<OperatorCustomerRow> rows,
        DateTimeOffset todayStartedAt)
    {
        var today = RunTally.Empty;
        var last24Hours = RunTally.Empty;
        var onboarding = 0;
        var nonProductionOnly = 0;
        var unavailable = 0;

        foreach (var row in rows)
        {
            if (!row.IsAvailable)
            {
                unavailable++;
                continue;
            }

            if (row.HasOnlyNonProductionAgents)
            {
                // Wel agents, alleen niet in productie. Dat is geen onboarding — die klant is bezig.
                nonProductionOnly++;
            }
            else if (row.AgentCount == 0)
            {
                onboarding++;
            }

            today = Add(today, row.Today);
            last24Hours = Add(last24Hours, row.Last24Hours);
        }

        return new OperatorOverviewKpis
        {
            CustomerCount = rows.Count,
            OnboardingCount = onboarding,
            NonProductionOnlyCount = nonProductionOnly,
            UnavailableCount = unavailable,
            Statuses = AgentStatusBreakdown.Combine(rows.Select(row => row.Statuses)),
            NonProductionStatuses = AgentStatusBreakdown.Combine(
                rows.Select(row => row.NonProductionStatuses)),
            TodayStartedAt = todayStartedAt,
            Today = today,
            Last24Hours = last24Hours,
        };
    }

    private static RunTally Add(RunTally left, RunTally right) => new(
        left.Ok + right.Ok,
        left.Failed + right.Failed,
        left.Skipped + right.Skipped,
        left.Running + right.Running);

    private static OperatorCustomerRow ToRow(
        CustomerTelemetry telemetry,
        CustomerRecord? record,
        DateTimeOffset now)
    {
        // De scheiding valt hier, en alleen hier. Ernst en statusbalk gaan over productie; wat
        // daarbuiten draait wordt apart geteld en telt niet mee in de sortering. Zie
        // docs/agent-portal/fase-0-afwijkingen.md §9.
        var production = new List<AgentSeverity>();
        var nonProduction = new List<AgentSeverity>();

        foreach (var agent in telemetry.Agents)
        {
            var target = agent.Registration.Environment == AgentEnvironment.Production
                ? production
                : nonProduction;

            target.Add(agent.Severity(now));
        }

        return new OperatorCustomerRow
        {
            CustomerId = telemetry.Scope.CustomerId,
            DisplayName = telemetry.Scope.DisplayName,
            IsInternal = telemetry.Scope.IsInternal,
            Environment = telemetry.Scope.Environment,
            EnvironmentDetail = record?.EnvironmentDetail,
            Statuses = AgentStatusBreakdown.FromStatuses(production.Select(severity => severity.Status)),
            NonProductionStatuses = AgentStatusBreakdown.FromStatuses(
                nonProduction.Select(severity => severity.Status)),
            Severity = CustomerSeverity.FromAgents(production),
            Today = telemetry.Today,
            Last24Hours = telemetry.Last24Hours,
            Unavailable = telemetry.Unavailable,
        };
    }

    /// <summary>
    /// De rij voor een klant die wel is ingericht maar geen opslag heeft.
    /// </summary>
    private static OperatorCustomerRow NotProvisionedRow(CustomerRecord record) => new()
    {
        CustomerId = record.Id,
        DisplayName = record.Name,
        IsInternal = record.IsInternal,
        Environment = record.Environment,
        EnvironmentDetail = record.EnvironmentDetail,
        Statuses = AgentStatusBreakdown.Empty,
        NonProductionStatuses = AgentStatusBreakdown.Empty,
        Severity = CustomerSeverity.None,
        Today = RunTally.Empty,
        Last24Hours = RunTally.Empty,
        Unavailable = new TelemetryUnavailable(
            "Voor deze klant is nog geen telemetrie-opslag ingericht.",
            "Telemetry:AccountEndpoint ontbreekt voor deze klant."),
    };

    /// <summary>
    /// De sparkline-blokken van één agent, of twaalf lege als hij niets deed.
    /// </summary>
    /// <remarks>
    /// De store laat agents zonder runs weg — die staan niet in de aggregatie. Hier worden ze
    /// aangevuld, zodat elke rij altijd evenveel blokken heeft en de pagina geen lege lijst hoeft
    /// af te vangen.
    /// </remarks>
    private static IReadOnlyList<SparkBlock> Blocks(
        IReadOnlyDictionary<string, IReadOnlyList<RunBucket>> histogram,
        string agentName,
        HistogramWindow window)
    {
        if (!histogram.TryGetValue(agentName, out var buckets))
        {
            return new SparkBlock[window.BlockCount];
        }

        var blocks = new SparkBlock[window.BlockCount];

        for (var index = 0; index < blocks.Length && index < buckets.Count; index++)
        {
            blocks[index] = new SparkBlock(buckets[index].Runs, buckets[index].Failed);
        }

        return blocks;
    }

    private static CustomerAgentRow ToCustomerRow(
        AgentSnapshot snapshot,
        DateTimeOffset now,
        IReadOnlyList<SparkBlock> runs24Hours)
    {
        var registration = snapshot.Registration;
        var severity = snapshot.Severity(now);

        return new CustomerAgentRow
        {
            AgentName = registration.AgentName,
            DisplayType = registration.DisplayType,
            Status = severity.Status,
            Version = registration.Version,
            StartedAt = registration.StartedAt,
            LastHeartbeatAt = registration.LastHeartbeatAt,
            Silence = AgentStatusCalculator.SilenceFor(registration, now),
            LastActivityAt = severity.LastActivityAt,
            Schedule = registration.Schedule,
            TriggerKind = registration.TriggerKind,
            TriggerDetail = registration.TriggerDetail,
            NextRunAt = registration.NextRunAt,
            LastRun = AgentRunSummary.From(snapshot.LastCompletedRun),
            Runs24Hours = runs24Hours,
        };
    }

    private static OperatorAgentRow ToOperatorRow(
        AgentSnapshot snapshot,
        DateTimeOffset now,
        IReadOnlyList<SparkBlock> runs24Hours)
    {
        var registration = snapshot.Registration;
        var severity = snapshot.Severity(now);

        return new OperatorAgentRow
        {
            AgentName = registration.AgentName,
            DisplayType = registration.DisplayType,
            Status = severity.Status,
            Version = registration.Version,
            StartedAt = registration.StartedAt,
            LastHeartbeatAt = registration.LastHeartbeatAt,
            Silence = AgentStatusCalculator.SilenceFor(registration, now),
            LastActivityAt = severity.LastActivityAt,
            Lifecycle = registration.Lifecycle,
            Schedule = registration.Schedule,
            TriggerKind = registration.TriggerKind,
            TriggerDetail = registration.TriggerDetail,
            NextRunAt = registration.NextRunAt,
            LastRun = AgentRunSummary.From(snapshot.LastCompletedRun),
            Runs24Hours = runs24Hours,
            AgentEnvironment = registration.Environment,
            ContractVersion = registration.ContractVersion,
        };
    }

    /// <summary>
    /// Sorteert agentrijen op ernst en dan op recentheid.
    /// </summary>
    /// <remarks>
    /// Hergebruikt bewust <see cref="CustomerSeverity.SeverityFirst"/>: de regel "ernstigste eerst,
    /// bij gelijke ernst het meest recent eerst, nooit-actief achteraan" hoort voor agents hetzelfde
    /// te zijn als voor klanten. Een tweede comparer met dezelfde bedoeling is een tweede comparer
    /// die kan gaan afwijken.
    /// </remarks>
    private static IReadOnlyList<T> SortBySeverity<T>(
        IReadOnlyList<T> rows,
        Func<T, (AgentStatus Status, DateTimeOffset? LastActivityAt)> key) =>
        [
            .. rows.OrderBy(
                row =>
                {
                    var (status, lastActivity) = key(row);
                    return new CustomerSeverity(status, lastActivity, 1);
                },
                CustomerSeverity.SeverityFirst)
        ];
}
