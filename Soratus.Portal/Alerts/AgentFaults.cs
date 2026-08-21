using Soratus.Agents.Contracts;
using Soratus.Portal.Data;

namespace Soratus.Portal.Alerts;

/// <summary>
/// Eén agent waarover gemeld hoort te worden, met alles wat een operator erover wil weten.
/// </summary>
/// <param name="CustomerId">De klantslug.</param>
/// <param name="CustomerName">De klantnaam.</param>
/// <param name="AgentName">De technische naam van de agent.</param>
/// <param name="DisplayType">Het type zoals het op het scherm staat.</param>
/// <param name="Status">De afgeleide status: <see cref="AgentStatus.Failed"/> of <see cref="AgentStatus.Degraded"/>.</param>
/// <param name="StartedAt">Wanneer het proces van deze agent startte. Zie <see cref="AgentFaultGroup"/>.</param>
/// <param name="Silence">Hoe lang deze agent zwijgt.</param>
/// <param name="Version">De versie die de agent publiceert.</param>
/// <param name="LastRun">De laatste afgeronde run, of <c>null</c>.</param>
/// <remarks>
/// <para><strong>Dit type draagt de volledige foutmelding en het volledige <c>errorType</c>, en dat is
/// het tegenovergestelde van wat de punten 13 en 14 voorschrijven — met opzet.</strong> Die punten
/// gaan over wat een <em>klant</em> mag zien: punt 13 vond een stacktrace "zichtbaar voor een klant"
/// en punt 14 zegt letterlijk "de klant ziet dit veld niet ... de operator vindt de typenaam op het
/// runtabblad". De koppelingentabel bij §5 zegt waar deze mail heen gaat: <em>storingsmeldingen aan
/// Soratus, maandoverzicht aan de klant</em>. De lezer is dus de operator, en een agentnaam, een
/// <c>errorType</c> met naamruimte en een stacktrace zijn precies wat hij nodig heeft. Voorzichtigheid
/// die hier ook geldt, maakt de melding waardeloos zonder iets te beschermen.</para>
///
/// <para><strong>Wat de garantie is dat dit niet bij een klant komt.</strong> Drie dingen, en geen
/// ervan is een afspraak: de ontvangers komen uit <see cref="AgentAlertOptions.Recipients"/> en dus uit
/// configuratie; de map <c>Alerts/</c> raakt <c>AccessDocument</c> en <c>IPortalDataStore</c> nergens
/// aan (broncodetest); en de opgemaakte melding is een <see cref="AgentAlertMail"/> en geen
/// <c>StatementMail</c>, dus hij kan het klantpad niet nemen — dat pad neemt alleen het andere type
/// aan.</para>
/// </remarks>
internal sealed record AgentFault(
    string CustomerId,
    string CustomerName,
    string AgentName,
    string DisplayType,
    AgentStatus Status,
    DateTimeOffset StartedAt,
    TimeSpan Silence,
    string Version,
    RunRecord? LastRun);

/// <summary>
/// Eén host met de agents die er tegelijk in omvielen: één oorzaak, één melding.
/// </summary>
/// <param name="CustomerId">De klantslug.</param>
/// <param name="CustomerName">De klantnaam.</param>
/// <param name="StartedAt">Het moment waarop het proces startte. Dit is de groepeersleutel.</param>
/// <param name="Faults">De agents in deze host waarover gemeld hoort te worden. Nooit leeg.</param>
/// <remarks>
/// <para><strong>Waarom er op <c>startedAt</c> wordt gegroepeerd.</strong> Punt 42 legt het geval
/// vast: bij de eerste echte klant zijn er geen achtergrondagents maar drie diensten binnen één
/// webapplicatie. Valt dat proces uit, dan worden alle drie tegelijk <see cref="AgentStatus.Degraded"/>
/// — één oorzaak, drie agents. Het veld waaraan dat te zien is, is <c>startedAt</c>: bij geherbergde
/// agents is dat de start van het <em>proces</em> en dus exact gelijk op alle drie de registraties, en
/// er is een test in <c>Soratus.Agents.Telemetry.Tests</c> die dat vastpint. Zonder deze groepering
/// gaan er drie mails uit, en dan is de derde de reden dat de eerste ook niet meer wordt gelezen.
/// </para>
///
/// <para><strong>Bij een agent met zijn eigen proces doet de groepering niets, en dat is juist.</strong>
/// Twee losse agents hebben twee verschillende starttijden — ze zijn niet in dezelfde milliseconde
/// gestart — dus komen ze in twee groepen en krijgen ze twee meldingen. Dat is wat je wilt: er zijn
/// dan ook twee oorzaken. Het veld doet het werk; er is nergens een controle op "is dit een
/// geherbergde agent".</para>
///
/// <para><strong>Wat de groepering níet is: de ontdubbeling.</strong> Een herstart schuift
/// <c>startedAt</c> op en levert dus een nieuwe groep — bij een proces dat elke minuut opnieuw start
/// zou dat elke minuut een nieuwe groep zijn. Daarom hangt de ontdubbeling niet aan deze sleutel maar
/// per agent aan <see cref="AgentAlertDocument"/>. Zie <see cref="AgentAlertDecision"/>.</para>
/// </remarks>
internal sealed record AgentFaultGroup(
    string CustomerId,
    string CustomerName,
    DateTimeOffset StartedAt,
    IReadOnlyList<AgentFault> Faults);

