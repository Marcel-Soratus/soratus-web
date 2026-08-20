using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry.Scheduling;

namespace Soratus.Agents.Telemetry.Internal;

/// <summary>
/// Laat een <see cref="IScheduledAgent"/> draaien op de cron-expressie uit de configuratie.
/// </summary>
/// <remarks>
/// Deze dienst berekent de volgende run met exact hetzelfde <see cref="AgentSchedule"/> dat het
/// registratiedocument gebruikt. Daarmee is de <c>nextRunAt</c> op het scherm geen belofte maar
/// een waarneming.
/// </remarks>
internal sealed class ScheduledAgentService(
    ISoratusAgent agent,
    AgentSchedule schedule,
    IServiceScopeFactory scopeFactory,
    ILogger<ScheduledAgentService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!schedule.HasSchedule)
        {
            logger.LogInformation(
                "Er is een IScheduledAgent geregistreerd maar geen SORATUS_AGENT__SCHEDULE; deze agent wacht op een externe trigger.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            DateTimeOffset? next = schedule.GetNextOccurrence(DateTimeOffset.UtcNow);
            if (next is null)
            {
                logger.LogWarning("De cron-expressie levert geen volgend moment meer op; de planner stopt.");
                return;
            }

            if (!await WaitUntilAsync(next.Value, stoppingToken).ConfigureAwait(false))
            {
                return;
            }

            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Wacht in stukjes van hoogstens een minuut. Eén lange <c>Task.Delay</c> zou een verzette
    /// systeemklok of een geschorst proces niet overleven.
    /// </summary>
    private static async Task<bool> WaitUntilAsync(DateTimeOffset moment, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan remaining = moment - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return true;
            }

            TimeSpan slice = remaining > TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : remaining;

            try
            {
                await Task.Delay(slice, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return false;
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        // Een scope per run, zodat de agent scoped afhankelijkheden mag gebruiken en er niets
        // van de vorige run blijft hangen.
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var scheduled = scope.ServiceProvider.GetRequiredService<IScheduledAgent>();

        try
        {
            await agent.RunAsync(TriggerKind.Timer, scheduled.ExecuteRunAsync, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Afsluiten; de run is al als afgebroken vastgelegd.
        }
        catch (Exception exception)
        {
            // De run staat al op 'failed' met errorType en errorMessage. Hier gaat het alleen
            // nog om de ontwikkelaarskant, en die hoort in de gewone log van de host.
            logger.LogError(exception, "De geplande run is mislukt.");
        }
    }
}
