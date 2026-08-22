using System.Text.Json;
using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry.HostedAgents;

namespace Soratus.Agents.Telemetry.Internal;

/// <summary>De implementatie van <see cref="ISoratusHostedAgent"/>.</summary>
/// <remarks>
/// Eén exemplaar per geherbergde agent, met een eigen <see cref="AgentIdentity"/> en een eigen
/// <see cref="LogRecordFactory"/>. De schrijflaag eronder is wél gedeeld: die is agentloos en één
/// buffer per proces is precies de bedoeling — anders kan de ene agent de andere uit zijn buffer
/// drukken langs een weg die niemand ziet.
/// </remarks>
internal sealed class HostedAgent(
    AgentIdentity identity,
    HostedAgentDeclaration declaration,
    TelemetryWriter writer,
    LogRecordFactory logs,
    TimeProvider clock) : ISoratusHostedAgent
{
    private int _inFlight;

    /// <summary>
    /// Het gemelde moment van de volgende run, als UTC-ticks; nul betekent "niet gemeld".
    /// </summary>
    /// <remarks>
    /// Als <c>long</c> en niet als <c>DateTimeOffset?</c>, omdat <c>volatile</c> niet op dat type
    /// kan en dit veld door de planlus wordt geschreven en door de hartslaglus gelezen. Nul als
    /// "afwezig" kan hier: het is 1 januari van jaar 1, en dat is geen moment waarop iets plant.
    /// </remarks>
    private long _nextRunUtcTicks;

    public AgentIdentity Identity => identity;

    public int RunsInFlight => Volatile.Read(ref _inFlight);

    /// <summary>
    /// Het moment waarop de host op de volgende run wacht, of <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Leeg bij een agent zonder schema, ook als er een moment is gemeld: een <c>nextRunAt</c> naast
    /// een trigger die zegt "op aanroep" is de tegenspraak die
    /// <see cref="HostedAgentDeclaration.Validate"/> weigert, en die hoort dan ook niet via een
    /// omweg in het document te komen.
    /// </remarks>
    internal DateTimeOffset? NextRunAt
    {
        get
        {
            if (identity.Schedule is null)
            {
                return null;
            }

            long ticks = Interlocked.Read(ref _nextRunUtcTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>De aankondiging waaruit deze agent is ontstaan, om er een tweede tegen te toetsen.</summary>
    internal HostedAgentDeclaration Declaration => declaration;

    /// <summary>
    /// De levensfase zoals die op de hartslag mee gaat.
    /// </summary>
    /// <remarks>
    /// <para>Hier zit het hele geval in twee regels. Bij een agent met een eigen lus meldt de
    /// agent zelf dat hij wacht, want van buiten is een leeg wachtinterval niet van een
    /// vastgelopen lus te onderscheiden. Bij een geherbergde agent is dat onderscheid er wél:
    /// de bibliotheek opent en sluit elke aanroep zelf, dus zij weet exact of er werk loopt. De
    /// levensfase is daarmee een waarneming en geen mededeling van de bouwer.</para>
    ///
    /// <para><see cref="AgentLifecycle.IdleWaiting"/> met een verse hartslag levert in
    /// <see cref="AgentStatusCalculator"/> de stand <see cref="AgentStatus.Idle"/> op — rang 1,
    /// dus het tilt de klant in het overzicht nooit naar boven. Dat is precies goed: een dienst
    /// die op een verzoek wacht is niet stuk. Wat het níet betekent staat bij
    /// <see cref="ISoratusHostedAgent"/>.</para>
    /// </remarks>
    internal AgentLifecycle Lifecycle =>
        RunsInFlight > 0 ? AgentLifecycle.Running : AgentLifecycle.IdleWaiting;

    public Task<IAgentRun> StartRunAsync(TriggerKind trigger, CancellationToken cancellationToken = default)
    {
        // Eerst optellen, dan de run openen: andersom bestaat er een ogenblik waarin er een
        // rundocument met 'running' staat terwijl de hartslag nog 'wacht op werk' meldt.
        Interlocked.Increment(ref _inFlight);

        // Bewust geen enkele await, net als bij ISoratusAgent: alleen zonder await komt de
        // AsyncLocal met de runId bij de aanroeper terecht.
        var run = new AgentRun(identity, writer, logs, trigger, clock, onCompleted: Completed);
        run.Begin();
        return Task.FromResult<IAgentRun>(run);
    }

    public async Task RunAsync(
        TriggerKind trigger,
        Func<IAgentRun, CancellationToken, Task> body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);

        await using IAgentRun run = await StartRunAsync(trigger, cancellationToken).ConfigureAwait(false);

        try
        {
            await body(run, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            run.Fail(
                exception.GetType().FullName ?? nameof(OperationCanceledException),
                "Run afgebroken tijdens afsluiten.");
            throw;
        }
        catch (Exception exception)
        {
            run.Fail(exception);
            throw;
        }
    }

    public void ReportNextRun(DateTimeOffset? moment) =>
        Interlocked.Exchange(ref _nextRunUtcTicks, moment?.ToUniversalTime().Ticks ?? 0L);

    public void ReportEvent(LogLevel level, string eventName, string message, object? extra = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        JsonElement? context = ExtraJson.Build(
            state: null,
            payload: extra,
            exception: null,
            category: null,
            eventId: default,
            scopeProvider: null,
            maxLength: logs.MaxExtraLength);

        writer.Enqueue(logs.Create(level, eventName, message, context, clock.GetUtcNow()));
    }

    private void Completed() => Interlocked.Decrement(ref _inFlight);
}
