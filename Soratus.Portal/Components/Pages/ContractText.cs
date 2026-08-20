using System.Globalization;
using Soratus.Portal.Views;

namespace Soratus.Portal.Components.Pages;

/// <summary>
/// De woorden en de getalvormen die de contractschermen delen: de contractkaart (§3.5), het
/// operator-eiland eronder en het aanmaakformulier van een klant (§3.9).
/// </summary>
/// <remarks>
/// <para>Dit is presentatie en geen rekenwerk. Elk getal komt uit een viewmodel of uit een
/// formulierveld; hier wordt het alleen in de juiste vorm gezet. Dezelfde afspraak en dezelfde
/// plek als <see cref="Klant.AgentText"/>: een klasse in de paginamap, want het is geen component
/// en alleen deze schermen gebruiken hem.</para>
///
/// <para>Waarom hij bestaat: drie schermen lezen en schrijven dezelfde vier getallen (urenbundel,
/// uurtarief, opslagpercentage) en dezelfde datum. Zouden ze elk hun eigen parser en hun eigen
/// opmaak hebben, dan is "125,50" op het ene scherm een bedrag en op het andere niets — precies
/// het patroon dat in dit werk al drie keer met gekopieerde CSS is misgegaan.</para>
///
/// <para>De datumvorm (<c>dd-MM-yyyy</c>) en de getalcultuur (<c>nl-NL</c>) zijn die van
/// <see cref="Shared.TimeFormat"/>: datums door een expliciet patroon zodat de vorm niet met de
/// servercultuur meebeweegt, getallen door de Nederlandse cultuur zodat er een komma staat. Er
/// staat hier een eigen kalenderdatum-methode omdat <c>TimeFormat</c> alleen momenten kent en een
/// contract op een dag ingaat en niet op een tijdstip. Komt er een tweede scherm dat een
/// <see cref="DateOnly"/> toont, dan hoort die methode naar <c>TimeFormat</c> te verhuizen.</para>
/// </remarks>
internal static class ContractText
{
    /// <summary>De cultuur voor getallen in beeld en in een formulierveld: komma als decimaalteken.</summary>
    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>
    /// Wat er in een getalveld mag staan.
    /// </summary>
    /// <remarks>
    /// <strong>Zonder <c>AllowThousands</c></strong>, en dat is het hele punt van
    /// <see cref="TryNumber"/>. Met duizendtallen aan leest <c>nl-NL</c> "125.50" als
    /// honderdvijfentwintigduizendvijftig — en "125.50" is precies wat een browser teruggeeft voor
    /// een <c>type="number"</c>-veld waarin iemand 125,50 typte. Een tarief dat stil met honderd
    /// wordt vermenigvuldigd gaat de factuur in.
    /// </remarks>
    private const NumberStyles Styles = NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign;

    /// <summary>De twee tekens die een getal in een geheel deel en een staart kunnen splitsen.</summary>
    /// <remarks>
    /// Beide, en niet alleen de punt. Zie <see cref="IsThousandsGrouping"/>: het is niet het teken
    /// dat het geval maakt maar het aantal cijfers erachter.
    /// </remarks>
    private static readonly char[] Separators = ['.', ','];

    /// <summary>De ingangsdatum zoals een lezer hem leest.</summary>
    /// <param name="date">De datum, of <c>null</c> als er niets is vastgelegd.</param>
    /// <returns>Bijvoorbeeld <c>01-11-2025</c>, of <c>null</c> als er geen datum is.</returns>
    public static string? Date(DateOnly? date) =>
        date?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

