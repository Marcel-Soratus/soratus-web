using System.Text.Json.Serialization;

namespace Soratus.Mcp.Uren;

/// <summary>
/// De aanvraag zoals hij op de draad naar <c>POST /api/uren</c> gaat.
/// </summary>
/// <remarks>
/// <para><strong>Er staat geen <c>status</c> op dit type, en geen <c>by</c> en geen <c>source</c>.
/// Dat is de hele veiligheidseigenschap.</strong> §5 van de spec legt vast: alles wat een agent of
/// koppeling inschiet landt als <em>te fiatteren</em> en telt pas mee na akkoord van Soratus. Zou
/// die regel hier als veld staan — ook op <c>"pending"</c> vastgezet, ook met een test eromheen —
/// dan bestaat er een pad waarlangs een andere waarde de deur uit kan. Wat niet op het type staat,
/// kan die grens niet over.</para>
///
/// <para>Dat is dezelfde vorm die het portaal al twee keer heeft toegepast en om dezelfde reden:
/// <c>CustomerLogLine</c> heeft geen <c>extra</c> en <c>CustomerRunRow</c> heeft geen
/// <c>errorType</c> (zie <c>fase-0-afwijkingen.md</c> §12 en §14). Geen <c>null</c> met een
/// voorwaarde eromheen en geen vlag: het veld bestaat niet.</para>
///
/// <para><c>by</c> ontbreekt om een tweede reden: dat veld zegt wie er geboekt heeft, en de enige
/// betrouwbare bron daarvan is het bearer-token. Zou de aanroeper het meesturen, dan kan hij op
/// naam van iemand anders boeken. Het portaal leidt het af uit het token.</para>
/// </remarks>
public sealed record HourBookingRequest
{
    /// <summary>De klantslug. Gelijk aan <c>cid</c> in §6 en aan het pad in de portaal-URL.</summary>
    [JsonPropertyName("cid")]
    public required string CustomerId { get; init; }

    /// <summary>De maand als <c>jjjj-MM</c>.</summary>
    [JsonPropertyName("month")]
    public required string Month { get; init; }

    /// <summary>Het aantal uren. Altijd groter dan nul; een correctie is portaalwerk.</summary>
    [JsonPropertyName("hours")]
    public required decimal Hours { get; init; }

    /// <summary>De categorie, zoals het portaal die kent.</summary>
    [JsonPropertyName("category")]
    public required string Category { get; init; }

    /// <summary>
    /// Eén zin over wat er is gedaan.
    /// </summary>
    /// <remarks>
    /// <strong>De klant leest dit veld.</strong> Het staat in de specificatie van de maand zodra de
    /// regel gefiatteerd is. Dezelfde eisen als aan <c>msg</c> in het agentcontract: één zin in het
    /// Nederlands, geen bestandspaden, geen klasse- of methodenamen, geen endpoints, en geen naam
    /// of id van een ándere klant.
    /// </remarks>
    [JsonPropertyName("note")]
    public required string Note { get; init; }
}

/// <summary>
/// De urenregel zoals het portaal hem teruggeeft nadat hij is vastgelegd.
/// </summary>
/// <remarks>
/// Alle velden zijn <c>nullable</c>, ook die het portaal altijd vult. Dat is opzet: dit is een
/// antwoord van buiten, en een <c>required</c> dat ontbreekt levert een deserialisatie-uitzondering
/// op in plaats van een leesbare melding. De controle op wat er ontbreekt gebeurt in
/// <see cref="PortalUrenClient"/>, waar er een Nederlandse regel bij kan.
/// </remarks>
public sealed record HourBookingResponse
{
    /// <summary>De documentsleutel van de urenregel.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>De klantslug.</summary>
    [JsonPropertyName("cid")]
    public string? CustomerId { get; init; }

    /// <summary>
    /// Het moment waarop de regel is vastgelegd, canoniek UTC.
    /// </summary>
    /// <remarks>
    /// Er is geen veld <c>date</c> op een urenregel, en dat is een besluit: zie
    /// <c>fase-0-afwijkingen.md</c> §20. Een MCP-boeking heeft geen werkdatum, dus <c>date</c> zou een
    /// kalenderdag-duplicaat van dit veld zijn op een grovere korrel en in een andere tijdzone — twee
    /// velden over hetzelfde moment die uiteen kunnen gaan lopen. De werkperiode zit in
    /// <see cref="Month"/>.
    /// </remarks>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>De maand als <c>jjjj-MM</c>.</summary>
    [JsonPropertyName("month")]
    public string? Month { get; init; }

    /// <summary>Het aantal uren.</summary>
    [JsonPropertyName("hours")]
    public decimal? Hours { get; init; }

    /// <summary>De categorie.</summary>
    [JsonPropertyName("category")]
    public string? Category { get; init; }

    /// <summary>De omschrijving.</summary>
    [JsonPropertyName("note")]
    public string? Note { get; init; }

    /// <summary>De bron. Voor deze server altijd <see cref="HourEntryContract.Source"/>.</summary>
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    /// <summary>De mens die het werk deed, door het portaal afgeleid uit het token.</summary>
    [JsonPropertyName("by")]
    public string? BookedBy { get; init; }

    /// <summary>
    /// De koppeling die de regel wegschreef.
    /// </summary>
    /// <remarks>
    /// Twee velden en niet één: met alleen <see cref="BookedBy"/> is "wie heeft dit in de opslag gezet"
    /// onbeantwoordbaar, en dat is de vraag bij een factuurdiscussie.
    /// </remarks>
    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; init; }

    /// <summary>
    /// De fiatteringsstatus. Voor deze server hoort dit altijd
    /// <see cref="HourEntryContract.PendingStatus"/> te zijn.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

/// <summary>
/// De vaste waarden uit het endpointcontract, op één plek.
/// </summary>
/// <remarks>
/// Dit zijn geen keuzes van deze server maar afspraken met de urenopslag in <c>platform</c>. Ze
/// staan hier als constante zodat er nergens een losse string rondslingert die stil verkeerd
/// gespeld kan raken — dezelfde reden als bij <c>PortalRoles</c> in het portaal.
/// </remarks>
public static class HourEntryContract
{
    /// <summary>Het pad van het boekingsendpoint, relatief aan de basis-URL van het portaal.</summary>
    /// <remarks>
    /// Het enige pad. Er is bewust geen tweede endpoint om de categorie- of klantenlijst op te halen:
    /// die lijsten horen nul keer in dit project te bestaan, ook niet opgehaald. Valideren op
    /// categorie is werk van het portaal, want dat is de enige plek waar het een eigenschap is in
    /// plaats van een afspraak.
    /// </remarks>
    public const string BookingPath = "api/uren";

    /// <summary>
    /// De bron die het portaal op een regel van deze server zet, uit §6 (<c>portaal</c>/<c>mcp</c>/<c>devops</c>).
    /// </summary>
    public const string Source = "mcp";

    /// <summary>
    /// De enige status waarmee een regel van deze server mag ontstaan.
    /// </summary>
    /// <remarks>
    /// Deze constante wordt niet <em>verstuurd</em> — zie de opmerking bij
    /// <see cref="HourBookingRequest"/> — maar wél <em>nagekeken</em> op het antwoord. Geeft het
    /// portaal een andere status terug, dan is de vaste regel uit §5 gebroken en hoort de aanroeper
    /// dat te horen in plaats van een geslaagde boeking.
    /// </remarks>
    public const string PendingStatus = "pending";
}
