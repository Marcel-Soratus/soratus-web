using System.Buffers;
using System.Text;
using Soratus.Agents.Contracts;

namespace Soratus.Portal.Support;

/// <summary>
/// De grenzen van een supportbericht.
/// </summary>
/// <remarks>
/// Eén plek, want de vraag van de klant en het antwoord van de operator toetsen hetzelfde. Twee
/// kopieën van "hoe lang mag een bericht" gaan uit de pas lopen, en dan is er een richting waarlangs
/// iets binnenkomt dat de andere richting zou weigeren.
/// </remarks>
public static class SupportLimits
{
    /// <summary>
    /// Het langste toegestane bericht.
    /// </summary>
    /// <remarks>
    /// <para><strong>Dit getal is niet gemeten, en dat hoort erbij te staan.</strong> Punt 13 van de
    /// fase-0-afwijkingen kon zijn grens op 93 echte logregels doormeten en vond daar dat elke
    /// lengtegrens onbruikbaar was. Hier bestaan er nog geen echte berichten om aan te meten. Het is
    /// dus een hygiënegrens en geen bevinding: ruim genoeg voor een foutmelding met een paar
    /// voorbeelden erin, en te krap voor een gepast logbestand.</para>
    ///
    /// <para>Wat de grens níet is: een verdediging tegen wat er in een bericht kan sluipen. Punt 13
    /// heeft dat argument definitief gemaakt — een grens van 200 tot 500 tekens verminkt een geldig
    /// bericht middenin en laat het lek weg, en het middengebied is het gevaarlijkst omdat het de
    /// veilige ruime keuze lijkt. Wat hier het werk doet is <see cref="SupportBody.Clean"/>, en die
    /// werkt niet op lengte.</para>
    /// </remarks>
    public const int MaximumLength = 4_000;
}

