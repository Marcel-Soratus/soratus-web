using System.Globalization;
using System.Text.RegularExpressions;

namespace Soratus.Mcp.Uren;

/// <summary>
/// Wat de aanroeper heeft opgegeven, ongewijzigd zoals het binnenkwam.
/// </summary>
/// <param name="Customer">De klant.</param>
/// <param name="Month">De maand.</param>
/// <param name="Hours">Het aantal uren.</param>
/// <param name="Category">De categorie.</param>
/// <param name="Note">De omschrijving.</param>
public sealed record HourBookingInput(
    string? Customer,
    string? Month,
    decimal Hours,
    string? Category,
    string? Note);

/// <summary>
/// Valideert een boeking vóór er iets de deur uit gaat.
/// </summary>
/// <remarks>
/// <para>Dezelfde regel die het seed-gereedschap volgt: een fout in wat er binnenkomt wordt gemeld
/// en er wordt niets weggeschreven. Een urenregel die later iemand moet opruimen is duurder dan een
/// afwijzing nu, en bij uren is "later" de maand waarin er gefactureerd wordt.</para>
///
/// <para><strong>Wat hier wordt getoetst is uitsluitend de vórm.</strong> Bestaat deze klant, bestaat
/// deze categorie — dat weet alleen het portaal, en die vragen worden daar gesteld en niet hier. Deze
/// klasse kent geen categorielijst en geen klantenlijst, en haalt ze ook niet op. Zie
/// <see cref="CheckCategory"/> voor waarom dat verschil ertoe doet.</para>
///
/// <para><strong>Alle fouten komen in één keer terug</strong>, niet alleen de eerste. De aanroeper is
/// een taalmodel dat de melding aan een mens toont; drie keer heen en weer voor drie fouten in
/// dezelfde aanroep kost drie keer een mens die wacht.</para>
/// </remarks>
public static partial class HourBookingValidation
{
    /// <summary>Het maximum aantal uren op één regel.</summary>
    /// <remarks>
    /// Een werkmaand is ruwweg 168 uur. 200 laat een uitschieter door en houdt de typefout tegen
    /// die er het meest voorkomt: een getal met een cijfer te veel. Meer dan dit boeken kan nog
    /// steeds — in twee regels, en dan heeft iemand het twee keer bedoeld.
    /// </remarks>
    public const decimal MaxHours = 200m;

    /// <summary>Het maximum aantal decimalen op het aantal uren.</summary>
    /// <remarks>
    /// Twee, want dat is een honderdste uur. Meer wordt niet stil afgerond: stil afronden verandert
    /// een bedrag zonder dat iemand het heeft gezien, en dat is precies het soort onwaarheid
    /// waarvoor deze hele server voorzichtig is.
    /// </remarks>
    public const int MaxHourDecimals = 2;

    /// <summary>De kortste bruikbare omschrijving.</summary>
    public const int MinNoteLength = 5;

    /// <summary>De langste omschrijving.</summary>
    public const int MaxNoteLength = 500;

    /// <summary>De langste categorienaam die als categorie wordt aangenomen.</summary>
    /// <remarks>
    /// Dit is geen toets op de lijst — die staat in het portaal — maar op de vorm: een tekst van
    /// tweehonderd tekens is geen categorie maar een omschrijving die in het verkeerde veld is
    /// beland, en die hoeft niet eerst een netwerkverzoek te kosten.
    /// </remarks>
    public const int MaxCategoryLength = 60;

    /// <summary>Het vroegste jaar dat als maand wordt geaccepteerd.</summary>
    /// <remarks>
    /// Soratus bestaat niet vóór dit jaar, dus een maand ervoor is een typefout in het jaartal en
    /// geen late boeking. Dit vangt <c>2016-08</c> voor <c>2026-08</c>, wat de plausibelste
    /// vergissing is die op een verkeerd jaar uitkomt.
    /// </remarks>
    public const int EarliestYear = 2024;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern { get; }

