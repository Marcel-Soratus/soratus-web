using System.Globalization;
using System.Text.Json.Serialization;
using Soratus.Portal.Data;

namespace Soratus.Portal.Api;

/// <summary>
/// De vaste waarden van <c>POST /api/uren</c>, op één plek.
/// </summary>
/// <remarks>
/// De tegenhanger van <c>HourEntryContract</c> in <c>Soratus.Mcp.Uren</c>. Die constanten staan daar
/// omdat de client ze nodig heeft; ze staan hier omdat het portaal ze schrijft. Dat is één afspraak
/// op twee plekken en dat is de prijs van een HTTP-grens tussen twee projecten die niets van elkaar
/// mogen weten — er is geen gedeelde bibliotheek waar dit in kan, en die aanleggen voor drie strings
/// zou de MCP-server aan de datalaag van het portaal knopen. Wat de twee bij elkaar houdt is de test
/// die de vorm van het antwoord vastpint, niet een gedeelde constante.
/// </remarks>
public static class HourBookingApiContract
{
    /// <summary>Het pad van het boekingsendpoint.</summary>
    /// <remarks>
    /// Het enige endpoint. Er komt met opzet geen <c>GET /api/uren/metadata</c> naast: dan bestaat de
    /// categorielijst op een tweede plek, en een tweede plek die de lijst kent gaat achterlopen. De
    /// aanroeper leert de geldige waarden uit de afwijzing.
    /// </remarks>
    public const string Path = "/api/uren";

    /// <summary>
    /// Wat er in <c>createdBy</c> komt te staan: de koppeling die de regel wegschreef.
    /// </summary>
    /// <remarks>
    /// Naast <c>by</c>, dat de mens uit het token draagt. Twee velden en niet één, want met één veld
    /// is "wie heeft dit in de opslag gezet" onbeantwoordbaar, en dat is de vraag bij een
    /// factuurdiscussie. Zie <see cref="HourEntryDocument.CreatedBy"/>.
    /// </remarks>
    public const string CreatedBy = "soratus-uren";
}

