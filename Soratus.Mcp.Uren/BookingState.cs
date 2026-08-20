using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soratus.Mcp.Uren;

/// <summary>
/// De uitkomst als machineleesbare stand, naast de tekst.
/// </summary>
/// <remarks>
/// <para><strong>Waarom dit er is.</strong> Een aanroeper die alleen naar <c>isError</c> kijkt, ziet
/// bij een geslaagde boeking <c>false</c> en kan daaruit concluderen dat het klaar is. Dat is precies
/// de stille onwaarheid die deze koppeling moet uitsluiten: de regel is vastgelegd, maar hij telt
/// nergens mee tot een operator hem fiatteert. De waarschuwing staat daarom niet alleen in de tekst
/// maar ook als veld, zodat "moet nog gefiatteerd worden" niet uit proza hoeft te worden
/// gelezen.</para>
///
/// <para><see cref="RequiresSoratusApproval"/> staat op élke uitkomst op <c>true</c>, ook bij een
/// afwijzing. Dat is geen slordigheid: het is een eigenschap van deze koppeling en niet van deze
/// aanroep. Zou het veld bij een afwijzing <c>false</c> zijn, dan zegt het iets over de aanroep en
/// moet een lezer per geval nadenken; nu zegt het iets over de tool, en dat is wat het hoort te
/// zeggen.</para>
///
/// <para><see cref="Recorded"/> heeft drie waarden en geen twee. <c>null</c> betekent "niet vast te
/// stellen" — bij een tijdslimiet of een <c>5xx</c> kan het verzoek zijn aangekomen en alleen het
/// antwoord zijn weggevallen. Dezelfde reden als bij de drie Entra-toestanden in het portaal en bij
/// een ontbrekend contractbedrag (<c>fase-0-afwijkingen.md</c> §15): een waarde die "onbekend" moet
/// kunnen uitdrukken, kan dat niet met een <c>bool</c>.</para>
/// </remarks>
public sealed record BookingState
{
    /// <summary>Welke van de vijf uitkomsten dit is.</summary>
    /// <remarks>
    /// <c>booked</c> · <c>dryRun</c> · <c>refused</c> · <c>unavailable</c> · <c>suspect</c>.
    /// </remarks>
    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    /// <summary>Of de regel is vastgelegd. <c>null</c> betekent: niet vast te stellen.</summary>
    [JsonPropertyName("recorded")]
    public bool? Recorded { get; init; }

    /// <summary>De fiatteringsstatus zoals het portaal hem gaf, of <c>null</c>.</summary>
    [JsonPropertyName("approvalStatus")]
    public string? ApprovalStatus { get; init; }

    /// <summary>
    /// Altijd <c>true</c>: via deze koppeling geboekte uren tellen pas mee na akkoord van Soratus.
    /// </summary>
    [JsonPropertyName("requiresSoratusApproval")]
    public bool RequiresSoratusApproval { get; init; } = true;

    /// <summary>
    /// Of deze regel meetelt in het maandtotaal. <c>false</c> bij een boeking, <c>null</c> als het
    /// portaal een antwoord gaf dat niet aan de vaste regel voldoet.
    /// </summary>
    [JsonPropertyName("countsTowardMonthTotal")]
    public bool? CountsTowardMonthTotal { get; init; }

    /// <summary>De documentsleutel van de urenregel, als die er is.</summary>
    [JsonPropertyName("entryId")]
    public string? EntryId { get; init; }

    /// <summary>De klantslug.</summary>
    [JsonPropertyName("customer")]
    public string? Customer { get; init; }

    /// <summary>De maand.</summary>
    [JsonPropertyName("month")]
    public string? Month { get; init; }

    /// <summary>Het aantal uren.</summary>
    [JsonPropertyName("hours")]
    public decimal? Hours { get; init; }

    /// <summary>Waar een mens de regel kan nakijken of fiatteren.</summary>
    [JsonPropertyName("reviewUrl")]
    public string? ReviewUrl { get; init; }

    /// <summary>De redenen van een afwijzing of van een onbereikbaar portaal.</summary>
    [JsonPropertyName("reasons")]
    public IReadOnlyList<string> Reasons { get; init; } = [];

    /// <summary>
    /// Leidt de stand af uit een uitkomst.
    /// </summary>
    /// <param name="outcome">Wat er is gebeurd.</param>
    /// <param name="portal">De basis-URL van het portaal.</param>
    /// <returns>De stand.</returns>
    /// <exception cref="ArgumentNullException">Een verplichte parameter is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Onbekende uitkomst.</exception>
    public static BookingState From(BookingOutcome outcome, Uri portal)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(portal);

        return outcome switch
        {
            BookingOutcome.Booked booked => new BookingState
            {
                Outcome = "booked",
                Recorded = true,
                ApprovalStatus = booked.Entry.Status,
                CountsTowardMonthTotal = false,
                EntryId = booked.Entry.Id,
                Customer = booked.Entry.CustomerId,
                Month = booked.Entry.Month,
                Hours = booked.Entry.Hours,
                ReviewUrl = Review(portal, booked.Entry.CustomerId, booked.Entry.Month),
            },

            BookingOutcome.DryRun dry => new BookingState
            {
                Outcome = "dryRun",
                Recorded = false,
                CountsTowardMonthTotal = false,
                Customer = dry.Request.CustomerId,
                Month = dry.Request.Month,
                Hours = dry.Request.Hours,
                Reasons = [$"Proefdraaimodus ({UrenConfiguration.DryRunKey}); er is niets verstuurd."],
            },

            BookingOutcome.Refused refused => new BookingState
            {
                Outcome = "refused",
                Recorded = false,
                CountsTowardMonthTotal = false,
                Reasons = refused.Reasons,
            },

            BookingOutcome.Unavailable unavailable => new BookingState
            {
                Outcome = "unavailable",
                // De hele reden dat dit veld nullable is.
                Recorded = unavailable.MayHaveLanded ? null : false,
                CountsTowardMonthTotal = unavailable.MayHaveLanded ? null : false,
                Reasons = [unavailable.Reason],
            },

            BookingOutcome.Suspect suspect => new BookingState
            {
                Outcome = "suspect",
                Recorded = true,
                ApprovalStatus = suspect.Entry.Status,
                // Onbekend, en dat is het punt: als de status niet 'pending' is, kan deze regel al
                // meetellen. Hier 'false' neerzetten zou de bewering doen die niet waar te maken is.
                CountsTowardMonthTotal = null,
                EntryId = suspect.Entry.Id,
                Customer = suspect.Entry.CustomerId,
                Month = suspect.Entry.Month,
                Hours = suspect.Entry.Hours,
                ReviewUrl = Review(portal, suspect.Entry.CustomerId, suspect.Entry.Month),
                Reasons = [suspect.Reason],
            },

            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Onbekende uitkomst."),
        };
    }

    /// <summary>
    /// Serialiseert de stand voor het <c>structuredContent</c>-veld van het toolresultaat.
    /// </summary>
    /// <returns>De stand als JSON.</returns>
    public JsonElement ToJson() =>
        JsonSerializer.SerializeToElement(this);

    private static string? Review(Uri portal, string? customer, string? month)
    {
        if (string.IsNullOrWhiteSpace(customer))
        {
            return null;
        }

        var url = new Uri(portal, $"klant/{customer}/uren");

        return string.IsNullOrWhiteSpace(month)
            ? url.ToString()
            : $"{url}?maand={Uri.EscapeDataString(month)}";
    }
}
