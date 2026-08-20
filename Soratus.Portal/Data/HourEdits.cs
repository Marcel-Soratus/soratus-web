using System.Globalization;

namespace Soratus.Portal.Data;

/// <summary>
/// De grenzen waarbinnen een urenregel moet vallen.
/// </summary>
/// <remarks>
/// Eén plek, want de boeking, de correctie en straks het aannamepad van een koppeling toetsen alle
/// drie hetzelfde. Drie kopieën van "hoeveel uren mag een regel" gaan uit de pas lopen, en dan is er
/// een pad waarlangs een getal binnenkomt dat een ander pad zou weigeren.
/// </remarks>
public static class HourLimits
{
    /// <summary>
    /// Het grootste aantal uren op één regel.
    /// </summary>
    /// <remarks>
    /// <para>Een etmaal minus wat slaap. Deze grens houdt niet fraude tegen maar een typefout: een
    /// vergeten decimaalteken maakt van 2,5 uur 25 uur, en van 12 uur 120. Bij een uurtarief van
    /// € 125 is dat verschil op één regel meer dan de hele bundel van de meeste klanten.</para>
    ///
    /// <para>Er is bewust geen grens per maand. Een maand met 200 uur is ongebruikelijk maar niet
    /// onmogelijk, en een grens die je in een drukke maand tegenkomt is een grens die iemand gaat
    /// omzeilen door de uren over twee maanden te verdelen — en dan staat de administratie verkeerd
    /// in plaats van dat de invoer geweigerd wordt.</para>
    /// </remarks>
    public const decimal MaximumPerEntry = 16m;

    /// <summary>Het langste toegestane omschrijvingsveld.</summary>
    /// <remarks>
    /// De omschrijving is één regel op het scherm van de klant. Ruim genoeg voor een volle zin en kort
    /// genoeg om te voorkomen dat er een halve pagina in belandt; dat laatste is niet theoretisch —
    /// zie punt 13 van de afwijkingennotitie, waar in <c>msg</c> 3349 tekens stacktrace stond.
    /// </remarks>
    public const int MaximumNoteLength = 400;

    /// <summary>Het langste toegestane <c>by</c>-veld.</summary>
    public const int MaximumByLength = 120;

    /// <summary>
    /// Controleert een omschrijving.
    /// </summary>
    /// <param name="note">De omschrijving.</param>
    /// <returns><c>null</c> als hij klopt, anders de melding voor het formulier.</returns>
    /// <remarks>
    /// <para><strong>Een regelovergang wordt hier geweigerd en niet stil afgekapt.</strong> Aan de
    /// leeskant knipt <c>CustomerHourRow</c> de omschrijving af op de eerste regelovergang, om dezelfde
    /// reden als bij een logregel. Maar dat is een vangnet voor wat er al staat en voor wat langs een
    /// ander pad binnenkomt. Hier, waar een operator zelf typt, hoort een meerregelige omschrijving
    /// een melding op te leveren: stil afkappen zou zijn tweede regel weggooien zonder dat hij het
    /// merkt.</para>
    /// </remarks>
    public static string? ValidateNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return "Vul een korte omschrijving in. De klant leest deze regel terug op zijn " +
                   "urenspecificatie.";
        }

        var value = note.Trim();

        if (value.Length > MaximumNoteLength)
        {
            return $"Een omschrijving is maximaal {MaximumNoteLength} tekens. Deze is " +
                   $"{value.Length} tekens; kort hem in tot één zin.";
        }

        return value.AsSpan().IndexOfAny('\n', '\r') >= 0
            ? "Een omschrijving is één regel. Haal de regelovergang eruit."
            : null;
    }

    /// <summary>
    /// Controleert het <c>by</c>-veld.
    /// </summary>
    /// <param name="by">Wie de uren op zijn naam krijgt.</param>
    /// <returns><c>null</c> als het klopt, anders de melding.</returns>
    public static string? ValidateBy(string? by)
    {
        if (string.IsNullOrWhiteSpace(by))
        {
            return "Vul in wie de uren heeft geboekt.";
        }

        return by.Trim().Length > MaximumByLength
            ? $"Dit veld is maximaal {MaximumByLength} tekens."
            : null;
    }
}