/// <summary>
/// Maakt van wat er is gelezen de groepen waarover gemeld hoort te worden.
/// </summary>
/// <remarks>
/// <para><strong>Puur, en zonder klok van zichzelf.</strong> <c>now</c> komt als parameter binnen,
/// dezelfde afspraak als in <see cref="AgentStatusCalculator"/> en om dezelfde reden: een drempel van
/// tien minuten is anders niet te testen zonder tien minuten te wachten.</para>
///
/// <para><strong>Het oordeel komt uit de contractbibliotheek en wordt hier niet nagebouwd.</strong>
/// <see cref="AgentStatusCalculator.ShouldAlert"/> beantwoordt de vraag "hoort hier een melding over";
/// deze klasse beantwoordt de vraag "welke melding, en aan wie hoort hij samen te gaan". Zou de eerste
/// vraag hier opnieuw worden beantwoord, dan kunnen scherm en melder uiteenlopen, en dat is de
/// tegenspraak tussen schermen die dit portaal verbiedt.</para>
/// </remarks>
internal static class AgentFaults
{
    /// <summary>
    /// De groepen waarover gemeld hoort te worden, op ernst en daarna op klant.
    /// </summary>
    /// <param name="scans">Wat er per klant is gelezen.</param>
    /// <param name="now">Het moment waarop wordt geoordeeld.</param>
    /// <returns>De groepen. Leeg als er niets aan de hand is.</returns>
    /// <remarks>
    /// <para><strong>Alleen productie-agents, en dat is punt 9 letterlijk.</strong> Daar staat het voor
    /// de ernstrang van het overzicht — "een acceptatie-agent die omvalt is geen storing" — en de
    /// reden geldt hier sterker: de interne klant draait <c>heartbeat-demo</c> op <c>dev</c>, die
    /// meestal uit staat en dus permanent <see cref="AgentStatus.Degraded"/> is. Zonder dit filter zou
    /// de melder daar elke zes uur over mailen, en dan is de melder binnen een week weggefilterd —
    /// precies de fout die punt 9 bij het overzicht beschrijft.</para>
    ///
    /// <para><strong>De ordening bepaalt wie er binnen de rem van
    /// <see cref="AgentAlertOptions.MaxMailsPerRun"/> valt.</strong> Eerst de groepen met een mislukte
    /// run, want dat is een afgerond feit; daarna de klantslug en de starttijd, zodat de volgorde
    /// tussen twee ronden dezelfde is. Een willekeurige volgorde zou bij een rem betekenen dat het
    /// wisselt wie er wordt overgeslagen.</para>
    /// </remarks>
    internal static IReadOnlyList<AgentFaultGroup> From(
        IReadOnlyList<CustomerAgentScan> scans,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(scans);

        var faults = new List<AgentFault>();

        foreach (var scan in scans)
        {
            foreach (var agent in scan.Agents)
            {
                if (agent.Registration.Environment != AgentEnvironment.Production)
                {
                    continue;
                }

                if (!AgentStatusCalculator.ShouldAlert(agent.Registration, agent.LastCompletedRun, now))
                {
                    continue;
                }

                faults.Add(new AgentFault(
                    scan.CustomerId,
                    scan.DisplayName,
                    agent.Registration.AgentName,
                    agent.Registration.DisplayType,
                    AgentStatusCalculator.Calculate(agent.Registration, agent.LastCompletedRun, now),
                    agent.Registration.StartedAt,
                    AgentStatusCalculator.SilenceFor(agent.Registration, now) ?? TimeSpan.Zero,
                    agent.Registration.Version,
                    agent.LastCompletedRun));
            }
        }

        return
        [
            .. faults
                .GroupBy(fault => (fault.CustomerId, fault.StartedAt))
                .Select(group => new AgentFaultGroup(
                    group.Key.CustomerId,
                    group.First().CustomerName,
                    group.Key.StartedAt,
                    [.. group.OrderBy(fault => fault.AgentName, StringComparer.Ordinal)]))
                .OrderByDescending(group => group.Faults.Any(fault => fault.Status == AgentStatus.Failed))
                .ThenBy(group => group.CustomerId, StringComparer.Ordinal)
                .ThenBy(group => group.StartedAt),
        ];
    }
}