/// <summary>
/// De bewerking op de vrije tekst van een supportbericht, in beide richtingen.
/// </summary>
/// <remarks>
/// <para><strong>Dit is de eerste plek in dit portaal waar vrije tekst twee kanten op gaat.</strong>
/// Punt 13 (<c>msg</c>) en punt 14 (<c>errorType</c>) gaan over tekst die wíj schrijven en die een
/// klant leest; de omschrijving van een urenregel (§3.6) idem. Een supportdraad heeft beide
/// richtingen in hetzelfde veld, en de twee hebben verschillende risico's.</para>
///
/// <para><strong>Wat er van de klant naar ons komt.</strong> De tekst wordt op ons scherm gerenderd en
/// door de eerstelijn gelezen. Twee dingen zijn hier echt en mechanisch te sluiten, en dat gebeurt
/// hieronder: onzichtbare besturingstekens en tekens die de leesrichting omkeren. Wat er niet met
/// tekens te sluiten is — een klant die de eerstelijn probeert te overtuigen dat zijn factuur nul is —
/// wordt niet hier gesloten maar in de vorm van <see cref="SupportAnswer"/>: er is geen veld waarin een
/// verzonnen bedrag past. Dat is de enige plek waar dat gesloten kán worden.</para>
///
/// <para><strong>Wat er van ons naar de klant gaat.</strong> Dezelfde bewerking, en één belangrijk
/// verschil met punt 13: hier wordt <em>niet</em> op de eerste regelovergang geknipt.
/// <see cref="MessageTruncation.Cut"/> hoort bij een veld dat één zin moet zijn — een logregel, de
/// omschrijving van een urenregel — en een antwoord aan een klant is proza met alinea's. Die knip zou
/// hier de tweede alinea van een operator stil weggooien. Er wordt daarom
/// <see cref="MessageTruncation.Shorten"/> gebruikt: dezelfde bibliotheek, dezelfde grafeemveilige
/// knip, maar de vorm die regelovergangen juist bewaart. De opmerkingen bij die twee functies zeggen
/// met zoveel woorden dat ze niet "consistent" gemaakt moeten worden; dit is het geval waarvoor dat
/// geldt.</para>
///
/// <para><strong>Wat er níet gesloten is, en dat is het eerlijkste deel.</strong> Een operator kan een
/// stacktrace, een pad, een interne codenaam of de naam van een ándere klant in zijn antwoord typen.
/// Daar is aan deze kant niets tegen te doen, en een inhoudsheuristiek ("ziet dit uit als een
/// stacktrace") is in dit project al twee keer afgewezen. Wat er in de plaats staat is een
/// ontwerpregel: <strong>niets in dit portaal vult het antwoordveld met machinetekst.</strong> Geen
/// knop die een logregel invoegt, geen voorvulling uit een run, geen foutmelding die in het formulier
/// belandt. Dat is het verschil met punt 13 en 14, waar de tekst dóór een machine was geschreven en
/// geen mens hem had gelezen voordat de klant hem zag. Er staat een broncodetest op dat deze map
/// nergens <c>Exception</c>, <c>StackTrace</c>, <c>ToString</c> of <c>ErrorCode</c> in een berichttekst
/// zet.</para>
/// </remarks>
public static class SupportBody
{
    /// <summary>
    /// Tekens die in geen enkel supportbericht horen en die worden verwijderd.
    /// </summary>
    /// <remarks>
    /// <para>Twee groepen, met verschillende redenen.</para>
    ///
    /// <para><strong>Onzichtbare breedteloze tekens</strong> (U+200B, U+FEFF). Ze doen in proza niets
    /// en ze doen iets in een controle: een woordgrens is met een breedteloze ruimte erin te
    /// verbergen, en dit portaal heeft controles die op woordgrenzen zoeken
    /// (<c>KlantVangnetTests</c>). Een teken dat niets toevoegt en een controle kan omzeilen, hoort er
    /// niet te staan.</para>
    ///
    /// <para><strong>Tekens die de leesrichting omkeren</strong> (U+061C, U+200E, U+200F, U+202A t/m
    /// U+202E, U+2066 t/m U+2069). Dat is de klasse waar "Trojan Source" over gaat, en in een
    /// berichtendraad is die concreet: met een <em>right-to-left override</em> is de tekst ná het teken
    /// omgekeerd te laten renderen. Dat raakt niet alleen het eigen bericht — de omkering loopt door
    /// tot het einde van het tekstblok, dus een klant kan er de weergave van ónze regels eronder mee
    /// beïnvloeden, en een pad of een naam kan er anders uitzien dan hij is. Dit is het enige punt in
    /// dit bestand dat werkelijk over veiligheid gaat en niet over netheid.</para>
    ///
    /// <para><strong>En de regelscheiders die geen <c>\n</c> zijn</strong> (U+2028, U+2029). Ze breken
    /// een regel wél in de weergave en worden door <c>IndexOfAny("\r\n")</c> niet gezien. Zelfde reden
    /// als in <see cref="Mail.MailText"/>, waar ze om dezelfde reden apart staan.</para>
    ///
    /// <para>ZWJ (U+200D) en ZWNJ (U+200C) staan er <em>niet</em> bij, en dat is een keuze: die zijn
    /// nodig om een samengestelde emoji één teken te laten blijven, en ze weghalen haalt
    /// een gezinsemoji uiteen in drie losse mensen. Ze zijn onzichtbaar maar niet misleidend —
    /// ze keren geen leesrichting om en verbergen geen woordgrens die iets betekent.</para>
    /// </remarks>
    /// <remarks>
    /// De tekens staan als escape-reeks en niet als letterlijk teken. Dat is geen stijl: een bestand
    /// met een <em>right-to-left override</em> erin leest zelf verkeerd in een editor en in een
    /// pull request, en dan is de regel die het teken weghaalt de regel die niemand kan nakijken.
    /// </remarks>
    private static readonly SearchValues<char> Dropped = SearchValues.Create(
        "\u200B\uFEFF"
        + "\u061C\u200E\u200F\u202A\u202B\u202C\u202D\u202E"
        + "\u2066\u2067\u2068\u2069"
        + "\u2028\u2029");