    /// <summary>De ingangsdatum zoals een <c>type="date"</c>-veld hem wil hebben.</summary>
    /// <param name="date">De datum, of <c>null</c>.</param>
    /// <returns><c>yyyy-MM-dd</c>, of een lege tekst.</returns>
    /// <remarks>
    /// Dezelfde vorm die <see cref="Data.ContractEdit.StartsOn"/> verlangt en die in het document
    /// staat. Er wordt hier dus niets omgezet dat straks weer terug moet.
    /// </remarks>
    public static string IsoDate(DateOnly? date) =>
        date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>De urenbundel in woorden.</summary>
    /// <param name="hours">De bundel per maand, of <c>null</c> als er niets is vastgelegd.</param>
    /// <returns>
    /// Bijvoorbeeld <c>8 uur per maand</c>, of <c>null</c> als er niets is vastgelegd — dan zet
    /// <c>FormField</c> zijn streepje neer.
    /// </returns>
    /// <remarks>
    /// <para><strong>Drie uitkomsten en niet twee.</strong> Zolang het veld een <c>decimal</c> was
    /// moest deze methode kiezen tussen "niet vastgelegd" en "nul", want dat was in de opslag
    /// dezelfde waarde; ze koos "geen urenbundel" en zei daarmee over een leeg contract iets wat er
    /// niet stond. Nu <see cref="Data.ContractDocument.BundledHours"/> nullable is, is <c>null</c>
    /// géén tekst: de kaart laat het veld leeg zoals bij elk ander niet-vastgelegd veld.</para>
    ///
    /// <para>Nul is wél een tekst, en een letterlijke: <c>0 uur per maand</c>. Dat is een afspraak
    /// die iemand heeft opgeschreven — alle uren gaan per uur — en die hoort als getal op de kaart
    /// te staan en niet als interpretatie.</para>
    /// </remarks>
    public static string? Hours(decimal? hours) =>
        hours is { } value ? $"{Number(value)} uur per maand" : null;

    /// <summary>Het uurtarief buiten de bundel, in woorden.</summary>
    /// <param name="rate">Het tarief, of <c>null</c> als er niets is vastgelegd.</param>
    /// <param name="isInternal">Of dit de interne beheerklant is.</param>
    /// <returns>
    /// Bijvoorbeeld <c>€ 125,00 per uur buiten bundel</c>, of <c>null</c> als er niets is
    /// vastgelegd.
    /// </returns>
    /// <remarks>
    /// <para>De zin wordt hier gemaakt en staat niet als tweede veld in de opslag. Zie de opmerking
    /// bij <see cref="Data.ContractDocument"/>: de mockup heeft naast <c>uurTarief</c> ook een
    /// kant-en-klare tekst <c>tarief</c>, en twee velden over hetzelfde bedrag kunnen elkaar
    /// tegenspreken.</para>
    ///
    /// <para>Zelfde drie uitkomsten als bij <see cref="Hours"/>: <c>null</c> is leeg en nul is
    /// <c>€ 0,00 per uur buiten bundel</c>. De oude tekst "geen tarief buiten bundel" is weg, want
    /// die las als beide dingen tegelijk — "we rekenen niets" en "we hebben niets afgesproken" — en
    /// dat is precies het verschil dat een klant wil weten voordat hij extra werk aanvraagt.</para>
    ///
    /// <para><c>isInternal</c> gaat nog steeds voor. Bij de interne beheerklant is er niets om door
    /// te belasten, en dan is elk bedrag misleidend — ook een leeg veld, want dat suggereert dat
    /// iemand vergeten is een tarief in te vullen.</para>
    /// </remarks>
    public static string? Rate(decimal? rate, bool isInternal) => isInternal
        ? "intern — niet doorbelast"
        : rate is { } value
            ? $"€ {Amount(value)} per uur buiten bundel"
            : null;

    /// <summary>Een getal in Nederlandse vorm, zonder overbodige nullen.</summary>
    /// <param name="value">De waarde.</param>
    /// <returns>Bijvoorbeeld <c>7,5</c>.</returns>
    public static string Number(decimal value) => value.ToString("0.##", Dutch);

    /// <summary>Een bedrag in Nederlandse vorm, met twee decimalen.</summary>
    /// <param name="value">De waarde.</param>
    /// <returns>Bijvoorbeeld <c>125,00</c>.</returns>
    public static string Amount(decimal value) => value.ToString("0.00", Dutch);