    [GeneratedRegex(@"^\d{4}-\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex MonthPattern { get; }

    /// <summary>
    /// Of dit de vorm van een klantslug heeft.
    /// </summary>
    /// <param name="value">De waarde.</param>
    /// <returns><c>true</c> als het een slug is.</returns>
    public static bool IsWellFormedSlug(string? value) =>
        value is not null && SlugPattern.IsMatch(value);

    /// <summary>
    /// Kijkt een boeking na en geeft elke gevonden fout terug als één leesbare regel.
    /// </summary>
    /// <param name="input">Wat de aanroeper opgaf.</param>
    /// <param name="allowedCustomers">De lokale beperking uit de configuratie, of leeg.</param>
    /// <param name="now">Het moment waarop de aanroep wordt gedaan, in UTC.</param>
    /// <returns>De fouten, of een lege lijst als de boeking mag doorgaan.</returns>
    /// <exception cref="ArgumentNullException">Een verplichte parameter is <c>null</c>.</exception>
    public static IReadOnlyList<string> Check(
        HourBookingInput input,
        IReadOnlyList<string> allowedCustomers,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(allowedCustomers);

        var errors = new List<string>();

        CheckCustomer(input.Customer, allowedCustomers, errors);
        CheckMonth(input.Month, now, errors);
        CheckHours(input.Hours, errors);
        CheckCategory(input.Category, errors);
        CheckNote(input.Note, errors);

        return errors;
    }

    /// <summary>
    /// Maakt van een gevalideerde boeking het verzoek dat de deur uit gaat.
    /// </summary>
    /// <param name="input">Wat de aanroeper opgaf. Moet door <see cref="Check"/> heen zijn.</param>
    /// <returns>Het verzoek, met de tekstvelden genormaliseerd.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">Als er een verplicht veld leeg is.</exception>
    public static HourBookingRequest ToRequest(HourBookingInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input.Customer)
            || string.IsNullOrWhiteSpace(input.Month)
            || string.IsNullOrWhiteSpace(input.Category)
            || string.IsNullOrWhiteSpace(input.Note))
        {
            // Onbereikbaar zolang Check() ervoor staat. Het staat er omdat de aanroepvolgorde een
            // afspraak is en geen eigenschap van de code, en een lege omschrijving stilzwijgend
            // wegschrijven is precies wat deze klasse moet voorkomen.
            throw new InvalidOperationException(
                "ToRequest is aangeroepen op een boeking die niet door Check() heen is.");
        }

        return new HourBookingRequest
        {
            CustomerId = input.Customer.Trim().ToLowerInvariant(),
            Month = input.Month.Trim(),
            Hours = input.Hours,
            Category = input.Category.Trim(),
            Note = input.Note.Trim(),
        };
    }

    private static void CheckCustomer(
        string? customer,
        IReadOnlyList<string> allowedCustomers,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(customer))
        {
            errors.Add("klant: geef de klant op als slug, bijvoorbeeld 'bakker'.");
            return;
        }

        string slug = customer.Trim().ToLowerInvariant();

        if (!IsWellFormedSlug(slug))
        {
            errors.Add(
                $"klant: '{customer.Trim()}' is geen klantslug. Een slug bestaat uit kleine letters, " +
                "cijfers en koppelstreepjes, begint met een letter of cijfer en is 2 tot 40 tekens lang. " +
                "Gebruik de slug uit de portaal-URL (/klant/<slug>/…), niet de bedrijfsnaam.");
            return;
        }

