namespace Soratus.Portal.Data;

/// <summary>
/// Wat er van het Azure-verbruik van één maand bekend is (§3.7).
/// </summary>
/// <remarks>
/// <para><strong>De invariant van dit type is de hele opgave: er is een subtotaal dan en slechts dan
/// als er regels zijn.</strong> <see cref="Subtotal"/> is de som van <see cref="Lines"/> en
/// <c>null</c> zodra die lijst leeg is. Er bestaat dus geen weg waarlangs "we weten het niet" als
/// € 0,00 op een factuur belandt, en dat komt niet doordat iemand ergens een <c>if</c> heeft gezet
/// maar doordat het veld geen getal draagt als er niets is opgeteld.</para>
///
/// <para>De keerzijde is even belangrijk: <strong>nul mét regels is een echte nul.</strong> In de
/// gemeten uitvoer staan <c>Bandwidth € 0,0000</c> en <c>Microsoft Entra € 0,0000</c> als gewone
/// regels. Een maand waarin alleen zulke regels staan heeft een subtotaal van nul, en dat is een
/// bedrag. Dat verschil — een som die nul is tegenover een som die niet bestaat — is precies wat
/// <c>decimal?</c> hier uitdrukt en wat een <c>decimal</c> niet kan.</para>
///
/// <para><strong>Dit type is voor beide rollen bruikbaar en draagt daarom geen marge.</strong> Er
/// staat geen opslagpercentage op en geen door te belasten bedrag; dat zit in
/// <see cref="MonthlyCharge"/>, en de rolscheiding zit in de weergavetypen die dat wel of niet
/// meenemen. Wat er wél op staat en operator-only is, is <see cref="Scope"/> — en die staat dus niet
/// op de klantweergave.</para>
/// </remarks>
public sealed record AzureCostReading
{
    /// <summary>De maand als <c>yyyy-MM</c>.</summary>
    public required string Month { get; init; }

    /// <summary>De maand zoals hij op het scherm hoort te staan, bijvoorbeeld <c>augustus 2026</c>.</summary>
    public required string MonthLabel { get; init; }

    /// <summary>Wat er van deze maand bekend is.</summary>
    public required AzureCostState State { get; init; }

    /// <summary>
    /// De diensten met hun bedragen, hoogste bedrag eerst.
    /// </summary>
    /// <remarks>
    /// <para>Operator-only (§2: "Facturatie: Azure per dienst + beheeropslag — nee" voor de klant), en
    /// daarom staat deze lijst niet op de klantweergave. Zie <see cref="Views.CustomerChargeRow"/>:
    /// dat type heeft het veld niet, in plaats van het te hebben en te verbergen.</para>
    ///
    /// <para>Gesorteerd op bedrag en niet op naam. Een operator die naar deze uitsplitsing kijkt,
    /// kijkt naar wat de kosten drijft; alfabetisch zou <c>Azure App Service</c> — 99,7% van het
    /// bedrag in de gemeten maand — willekeurig ergens in de lijst staan.</para>
    /// </remarks>
    public IReadOnlyList<AzureCostLine> Lines { get; init; } = [];

    /// <summary>
    /// De som van de bedragen in <see cref="Lines"/>, of <c>null</c> als er geen regels zijn.
    /// </summary>
    /// <remarks>
    /// <para>Onafgerond. Het afronden gebeurt één keer, op het bedrag dat wordt doorbelast; zie
    /// <see cref="MonthlyChargeCalculator"/>. Zou hier al worden afgerond, dan is het subtotaal de som
    /// van afgeronde regels en wijkt hij af van wat Azure ons factureert.</para>
    ///
    /// <para>Een <em>afgeleide</em> eigenschap en geen veld in de opslag. Zie
    /// <see cref="AzureCostDocument"/>: een opgeslagen som die de regels tegenspreekt is een tweede
    /// waarheid, en de verkeerde van de twee zou degene zijn die niemand bijwerkt. Dezelfde keuze als
    /// bij <see cref="HourBalance.Booked"/>.</para>
    /// </remarks>
    public decimal? Subtotal => Lines.Count == 0 ? null : Lines.Sum(line => line.Amount);

    /// <summary>De valuta van de bedragen, of <c>null</c> als er niets is gemeten.</summary>
    public string? Currency { get; init; }

    /// <summary>
    /// De scope waartegen is gemeten, of <c>null</c>. Operator-only.
    /// </summary>
    /// <remarks>
    /// Zie <see cref="AzureCostDocument.Scope"/>: dit veld bestaat omdat een resource group die niet
    /// bestaat een geslaagd, leeg antwoord geeft, en de code die ambiguïteit niet kan oplossen. Een
    /// mens die de scope ziet staan kan dat wel.
    /// </remarks>
    public string? Scope { get; init; }

