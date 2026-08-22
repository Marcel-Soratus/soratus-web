using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Soratus.Agents.Contracts;

namespace Soratus.Agents.Telemetry.Internal;

/// <summary>
/// Publiceert het registratiedocument van elke geherbergde agent en houdt hun hartslag bij.
/// </summary>
/// <remarks>
/// <para><strong>Eén dienst voor alle agents, en dat is de kern van dit geval.</strong> Bij een
/// agent met een eigen proces komt de hartslag uit datzelfde proces, dus zegt hij iets over die
/// ene agent. Bij drie diensten in één webapplicatie is er maar één proces, dus is er ook maar
/// één ding om over te kloppen: de host. Deze dienst klopt namens elke agent die hij herbergt, en
/// alle drie de hartslagen zijn per constructie even oud.</para>
///
/// <para><strong>Wat een verse hartslag hier bewijst, en wat niet.</strong> Hij bewijst dat het
/// proces leeft en dat de weg naar de opslag open is. Hij bewijst niet dat een van deze diensten
/// werkt: een endpoint dat niemand aanroept, of dat achter een kapotte inlog staat, klopt even
/// trouw door. <see cref="AgentStatus.Idle"/> betekent hier dus letterlijk "de host leeft en er
/// loopt geen aanroep", en het enige bewijs dat een van deze agents zijn werk doet is zijn
/// laatste geslaagde <see cref="RunRecord"/>. Die twee zijn in de gegevens te onderscheiden aan
/// <see cref="AgentRegistration.TriggerKind"/> naast een leeg
/// <see cref="AgentRegistration.Schedule"/> en een leeg <see cref="AgentRegistration.NextRunAt"/>:
/// dat drietal is het handschrift van een agent op aanvraag, en bij zo'n agent zegt de status
/// niets over het werk.</para>
///
/// <para><strong>De afhankelijkheid die buiten de code ligt.</strong> Deze hartslag bestaat alleen
/// zolang het proces geladen blijft. Op een Azure App Service is dat een instelling — Always On —
/// en niet een eigenschap van deze code. Staat die uit, dan laadt het platform de app na ongeveer
/// twintig minuten zonder verkeer uit; de hartslag stopt, en het portaal meldt na
/// <see cref="AgentStatusThresholds.Degraded"/> een storing terwijl er niets aan de hand is. Een
/// instelling buiten de code draait dan de betekenis van de code om.</para>
///
/// <para>Daar is in code niets tegen te doen — een uitgeladen proces kan niets meer melden — dus
/// wat we doen is het aflééspaar in de gegevens leggen, en niet een veld verzinnen dat het
/// contract niet heeft:</para>
/// <list type="number">
///   <item><description>
///     <see cref="AgentRegistration.StartedAt"/> is het moment waarop dít proces startte, gelijk
///     op alle geherbergde agents. Een <c>startedAt</c> die na elke stilte opschuift is het
///     handschrift van een uitgeladen host; een <c>startedAt</c> die blijft staan terwijl de
///     hartslag stokt is een echt probleem in het proces. Dat zijn twee verschillende diagnoses
///     uit één veld dat al bestond.
///   </description></item>
///   <item><description>
///     Bij elke start schrijft deze dienst per agent één <c>host.started</c>-regel. Eén zo'n regel
///     per uitrol is normaal. Staat hij elke twintig minuten opnieuw in de logtabel, dan wordt het
///     proces telkens uitgeladen, en dat is precies wat er te zien is als Always On uit staat. De
///     uitleg staat operator-only in <c>extra</c> mee, want de lezer die dit patroon aantreft
///     zoekt op dat moment de betekenis en niet de documentatie.
///   </description></item>
/// </list>
/// </remarks>
internal sealed class HostedAgentsRegistrationService(
    HostedAgentRegistry registry,
    TelemetryWriter writer,
    IHostApplicationLifetime applicationLifetime,
    IOptions<SoratusTelemetryOptions> options,
    TimeProvider clock,
    ILogger<HostedAgentsRegistrationService> logger) : BackgroundService
{
    /// <summary>
    /// De uitleg die operator-only bij <c>host.started</c> mee gaat.
    /// </summary>
    /// <remarks>
    /// Als constante, zodat een test hem kan vastpinnen. Een uitleg die in de code staat maar niet
    /// in de gegevens belandt is geen uitleg voor de lezer die hem nodig heeft.
    /// </remarks>
    internal const string StartExplanation =
        "Deze regel hoort één keer per uitrol te staan. Staat hij elke twintig minuten opnieuw, dan " +
        "wordt dit proces telkens uitgeladen en stopt de hartslag daartussen; op een Azure App " +
        "Service is dat de instelling Always On.";

    /// <summary>De gebeurtenisnaam van de startregel.</summary>
    internal const string StartEvent = "host.started";

    private readonly SoratusTelemetryOptions _options = options.Value;
    private readonly HashSet<string> _announced = new(StringComparer.Ordinal);
    private int _finalWritten;
    private volatile bool _stopping;

    /// <summary>
    /// Meldt zich meteen, en nog een keer zodra de host helemaal staat.
    /// </summary>
    /// <param name="cancellationToken">Afbreken tijdens het opstarten.</param>
    /// <remarks>
    /// <para><strong>Waarom dit hier staat en niet in <see cref="ExecuteAsync"/>.</strong> Gemeten:
    /// het lijf van <c>ExecuteAsync</c> van een <c>BackgroundService</c> is niet gegarandeerd
    /// gelopen op het moment dat <c>StartAsync</c> terugkomt. Zes opstarts van dezelfde host, elke
    /// keer geteld direct na <c>StartAsync</c>: nul agents bekend, zes van de zes. De host wacht
    /// wél op <c>StartAsync</c>, dus alleen wat híer staat is af. Stond de eerste melding in
    /// <c>ExecuteAsync</c>, dan hangt het van de planner af of een net gestarte dienst zich meldt
    /// vóór het eerste verzoek — en in de tests was dat vier van de tien keer niet.</para>
    ///
    /// <para><strong>En waarom er twéé rondes zijn.</strong> Bij een webhost is de lijst met
    /// endpoints op dit moment soms nog leeg, omdat de verzoekpijplijn door een ándere
    /// achtergronddienst wordt gebouwd en de volgorde daarvan niet van ons is. Gemeten over vijf
    /// opstarts: twee keer nul agents, drie keer drie. Op <c>ApplicationStarted</c> staat de
    /// pijplijn er zeker. Het registratiedocument is een upsert, dus de tweede ronde kost één
    /// schrijfactie per agent en verandert niets. Zonder die ronde zou het portaal in twee van de
    /// vijf gevallen een halve minuut niets van de diensten weten, en dat is op het scherm geen
    /// fout maar afwezigheid.</para>
    /// </remarks>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        RegisterShutdownFallback();
        applicationLifetime.ApplicationStarted.Register(Publish);

        // Meteen melden, niet pas na het eerste interval. Anders staat een net uitgerolde dienst
        // een halve minuut op 'unknown' terwijl hij al verzoeken aanneemt.
        Publish();

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(AgentStatusThresholds.HeartbeatInterval, clock, stoppingToken)
                    .ConfigureAwait(false);

                Publish();
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
    /// Vraagt opnieuw wat deze host herbergt en zet voor elke agent een registratie in de buffer.
    /// </summary>
    internal void Publish()
    {
        foreach (HostedAgent agent in registry.Refresh())
        {
            Announce(agent);
            writer.Enqueue(BuildRegistration(agent));
        }
    }

    /// <summary>
    /// Bouwt het registratiedocument van één geherbergde agent.
    /// </summary>
    /// <remarks>
    /// <para>De levensfase komt uit <see cref="HostedAgent.Lifecycle"/> — een waarneming van de
    /// bibliotheek — behalve tijdens het afsluiten. Dan is
    /// <see cref="AgentLifecycle.StoppedCleanly"/> de waarheid, ook als er nog een verzoek liep:
    /// wat er van dat verzoek geworden is staat op de run en niet op de registratie.</para>
    ///
    /// <para>Het plan en de volgende run komen uit twee verschillende bronnen, en dat is opzet. Het
    /// plan staat op de aankondiging en verandert niet; de volgende run is wat de host <em>meldt</em>
    /// dat hij afwacht (<see cref="HostedAgents.ISoratusHostedAgent.ReportNextRun"/>) en niet een herberekening
    /// uit de cron vanaf nu. Dat tweede zou per constructie altijd in de toekomst liggen en dus
    /// nooit een gemiste run kunnen laten zien.</para>
    /// </remarks>
    internal AgentRegistration BuildRegistration(HostedAgent agent) => new()
    {
        Id = agent.Identity.AgentName,
        PartitionKey = agent.Identity.AgentName,
        CustomerId = agent.Identity.CustomerId,
        AgentName = agent.Identity.AgentName,
        DisplayType = agent.Identity.DisplayType,
        Version = agent.Identity.Version,
        StartedAt = agent.Identity.StartedAt,
        LastHeartbeatAt = clock.GetUtcNow(),
        Lifecycle = _stopping ? AgentLifecycle.StoppedCleanly : agent.Lifecycle,
        Schedule = agent.Identity.Schedule,
        TriggerKind = agent.Identity.TriggerKind,
        TriggerDetail = agent.Identity.TriggerDetail,
        NextRunAt = agent.NextRunAt,
        Environment = agent.Identity.Environment,
    };

    /// <summary>Schrijft de startregel van een agent die deze dienst voor het eerst ziet.</summary>
    private void Announce(HostedAgent agent)
    {
        if (!_announced.Add(agent.Identity.AgentName))
        {
            return;
        }

        agent.ReportEvent(
            Contracts.LogLevel.Info,
            StartEvent,
            "De host van deze dienst is gestart; de hartslag komt vanaf nu van dit proces.",
            new
            {
                startedAt = agent.Identity.StartedAt,
                heartbeatSeconds = (int)AgentStatusThresholds.HeartbeatInterval.TotalSeconds,
                uitleg = StartExplanation,
            });
    }

    /// <summary>
    /// Schrijft van elke agent een laatste document met <see cref="AgentLifecycle.StoppedCleanly"/>,
    /// buiten de buffer om.
    /// </summary>
    /// <remarks>
    /// Buiten de buffer om, omdat de schrijflus bij afsluiten al gestopt kan zijn. En alleen bij een
    /// nette afsluiting: wordt het proces hard weggehaald, dan komt dit nooit langs en blijft de
    /// laatste hartslag staan — waarna het portaal na de drempel <see cref="AgentStatus.Degraded"/>
    /// meldt. Dat onderscheid is precies wat je bij een uitrol wilt hebben: een geplande herstart
    /// laat elke dienst netjes op <see cref="AgentStatus.Idle"/> achter en belt niemand wakker.
    /// </remarks>
    private async Task WriteFinalAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _finalWritten, 1) != 0)
        {
            return;
        }

        _stopping = true;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.ShutdownDrainTimeout);

        // Bewust zonder vernieuwen: een agent die pas bij het afsluiten voor het eerst zou opduiken
        // heeft nooit een hartslag gehad, en een document dat in één keer 'netjes gestopt' meldt
        // zonder ooit gelopen te hebben is geen feit maar ruis.
        foreach (HostedAgent agent in registry.All.OfType<HostedAgent>())
        {
            try
            {
                await writer.WriteRegistrationDirectAsync(BuildRegistration(agent), timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "De afsluitende registratie van agent {AgentName} kon niet worden weggeschreven.",
                    agent.Identity.AgentName);
            }
        }
    }

    /// <summary>
    /// Haakt aan op <c>ApplicationStopped</c> als vangnet, voor het geval de host deze dienst niet
    /// langs <see cref="StopAsync"/> voert.
    /// </summary>
    private void RegisterShutdownFallback() =>
        applicationLifetime.ApplicationStopped.Register(() =>
        {
            if (Volatile.Read(ref _finalWritten) != 0)
            {
                return;
            }

            WriteFinalAsync(CancellationToken.None).GetAwaiter().GetResult();
        });
}