        // Of deze klant bestáát weet alleen het portaal. Die vraag wordt daar gesteld; hier alleen de
        // vorm, plus de lokale beperking hieronder.
        if (allowedCustomers.Count > 0 && !allowedCustomers.Contains(slug, StringComparer.Ordinal))
        {
            errors.Add(
                $"klant: deze installatie mag alleen boeken voor {Join(allowedCustomers)}, en niet voor " +
                $"'{slug}'. Dat is een grens op deze machine ({UrenConfiguration.CustomersKey}), niet die " +
                "van het portaal.");
        }
    }

    private static void CheckMonth(string? month, DateTimeOffset now, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(month))
        {
            errors.Add(
                "maand: geef de maand op als jjjj-MM, bijvoorbeeld " +
                $"'{now.UtcDateTime:yyyy-MM}' voor de huidige maand.");
            return;
        }

        string value = month.Trim();

        if (!MonthPattern.IsMatch(value)
            || !DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed))
        {
            errors.Add(
                $"maand: '{value}' is geen maand. Gebruik jjjj-MM, bijvoorbeeld " +
                $"'{now.UtcDateTime:yyyy-MM}'. Niet 'augustus', niet '08-2026' en niet een losse datum.");
            return;
        }

        var current = new DateOnly(now.UtcDateTime.Year, now.UtcDateTime.Month, 1);

        if (parsed > current)
        {
            errors.Add(
                $"maand: '{value}' ligt in de toekomst. Uren die nog niet zijn gewerkt kunnen niet " +
                $"worden geboekt; de huidige maand is '{current:yyyy-MM}'.");
            return;
        }

        if (parsed.Year < EarliestYear)
        {
            errors.Add(
                $"maand: '{value}' ligt voor {EarliestYear} en is vrijwel zeker een typefout in het " +
                $"jaartal. Bedoelde je '{now.UtcDateTime.Year}-{value[5..]}'?");
        }
    }

    private static void CheckHours(decimal hours, List<string> errors)
    {
        if (hours <= 0m)
        {
            errors.Add(
                $"uren: {Format(hours)} is geen aantal uren om te boeken. Geef meer dan nul. " +
                "Een correctie naar beneden is portaalwerk en gaat niet via deze koppeling.");
            return;
        }

        if (hours > MaxHours)
        {
            errors.Add(
                $"uren: {Format(hours)} is meer dan {Format(MaxHours)} uur op één regel. Een werkmaand " +
                "is ruwweg 168 uur, dus dit is meestal een cijfer te veel. Klopt het toch, boek het " +
                "dan in meerdere regels.");
            return;
        }

        if (decimal.Round(hours, MaxHourDecimals) != hours)
        {
            errors.Add(
                $"uren: {Format(hours)} heeft meer dan {MaxHourDecimals} decimalen. Rond zelf af — " +
                "stil afronden zou een bedrag veranderen zonder dat iemand het heeft gezien.");
        }
    }

    /// <summary>
    /// Toetst de vorm van de categorie, en <em>niet</em> of hij bestaat.
    /// </summary>
    /// <remarks>
    /// <para>Dit is de plek waar de verleiding zit om de vier boekbare categorieën neer te zetten, en
    /// waar dat niet mag. Het onderscheid is de <strong>houdbaarheid van een kopie</strong>:</para>
    ///
    /// <para>De categorieën staan wél als voorbeeld in de beschrijving van de tool. Loopt die lijst
    /// achter, dan gokt een taalmodel een verouderde naam en krijgt het een afwijzing die de goede
    /// namen noemt — hinderlijk, en zelfherstellend. Zou dezelfde lijst hier de <em>validatie</em>
    /// doen, dan weigert hij bij achterlopen een geldige boeking, of laat hij een categorie door die
    /// het portaal net heeft afgeschaft. Een beschrijving die achterloopt kost een ronde; een
    /// validatie die achterloopt geeft het verkeerde antwoord met gezag.</para>
    ///
    /// <para>Daarom is de enige eigenaar van de lijst het portaal
    /// (<c>HourCategories.Bookable</c> / <c>HourCategories.IsBookable</c>), en gaat de string hier
    /// ongewijzigd door.</para>
    /// </remarks>
    private static void CheckCategory(string? category, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            errors.Add(
                "categorie: geef een categorie op. Het portaal kent de geldige waarden en noemt ze in " +
                "de afwijzing; deze koppeling houdt er bewust geen eigen lijst van.");
            return;
        }

        string value = category.Trim();

        if (value.Length > MaxCategoryLength || value.AsSpan().ContainsAny('\n', '\r'))
        {
            errors.Add(
                $"categorie: '{Shorten(value)}' is geen categorie maar een tekst. Hoort dit in de " +
                "omschrijving?");
        }
    }

    private static void CheckNote(string? note, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            errors.Add(
                "omschrijving: geef één zin over wat er is gedaan. Dit is wat een operator straks moet " +
                "fiatteren en wat de klant op zijn specificatie leest; zonder omschrijving is dat niet " +
                "te doen.");
            return;
        }

        string value = note.Trim();

        if (value.Length < MinNoteLength)
        {
            errors.Add(
                $"omschrijving: '{value}' is te kort om iemand iets te vertellen. Schrijf één zin.");
            return;
        }

        if (value.Length > MaxNoteLength)
        {
            errors.Add(
                $"omschrijving: {value.Length} tekens is meer dan {MaxNoteLength}. Dit is een regel in " +
                "een tabel, niet een verslag.");
            return;
        }

        if (value.AsSpan().ContainsAny('\n', '\r'))
        {
            // Hier wordt geweigerd en niet geknipt, anders dan in het agentcontract. Daar knipt de
            // bibliotheek omdat de schrijver een achtergrondproces is dat niet kan worden gevraagd
            // het over te doen, en de overloop naar extra kan verhuizen. Hier zit er een aanroeper
            // aan de andere kant die het meteen kan herstellen, en een urenregel heeft geen veld
            // om de rest in te bewaren. Dan is knippen informatie weggooien.
            errors.Add(
                "omschrijving: dit is meer dan één regel. Een urenregel draagt één zin, en de klant " +
                "leest hem. Er is geen veld waarin de rest bewaard blijft, dus hij wordt niet " +
                "afgeknipt maar geweigerd. Vat het samen in één regel.");
        }
    }

    /// <summary>Voegt een lijst samen tot leesbaar Nederlands: "a, b en c".</summary>
    private static string Join(IReadOnlyList<string> values) => values.Count switch
    {
        0 => "niets",
        1 => $"'{values[0]}'",
        _ => string.Join(", ", values.Take(values.Count - 1).Select(static v => $"'{v}'"))
             + $" en '{values[^1]}'",
    };

    private static string Shorten(string value) =>
        value.Length <= 30 ? value : string.Concat(value.AsSpan(0, 30), "…");

    private static string Format(decimal value) =>
        value.ToString("0.####", CultureInfo.GetCultureInfo("nl-NL"));
}
