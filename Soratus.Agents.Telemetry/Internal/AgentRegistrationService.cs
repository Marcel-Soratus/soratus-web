using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Soratus.Agents.Contracts;

namespace Soratus.Agents.Telemetry.Internal;

/// <summary>
/// Publiceert het registratiedocument en houdt de hartslag bij.
/// </summary>
/// <remarks>
/// De hartslag draagt in zijn eentje het verschil tussen live en degraded, dus hij wordt hier
/// geschreven en nooit door de agent zelf. Het interval komt uit
/// <see cref="AgentStatusThresholds.HeartbeatInterval"/>, zodat scherm en bibliotheek per
/// definitie dezelfde grens hanteren.
/// </remarks>
internal sealed class AgentRegistrationService(
    AgentIdentity identity,
    AgentSchedule schedule,
    AgentLifecycleState lifecycle,
    TelemetryWriter writer,
    IHostApplicationLifetime applicationLifetime,
    IOptions<SoratusTelemetryOptions> options,
    ILogger<AgentRegistrationService> logger) : BackgroundService
{
    private readonly SoratusTelemetryOptions _options = options.Value;
    private int _finalWritten;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RegisterShutdownFallback();

        // Het proces meldt zich meteen, niet pas na het eerste interval. Anders staat een net
        // uitgerolde agent een halve minuut op 'unknown'.
        writer.Enqueue(BuildRegistration());

        using var timer = new PeriodicTimer(AgentStatusThresholds.HeartbeatInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                writer.Enqueue(BuildRegistration());
            }
        }
        catch (OperationCanceledException)
        {
            // Normale afsluiting.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        await WriteFinalAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Schrijft het laatste document met <see cref="AgentLifecycle.StoppedCleanly"/>, buiten de
    /// buffer om.
    /// </summary>
    /// <remarks>
    /// Buiten de buffer om, omdat de schrijflus bij afsluiten al kan zijn gestopt. En alleen bij
    /// een nette afsluiting: crasht het proces, dan komt dit nooit langs en blijft de laatste
    /// hartslag staan — precies zoals het contract wil, want dan is de agent degraded en niet
    /// 'netjes gestopt'.
    /// </remarks>
    private async Task WriteFinalAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _finalWritten, 1) != 0)
        {
            return;
        }

        lifecycle.Current = AgentLifecycle.StoppedCleanly;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.ShutdownDrainTimeout);

        try
        {
            await writer.WriteRegistrationDirectAsync(BuildRegistration(), timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "De afsluitende registratie kon niet worden weggeschreven.");
        }
    }

    internal AgentRegistration BuildRegistration() => new()
    {
        Id = identity.AgentName,
        PartitionKey = identity.AgentName,
        CustomerId = identity.CustomerId,
        AgentName = identity.AgentName,
        DisplayType = identity.DisplayType,
        Version = identity.Version,
        StartedAt = identity.StartedAt,
        LastHeartbeatAt = DateTimeOffset.UtcNow,
        Lifecycle = lifecycle.Current,
        Schedule = schedule.Raw,
        TriggerKind = identity.TriggerKind,
        TriggerDetail = identity.TriggerDetail,
        NextRunAt = schedule.GetNextOccurrence(DateTimeOffset.UtcNow),
        Environment = identity.Environment,
    };

    /// <summary>
    /// Haakt aan op <c>ApplicationStopped</c> als vangnet, voor het geval de host de dienst niet
    /// langs <see cref="StopAsync"/> voert.
    /// </summary>
    internal void RegisterShutdownFallback() =>
        applicationLifetime.ApplicationStopped.Register(() =>
        {
            if (Volatile.Read(ref _finalWritten) != 0)
            {
                return;
            }

            // Op dit punt is er geen asynchrone context meer om op te wachten; de korte
            // begrensde wachttijd is het minst slechte alternatief.
            WriteFinalAsync(CancellationToken.None).GetAwaiter().GetResult();
        });
}