/// <summary>
/// Het verzoek zoals het op <c>POST /api/uren</c> binnenkomt: vijf velden en niet meer.
/// </summary>
/// <remarks>
/// <para><strong>Er staat geen <c>status</c>, geen <c>by</c>, geen <c>source</c> en geen
/// <c>createdAt</c> op dit type, en dat is de hele veiligheidseigenschap van dit endpoint.</strong>
/// §5 van de spec legt vast dat alles wat een agent of koppeling inschiet als te fiatteren landt en
/// pas meetelt na akkoord van Soratus. Zou <c>status</c> hier als veld staan — ook op
/// <c>"pending"</c> vastgezet, ook met een test eromheen — dan bestaat er een pad waarlangs een
/// andere waarde binnenkomt. Wat niet op het type staat, kan die grens niet over. Dezelfde vorm als
/// <c>CustomerLogLine</c> zonder <c>extra</c> en <c>CustomerRunRow</c> zonder <c>errorType</c>
/// (punt 12 en 14 van de afwijkingennotitie).</para>
///
/// <para><c>by</c> ontbreekt om een tweede reden: dat veld zegt wie er geboekt heeft, en de enige
/// betrouwbare bron daarvan is het bearer-token. Zou de aanroeper het meesturen, dan kan hij uren op
/// naam van iemand anders boeken. Het portaal leidt het af uit het token; zie
/// <see cref="Soratus.Portal.Security.CustomerWriteScope.Actor"/>.</para>
///
/// <para><strong>Een veld dat er niet hoort te staan levert een afwijzing op en wordt niet stil
/// genegeerd.</strong> Dat is wat <see cref="JsonUnmappedMemberHandlingAttribute"/> hier doet.
/// Zonder die regel is het standaardgedrag van <c>System.Text.Json</c> dat een meegestuurde
/// <c>"status": "approved"</c> wordt overgeslagen: het verzoek slaagt, de regel landt goed, en de
/// aanroeper heeft geen enkele aanwijzing dat het portaal zijn veld heeft weggegooid. Dat werkt, maar
/// het is niet te onderscheiden van een portaal dat het veld wél overneemt — en dat verschil is
/// precies wat iemand een half jaar later wil weten. Nu is het antwoord een <c>400</c>.</para>
///
/// <para><strong>Alle velden zijn nullable, ook de vijf die er altijd horen te staan.</strong> Met
/// <c>required</c> levert een ontbrekend veld een deserialisatiefout op — een lege <c>400</c> zonder
/// tekst — in plaats van een Nederlandse melding die zegt welk veld mist. Dezelfde afweging als bij
/// <c>HourBookingResponse</c> aan de clientkant: dit is invoer van buiten, en de controle hoort op
/// een plek te staan waar er een melding bij kan.</para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HourBookingRequest
{
    /// <summary>De klantslug, gelijk aan <c>cid</c> in §6 en aan het pad in de portaal-URL.</summary>
    [JsonPropertyName("cid")]
    public string? CustomerId { get; init; }

    /// <summary>De maand als <c>jjjj-MM</c>.</summary>
    [JsonPropertyName("month")]
    public string? Month { get; init; }

    /// <summary>Het aantal uren. Groter dan nul; een correctie naar beneden is portaalwerk.</summary>
    [JsonPropertyName("hours")]
    public decimal? Hours { get; init; }

    /// <summary>De categorie. Zie <see cref="HourCategories.Bookable"/>.</summary>
    [JsonPropertyName("category")]
    public string? Category { get; init; }

    /// <summary>Eén zin over wat er is gedaan. <strong>De klant leest dit veld.</strong></summary>
    [JsonPropertyName("note")]
    public string? Note { get; init; }

    /// <summary>
    /// Zet dit verzoek om in de boeking zoals de datalaag hem kent, met <paramref name="by"/> uit het
    /// token.
    /// </summary>
    /// <param name="by">Wie de uren op zijn naam krijgt. Komt uit het token, nooit uit het verzoek.</param>
    /// <returns>De boeking. Nog niet gevalideerd; dat doet <see cref="HourBooking.Validate"/>.</returns>
    /// <remarks>
    /// <para><strong>Waarom hier wordt omgezet naar <see cref="HourBooking"/> en niet naar een eigen
    /// type met eigen controles.</strong> Dat type draagt precies deze vijf gegevens plus de boeker, en
    /// zijn <c>Validate</c> is de controle die het boekformulier van de operator ook doet — inclusief
    /// de maandvorm, de urengrens uit <see cref="HourLimits"/> en de categorietoets uit
    /// <see cref="HourCategories.IsBookable"/> met een melding die de geldige waarden noemt. Een tweede
    /// validatie hiernaast zou betekenen dat er een pad bestaat waarlangs een waarde binnenkomt die het
    /// scherm zou weigeren, en dan is "wat mag er in een urenregel staan" geen eigenschap meer maar een
    /// afspraak tussen twee bestanden.</para>
    ///
    /// <para>Wat <see cref="HourBooking"/> níet heeft is een statusveld, en dat is waarom het hier past
    /// en niet in de weg zit: het type kan de vaste regel uit §5 niet uitdrukken en dus ook niet
    /// overtreden.</para>
    /// </remarks>
    public HourBooking ToBooking(string by) => new()
    {
        Month = Month?.Trim() ?? string.Empty,
        Hours = Hours ?? 0m,
        Category = Category?.Trim() ?? string.Empty,
        By = by,
        Note = Note?.Trim() ?? string.Empty,
    };
}

/// <summary>
/// De urenregel zoals het portaal hem teruggeeft nadat hij is vastgelegd.
/// </summary>
/// <remarks>
/// <para>Deze vorm is het antwoord op een <c>201</c> en wordt door de MCP-server nagekeken: hij
/// controleert <c>status</c> en <c>source</c>, en meldt een geslaagd verzoek met een andere status
/// níet als geboekt maar als een gebroken §5. Dat is een tweede slot en geen vervanging — het kan een
/// gebroken regel melden, niet voorkomen. Voorkomen doet dit portaal, door de velden niet als
/// parameter te hebben.</para>
///
/// <para><strong>Er staat geen <c>date</c> op.</strong> Punt 20 van de afwijkingennotitie: een
/// urenregel kent één tijdstip (<c>createdAt</c>) en één periode (<c>month</c>). Een MCP-boeking heeft
/// geen werkdatum, dus <c>date</c> zou een kalenderdag-duplicaat van <c>createdAt</c> zijn op een
/// grovere korrel en in een andere tijdzone.</para>
///
/// <para><strong>Er staat ook geen etag op.</strong> Een etag is een schrijfvoorwaarde en deze
/// aanroeper schrijft nooit een tweede keer naar dezelfde regel: fiatteren en afwijzen zijn
/// handelingen van een mens in het portaal en hebben geen endpoint. Een etag meesturen zou suggereren
/// dat er iets mee te doen is.</para>
/// </remarks>
public sealed record HourBookingResponse
{
    /// <summary>De documentsleutel van de urenregel.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>De klantslug.</summary>
    [JsonPropertyName("cid")]
    public required string CustomerId { get; init; }