/// <summary>
/// Wat een operator invult om uren te boeken (§3.6, "Uren boeken").
/// </summary>
/// <remarks>
/// <para><strong>Er is geen statusveld, en dat is de manier waarop §5 hier wordt afgedwongen.</strong>
/// Die regel zegt dat alles wat een agent of koppeling inschiet als te fiatteren landt. Een operator
/// die in het portaal boekt is geen van beide: hij ís het akkoord van Soratus, en de mockup boekt
/// zo'n regel dan ook meteen als gefiatteerd. Zou de status hier een parameter zijn, dan bestond er
/// een aanroep waarmee een koppeling zichzelf fiatteert — en die aanroep zou compileren.</para>
///
/// <para><strong>Er is ook geen datumveld.</strong> §3.6 noemt maand, uren, categorie, geboekt door en
/// omschrijving, en geen datum. De datum wordt de dag van invoeren; de <em>maand</em> is wat de
/// operator kiest, en dat is het veld dat ertoe doet — werk van 31 juli dat op 1 augustus wordt
/// geboekt hoort op juli. Een apart datumveld zou de vraag oproepen wat er gebeurt als datum en maand
/// niet bij elkaar horen, en daar is geen goed antwoord op.</para>
///
/// <para>De categorie <see cref="HourCategories.Correction"/> kan hier niet: een correctie is een
/// eigen aanroep met een eigen type. Zie <see cref="HourCorrection"/>.</para>
/// </remarks>
public sealed record HourBooking
{
    /// <summary>De maand waarop de uren worden geboekt, als <c>yyyy-MM</c>.</summary>
    public string Month { get; init; } = string.Empty;

    /// <summary>Het aantal uren. Groter dan nul.</summary>
    public decimal Hours { get; init; }

    /// <summary>De categorie. Zie <see cref="HourCategories.Bookable"/>.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Wie de uren op zijn naam krijgt (§6 <c>by</c>).</summary>
    public string By { get; init; } = string.Empty;

    /// <summary>De omschrijving: één regel, leesbaar voor de klant.</summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>
    /// Controleert de invoer.
    /// </summary>
    /// <returns><c>null</c> als het klopt, anders de melding voor het formulier.</returns>
    public string? Validate()
    {
        if (HourMonths.Validate(Month) is { } monthError)
        {
            return monthError;
        }

        if (Hours <= 0m)
        {
            return "Vul een aantal uren groter dan nul in. Moet het totaal naar beneden, dan is dat " +
                   "een correctie en geen boeking.";
        }

        if (Hours > HourLimits.MaximumPerEntry)
        {
            return $"Meer dan {HourLimits.MaximumPerEntry:0.#} uur op één regel kan niet. Klopt dit, " +
                   "dan boek je het als twee regels; klopt het niet, dan staat het decimaalteken " +
                   "verkeerd.";
        }

        if (!HourCategories.IsBookable(Category))
        {
            return $"'{Category}' is geen categorie om op te boeken. Kies " +
                   $"{string.Join(", ", HourCategories.Bookable)}.";
        }

        return HourLimits.ValidateBy(By) ?? HourLimits.ValidateNote(Note);
    }
}

/// <summary>
/// Een handmatige correctie op het maandtotaal (§3.6).
/// </summary>
/// <remarks>
/// <para><strong>Dit is besluit 16 van de fase-0-afwijkingen in typevorm.</strong> §3.6 vraagt twee
/// dingen van hetzelfde getal: het maandtotaal is de som van de gefiatteerde regels, én een handmatige
/// correctie wordt als afwijking in de tooltip gemeld. Dat kan niet van één getal — een correctie die
/// het totaal overschrijft maakt het geen som meer. Een correctie wordt daarom nóg een gefiatteerde
/// urenregel, met bron <see cref="HourEntrySource.Portal"/> en categorie
/// <see cref="HourCategories.Correction"/>. Het totaal blijft een zuivere som, de correctie is een rij
/// in de specificatie, en de tooltip heeft iets te melden:
/// <see cref="HourBalance.CorrectionHours"/>.</para>
///
/// <para><strong>Een eigen type en niet een vlag op <see cref="HourBooking"/>, om precies één
/// verschil: <see cref="Hours"/> mag hier negatief zijn.</strong> Dat is wat een correctie naar
/// beneden mogelijk maakt zonder een gefiatteerde regel te wijzigen — en dat laatste mag niet, want
/// dan is de som van vandaag niet meer de som van gisteren. Met één type en een vlag zou de
/// controle op "groter dan nul" een <c>if</c> op die vlag worden, en dan kan een gewone boeking
/// negatief zijn zodra iemand die <c>if</c> verkeerd schrijft.</para>
///
/// <para><strong>De omschrijving is verplicht en er staat waarom in de melding.</strong> §9 van de
/// spec houdt open of er per correctie een audittrail komt (wie, wanneer, waarom). Met dit besluit
/// vervalt die vraag: de correctie is zelf een document met <c>createdAt</c>, <c>createdBy</c> en
/// deze omschrijving. Dat is de audittrail, en hij staat op het scherm in plaats van in een tabel die
/// niemand opvraagt.</para>
/// </remarks>
public sealed record HourCorrection
{
    /// <summary>De maand die gecorrigeerd wordt, als <c>yyyy-MM</c>.</summary>
    public string Month { get; init; } = string.Empty;

