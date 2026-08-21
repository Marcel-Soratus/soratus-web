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

    /// <summary>
    /// Of deze agent zich heeft aangemeld. Bestaat om te meten.
    /// </summary>
    /// <remarks>
    /// De invariant die hieronder wordt afgedwongen: als <see cref="StartAsync"/> terugkomt, is dit
    /// waar. Dat is deterministisch te meten, en het gevolg — "staat de registratie in de opslag" —
    /// is dat niet: dat hangt af van de planner. Zie <c>TelemetryWriter.DrainPathArmed</c>, waar
    /// dezelfde afweging staat en waar dezelfde fout zat.
    /// </remarks>
    internal bool Announced { get; private set; }

    /// <summary>
    /// Meldt de agent aan en zet het vangnet voor het afsluiten, vóór <c>StartAsync</c> terugkomt.
    /// </summary>
    /// <remarks>
    /// <para><strong>Dit stond in <see cref="ExecuteAsync"/>, en dat maakte een kortlevende agent
    /// onzichtbaar.</strong> Het lijf van <c>ExecuteAsync</c> van een <c>BackgroundService</c> is
    /// niet gegarandeerd gelopen als <c>StartAsync</c> terugkomt. Een agent die start, werkt en
    /// binnen dat venster afsluit, meldde zich dus helemaal niet — en dan bestaat hij niet in het
    /// portaal. Dat is erger dan een ontbrekende logregel: er is geen rij om iets aan te zien.</para>
    ///
    /// <para>Het vangnet voor het afsluiten hing aan hetzelfde lijf en ontbrak dus in precies dat
    /// geval waarin het nodig was.</para>
    ///
    /// <para>Dezelfde vorm en dezelfde reparatie als in <c>TelemetryWriter</c> en
    /// <c>HostedAgentsRegistrationService</c>. Het is nu drie keer opgetreden in deze bibliotheek:
    /// wat vóór het einde van <c>StartAsync</c> moet zijn gebeurd, hoort in <c>StartAsync</c>.</para>
    /// </remarks>
    /// <param name="cancellationToken">Annuleringstoken van de host.</param>
    /// <returns>De taak van het opstarten.</returns>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        RegisterShutdownFallback();

        // Het proces meldt zich meteen, niet pas na het eerste interval. Anders staat een net
        // uitgerolde agent een halve minuut op 'unknown'.
        writer.Enqueue(BuildRegistration());
        Announced = true;

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
