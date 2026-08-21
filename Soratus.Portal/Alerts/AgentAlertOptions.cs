using System.ComponentModel.DataAnnotations;
using Soratus.Agents.Contracts;
using Soratus.Portal.Mail;

namespace Soratus.Portal.Alerts;

/// <summary>
/// De configuratiesectie <c>PortalAlerts</c>: de storingsmelder van §4.
/// </summary>
/// <remarks>
/// <para><strong>De drempels staan hier niet.</strong> Wanneer een agent <c>Degraded</c> is en wanneer
/// er over gemeld hoort te worden staat in <see cref="AgentStatusThresholds"/>, in de
/// contractbibliotheek, omdat het scherm dezelfde grens hanteert. Zouden die twee uiteenlopen, dan
/// mailt de melder over iets dat het scherm niet toont. Wat hier staat gaat over de melder zelf: hoe
/// vaak hij kijkt, naar wie hij mailt, en wanneer hij zichzelf herhaalt.</para>
///
/// <para><strong>Het afzenderadres en de proefdraaimodus staan hier ook niet.</strong> Die horen bij
/// de verzendlaag en staan in <see cref="PortalMailOptions"/>. Eén proefdraaivlag voor alle doelen:
/// een tweede zou betekenen dat je er één kunt vergeten, en dan verstuurt een ontwikkelmachine
/// storingsmeldingen over de agents van een echte klant.</para>
/// </remarks>
public sealed class AgentAlertOptions
{
    /// <summary>De naam van de configuratiesectie.</summary>
    public const string SectionName = "PortalAlerts";

    /// <summary>
    /// Of de storingsmelder draait.
    /// </summary>
    /// <remarks>
    /// Standaard aan, om dezelfde reden als bij <c>AzureCostOptions.Enabled</c>: een vlag die
    /// standaard uit staat is een storing die zich voordoet als werkende functionaliteit. Dat een
    /// ontwikkelmachine hier niets verstuurt komt niet van deze vlag maar van de proefdraaimodus
    /// (<see cref="PortalMailOptions.DryRun"/>), en dat is de veiligere van de twee: hij werkt ook als
    /// iemand deze vlag aanzet.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// De e-mailadressen van Soratus waar een storingsmelding heen gaat.
    /// </summary>
    /// <remarks>
    /// <para><strong>Uit configuratie en nooit uit de toegangsdocumenten van een klant, en dat is de
    /// hele reden dat dit veld bestaat.</strong> De koppelingentabel bij §5 zegt het: storingsmeldingen
    /// gaan naar Soratus en het maandoverzicht naar de klant. Deze mail draagt een agentnaam, een
    /// foutmelding en een <c>errorType</c> met onze naamruimtestructuur erin — precies de tekst die de
    /// punten 13 en 14 uit een klantoppervlak hebben gehaald. Er is dus geen pad nodig waarlangs een
    /// klantadres hier terechtkomt, en er is er ook geen: er staat een broncodetest op dat de map
    /// <c>Alerts/</c> de toegangsdocumenten niet aanraakt.</para>
    ///
    /// <para><strong>Leeg betekent: er wordt niet gemeld, en dat hoort geen stilte te zijn.</strong>
    /// De melder logt dat dan bij elke run als <c>error</c>. Een melder die niets kan melden is de
    /// klasse fout die dit portaal overal dichtzet — een storing die zich voordoet als werkende
    /// functionaliteit — en het log is de enige plek waar hij zichtbaar kan zijn, want de melding die
    /// erover zou gaan is precies wat er niet werkt.</para>
    /// </remarks>
    public IList<string> Recipients { get; set; } = [];

    /// <summary>
    /// Hoe vaak de melder kijkt, in seconden.
    /// </summary>
    /// <remarks>
    /// <para>Zestig, zoals §4 zegt. Wat dat oplevert is smaller dan het lijkt: een
    /// <see cref="AgentStatus.Degraded"/> meldt pas na <see cref="AgentStatusThresholds.Alert"/> — tien
    /// minuten — dus alleen bij <see cref="AgentStatus.Failed"/> maakt een minuut verschil met twee
    /// minuten.</para>
    ///
    /// <para><strong>Wat het kost is niet verwaarloosbaar en het is niet gemeten.</strong> Eén ronde
    /// vraagt per klant één query voor de registraties plus één per agent voor de laatste afgeronde
    /// run. De stand van zaken meet dat op het overzicht: bij 20 agents ongeveer 130 RU, en richting
    /// 200 agents ongeveer 1300 RU. Elke minuut is dat 1440 keer per dag. Wordt dat een probleem, dan
    /// is dit getal de eerste knop — en de tweede is een goedkopere scan achter dezelfde naad
    /// (<see cref="IAgentFaultSource"/>), waarvoor de melder niet hoeft te veranderen.
    /// </para>
    /// </remarks>
    [Range(15, 3600, ErrorMessage = "PortalAlerts:IntervalSeconds hoort tussen 15 en 3600 te liggen.")]
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Na hoeveel uur een storing die niet is veranderd opnieuw wordt gemeld.
    /// </summary>
    /// <remarks>
    /// <para><strong>Zes uur, en dat is een keuze tussen twee fouten.</strong> Eén melding per storing
    /// is óók fout: een storing die drie dagen duurt en één keer is gemeld, is een storing waarvan
    /// niemand meer weet dat hij er is. Elke minuut melden is de andere fout, en die is duurder — dan
    /// wordt de melder weggefilterd en is de eerste mail ook niets meer waard.</para>
    ///
    /// <para>Zes uur betekent hoogstens vier meldingen per storing per dag: binnen één werkdag komt een
    /// nog openstaande storing minstens één keer terug, en een storing die een weekend duurt levert
    /// acht mails op in plaats van twee. Genoeg om op te vallen, weinig genoeg om te blijven lezen.
    /// Vierentwintig uur is even verdedigbaar en het is één configuratiewaarde; wat er níet op wacht is
    /// een verergering — een status die verandert meldt meteen (zie <see cref="AgentAlertDecision"/>).
    /// </para>
    /// </remarks>
    [Range(1, 720, ErrorMessage = "PortalAlerts:RepeatAfterHours hoort tussen 1 en 720 te liggen.")]
    public int RepeatAfterHours { get; set; } = 6;