    /// <summary>
    /// Wanneer de bedragen bij Cost Management zijn opgehaald, of <c>null</c> als er nooit is gemeten.
    /// </summary>
    /// <remarks>
    /// Dit is het "tijdstip van ophalen" uit §3.7 en het hoort bij élke toestand op het scherm te
    /// staan. Bij <see cref="AzureCostState.Unknown"/> is het het antwoord op de enige vraag die er
    /// dan is: hoe oud is wat ik hier zie.
    /// </remarks>
    public DateTimeOffset? MeasuredAt { get; init; }

    /// <summary>De laatste dag waarover er bedragen zijn, of <c>null</c>.</summary>
    public DateOnly? CoversThrough { get; init; }

    /// <summary>Waarom er niets bekend is, of <c>null</c>.</summary>
    public string? Failure { get; init; }

    /// <summary>Of er een bedrag is dat opgeteld mag worden.</summary>
    /// <remarks>
    /// Precies <c><see cref="Subtotal"/> is not null</c>, als leesbare naam. Er staat geen tweede
    /// voorwaarde in: elke poging om "bekend genoeg" anders te definiëren dan "er is een som" opent
    /// de deur naar een pad waarop er tóch een nul verschijnt.
    /// </remarks>
    public bool HasAmount => Subtotal is not null;

    /// <summary>
    /// Maakt de leesbare vorm van een opgeslagen maand, of van de afwezigheid daarvan.
    /// </summary>
    /// <param name="month">De maand als <c>yyyy-MM</c>.</param>
    /// <param name="monthLabel">Het maandlabel voor het scherm.</param>
    /// <param name="document">Het document, of <c>null</c> als er voor deze maand nooit is gemeten.</param>
    /// <returns>De lezing.</returns>
    /// <remarks>
    /// <para><strong><paramref name="document"/> is <c>null</c> geeft
    /// <see cref="AzureCostState.Unknown"/> en geen lege maand met nul erin.</strong> Dat is dezelfde
    /// regel als "geen document betekent geen status" (punt 2 van de fase-0-afwijkingen): de
    /// afwezigheid van een meting is geen meting van nul. Voor een maand waarin een klant nog niet
    /// bestond is dat op het scherm een streepje, en dat is waar.</para>
    ///
    /// <para><strong>Wat deze methode níet doet is de toestand corrigeren.</strong> Een document dat
    /// <see cref="AzureCostState.Measured"/> zegt terwijl er geen regels in staan, blijft hier
    /// "gemeten" heten — met een subtotaal van <c>null</c>, want de invariant hierboven staat boven de
    /// bewering van het document. Op het scherm levert dat "gemeten" naast een streepje op, en dat is
    /// zichtbaar verkeerd. Dat is met opzet de veilige richting: de fout is dan een collector die
    /// gerepareerd moet worden, en niet een bedrag dat te laag is. Het alternatief — de toestand stil
    /// verlagen naar <see cref="AzureCostState.NoLines"/> — zou een kapotte collector maanden lang
    /// laten lijken op een klant zonder verbruik.</para>
    /// </remarks>
    public static AzureCostReading From(string month, string monthLabel, AzureCostDocument? document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(month);

        if (document is null)
        {
            return new AzureCostReading
            {
                Month = month,
                MonthLabel = monthLabel,
                State = AzureCostState.Unknown,
            };
        }

        return new AzureCostReading
        {
            Month = month,
            MonthLabel = monthLabel,
            State = document.State,
            Lines = [.. document.Lines.OrderByDescending(line => line.Amount)],
            Currency = document.Currency,
            Scope = document.Scope,
            MeasuredAt = document.MeasuredAt,
            CoversThrough = Day(document.CoversThrough),
            Failure = document.Failure,
        };
    }

    /// <summary>
    /// De dag uit een opgeslagen <c>yyyy-MM-dd</c>, of <c>null</c> als hij er niet is of niet te lezen valt.
    /// </summary>
    /// <param name="text">De tekst uit het document.</param>
    /// <returns>De dag, of <c>null</c>.</returns>
    /// <remarks>
    /// Een onleesbare dag wordt <c>null</c> en niet een verzonnen dag. Het gevolg is dat het scherm
    /// niet kan zeggen tot wanneer de meting loopt; het bedrag verandert er niet van, en dat is de
    /// juiste verhouding — een kapotte datum hoort geen bedrag te beïnvloeden.
    /// </remarks>
    private static DateOnly? Day(string? text) =>
        DateOnly.TryParseExact(
            text?.Trim(),
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var day)
            ? day
            : null;
}
