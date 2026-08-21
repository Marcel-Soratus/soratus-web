using Soratus.Agents.Contracts;

namespace Soratus.Agents.Telemetry.Internal;

/// <summary>De implementatie van <see cref="ISoratusAgent"/>.</summary>
internal sealed class SoratusAgent(
    AgentIdentity identity,
    TelemetryWriter writer,
    LogRecordFactory logs,
    AgentSchedule schedule,
    AgentLifecycleState lifecycle,
    TimeProvider clock) : ISoratusAgent
{
    public AgentIdentity Identity => identity;

    public string? CurrentRunId => RunScope.Current?.RunId;

    public DateTimeOffset? NextRunAt => schedule.GetNextOccurrence(clock.GetUtcNow());

    public Task<IAgentRun> StartRunAsync(TriggerKind trigger, CancellationToken cancellationToken = default)
    {
        // Bewust geen enkele await: alleen dan komt de AsyncLocal met de runId bij de aanroeper
        // terecht. Wegschrijven gebeurt toch gebufferd, dus er valt hier niets af te wachten.
        var run = new AgentRun(identity, writer, logs, trigger, clock);
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
            // Afgebroken door een uitrol of een herstart. Dat is geen geslaagde run, en het
            // portaal mag hem niet als 'ok' tonen — maar het is ook geen bug in de agent, dus
            // het foutbericht zegt precies wat er gebeurde.
            run.Fail(exception.GetType().FullName ?? nameof(OperationCanceledException), "Run afgebroken tijdens afsluiten.");
            throw;
        }
        catch (Exception exception)
        {
            run.Fail(exception);
            throw;
        }
    }

    public void ReportLifecycle(AgentLifecycle value) => lifecycle.Current = value;
}
