using Soratus.Portal.Data;

namespace Soratus.Portal.Components.Pages.Klant;

/// <summary>
/// Wat er in het boekformulier van het urenscherm staat (§3.6, "Uren boeken"), als tekst zoals de
/// browser het oplevert.
/// </summary>
/// <remarks>
/// <para><strong>Alles is <c>string</c></strong>, om dezelfde reden als bij
/// <see cref="Pages.NewCustomerForm"/>: dit model wordt door een POST gevuld en een POST levert
/// tekst. Omzetten naar <c>decimal</c> gebeurt in <see cref="ToBooking"/>, op één plek, ná
/// <see cref="FieldErrors"/>.</para>
///
/// <para><strong>Hier zit punt 15, en scherper dan bij het contract.</strong> Een leeg urenveld mag
/// nooit nul worden. Bij een contractbedrag levert dat een afspraak op die niemand heeft gemaakt;
/// hier zou het een urenregel van nul uur opleveren die wél in de specificatie van de klant
/// verschijnt. <see cref="Pages.ContractText.TryNumber"/> geeft bij een leeg veld <c>null</c> terug
/// en niet <c>0m</c>, en <see cref="FieldErrors"/> maakt van die <c>null</c> een melding onder het
/// veld. Er is dus geen pad waarlangs een leeg veld een regel wordt.</para>
///
/// <para><strong>Wat hier niet gebeurt: beslissen of een waarde mag.</strong> Dat doet
/// <see cref="HourBooking.Validate"/>, en de opslag roept die aan. <see cref="FieldErrors"/> doet
/// alleen wat aan één veld hangt en waarvoor de datalaag al een controle heeft
/// (<see cref="HourMonths.Validate"/>, <see cref="HourLimits"/>, <see cref="HourCategories"/>) of
/// wat het scherm bezit: of de tekst in een getalveld een getal is. De grenzen zelf — groter dan
/// nul, niet meer dan een etmaal — komen uit <see cref="HourBooking.Validate"/> en komen als blok
/// boven de knop te staan. Eén definitie van "klopt dit", en de melding staat waar hij hoort.</para>
/// </remarks>
public sealed class HourBookingForm
{
    /// <summary>De maand waarop wordt geboekt, als <c>yyyy-MM</c>.</summary>
    /// <remarks>
    /// Uit de keuzelijst van <see cref="Views.OperatorHoursView.BookableMonths"/> en niet vrij te
    /// typen: een boeking op een maand die niet in de tabel staat is een boeking die niemand
    /// terugvindt.
    /// </remarks>
    public string? Month { get; set; }

    /// <summary>Het aantal uren, als tekst uit het veld.</summary>
    public string? Hours { get; set; }

    /// <summary>De categorie. Zie <see cref="HourCategories.Bookable"/>.</summary>
    public string? Category { get; set; }

    /// <summary>Wie de uren op zijn naam krijgt (§6 <c>by</c>).</summary>
    public string? By { get; set; }

    /// <summary>De omschrijving: één regel, die de klant terugleest.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// De meldingen die bij één veld horen.
    /// </summary>
    /// <returns>Veldnaam (uit <c>nameof</c>) naar melding; leeg als er niets aan de hand is.</returns>
    public IReadOnlyDictionary<string, string> FieldErrors()
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        if (HourMonths.Validate(Month) is { } monthError)
        {
            errors[nameof(Month)] = monthError;
        }

        if (HourFormText.HoursError(Hours) is { } hoursError)
        {
            errors[nameof(Hours)] = hoursError;
        }

        if (!HourCategories.IsBookable(Category))
        {
            errors[nameof(Category)] =
                $"Kies een categorie: {string.Join(", ", HourCategories.Bookable)}.";
        }

        if (HourLimits.ValidateBy(By) is { } byError)
        {
            errors[nameof(By)] = byError;
        }

        if (HourLimits.ValidateNote(Note) is { } noteError)
        {
            errors[nameof(Note)] = noteError;
        }