    /// <summary>De maand als <c>jjjj-MM</c>.</summary>
    [JsonPropertyName("month")]
    public required string Month { get; init; }

    /// <summary>Het aantal uren.</summary>
    [JsonPropertyName("hours")]
    public required decimal Hours { get; init; }

    /// <summary>De categorie.</summary>
    [JsonPropertyName("category")]
    public required string Category { get; init; }

    /// <summary>De omschrijving.</summary>
    [JsonPropertyName("note")]
    public required string Note { get; init; }

    /// <summary>De bron, altijd <c>mcp</c> voor dit endpoint (§6).</summary>
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    /// <summary>De mens die het werk deed, door het portaal uit het token afgeleid.</summary>
    [JsonPropertyName("by")]
    public required string By { get; init; }

    /// <summary>De koppeling die de regel wegschreef.</summary>
    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; init; }

    /// <summary>Wanneer de regel is vastgelegd, canoniek UTC.</summary>
    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>De fiatteringsstand. Voor dit endpoint altijd <c>pending</c>.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// De regel zoals hij is vastgelegd.
    /// </summary>
    /// <param name="entry">Het document dat de opslag heeft teruggegeven.</param>
    /// <returns>Het antwoord.</returns>
    /// <remarks>
    /// <para><strong>De status en de bron komen uit het document en niet uit een constante.</strong>
    /// Dat is het verschil tussen een antwoord dat vertelt wat er staat en een antwoord dat vertelt wat
    /// er hoort te staan. Zou hier <c>"pending"</c> worden opgeschreven, dan is de controle die de
    /// MCP-server op dit veld doet een controle op onze eigen bewering en niet op de opslag — en dan
    /// meldt hij "vastgelegd als te fiatteren" ook als er iets anders in Cosmos staat.</para>
    ///
    /// <para>Ze komen daarbij uit <c>HourJsonValues</c>, dezelfde bron waar het document en de
    /// Cosmos-query hun tekst vandaan halen: die klasse serialiseert de enumwaarde met de attributen
    /// van het type zelf. Daarmee kan een hernoeming van een enumwaarde het antwoord en het document
    /// niet uit elkaar laten lopen — en dus ook niet de controle die de MCP-server erop doet.</para>
    /// </remarks>
    public static HourBookingResponse From(HourEntryDocument entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new HourBookingResponse
        {
            Id = entry.Id,
            CustomerId = entry.CustomerId,
            Month = entry.Month,
            Hours = entry.Hours,
            Category = entry.Category,
            Note = entry.Note,
            Source = HourJsonValues.Of(entry.Source),
            By = entry.By,
            CreatedBy = entry.CreatedBy,
            CreatedAt = entry.CreatedAt,
            Status = HourJsonValues.Of(entry.Status),
        };
    }

    /// <summary>
    /// Het adres waar een operator deze regel kan fiatteren.
    /// </summary>
    /// <returns>Het relatieve pad, voor de <c>Location</c>-kop van de <c>201</c>.</returns>
    /// <remarks>
    /// Het scherm uit §3.6, met de maand voorgeselecteerd. Relatief en niet absoluut: het portaal kent
    /// zijn eigen publieke hostnaam niet met zekerheid (hij staat achter een proxy), en een
    /// <c>Location</c> met de verkeerde host is erger dan een relatieve.
    /// </remarks>
    public string ReviewPath() =>
        string.Create(CultureInfo.InvariantCulture, $"/klant/{CustomerId}/uren?maand={Month}");
}