    /// <summary>
    /// Hoeveel meldingen er in één ronde hoogstens de deur uit gaan.
    /// </summary>
    /// <remarks>
    /// <para>Dit is de rem voor het geval dat er niet één agent stuk is maar alles. Valt de
    /// telemetrieopslag weg, dan zwijgt élke agent van élke klant en zijn ze na tien minuten allemaal
    /// <see cref="AgentStatus.Degraded"/>. Zonder rem gaan er dan tientallen mails uit over één
    /// oorzaak, en die zijn geen van alle nuttig.</para>
    ///
    /// <para><strong>De rem staat vóór de claim en niet erna.</strong> Wat er niet uitgaat wordt dus
    /// ook niet als gemeld vastgelegd, en komt de volgende ronde weer in aanmerking. De rij loopt
    /// daarmee zichzelf leeg in plaats van dat er meldingen verdwijnen. Wat er wél gebeurt is een
    /// <c>error</c>-regel met het aantal dat is overgeslagen: dat een melder aan zijn grens zit hoort
    /// niet stil te zijn.</para>
    /// </remarks>
    [Range(1, 100, ErrorMessage = "PortalAlerts:MaxMailsPerRun hoort tussen 1 en 100 te liggen.")]
    public int MaxMailsPerRun { get; set; } = 10;

    /// <summary>Hoe vaak de melder kijkt.</summary>
    public TimeSpan Interval => TimeSpan.FromSeconds(IntervalSeconds);

    /// <summary>Na hoeveel tijd een onveranderde storing opnieuw wordt gemeld.</summary>
    public TimeSpan RepeatAfter => TimeSpan.FromHours(RepeatAfterHours);

    /// <summary>
    /// De ontvangers die als ontvanger van één bericht te gebruiken zijn, genormaliseerd.
    /// </summary>
    /// <returns>De adressen, op alfabet en zonder dubbelen. Leeg als er geen bruikbare is.</returns>
    /// <remarks>
    /// <para>Eén methode die de configuratie tot een antwoord maakt, zodat er geen aanroeper is die de
    /// lijst zelf filtert en de controle vergeet. Dezelfde vorm en dezelfde reden als
    /// <see cref="PortalMailOptions.Sender"/>.</para>
    ///
    /// <para><strong>Een onbruikbaar adres wordt hier overgeslagen en houdt de melding niet
    /// tegen</strong>, en dat is precies andersom dan bij het maandoverzicht
    /// (<c>StatementRecipients</c>). Daar is één fout adres een reden om helemaal niet te versturen,
    /// want de bevestiging zou "verstuurd" zeggen terwijl de bedoelde lezer niets kreeg. Hier is de
    /// afweging omgekeerd: een storingsmelding die niemand bereikt omdat er een tikfout in het tweede
    /// adres staat, is erger dan een storingsmelding die één van de twee lezers bereikt. De
    /// overgeslagen adressen worden gelogd.</para>
    /// </remarks>
    public IReadOnlyList<string> UsableRecipients() =>
    [
        .. Recipients
            .Where(MailAddresses.IsUsable)
            .Select(address => address.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(address => address, StringComparer.Ordinal),
    ];

    /// <summary>
    /// De adressen uit de configuratie die niet als ontvanger te gebruiken zijn.
    /// </summary>
    /// <returns>De onbruikbare adressen, zoals ze in de configuratie staan.</returns>
    /// <remarks>
    /// Bestaat zodat de melder ze kan noemen. Een adres dat stil wordt overgeslagen is een adres
    /// waarvan de eigenaar denkt dat hij meldingen krijgt.
    /// </remarks>
    public IReadOnlyList<string> UnusableRecipients() =>
    [
        .. Recipients.Where(address => !MailAddresses.IsUsable(address)),
    ];
}