        return errors;
    }

    /// <summary>
    /// Het formulier als boeking voor de opslag.
    /// </summary>
    /// <returns>De boeking.</returns>
    /// <remarks>
    /// Roep dit aan nadat <see cref="FieldErrors"/> leeg is teruggekomen. Een onleesbaar of leeg
    /// urenveld levert hier <c>0m</c> op, en dat is precies de waarde die
    /// <see cref="HourBooking.Validate"/> weigert — dus ook als iemand die volgorde omdraait belandt
    /// er geen regel van nul uur in de opslag.
    /// </remarks>
    public HourBooking ToBooking()
    {
        ContractText.TryNumber(Hours, out var hours);

        return new HourBooking
        {
            Month = Month?.Trim() ?? string.Empty,
            Hours = hours ?? 0m,
            Category = Category?.Trim() ?? string.Empty,
            By = By?.Trim() ?? string.Empty,
            Note = Note?.Trim() ?? string.Empty,
        };
    }
}

/// <summary>
/// Wat er in het correctieformulier van het urenscherm staat (§3.6, besluit 16).
/// </summary>
/// <remarks>
/// <para><strong>Een eigen formulier naast <see cref="HourBookingForm"/>, om precies één
/// verschil.</strong> Dezelfde scheiding als tussen <see cref="HourBooking"/> en
/// <see cref="HourCorrection"/> in de datalaag, en om dezelfde reden: hier mogen de uren negatief
/// zijn. Met één formulier en een aanvinkvak zou "mag dit negatief" een <c>if</c> op dat vakje
/// worden, en dan is een negatieve <em>boeking</em> één verkeerd geschreven <c>if</c> ver weg.</para>
///
/// <para>Er is geen categorieveld: een correctie krijgt altijd
/// <see cref="HourCategories.Correction"/>, en dat staat vast in
/// <see cref="IPortalHoursStore.CorrectHoursAsync"/> en niet hier.</para>
/// </remarks>
public sealed class HourCorrectionForm
{
    /// <summary>De maand die gecorrigeerd wordt, als <c>yyyy-MM</c>.</summary>
    public string? Month { get; set; }

    /// <summary>Het aantal uren dat erbij komt of eraf gaat, als tekst uit het veld.</summary>
    public string? Hours { get; set; }

    /// <summary>Wie de correctie op zijn naam krijgt.</summary>
    public string? By { get; set; }

    /// <summary>Waarom er gecorrigeerd wordt. Verplicht; dit is de audittrail (besluit 16).</summary>
    public string? Note { get; set; }

    /// <summary>De meldingen die bij één veld horen.</summary>
    /// <returns>Veldnaam naar melding; leeg als er niets aan de hand is.</returns>
    public IReadOnlyDictionary<string, string> FieldErrors()
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        if (HourMonths.Validate(Month) is { } monthError)
        {
            errors[nameof(Month)] = monthError;
        }

        if (HourFormText.HoursError(Hours) is { } hoursError)
        {
            errors[nameof(Hours)] = hoursError;
        }

        if (HourLimits.ValidateBy(By) is { } byError)
        {
            errors[nameof(By)] = byError;
        }

        if (HourLimits.ValidateNote(Note) is { } noteError)
        {
            errors[nameof(Note)] = noteError;
        }

        return errors;
    }

    /// <summary>Het formulier als correctie voor de opslag.</summary>
    /// <returns>De correctie.</returns>
    /// <remarks>
    /// Een leeg of onleesbaar urenveld wordt <c>0m</c>, en nul is precies wat
    /// <see cref="HourCorrection.Validate"/> weigert — "een correctie van nul uur verandert niets".
    /// Dezelfde vangnetgedachte als bij <see cref="HourBookingForm.ToBooking"/>.
    /// </remarks>
    public HourCorrection ToCorrection()
    {
        ContractText.TryNumber(Hours, out var hours);

        return new HourCorrection
        {
            Month = Month?.Trim() ?? string.Empty,
            Hours = hours ?? 0m,
            By = By?.Trim() ?? string.Empty,
            Note = Note?.Trim() ?? string.Empty,
        };
    }
}