    /// <summary>
    /// Het aantal uren dat erbij komt. Negatief om het maandtotaal te verlagen. Nooit nul.
    /// </summary>
    public decimal Hours { get; init; }

    /// <summary>Wie de correctie op zijn naam krijgt.</summary>
    public string By { get; init; } = string.Empty;

    /// <summary>Waarom er gecorrigeerd wordt. Verplicht.</summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>
    /// Controleert de invoer.
    /// </summary>
    /// <returns><c>null</c> als het klopt, anders de melding voor het formulier.</returns>
    public string? Validate()
    {
        if (HourMonths.Validate(Month) is { } monthError)
        {
            return monthError;
        }

        if (Hours == 0m)
        {
            return "Een correctie van nul uur verandert niets. Vul in hoeveel uren erbij of eraf " +
                   "moeten.";
        }

        if (Math.Abs(Hours) > HourLimits.MaximumPerEntry)
        {
            return $"Een correctie van meer dan {HourLimits.MaximumPerEntry:0.#} uur in één keer kan " +
                   "niet. Splits hem, of kijk of het decimaalteken goed staat.";
        }

        return HourLimits.ValidateBy(By) ?? HourLimits.ValidateNote(Note);
    }
}

/// <summary>
/// Het afwijzen van één te fiatteren urenregel (§3.6).
/// </summary>
/// <remarks>
/// De reden is verplicht. Zie <see cref="HourEntryDocument.RejectionReason"/>: de regel blijft staan,
/// en een afgewezen regel zonder reden is over een maand niet meer te verklaren tegenover de klant die
/// vraagt waarom er iets niet op zijn factuur staat.
/// </remarks>
public sealed record HourRejection
{
    /// <summary>De id van de regel, zoals hij op het scherm stond.</summary>
    public string EntryId { get; init; } = string.Empty;

    /// <summary>Waarom de regel wordt afgewezen. Verplicht.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// De etag van de regel zoals hij op het scherm stond, of <c>null</c> om hem af te wijzen zoals
    /// hij nu is.
    /// </summary>
    public string? BasedOnETag { get; init; }

    /// <summary>
    /// Controleert de invoer.
    /// </summary>
    /// <returns><c>null</c> als het klopt, anders de melding.</returns>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(EntryId))
        {
            return "Er is geen urenregel meegegeven om af te wijzen.";
        }

        if (string.IsNullOrWhiteSpace(Reason))
        {
            return "Vul in waarom deze regel wordt afgewezen. De regel blijft staan met deze reden " +
                   "erbij, zodat later na te gaan is waarom hij niet op de factuur staat.";
        }

        return Reason.Trim().Length > HourLimits.MaximumNoteLength
            ? $"Een reden is maximaal {HourLimits.MaximumNoteLength} tekens."
            : null;
    }
}

/// <summary>
/// Welke urenregels er worden opgehaald: één maand, of een heel jaar (§3.6).
/// </summary>
/// <remarks>
/// <para>§3.6 kent precies twee weergaven: standaard de huidige maand, en met "Alle maanden" de
/// historie plus het jaartotaal. Dit type kent daarom precies die twee vormen en geen derde. Er is
/// bewust geen "haal alles op": urenregels groeien onbeperkt door, en een query zonder grens is een
/// query die pas over drie jaar te duur wordt — als niemand meer weet waar hij vandaan komt.</para>
///
/// <para>Er zijn geen publieke setters en geen constructor, alleen de twee fabrieksmethoden. Zo
/// bestaat de toestand "geen van beide gevuld" niet.</para>
/// </remarks>
public sealed record HoursQuery
{
    private HoursQuery(string? month, int year)
    {
        Month = month;
        Year = year;
    }

    /// <summary>De maand als <c>yyyy-MM</c>, of <c>null</c> bij een jaarquery.</summary>
    public string? Month { get; }

    /// <summary>
    /// Het jaar waarover wordt gelezen. Bij een maandquery het jaar van die maand.
    /// </summary>
    /// <remarks>
    /// Staat er ook bij een maandquery, zodat het jaartotaal en het maandtotaal dezelfde jaargrens
    /// gebruiken en de weergave niet zelf het jaar uit een tekst hoeft te vissen.
    /// </remarks>
    public int Year { get; }

    /// <summary>Of dit een enkele maand betreft.</summary>
    public bool IsSingleMonth => Month is not null;