    /// <summary>
    /// Maakt van een ingetypte of opgeslagen tekst de tekst zoals hij in een bubbel mag staan.
    /// </summary>
    /// <param name="text">De tekst uit een formulier of uit een document.</param>
    /// <returns>De geschoonde tekst. Nooit <c>null</c>, mogelijk leeg.</returns>
    /// <remarks>
    /// <para>De stappen, in deze volgorde en met de reden:</para>
    /// <list type="number">
    ///   <item><description>
    ///     <c>\r\n</c> en een losse <c>\r</c> worden <c>\n</c>. Eerst, zodat de stap die
    ///     besturingstekens verwijdert de <c>\r</c> niet als losse regelovergang hoeft te
    ///     herkennen — dan zouden er twee opvattingen over "regelovergang" in dit bestand staan.
    ///   </description></item>
    ///   <item><description>
    ///     Een tab wordt een spatie. Niet verwijderd: dan plakken twee woorden aan elkaar. Een tab in
    ///     een bubbel is opmaak, en een bubbel heeft geen opmaak om weg te geven.
    ///   </description></item>
    ///   <item><description>
    ///     Besturingstekens (behalve <c>\n</c>) en de tekens uit <see cref="Dropped"/> verdwijnen.
    ///     <c>char.IsControl</c> dekt C0 én C1, dus NEL (U+0085) valt hieronder en hoeft niet apart.
    ///   </description></item>
    ///   <item><description>
    ///     Meer dan één lege regel achter elkaar wordt één lege regel. Een bericht dat met tweehonderd
    ///     lege regels begint duwt de rest van de draad uit beeld, en dat is geen opmaak maar een
    ///     bijwerking.
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="MessageTruncation.Shorten"/> op <see cref="SupportLimits.MaximumLength"/>, op een
    ///     grafeemgrens. Als laatste, want de stappen ervoor maken de tekst korter en niet langer — een
    ///     knip vooraan zou dus strenger zijn dan de grens belooft.
    ///   </description></item>
    /// </list>
    ///
    /// <para><strong>Deze functie staat op twee plekken in het pad, en dat is met opzet.</strong> Bij
    /// het schrijven, waar de tekst ontstaat, en in de projectie naar de bubbel, waar hij de HTML in
    /// gaat. Punt 13 zegt dat een knip op twee van de drie plekken geen knip is; hier zijn er twee, en
    /// de tweede dekt wat de eerste niet kan: een document dat langs een ander pad in de container
    /// terecht is gekomen. De identiteit van het portaal heeft schrijfrecht op de hele container
    /// <c>customers</c> — dat is de prijs van één container, en dit is een van de plekken waar hij
    /// betaald wordt.</para>
    /// </remarks>
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var newlines = 0;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (character == '\r')
            {
                // \r\n telt als één overgang; een losse \r ook.
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                character = '\n';
            }

            if (character == '\n')
            {
                newlines++;

                // Eén lege regel mag: twee overgangen achter elkaar. De derde en verder niet.
                if (newlines <= 2)
                {
                    builder.Append('\n');
                }

                continue;
            }

            newlines = 0;

            if (character == '\t')
            {
                builder.Append(' ');
                continue;
            }

            if (char.IsControl(character) || Dropped.Contains(character))
            {
                continue;
            }

            builder.Append(character);
        }

        return MessageTruncation.Shorten(builder.ToString().Trim(), SupportLimits.MaximumLength);
    }

    /// <summary>
    /// Controleert een ingetypt bericht.
    /// </summary>
    /// <param name="text">De tekst uit het formulier.</param>
    /// <returns><c>null</c> als hij klopt, anders de melding voor het formulier.</returns>
    /// <remarks>
    /// <para><strong>Te lang wordt geweigerd en niet stil afgekapt.</strong> Dezelfde keuze en dezelfde
    /// reden als bij <c>HourLimits.ValidateNote</c>: aan de leeskant is er een vangnet voor wat er al
    /// staat, maar hier zit een mens aan het toetsenbord, en stil afkappen zou zijn laatste alinea
    /// weggooien zonder dat hij het merkt. Bij een supportbericht is dat erger dan bij een urenregel —
    /// wat eraf valt kan de vraag zelf zijn.</para>
    ///
    /// <para>De lengte wordt op de <em>geschoonde</em> tekst gemeten. Zou hij op de ruwe tekst worden
    /// gemeten, dan kan een bericht geweigerd worden om tekens die er na het schonen niet meer in
    /// staan, en dan klopt het getal in de melding niet met wat de schrijver ziet.</para>
    /// </remarks>
    public static string? Validate(string? text)
    {
        var value = Clean(text);

        if (value.Length == 0)
        {
            return "Typ een bericht.";
        }

        // Shorten heeft in Clean al geknipt als het te lang was, en zet daar zijn markering achter.
        // Daarop toetsen is de enige manier om te zien dat er iets af zou gaan zonder de grens hier
        // een tweede keer op te schrijven.
        return value.EndsWith(MessageTruncation.Marker, StringComparison.Ordinal)
            ? $"Een bericht is maximaal {SupportLimits.MaximumLength} tekens. Dit bericht is langer; "
              + "splits het, of zet de details in een bijlage die je ons per mail stuurt."
            : null;
    }
}
