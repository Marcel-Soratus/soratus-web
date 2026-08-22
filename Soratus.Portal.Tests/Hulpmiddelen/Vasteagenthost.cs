using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry;
using Soratus.Agents.Telemetry.HostedAgents;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// Een opvangende <see cref="ISoratusHostedAgents"/>: hij legt vast wat er wordt aangekondigd,
/// gemeld en gedraaid.
/// </summary>
/// <remarks>
/// <para>Een dubbel en geen echte registry, want wat hier gemeten moet worden is wat de
/// <em>achtergronddiensten van het portaal</em> aan de bibliotheek doorgeven: welke aankondiging,
/// welk moment als volgende run, en hoeveel items per run. De bibliotheek zelf is aan de andere kant
/// gemeten, in <c>Soratus.Agents.Telemetry.Tests</c>, tegen een opvangende sink.</para>
///
/// <para>De dubbel rekent zelf niets uit. Dat is het punt van punt 41, gat 2: een dubbel die de
/// beslissing van de productiecode nabouwt, dekt de afwezigheid ervan.</para>
/// </remarks>
internal sealed class Vasteagenthost : ISoratusHostedAgents
{
    private readonly Dictionary<string, Vasteagent> _agents = new(StringComparer.Ordinal);

    private readonly TaskCompletionSource _aangekondigd =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>De aankondigingen, in de volgorde waarin ze langskwamen.</summary>
    public List<HostedAgentDeclaration> Aankondigingen { get; } = [];

    /// <summary>
    /// Wordt voltooid zodra er voor het eerst een agent is aangekondigd.
    /// </summary>
    /// <remarks>
    /// <para><strong>Dit bestaat door een meting en niet door voorzichtigheid.</strong> Het lijf van
    /// <c>ExecuteAsync</c> van een <c>BackgroundService</c> is niet gelopen op het moment dat
    /// <c>StartAsync</c> terugkomt: geteld direct na <c>StartAsync</c> nul aankondigingen, en een
    /// fractie later één. Dat is dezelfde eigenschap die deze bibliotheek al drie keer heeft
    /// opgeleverd, nu in de lus van het portaal.</para>
    ///
    /// <para>Wat er wél op <c>StartAsync</c> mag rusten, rust dat ook: de <em>registratie</em> van
    /// beide agents komt van de aankondigingsbron die bij het opstarten is geregistreerd, en niet van
    /// deze lus. Wat hier op zich laat wachten is alleen het moment van de volgende run.</para>
    /// </remarks>
    public Task EersteAankondiging => _aangekondigd.Task;

    /// <inheritdoc />
    public IReadOnlyList<ISoratusHostedAgent> All => [.. _agents.Values];

    /// <summary>De agent met deze naam, of <c>null</c>.</summary>
    /// <param name="agentName">De technische naam.</param>
    /// <returns>De agent.</returns>
    public Vasteagent? Agent(string agentName) => _agents.GetValueOrDefault(agentName);

    /// <inheritdoc />
    public ISoratusHostedAgent? Find(string agentName) => Agent(agentName);

    /// <inheritdoc />
    public ISoratusHostedAgent GetOrAdd(HostedAgentDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        declaration.Validate();

        Aankondigingen.Add(declaration);
        _aangekondigd.TrySetResult();

        if (!_agents.TryGetValue(declaration.AgentName, out var agent))
        {
            agent = new Vasteagent(declaration);
            _agents[declaration.AgentName] = agent;
        }

        return agent;
    }
}

/// <summary>Eén opvangende geherbergde agent.</summary>
/// <param name="declaration">De aankondiging waaruit hij is ontstaan.</param>
internal sealed class Vasteagent(HostedAgentDeclaration declaration) : ISoratusHostedAgent
{
    private readonly TaskCompletionSource _gemeld =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource _gedraaid =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>De aankondiging.</summary>
    public HostedAgentDeclaration Declaration => declaration;

    /// <summary>Elk moment dat als volgende run is gemeld, in volgorde.</summary>
    public List<DateTimeOffset?> GemeldeVolgendeRuns { get; } = [];

    /// <summary>De runs die zijn geopend, met hun uitkomst.</summary>
    public List<Vasterun> Runs { get; } = [];