/// <summary>
/// Wat er in het beoordelingsformulier staat: de reden van een afwijzing (§3.6).
/// </summary>
/// <remarks>
/// <para><strong>De regel zelf staat niet in dit formulier maar in de URL.</strong> Zie
/// <see cref="HourText.JudgePath"/>: welke regel er wordt beoordeeld is een aanduiding en geen
/// invoer, hij is deelbaar, en hij hoort bij de vraag die de pagina stelt. Wat hier wél in staat is
/// het enige dat de operator intypt.</para>
///
/// <para>Bij fiatteren blijft dit formulier leeg. Het bestaat dan alleen om de POST een model te
/// geven; de bevestiging zit in het feit dat er een tweede pagina en een tweede klik nodig is. Zie
/// de toelichting bovenaan <c>Uren.razor</c>.</para>
/// </remarks>
public sealed class HourJudgementForm
{
    /// <summary>Waarom de regel wordt afgewezen. Verplicht bij een afwijzing.</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// De melding onder het redenveld, of <c>null</c> als er niets aan de hand is.
    /// </summary>
    /// <returns>De melding.</returns>
    /// <remarks>
    /// Uit <see cref="HourRejection.Validate"/> en niet met een eigen tekst, zodat het formulier
    /// niets weigert wat de schrijfkant zou toestaan en niets doorlaat wat zij weigert. De id die
    /// daar wordt meegegeven is een houder: die controle hangt niet aan dit veld.
    /// </remarks>
    public string? ReasonError() =>
        new HourRejection { EntryId = "-", Reason = Reason ?? string.Empty }.Validate();

    /// <summary>De afwijzing voor de opslag.</summary>
    /// <param name="entryId">De id van de regel, uit de URL.</param>
    /// <returns>De afwijzing.</returns>
    /// <remarks>
    /// <para><strong>Er gaat geen etag mee, en dat is een besluit met een prijs.</strong> Zie de
    /// toelichting bovenaan <c>Uren.razor</c>: op een static-SSR-scherm is er geen plek om een etag
    /// tussen twee verzoeken vast te houden behalve de paginabron, en daar hoort een
    /// schrijfvoorwaarde niet te staan. De overgangsregels in
    /// <see cref="HourEntryTransitions"/> worden aan de schrijfkant tegen het huidige document
    /// getoetst, en dat is het enige dat aan een bestaande regel kan veranderen.</para>
    /// </remarks>
    public HourRejection ToRejection(string entryId) => new()
    {
        EntryId = entryId,
        Reason = Reason?.Trim() ?? string.Empty,
        BasedOnETag = null,
    };
}

/// <summary>
/// De meldingen die het boek- en het correctieformulier delen.
/// </summary>
/// <remarks>
/// Eén plek, want beide formulieren hebben hetzelfde urenveld met dezelfde twee manieren om mis te
/// gaan: er staat niets, of er staat iets wat geen getal is. Twee kopieën van die melding gaan uit
/// de pas lopen zodra iemand er een voorbeeld in verandert.
/// </remarks>
internal static class HourFormText
{
    /// <summary>Het voorbeeld dat in de melding onder een urenveld staat.</summary>
    public const string Example = "2,5";

    /// <summary>
    /// De melding onder een urenveld, of <c>null</c> als er een leesbaar getal staat.
    /// </summary>
    /// <param name="text">Wat er in het veld staat.</param>
    /// <returns>De melding.</returns>
    /// <remarks>
    /// <para><strong>Leeg is hier een fout en niet "niet vastgelegd".</strong> Daarin wijkt een
    /// urenveld af van een contractbedrag: een urenregel zonder uren is geen halfleeg document maar
    /// een ongeldig document (zie <see cref="HourEntryDocument.Hours"/>). De melding zegt dat, en er
    /// wordt geen nul van gemaakt.</para>
    ///
    /// <para><strong>De invoer gaat mee naar <see cref="ContractText.NumberError"/>.</strong> Dat
    /// argument is optioneel en het weglaten levert de algemene melding op — niet onwaar, maar wel
    /// stil bij precies het geval waar er iets uit te leggen valt: "1.250" wordt geweigerd omdat drie
    /// cijfers achter een scheidingsteken een duizendgroep is, en met de invoer erbij zegt de melding
    /// dat en vraagt om een komma. Zie punt 23 van de afwijkingen.</para>
    /// </remarks>
    public static string? HoursError(string? text)
    {
        if (!ContractText.TryNumber(text, out var hours))
        {
            return ContractText.NumberError(Example, text);
        }

        return hours is null
            ? $"Vul het aantal uren in, bijvoorbeeld {Example}. Een komma of een punt mag."
            : null;
    }
}