    /// <summary>
    /// Een getal zoals het in een bewerkbaar veld hoort te staan.
    /// </summary>
    /// <param name="value">De waarde uit het viewmodel, of <c>null</c> als er niets is vastgelegd.</param>
    /// <returns>De tekst, of leeg als er niets is vastgelegd.</returns>
    /// <remarks>
    /// <para><strong>Nul wordt "0" en niet leeg.</strong> Dat was het omgekeerde toen het veld een
    /// <c>decimal</c> was: nul werd leeg gezet omdat leeg bij het bewaren weer nul opleverde, dus
    /// veranderde er niets aan de betekenis. Met een <c>decimal?</c> gaat die vlieger niet meer op —
    /// leeg wordt nu <c>null</c>. Zou nul hier leeg blijven, dan verandert een operator die een
    /// contract met een afgesproken nul opent en op Bewaren drukt die nul stil in "niet vastgelegd",
    /// zonder één toetsaanslag en zonder dat de wijzigingslijst er iets over zegt.</para>
    ///
    /// <para>Alleen <c>null</c> geeft dus een leeg veld, en dat is ook wat het veld betekent.</para>
    /// </remarks>
    public static string Editable(decimal? value) =>
        value?.ToString("0.##", Dutch) ?? string.Empty;

    /// <summary>
    /// Leest een getal uit een formulierveld.
    /// </summary>
    /// <param name="text">Wat er in het veld staat.</param>
    /// <param name="value">
    /// Het getal, of <c>null</c> als het veld leeg is. Bij onleesbare invoer ook <c>null</c>, en dan
    /// is de uitkomst <c>false</c>.
    /// </param>
    /// <returns><c>true</c> als het veld leeg is of een leesbaar getal bevat.</returns>
    /// <remarks>
    /// <para>Eerst Nederlands (komma), dan invariant (punt). Beide zonder duizendtallen — zie
    /// <see cref="Styles"/>. Gevolg: "125,50" en "125.50" zijn hetzelfde bedrag, en "1.250,50"
    /// wordt geweigerd in plaats van stil verkeerd gelezen. Dat is de bedoeling: een geweigerde
    /// invoer levert een melding onder het veld op, een stil verkeerd gelezen bedrag levert een
    /// factuur op.</para>
    ///
    /// <para><strong>Drie cijfers achter één scheidingsteken wordt geweigerd</strong>, ook als het
    /// getal in één van de twee culturen prima leest. Zie <see cref="IsThousandsGrouping"/>: dat is
    /// het enige geval waarin de dubbele cultuur een duizendscheiding niet kan onderscheiden van een
    /// decimaalteken, en het verschil is een factor duizend. Dat geval is geen gok waard.</para>
    ///
    /// <para><strong>Leeg is <c>null</c> en niet nul.</strong> Dit is de plek waar de bevinding
    /// werkelijk zat. Deze methode gaf een <c>out decimal</c> en zette een leeg veld op <c>0m</c>;
    /// de aanroepers schreven dat getal daarna in het contract. Een operator die bij het aanmaken van
    /// een klant het tarief nog niet wist, legde daarmee <c>uurTarief: 0</c> vast — een bedrag dat
    /// hij niet heeft ingevoerd, dat als afspraak in de opslag staat en dat in een berekening als
    /// nul meetelt. Leeg blijft geen fout: een klant in onboarding heeft nog geen tarief, en een
    /// verplicht getalveld levert dan een verzonnen bedrag op. Maar leeg is nu ook geen waarde.</para>
    ///
    /// <para>Bij onleesbare invoer komt er <c>null</c> uit in plaats van nul, om dezelfde reden. De
    /// aanroeper hoort op <c>false</c> te letten — <see cref="NumberError"/> staat ernaast — maar als
    /// hij dat vergeet, belandt er geen bedrag in de opslag dat niemand heeft getypt.</para>
    /// </remarks>
    public static bool TryNumber(string? text, out decimal? value)
    {
        value = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var clean = Clean(text);

        // Vóór het parsen en niet erna: de parser geeft hier een uitkomst, en juist die
        // uitkomst is het probleem. "1.250" leest invariant als 1,25 — een factor duizend mis,
        // stil, en met true als antwoord.
        if (IsThousandsGrouping(clean))
        {
            return false;
        }

        if (decimal.TryParse(clean, Styles, Dutch, out var parsed)
            || decimal.TryParse(clean, Styles, CultureInfo.InvariantCulture, out parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    /// <summary>De melding onder een getalveld waarin iets onleesbaars staat.</summary>
    /// <param name="voorbeeld">Een waarde die het wél haalt, bijvoorbeeld <c>125,50</c>.</param>
    /// <param name="invoer">
    /// Wat er in het veld stond, of <c>null</c>. Geef dit mee: dan noemt de melding de
    /// dubbelzinnigheid bij naam in plaats van om "een getal" te vragen terwijl er al een getal staat.
    /// </param>
    /// <returns>De melding.</returns>
    /// <remarks>
    /// <para><strong>Twee meldingen, één regel.</strong> Welke van de twee het wordt, bepaalt
    /// dezelfde <see cref="IsThousandsGrouping"/> waarmee <see cref="TryNumber"/> weigert. Zou deze
    /// methode zelf een oordeel over de invoer vellen, dan bestonden er twee definities van "dit is een
    /// duizendscheiding" en kon er een melding staan die niet bij de reden van de weigering
    /// hoort.</para>
    ///
    /// <para><paramref name="invoer"/> is optioneel, en dat is een afruil. Een aanroeper die hem
    /// weglaat krijgt de algemene melding; die zegt óók dat het scheidingsteken voor duizenden
    /// weg moet, dus hij is niet onwaar — alleen minder scherp bij precies het geval waar hij het
    /// meest te zeggen heeft.</para>
    /// </remarks>
    public static string NumberError(string voorbeeld, string? invoer = null) =>
        invoer is not null && IsThousandsGrouping(Clean(invoer))
            ? "Drie cijfers achter een punt of een komma kan een scheidingsteken voor duizenden " +
              "zijn of een decimaalteken, en het verschil is een factor duizend. Laat het " +
              "scheidingsteken weg (1250) of zet een komma voor de decimalen (1250,5)."
            : $"Vul een getal in, bijvoorbeeld {voorbeeld}. Een punt of een komma mag, maar laat " +
              "het scheidingsteken voor duizenden weg.";

    /// <summary>
    /// Haalt weg wat in een getalveld mag staan zonder dat het er een ander getal van maakt.
    /// </summary>
    /// <param name="text">De invoer.</param>
    /// <returns>De invoer zonder eenheid en zonder spaties.</returns>
    /// <remarks>
    /// <para>Het euro- en procentteken en elke soort spatie eraf. Die staan naast het veld als eenheid
    /// (zie <c>FormField.Unit</c>), maar wie een bedrag uit een mail kopieert neemt ze mee. De eerste
    /// van de twee spaties is een vaste spatie: die zet Windows in bedragen.</para>
    ///
    /// <para>Staat als eigen methode omdat <see cref="NumberError"/> naar dezelfde opgeschoonde tekst
    /// moet kijken als <see cref="TryNumber"/>. Zouden die twee elk hun eigen schoonmaak doen, dan
    /// weigert de één "€ 1.250" en legt de ander uit dat er geen getal staat.</para>
    /// </remarks>
    private static string Clean(string text) => text
        .Replace("€", string.Empty, StringComparison.Ordinal)
        .Replace("%", string.Empty, StringComparison.Ordinal)
        .Replace(" ", string.Empty, StringComparison.Ordinal)
        .Replace(" ", string.Empty, StringComparison.Ordinal)
        .Trim();

    /// <summary>
    /// Of hier een scheidingsteken voor duizenden staat waar de parser een decimaalteken van maakt.
    /// </summary>
    /// <param name="clean">De opgeschoonde invoer, uit <see cref="Clean"/>.</param>
    /// <returns><c>true</c> als er precies drie cijfers achter één enkel scheidingsteken staan.</returns>
    /// <remarks>
    /// <para><strong>Het aantal cijfers achter het scheidingsteken maakt het onderscheid, niet het
    /// teken zelf.</strong> Een groep van een duizendscheiding is per definitie exact drie cijfers
    /// lang. Daaruit volgt de hele regel:</para>
    /// <list type="bullet">
    ///   <item><description>één of twee cijfers ("125.5", "125.50") kán geen
    ///   duizendscheiding zijn — en die tweede vorm is precies wat een browser teruggeeft voor een
    ///   <c>type="number"</c>-veld waarin iemand 125,50 typte, dus die móét
    ///   doorkomen;</description></item>
    ///   <item><description>vier of meer ("1.2500") kán het ook niet zijn, want dan had er nog een
    ///   scheidingsteken tussen gestaan;</description></item>
    ///   <item><description>nul cijfers ("125.") is een losse punt aan het eind en geen groep; die
    ///   leest invariant als 125, en dat is ook wat er staat;</description></item>
    ///   <item><description>exact drie ("1.250", "12.500") kán beide zijn, en dáár wordt
    ///   niet gegokt;</description></item>
    ///   <item><description>meer dan één scheidingsteken ("1.250,50", "1.250.000") valt hier
    ///   niet onder en wordt door de parser zelf al geweigerd: twee scheidingstekens in één
    ///   getal kan in geen van beide culturen.</description></item>
    /// </list>
    ///
    /// <para><strong>Waarom de komma net zo goed als de punt.</strong> Één regel is beter dan
    /// twee, en een tweede is niet nodig: bij alle drie de getallen die deze parser bedient — een
    /// urenbundel, een uurtarief in euro's en een opslagpercentage — is een derde decimaal zinloos.
    /// Een waarde met exact drie cijfers achter het scheidingsteken is dus nooit een geldige waarde van
    /// zo'n veld, welk teken er ook staat. Alleen de punt afvangen zou het gat open laten voor een
    /// bedrag uit een Engelse bron: <c>nl-NL</c> leest "1,250" als 1,25, en dat is dezelfde factor
    /// duizend de andere kant op. Bijkomend: deze methode hoeft niet te weten welk veld hem aanroept, en
    /// dat is wat één parser voor drie schermen mogelijk maakt.</para>
    ///
    /// <para><strong>Wat het kost, eerlijk.</strong> "0,500" en "12,500" zijn in het Nederlands te lezen
    /// als 0,5 en 12,5 met een overbodige nul erachter, en die worden nu geweigerd. Dat is de prijs, en
    /// hij is klein en zichtbaar: de operator ziet een melding en typt "0,5". De omgekeerde fout is
    /// onzichtbaar en staat op een factuur.</para>
    /// </remarks>
    private static bool IsThousandsGrouping(string clean)
    {
        var index = clean.LastIndexOfAny(Separators);

        // Geen scheidingsteken, of meer dan één: dan is dit dit geval niet.
        if (index < 0 || clean.IndexOfAny(Separators) != index)
        {
            return false;
        }

        var after = clean.AsSpan(index + 1);

        if (after.Length != 3)
        {
            return false;
        }

        foreach (var character in after)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Hoeveel mensen er in de toegangslijst staan, in woorden.</summary>
    /// <param name="count">Het aantal regels.</param>
    /// <returns>Bijvoorbeeld <c>3 personen</c>.</returns>
    /// <remarks>
    /// Staat hier omdat beide contractschermen dezelfde regel in hun kaartkop zetten — de
    /// leesvorm van de klant en het bewerkbare eiland van de operator. Twee keer hetzelfde
    /// enkelvoud/meervoud is één keer te weinig nagedacht en twee keer om aan te passen.
    /// </remarks>
    public static string People(int count) =>
        count == 1 ? "1 persoon" : $"{count} personen";

    /// <summary>
    /// Of deze persoon in Entra ID kan aanmelden, in één woord.
    /// </summary>
    /// <param name="state">De toestand uit het viewmodel.</param>
    /// <returns><c>onbekend</c>, <c>actief</c> of <c>ontbreekt</c>.</returns>
    /// <remarks>
    /// Drie woorden voor drie toestanden, en "onbekend" is er één van. Vandaag is het de enige die
    /// voorkomt: het portaal heeft geen leesrecht op Entra. Zie
    /// <see cref="ContractNotice.EntraStateUnknown"/> — er staat dus geen suggestie op het scherm
    /// en geen knop die belooft dat het portaal de uitnodiging kan versturen.
    /// </remarks>
    public static string AccessState(AccessEntraState state) => state switch
    {
        AccessEntraState.Active => "actief",
        AccessEntraState.Missing => "ontbreekt",
        _ => "onbekend",
    };

    /// <summary>De tooltip bij <see cref="AccessState"/>: wat dat woord betekent.</summary>
    /// <param name="state">De toestand.</param>
    /// <returns>De tooltip.</returns>
    public static string AccessStateTitle(AccessEntraState state) => state switch
    {
        AccessEntraState.Active => "de rol staat in Entra ID: deze persoon kan aanmelden",
        AccessEntraState.Missing => "de rol staat niet in Entra ID: aanmelden kan nog niet",
        _ => "het portaal heeft geen leesrecht op Entra ID en kan dit niet zien",
    };
}