    /// <summary>Wordt voltooid zodra er voor het eerst een volgende run is gemeld.</summary>
    public Task EersteMelding => _gemeld.Task;

    /// <summary>Wordt voltooid zodra de eerste run is afgerond, geslaagd of niet.</summary>
    /// <remarks>
    /// Bestaat zodat een test de lus van een achtergronddienst kan afwachten in plaats van hem te
    /// pollen. Een test die polt is een test die op een trage machine anders meet dan op een snelle.
    /// </remarks>
    public Task EersteRun => _gedraaid.Task;

    /// <inheritdoc />
    public AgentIdentity Identity => new()
    {
        CustomerId = "soratus",
        AgentName = declaration.AgentName,
        DisplayType = declaration.DisplayType ?? declaration.AgentName,
        Version = "0.0.0",
        Environment = AgentEnvironment.Production,
        TriggerKind = declaration.Trigger,
        TriggerDetail = declaration.TriggerDetail,
        Schedule = declaration.Schedule?.Expression,
        ScheduleTimeZone = declaration.Schedule?.TimeZone ?? TimeZoneInfo.Utc,
        StartedAt = DateTimeOffset.UnixEpoch,
    };

    /// <inheritdoc />
    public int RunsInFlight => Runs.Count(run => !run.Afgerond);

    /// <inheritdoc />
    public void ReportNextRun(DateTimeOffset? moment)
    {
        GemeldeVolgendeRuns.Add(moment);
        _gemeld.TrySetResult();
    }

    /// <inheritdoc />
    public Task<IAgentRun> StartRunAsync(TriggerKind trigger, CancellationToken cancellationToken = default)
    {
        var run = new Vasterun(trigger);
        Runs.Add(run);
        return Task.FromResult<IAgentRun>(run);
    }

    /// <inheritdoc />
    public async Task RunAsync(
        TriggerKind trigger,
        Func<IAgentRun, CancellationToken, Task> body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);

        var run = new Vasterun(trigger);
        Runs.Add(run);

        try
        {
            await body(run, cancellationToken);
        }
        catch (Exception exception)
        {
            // Wat de echte bibliotheek doet: de run mislukt en de uitzondering gaat door. Dat de
            // uitzondering doorgaat is hier de eigenschap die ertoe doet — de aanroeper hoort hem nog
            // te zien.
            run.Fail(exception);
            run.Afgerond = true;
            _gedraaid.TrySetResult();
            throw;
        }

        run.Afgerond = true;
        _gedraaid.TrySetResult();
    }

    /// <inheritdoc />
    public void ReportEvent(LogLevel level, string eventName, string message, object? extra = null)
    {
        // Niet gemeten in deze lane: de logroutering is aan de bibliotheekkant getest.
    }
}

/// <summary>Eén opvangende run.</summary>
/// <param name="trigger">Waardoor deze run is gestart.</param>
internal sealed class Vasterun(TriggerKind trigger) : IAgentRun
{
    /// <inheritdoc />
    public string RunId { get; } = "r-test";

    /// <inheritdoc />
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UnixEpoch;

    /// <inheritdoc />
    public TriggerKind Trigger => trigger;

    /// <inheritdoc />
    public int ItemsProcessed { get; private set; }

    /// <inheritdoc />
    public int ItemsFailed { get; private set; }

    /// <summary>Of deze run is afgesloten.</summary>
    public bool Afgerond { get; set; }

    /// <summary>De uitzondering waarop deze run is mislukt, of <c>null</c>.</summary>
    public Exception? Mislukking { get; private set; }

    /// <inheritdoc />
    public void Processed(int count = 1) => ItemsProcessed += count;

    /// <inheritdoc />
    public void FailedItems(int count = 1) => ItemsFailed += count;

    /// <inheritdoc />
    public void MarkRolledBack()
    {
        // Niet gemeten in deze lane.
    }

    /// <inheritdoc />
    public void Fail(Exception exception) => Mislukking = exception;

    /// <inheritdoc />
    public void Fail(string errorType, string errorMessage) =>
        Mislukking = new InvalidOperationException($"{errorType}: {errorMessage}");

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Afgerond = true;
        return ValueTask.CompletedTask;
    }
}
