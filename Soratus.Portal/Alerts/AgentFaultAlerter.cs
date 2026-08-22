using Microsoft.Extensions.Options;
using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry;
using Soratus.Agents.Telemetry.HostedAgents;
using Soratus.Portal.Mail;
using Soratus.Portal.Platform;

namespace Soratus.Portal.Alerts;

/// <summary>
/// De storingsmelder van §4: mailt Soratus bij <see cref="AgentStatus.Failed"/> en
/// <see cref="AgentStatus.Degraded"/> (fase 6).
/// </summary>
/// <remarks>
/// <para><strong>Waarom hij in het portaal draait en niet als eigen dienst.</strong> Dezelfde reden als
/// bij <c>AzureCostCollector</c> (punt 38): alles wat hij nodig heeft staat hier al en nergens anders —
/// de managed identity met leesrecht op elke klantopslag, de klantenlijst, de statusregel uit de
/// contractbibliotheek, en de verzendlaag met de rol op de Communication Service. Een eigen deployable
/// zou een eigen identity, een eigen rolverlening per klantabonnement, een eigen Cosmos-verlening en
/// een eigen uitrol vragen, en er niets voor teruggeven.</para>
///
/// <para><strong>Wat dat kost: het portaal kan meer dan één instantie hebben, en dan draaien er twee
/// melders.</strong> Bij de kostencollector is dat een dagclaim, en die claim is daar een
/// <em>wederzijdse uitsluiting</em> op een schaars aanroepbudget — punt 38 zegt met zoveel woorden dat
/// dat een andere betekenis is dan bij de mail. Hier is het het mailgeval: een verstuurde mail is niet
/// terug te halen. Vandaar dat de claim niet per dag maar <strong>per agent per melding</strong> gaat
/// en dat hij toestand draagt; zie <see cref="AgentAlertDocument"/>. Een dagclaim zou hier niet passen:
/// hij zou de eerste melder van de dag alle meldingen laten doen en de tweede geen, en bij een herstart
/// zou er een dag lang niets meer worden gemeld.</para>
///
/// <para><strong>De volgorde van deze klasse is het ontwerp.</strong> Lezen, groeperen, ontdubbelen,
/// afremmen, claimen, versturen, vastleggen. Twee dingen daarin staan vast:</para>
///
/// <list type="number">
///   <item><description>
///     <strong>De proefdraaimodus staat vóór de claim.</strong> Een proefdraai die een markering
///     achterlaat is geen proefdraai — dan staat er een "gemeld" bij een mail die nooit is verstuurd,
///     en de echte storing wordt daarna zes uur lang onderdrukt. Dezelfde regel en dezelfde reden als
///     §29.8.
///   </description></item>
///   <item><description>
///     <strong>De rem staat vóór de claim.</strong> Wat er door
///     <see cref="AgentAlertOptions.MaxMailsPerRun"/> niet uitgaat wordt ook niet vastgelegd, en komt
///     de volgende ronde weer in aanmerking.
///   </description></item>
/// </list>
///
/// <para><strong>Er zit geen herhaling in dit pad.</strong> Geen <c>retry</c> op een verzending, ook
/// niet bij een uitkomst die zeker "niet verstuurd" is. Dat is de vaste stelregel van dit project, en
/// hier komt er een tweede reden bij: de volgende ronde is een minuut later, dus een fout die zichzelf
/// oplost lost zich vanzelf op en een fout die dat niet doet zou elke minuut opnieuw worden gemaakt.
/// Zie <see cref="AgentAlertDocument.Delivery"/>.</para>
///
/// <para><strong>Er wordt bij het opstarten niet meteen gekeken maar gewacht tot de eerste tik.</strong>
/// Dezelfde keuze als bij <c>AzureCostCollector</c> en om dezelfde soort reden: een uitrol is anders een
/// ronde, en een dag met vijf uitrollen zou vijf extra rondes zijn — met bij elke uitrol een venster
/// waarin de agents van de klant net zijn herstart en dus even zwijgen. Wat het kost is één interval
/// vertraging na een uitrol, en dat is een minuut.</para>
///
/// <para><strong>Wat er níet in zit: een melding over de melder.</strong> Gaat het versturen stuk, dan
/// is er geen mail waarmee dat te melden is — dat is het ding dat stuk is. Het staat dus als
/// <c>error</c> in het log, en dat is de enige plek waar het kan staan. Dat is een echte beperking en
/// geen detail: er is vandaag geen tweede kanaal.</para>
///
/// <para><strong>Sinds fase 6 publiceert de melder zich als agent, en daarmee ontstaat een kringloop
/// die het opschrijven waard is (§4, <c>storingsmelder</c>).</strong> Elke tik is één run. Valt een
/// ronde om, dan staat die run op <c>failed</c>, en de vólgende ronde leest die mislukking als
/// storing van de agent <c>storingsmelder</c> en mailt erover. De melder meldt dus over de melder, en
/// dat wérkt — zolang de ronde daarna nog loopt en het mailen zelf heel is. De ontdubbeling houdt het
/// binnen de perken: één markering per agent, dus niet zestig mails per uur over dezelfde mislukte
/// ronde.</para>
///
/// <para><strong>Waar die kringloop ophoudt, en dat is de eerlijke helft.</strong> Ligt het proces
/// stil, dan stopt de hartslag van deze agent én van de kostencollector — één proces, dus één
/// <c>startedAt</c> en één groep, en na tien minuten stilte hoort daar één melding over te gaan. Maar
/// de melder die dat zou doen is precies wat er stilligt. Er gaat dan geen mail; wat er wél is, is een
/// registratiedocument dat na twee minuten <c>Degraded</c> oplevert en op elk scherm te zien is dat de
/// opslag nog kan lezen — draait het portaal op meer dan één instantie, dan is dat de andere instantie,
/// en die mailt wel. Op één instantie is de beperking hard: <em>dat de storingsmelder stuk is, is niet
/// met de storingsmelder te melden.</em> Wat het wel is geworden: zichtbaar in de opslag in plaats van
/// alleen in een logregel.</para>
///
/// <para><strong>De telemetrie is optioneel en het werk niet.</strong> Zelfde richting en zelfde
/// reden als bij <c>AzureCostCollector</c>: is <see cref="ISoratusHostedAgents"/> er niet, dan kijkt
/// deze melder precies hetzelfde rondje en legt hij niets vast.</para>
/// </remarks>
internal sealed class AgentFaultAlerter(
    IAgentFaultSource source,
    IAgentAlertStore store,
    IMailOutbox outbox,
    IOptions<AgentAlertOptions> options,
    IOptions<PortalMailOptions> mailOptions,
    TimeProvider timeProvider,
    ILogger<AgentFaultAlerter> logger,
    ISoratusHostedAgents? hostedAgents = null) : BackgroundService
{
    private readonly AgentAlertOptions _options = options.Value;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            // Luidruchtig, want dit is de vlag waarmee een omgeving stil zonder storingsmeldingen kan
            // draaien. Zie AgentAlertOptions.Enabled: hij staat standaard aan.
            logger.LogWarning(
                "PortalAlerts:Enabled staat uit. Er worden geen storingsmeldingen verstuurd; storingen "
                + "zijn alleen op het scherm te zien.");
            return;
        }

        var declaration = PlatformAgents.AlertsDeclaration(_options);
        var plan = PlatformAgentPlans.Alerts(_options.IntervalSeconds);
        var planned = PlatformAgentPlans.PlannedInterval(_options.IntervalSeconds);
        var agent = Announce(declaration);

        logger.LogInformation(
            "De storingsmelder kijkt op '{Plan}' en herhaalt een onveranderde storing na {Repeat} "
            + "uur, hoogstens {Max} melding(en) per ronde. Hij publiceert zich {Published} als agent "
            + "'{AgentName}'.",
            plan.Expression,
            _options.RepeatAfterHours,
            _options.MaxMailsPerRun,
            agent is null ? "niet" : "wel",
            PlatformAgentNames.Alerts);

        if (planned != _options.Interval)
        {
            // Een afronding die niemand ziet is een verschil tussen wat er is ingesteld en wat er
            // gebeurt. Zie PlatformAgentPlans.Alerts: een cron-expressie kan geen halve minuten.
            logger.LogWarning(
                "PortalAlerts:IntervalSeconds staat op {Requested} s, maar een plan wordt als "
                + "cron-expressie gepubliceerd en die kent alleen hele minuten. De melder kijkt "
                + "daarom elke {Planned} s, en dat is ook wat er in het portaal staat.",
                _options.IntervalSeconds,
                (int)planned.TotalSeconds);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!await SleepAsync(plan, agent, stoppingToken).ConfigureAwait(false))
            {
                return;
            }

            try
            {
                await ObservedRunAsync(agent, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Een ronde die omvalt mag het portaal niet meenemen: een BackgroundService die een
                // uitzondering laat ontsnappen stopt de host, en er is niets aan een mislukte
                // storingsmelding dat het bekijken van een agentstatus in de weg staat. Een minuut
                // later opnieuw.
                //
                // En sinds fase 6 staat die mislukking als 'failed' op de run van de agent
                // 'storingsmelder'. De volgende ronde leest die en meldt erover — de melder meldt
                // over de melder. Dat is de kringloop uit de klassedocumentatie; hij werkt zolang de
                // ronde daarna nog loopt.
                logger.LogError(
                    exception,
                    "De ronde van de storingsmelder is afgebroken. Er is niets half weggeschreven — "
                    + "elke melding is een eigen claim — en de volgende ronde leest alles opnieuw.");
            }
        }
    }

    /// <summary>
    /// Meldt deze melder aan als geherbergde agent, of levert <c>null</c> als dat niet kan.
    /// </summary>
    /// <param name="declaration">De aankondiging.</param>
    /// <returns>De agent, of <c>null</c> als er geen telemetrie is ingericht.</returns>
    /// <remarks>
    /// <c>GetOrAdd</c> en niet <c>Find</c>, en met een <c>catch</c> eromheen. Zelfde twee redenen als
    /// bij <c>AzureCostCollector.Announce</c>: <c>Find</c> zou afhangen van de startvolgorde van
    /// achtergronddiensten, en een uitzondering hier zou de host meenemen omdat dit buiten de lus van
    /// een <see cref="BackgroundService"/> staat.
    /// </remarks>
    internal ISoratusHostedAgent? Announce(HostedAgentDeclaration declaration)
    {
        if (hostedAgents is null)
        {
            return null;
        }

        try
        {
            return hostedAgents.GetOrAdd(declaration);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "De storingsmelder kon zich niet als agent aanmelden en publiceert dus geen runs. "
                + "Hij kijkt gewoon door; wat er ontbreekt is de zichtbaarheid.");
            return null;
        }
    }

    /// <summary>
    /// Draait één ronde, en legt hem vast als er telemetrie is ingericht.
    /// </summary>
    /// <param name="agent">De agent, of <c>null</c>.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De taak van de ronde.</returns>
    /// <remarks>
    /// Het aantal verstuurde of opgemaakte meldingen wordt het aantal verwerkte items van de run. Een
    /// ronde zonder storingen is dus een run met nul items en resultaat <c>ok</c> — en dat is de
    /// juiste uitkomst: er was werk (kijken) en er was niets te melden.
    /// </remarks>
    internal Task ObservedRunAsync(ISoratusHostedAgent? agent, CancellationToken cancellationToken)
    {
        if (agent is null)
        {
            return RunAsync(cancellationToken);
        }

        return agent.RunAsync(
            TriggerKind.Timer,
            async (run, token) => run.Processed(await RunAsync(token).ConfigureAwait(false)),
            cancellationToken);
    }

    /// <summary>
    /// Wacht tot de volgende tik en meldt dat moment aan de agent.
    /// </summary>
    /// <param name="plan">Het plan waarop wordt gewacht.</param>
    /// <param name="agent">De agent, of <c>null</c>.</param>
    /// <param name="stoppingToken">Het stoptoken van de host.</param>
    /// <returns><c>false</c> als het portaal afsluit of het plan niets meer oplevert.</returns>
    /// <remarks>
    /// <para>Dit was een <see cref="PeriodicTimer"/> op <c>IntervalSeconds</c> en is nu hetzelfde plan
    /// dat wordt aangekondigd. Dat is de reden voor de wissel: de expressie in het document moet de
    /// expressie zijn waarop werkelijk wordt gepland, en met twee bronnen — een timer hier en een cron
    /// daar — is dat een afspraak in plaats van een eigenschap.</para>
    ///
    /// <para>Het gemelde moment is het moment waarop hier werkelijk wordt gewacht, en niet de cron
    /// vanaf nu. Zie <see cref="ISoratusHostedAgent.ReportNextRun"/>: alleen zó kan een volgende run
    /// in het verleden komen te staan als deze lus stilvalt terwijl het portaal doorklopt.</para>
    /// </remarks>
    private async Task<bool> SleepAsync(
        SoratusSchedule plan,
        ISoratusHostedAgent? agent,
        CancellationToken stoppingToken)
    {
        var now = timeProvider.GetUtcNow();

        if (MeldVolgendeRun(plan, agent) is not { } target)
        {
            return false;
        }

        try
        {
            await Task.Delay(target - now, timeProvider, stoppingToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Rekent de volgende tik uit en meldt hem aan de agent.
    /// </summary>
    /// <param name="plan">Het plan.</param>
    /// <param name="agent">De agent, of <c>null</c>.</param>
    /// <returns>Het moment waarop gewacht gaat worden, of <c>null</c> als het plan is uitgeput.</returns>
    /// <remarks>
    /// <c>internal</c> en met een uitkomst, dezelfde afweging en dezelfde reden als bij
    /// <c>AzureCostCollector.MeldVolgendeRun</c>: zo kan een test meten wát er wordt gemeld zonder de
    /// lus te draaien en zonder tijdslimiet. Een testgrens die van de belasting van de machine afhangt,
    /// meet de belasting en niet het gedrag.
    /// </remarks>
    internal DateTimeOffset? MeldVolgendeRun(SoratusSchedule plan, ISoratusHostedAgent? agent)
    {
        if (plan.NextAfter(timeProvider.GetUtcNow()) is not { } target)
        {
            logger.LogError(
                "Het plan '{Plan}' levert geen volgend moment meer op; de storingsmelder stopt.",
                plan.Expression);
            agent?.ReportNextRun(null);
            return null;
        }

        agent?.ReportNextRun(target);
        return target;
    }

    /// <summary>
    /// Eén ronde: kijken, ontdubbelen, en melden wat er over is.
    /// </summary>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Het aantal meldingen dat is verstuurd of aangeboden.</returns>
    /// <remarks>
    /// <para><c>internal</c> en met een uitkomst, zodat een test één ronde kan doen zonder een minuut
    /// te wachten. Dezelfde afweging als bij <c>AzureCostCollector.RunAsync</c>.</para>
    ///
    /// <para>En met de vlag er nog een keer in, om dezelfde reden als daar: dit is de enige methode die
    /// werk doet en ze is <c>internal</c>, dus een tweede aanroeper is mogelijk. Dat de vlag in
    /// <see cref="ExecuteAsync"/> ook staat is een planningsbeslissing — er wordt niet gewacht op een
    /// moment dat toch niets doet — en dat die daar niet te testen is zonder de lus te draaien, is
    /// precies het gat dat punt 41 bij de kostencollector met een mutatie vond.</para>
    /// </remarks>
    internal async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return 0;
        }

        // De stand wordt één keer per ronde gelezen. Staat de verzendlaag op "niet ingericht", dan
        // wordt er niets gelezen en niets vastgelegd: een ronde die toch niets kan versturen hoort geen
        // query's te kosten.
        var outboxState = outbox.State;

        if (outboxState == MailOutboxState.NotConfigured)
        {
            logger.LogError(
                "Mailen is niet ingericht (PortalMail:Endpoint of PortalMail:FromAddress is leeg), dus "
                + "er kan geen storing worden gemeld. Storingen zijn alleen op het scherm te zien.");
            return 0;
        }

        var recipients = _options.UsableRecipients();

        if (_options.UnusableRecipients() is { Count: > 0 } unusable)
        {
            // Stil overslaan zou betekenen dat de eigenaar van dat adres denkt dat hij meldingen
            // krijgt. De adressen staan erbij: het zijn onze eigen adressen uit een app-setting en
            // geen klantgegevens.
            logger.LogError(
                "{Count} adres(sen) in PortalAlerts:Recipients zijn niet als ontvanger te gebruiken en "
                + "worden overgeslagen: {Addresses}",
                unusable.Count,
                string.Join(", ", unusable));
        }

        if (recipients.Count == 0)
        {
            logger.LogError(
                "PortalAlerts:Recipients bevat geen bruikbaar adres. De storingsmelder kan niets "
                + "melden. Dit is de enige plek waar dat zichtbaar is: de melding die er over zou "
                + "gaan, is precies wat niet werkt.");
            return 0;
        }

        var now = timeProvider.GetUtcNow();
        var scans = await source.ScanAsync(cancellationToken).ConfigureAwait(false);

        foreach (var scan in scans.Where(scan => scan.Unavailable is not null))
        {
            logger.LogWarning(
                "De agents van klant {CustomerId} waren niet te lezen ({Reason}); over deze klant "
                + "wordt niets gemeld.",
                scan.CustomerId,
                scan.Unavailable);
        }

        var groups = AgentFaults.From(scans, now);
        var markers = await store.MarkersAsync(cancellationToken).ConfigureAwait(false);

        var byAgent = markers.ToDictionary(
            marker => (marker.CustomerId, marker.AgentName),
            marker => marker);

        await CloseRecoveredAsync(groups, markers, scans, now, cancellationToken).ConfigureAwait(false);

        // Ontdubbelen vóór het afremmen: de rem hoort te gelden voor wat er werkelijk uit zou gaan en
        // niet voor wat er onderdrukt wordt. Andersom zou één onderdrukte groep een echte melding
        // kunnen verdringen.
        var due = groups
            .Select(group => (Group: group, Faults: DueIn(group, byAgent, now)))
            .Where(candidate => candidate.Faults.Count > 0)
            .ToArray();

        if (due.Length > _options.MaxMailsPerRun)
        {
            logger.LogError(
                "Er zijn {Total} host(s) met een storing en de rem staat op {Max} melding(en) per "
                + "ronde. De rest wordt deze ronde overgeslagen en komt de volgende ronde weer in "
                + "aanmerking; er is niets vastgelegd. Zoveel storingen tegelijk wijst op één oorzaak "
                + "bij ons en niet op evenveel oorzaken bij klanten.",
                due.Length,
                _options.MaxMailsPerRun);
        }

        var sent = 0;

        foreach (var (group, faults) in due.Take(_options.MaxMailsPerRun))
        {
            if (await NotifyAsync(group, faults, byAgent, recipients, outboxState, now, cancellationToken)
                .ConfigureAwait(false))
            {
                sent++;
            }
        }

        logger.LogInformation(
            "Ronde klaar: {Groups} host(s) met een storing, {Sent} melding(en) {Verb}.",
            groups.Count,
            sent,
            outboxState == MailOutboxState.DryRun ? "opgemaakt (proefdraai)" : "verstuurd");

        return sent;
    }

    /// <summary>
    /// Welke agents in deze groep nu gemeld mogen worden.
    /// </summary>
    /// <remarks>
    /// <para>Per agent en niet per groep, en dat is de kern van de ontdubbeling. De groep bepaalt wat er
    /// in één mail hoort (§42: één host, drie diensten); de markering per agent bepaalt of er over die
    /// dienst nú iets gestuurd mag worden. Zou de ontdubbeling aan de groep hangen, dan zou een proces
    /// dat elke minuut opnieuw start elke minuut een nieuwe groepsleutel opleveren — <c>startedAt</c>
    /// schuift dan mee — en dan ontdubbelt er niets.</para>
    /// </remarks>
    private IReadOnlyList<AgentFault> DueIn(
        AgentFaultGroup group,
        IReadOnlyDictionary<(string, string), AgentAlertDocument> markers,
        DateTimeOffset now)
    {
        var due = new List<AgentFault>();

        foreach (var fault in group.Faults)
        {
            markers.TryGetValue((fault.CustomerId, fault.AgentName), out var marker);

            var verdict = AgentAlertDecision.Judge(marker, fault.Status, now, _options.RepeatAfter);

            if (verdict == AlertDue.Suppressed)
            {
                logger.LogDebug(
                    "Over {AgentName} van {CustomerId} is al gemeld ({Status}); binnen het "
                    + "herhaalvenster gaat er niets uit.",
                    fault.AgentName,
                    fault.CustomerId,
                    fault.Status);
                continue;
            }

            logger.LogInformation(
                "{AgentName} van {CustomerId} staat op {Status} en wordt gemeld: {Reason}.",
                fault.AgentName,
                fault.CustomerId,
                fault.Status,
                verdict);

            due.Add(fault);
        }

        return due;
    }

    /// <summary>
    /// Sluit de markeringen van agents die weer in orde zijn.
    /// </summary>
    /// <remarks>
    /// <para>Een markering blijft alleen open zolang de agent in de huidige ronde nog een storing heeft.
    /// Dat is nodig voor de ontdubbeling: zonder afsluiten zou een storing die weg was en terugkomt als
    /// een herhaling worden gelezen en tot zes uur worden onderdrukt.</para>
    ///
    /// <para><strong>Een klant die niet te lezen was wordt overgeslagen, en dat is het enige subtiele
    /// hier.</strong> "Wij konden niet lezen" is geen bewijs dat de agent in orde is, en zou de
    /// markering afsluiten. Bij de volgende ronde zou de storing dan opnieuw als nieuw gelden en gaat er
    /// weer een mail uit — een hapering in Cosmos zou zo een mailstroom worden.</para>
    /// </remarks>
    private async Task CloseRecoveredAsync(
        IReadOnlyList<AgentFaultGroup> groups,
        IReadOnlyList<AgentAlertDocument> markers,
        IReadOnlyList<CustomerAgentScan> scans,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var readable = scans
            .Where(scan => scan.Unavailable is null)
            .Select(scan => scan.CustomerId)
            .ToHashSet(StringComparer.Ordinal);

        var faulty = groups
            .SelectMany(group => group.Faults)
            .Select(fault => (fault.CustomerId, fault.AgentName))
            .ToHashSet();

        foreach (var marker in markers)
        {
            if (marker.ClearedAt is not null
                || !readable.Contains(marker.CustomerId)
                || faulty.Contains((marker.CustomerId, marker.AgentName)))
            {
                continue;
            }

            await store.ClearAsync(marker, now, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Claimt, maakt op, verstuurt en legt vast — in die volgorde.
    /// </summary>
    /// <returns><c>true</c> als er een melding is verstuurd of, bij een proefdraai, opgemaakt.</returns>
    /// <remarks>
    /// <para><strong>De proefdraai staat vóór de claim.</strong> Wat er dan gebeurt: de melding wordt
    /// opgemaakt en in het log gezet, en er wordt niets vastgelegd. De getoonde tekst is letterlijk de
    /// tekst die zou zijn verstuurd — geen markering erin, geen aanpassing — want een proefdraai die
    /// iets anders toont dan hij zou versturen bewijst niets. §29.8, met dezelfde woorden.</para>
    ///
    /// <para><strong>Claimen gebeurt per agent, en de mail gaat over wat er is geclaimd.</strong> Lukt
    /// een claim niet, dan doet een andere instantie die dienst en hoort hij niet in ónze mail: dan
    /// zouden twee mails dezelfde dienst noemen. Wat dat kost, eerlijk: raken twee instanties elkaar
    /// precies op dit moment, dan kan één host twee mails opleveren met elk een deel van de diensten —
    /// het geval dat §42 wilde vermijden, nu alleen nog onder een race in plaats van standaard. De
    /// vorm die dat ook zou dichten is één claim per groep, en die valt af omdat de groepsleutel bij
    /// elke processtart verschuift.</para>
    /// </remarks>
    private async Task<bool> NotifyAsync(
        AgentFaultGroup group,
        IReadOnlyList<AgentFault> faults,
        IReadOnlyDictionary<(string, string), AgentAlertDocument> markers,
        IReadOnlyList<string> recipients,
        MailOutboxState outboxState,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (outboxState == MailOutboxState.DryRun)
        {
            var preview = AgentAlertComposer.Compose(
                group with { Faults = faults },
                recipients,
                now,
                mailOptions.Value.PortalBaseUri,
                _options.RepeatAfter);

            logger.LogInformation(
                "PROEFDRAAI — er is NIETS verstuurd en niets vastgelegd. Dit is letterlijk de melding "
                + "die zou zijn verstuurd, aan {Recipients}:\n{Subject}\n\n{Body}",
                string.Join(", ", preview.Recipients),
                preview.Subject,
                preview.PlainText);

            return true;
        }

        var claimed = new List<(AgentFault Fault, AgentAlertDocument Marker)>();

        foreach (var fault in faults)
        {
            var marker = await store
                .ClaimAsync(
                    new AgentAlertClaim(
                        fault.CustomerId,
                        fault.AgentName,
                        fault.Status,
                        now,
                        Existing(fault)),
                    cancellationToken)
                .ConfigureAwait(false);

            if (marker is not null)
            {
                claimed.Add((fault, marker));
            }
        }

        if (claimed.Count == 0)
        {
            return false;
        }

        var mail = AgentAlertComposer.Compose(
            group with { Faults = [.. claimed.Select(entry => entry.Fault)] },
            recipients,
            now,
            mailOptions.Value.PortalBaseUri,
            _options.RepeatAfter);

        var send = await outbox.SendAsync(mail, cancellationToken).ConfigureAwait(false);

        foreach (var (_, marker) in claimed)
        {
            await store
                .ConfirmAsync(marker, send.Delivery, send.OperationId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (send.Delivery != MailDelivery.Accepted)
        {
            // Dat het melden zelf stuk is, is niet met een mail te melden. Error en niet warning: dit
            // is het enige signaal dat er is, en de volgende ronde probeert het níet opnieuw.
            logger.LogError(
                "De storingsmelding over {Customer} is {Outcome}. Er wordt niets opnieuw geprobeerd; "
                + "de markering staat en de volgende melding volgt pas na het herhaalvenster of bij "
                + "een verandering van status.",
                group.CustomerId,
                send.Delivery == MailDelivery.Refused
                    ? "geweigerd door Communication Services"
                    : "van onbekende uitkomst");
        }

        return true;

        // De markering zoals hij bij het lezen van deze ronde stond, en niet opnieuw opgezocht: een
        // tweede lezing zou een tweede moment zijn, en dan claimt de melder op een etag die intussen
        // van iemand anders is. Als lokale functie en niet als veld op deze klasse — een singleton met
        // ronde-staat is de plek waar twee rondes elkaars gegevens gaan lezen.
        AgentAlertDocument? Existing(AgentFault fault) =>
            markers.TryGetValue((fault.CustomerId, fault.AgentName), out var marker) ? marker : null;
    }
}