    /// <summary>
    /// Alleen deze maand.
    /// </summary>
    /// <param name="month">De maand als <c>yyyy-MM</c>.</param>
    /// <returns>De query.</returns>
    /// <exception cref="ArgumentException">Als de maand niet de vorm <c>yyyy-MM</c> heeft.</exception>
    /// <remarks>
    /// Werpt in plaats van een melding terug te geven, en daarin wijkt dit af van de
    /// <c>Validate</c>-methoden hiernaast. Die krijgen invoer uit een formulier; deze maand komt uit
    /// de URL of uit de klok van het portaal, en een onleesbare waarde is dan geen gebruikersfout maar
    /// een fout in de aanroeper.
    /// </remarks>
    public static HoursQuery ForMonth(string month)
    {
        if (HourMonths.Parse(month) is not { } parsed)
        {
            throw new ArgumentException(
                $"'{month}' is geen maand in de vorm jjjj-mm.",
                nameof(month));
        }

        return new HoursQuery(month.Trim(), parsed.Year);
    }

    /// <summary>
    /// Alle maanden van dit jaar.
    /// </summary>
    /// <param name="year">Het jaartal.</param>
    /// <returns>De query.</returns>
    public static HoursQuery ForYear(int year) => new(month: null, year);

    /// <summary>
    /// De eerste maandsleutel die binnen deze query valt.
    /// </summary>
    /// <returns>De sleutel.</returns>
    internal string FirstMonth() =>
        Month ?? string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-01");

    /// <summary>
    /// De laatste maandsleutel die binnen deze query valt.
    /// </summary>
    /// <returns>De sleutel.</returns>
    internal string LastMonth() =>
        Month ?? string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-12");
}

/// <summary>
/// Welke overgangen een urenregel mag maken, en waarom de andere niet.
/// </summary>
/// <remarks>
/// <para>Puur, en één plek. De schrijfkant gebruikt dit om te weigeren, en de weergave om te bepalen
/// of er een knop hoort te staan. Zouden die twee elk hun eigen regel hebben, dan staat er een knop
/// die een melding oplevert — of erger, er staat geen knop bij iets wat wel mag.</para>
/// </remarks>
public static class HourEntryTransitions
{
    /// <summary>
    /// Of deze regel gefiatteerd mag worden, en anders waarom niet.
    /// </summary>
    /// <param name="status">De huidige stand.</param>
    /// <returns><c>null</c> als het mag, anders de melding.</returns>
    /// <remarks>
    /// Een afgewezen regel mag alsnog gefiatteerd worden. Afwijzen is een besluit van een mens en
    /// mensen klikken mis; zou dat onomkeerbaar zijn, dan is de enige uitweg de regel opnieuw laten
    /// inschieten door de koppeling — en dat kan niet, want de idempotentiesleutel botst op het
    /// document dat er al staat.
    /// </remarks>
    public static string? WhyNotApprove(HourEntryStatus status) => status switch
    {
        HourEntryStatus.Pending or HourEntryStatus.Rejected => null,
        _ => "Deze regel is al gefiatteerd.",
    };

    /// <summary>
    /// Of deze regel afgewezen mag worden, en anders waarom niet.
    /// </summary>
    /// <param name="status">De huidige stand.</param>
    /// <returns><c>null</c> als het mag, anders de melding.</returns>
    /// <remarks>
    /// <para><strong>Een gefiatteerde regel kan niet meer worden afgewezen, en dat is een besluit met
    /// een prijs.</strong> Het maandtotaal is de som van de gefiatteerde regels; kan een regel later
    /// uit die som verdwijnen, dan is het totaal van vandaag niet het totaal van gisteren, en dan
    /// verschilt een conceptfactuur van de maand waarover hij gaat zonder dat er iets aan is
    /// toegevoegd. Terugdraaien gebeurt daarom door er een correctie tegenover te zetten — een tweede
    /// rij die zichtbaar is en zichzelf verklaart — en niet door de eerste te laten verdwijnen.</para>
    ///
    /// <para>De prijs is dat een operator die per ongeluk fiatteert een extra handeling moet doen.
    /// Dat is een bewuste ruil: één handeling meer tegen een som die niet met terugwerkende kracht kan
    /// veranderen.</para>
    /// </remarks>
    public static string? WhyNotReject(HourEntryStatus status) => status switch
    {
        HourEntryStatus.Pending or HourEntryStatus.Rejected => null,
        _ => "Deze regel is al gefiatteerd en telt mee in het maandtotaal. Een gefiatteerd uur " +
             "verdwijnt niet meer uit de som; zet er een correctie tegenover.",
    };

    /// <summary>Of er bij deze regel een fiatteerknop hoort te staan.</summary>
    /// <param name="status">De huidige stand.</param>
    /// <returns><c>true</c> als fiatteren mag.</returns>
    public static bool CanApprove(HourEntryStatus status) => WhyNotApprove(status) is null;

    /// <summary>Of er bij deze regel een afwijsknop hoort te staan.</summary>
    /// <param name="status">De huidige stand.</param>
    /// <returns><c>true</c> als afwijzen mag.</returns>
    public static bool CanReject(HourEntryStatus status) => WhyNotReject(status) is null;
}
