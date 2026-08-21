using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soratus.Portal.Mail;

/// <summary>
/// De documentsoort en de sleutel van een verzendbevestiging.
/// </summary>
/// <remarks>
/// <para><strong>Waarom deze twee constanten hier staan en niet bij
/// <see cref="Data.PortalDocumentKinds"/>.</strong> Daar hóren ze, en dat is ook waar ze op een dag
/// terecht moeten komen: één plek voor de documentsoorten van de container <c>customers</c>. Ze
/// staan hier omdat er op het moment van bouwen twee andere sessies in <c>Data/</c> werkten en een
/// gedeeld bestand met drie schrijvers de gegarandeerde merge-botsing is. Dat is een
/// werkomstandigheid en geen ontwerp; het staat als punt van twijfel in het rapport.</para>
///
/// <para>Wat de tussentijd veilig houdt: er is een test die alle <c>kind</c>-waarden in het portaal
/// verzamelt en op dubbelen controleert. Twee soorten met dezelfde <c>kind</c> in dezelfde container
/// is de fout die dit zou kunnen opleveren, en die is niet zichtbaar — een query op <c>kind</c>
/// levert dan documenten van het verkeerde type op en de deserialisatie vult de ontbrekende velden
/// met hun standaardwaarde.</para>
/// </remarks>
public static class StatementDocumentKeys
{
    /// <summary>
    /// De documentsoort: één verzendbevestiging van één maandoverzicht.
    /// </summary>
    /// <remarks>
    /// Enkelvoud en in het Engels, zoals de vier bestaande soorten (<c>customer</c>, <c>contract</c>,
    /// <c>access</c>, <c>hourEntry</c>). <c>statement</c> en niet <c>mail</c>: het document gaat over
    /// het maandoverzicht en over de vraag of het is aangekomen, en dat het per mail gaat is de vorm
    /// en niet het feit.
    /// </remarks>
    public const string Kind = "statement";

    /// <summary>
    /// De documentsleutel van de verzendbevestiging van één maand, binnen de partitie van die klant.
    /// </summary>
    /// <param name="month">De maand als <c>jjjj-MM</c>.</param>
    /// <returns>Bijvoorbeeld <c>statement-2026-08</c>.</returns>
    /// <remarks>
    /// <para><strong>De sleutel is afgeleid en niet willekeurig, en dat is het slot op een dubbele
    /// mail.</strong> Eén klant, één maand, één maandoverzicht. Met deze sleutel levert een tweede
    /// verzendpoging een <c>409</c> op bij Cosmos in plaats van een tweede mail — en die 409 valt
    /// vóór de aanroep naar Communication Services, want het document wordt geschreven vóórdat er
    /// wordt verstuurd. Dezelfde eigenschap en dezelfde reden als bij
    /// <see cref="Data.PortalDocumentIds.HourEntry(string)"/>, met dit verschil: een dubbele
    /// urenregel is een correctie voor een mens, een dubbele mail naar een klant is niet terug te
    /// halen.</para>
    ///
    /// <para>De maand staat er leesbaar in en niet als hash. Deze sleutel komt in een logregel en op
    /// een operatorscherm terecht, en <c>statement-2026-08</c> is daar het antwoord op de vraag
    /// welke maand het was.</para>
    /// </remarks>
    public static string Id(string month) => $"{Kind}-{month}";
}

/// <summary>
/// Of het maandoverzicht van deze maand is verstuurd.
/// </summary>
/// <remarks>
/// <para><strong>Drie toestanden, en met opzet geen <c>bool</c>.</strong> Dat is in dit portaal de
/// vierde keer dezelfde afweging: <see cref="Views.AccessEntraState"/> (uitgenodigd in Entra),
/// punt 2 van de fase-0-afwijkingen (geen document betekent geen status), punt 15 (een contractbedrag
/// dat ontbreekt is niet nul) en <c>recorded</c> in de MCP-server. Een <c>bool</c> kan er maar één
/// van de twee mededelingen doen, en juist de derde is degene die geld en vertrouwen kost.</para>
///
/// <para>Waarom hier, concreet. Bij een tijdslimiet of een <c>5xx</c> van Communication Services kan
/// het bericht zijn aangenomen en alleen het antwoord zijn weggevallen. Zegt het document dan "niet
/// verstuurd", dan probeert iemand het opnieuw en krijgt de klant twee maandoverzichten. Zegt het
/// "verstuurd", dan krijgt hij er nul en weet niemand dat. Onbekend is een eigen antwoord.</para>
///
/// <para><strong>En de vaste stelregel van dit project erbij:</strong> "onbekend of het gelukt is"
/// is geen reden om het opnieuw te proberen. Zie <c>docs/agent-portal/mcp-uren.md</c> en §6 van
/// <c>docs/agent-portal/fase-4-haalbaarheid.md</c>, waar dezelfde afweging voor een urenboeking en
/// voor een conceptfactuur is gemaakt. Uit <see cref="Unknown"/> komt het portaal alleen langs een
/// mens: <see cref="IStatementStore.ReleaseAsync"/>.</para>
///
/// <para><strong>De vierde toestand is de afwezigheid van het document.</strong> Geen document
/// betekent: er is nooit een poging gedaan. Dat is punt 2 van de fase-0-afwijkingen — geen document
/// betekent geen status — en het is de reden dat deze enum géén waarde <c>NotAttempted</c> heeft.
/// Zou die erin staan, dan zou een document met die waarde bestaan zonder dat er iets is gebeurd, en
/// dan is de afwezigheid van het document geen antwoord meer op dezelfde vraag.</para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<StatementSendState>))]
public enum StatementSendState
{
    /// <summary>
    /// Onbekend of het maandoverzicht is aangekomen. Deze waarde staat er ook zolang een verzending
    /// nog loopt.
    /// </summary>
    /// <remarks>
    /// <para>De eerste waarde van de enum, en dat is opzet: dit is wat een document zegt dat is
    /// aangemaakt en waar daarna niets meer op is geschreven. De standaardwaarde van een
    /// niet-geïnitialiseerde enum is hier dus de veilige waarde en niet de gevaarlijke. Zou
    /// <see cref="Sent"/> op nul staan, dan zou een document met een leeg of onleesbaar
    /// <c>state</c>-veld lezen als "verstuurd".</para>
    ///
    /// <para>Er is geen aparte waarde voor "de verzending loopt nu". Dat lijkt informatie die je
    /// wilt hebben, en het is precies de verkeerde: het verschil tussen "loopt nog" en "onbekend" is
    /// alleen door de tijd te bepalen, en een proces dat halverwege omvalt laat "loopt nog" staan.
    /// Dan staat er een toestand die zegt dat er iemand aan het werk is terwijl er niemand is.
    /// </para>
    /// </remarks>
    [JsonStringEnumMemberName("unknown")]
    Unknown,

    /// <summary>
    /// Verstuurd. Communication Services heeft het bericht aangenomen en er is een operatie-id.
    /// </summary>
    /// <remarks>
    /// Let op wat dit wél en niet zegt. Aangenomen door Communication Services is niet "in de inbox
    /// van de klant": een afgewezen ontvanger, een volle postbus of een spamfilter komt daarna. Het
    /// scherm zegt daarom "verstuurd" en niet "afgeleverd" — dat woord zou een gegeven beloven dat
    /// we niet hebben. Dezelfde reden waarom de status van een SnelStart-factuur "Gefactureerd"
    /// hoort te heten en niet "Verzonden" (§7 van het haalbaarheidsrapport).
    /// </remarks>
    [JsonStringEnumMemberName("sent")]
    Sent,

    /// <summary>
    /// Niet verstuurd, en dat is zeker. Er is niets de deur uit gegaan.
    /// </summary>
    /// <remarks>
    /// Alleen als er geen twijfel is: een afwijzing van Communication Services vóór het versturen
    /// (een ongeldige ontvanger, een niet-geverifieerde afzender, een ontbrekend recht), of een mens
    /// die heeft vastgesteld dat er niets is aangekomen. Bij twijfel geldt <see cref="Unknown"/>.
    /// </remarks>
    [JsonStringEnumMemberName("notSent")]
    NotSent,
}

/// <summary>
/// De opslagvorm van een verzendtoestand, zoals hij in het document staat.
/// </summary>
/// <remarks>
/// Uit de serializer en niet uit een <c>switch</c> hiernaast. Dezelfde constructie en dezelfde reden
/// als <c>HourJsonValues</c>: dit is de plek waar een verkeerde schrijfwijze nul documenten oplevert
/// in plaats van een fout, en dan zegt het scherm dat er nooit is gemaild.
/// </remarks>
internal static class StatementJsonValues
{
    private static readonly Dictionary<StatementSendState, string> States =
        Enum.GetValues<StatementSendState>().ToDictionary(state => state, Text);

    /// <summary>De opslagvorm van een toestand.</summary>
    /// <param name="state">De toestand.</param>
    /// <returns>De tekst, bijvoorbeeld <c>sent</c>.</returns>
    internal static string Of(StatementSendState state) => States[state];

    private static string Text<T>(T value)
        where T : struct, Enum =>
        JsonSerializer.Serialize(value).Trim('"');
}

/// <summary>
/// De verzendbevestiging van één maandoverzicht: één document per klant per maand.
/// </summary>
/// <remarks>
/// <para><strong>Dit document is een vastgelegd feit en geen vlag op iets anders.</strong> Het staat
/// in de container <c>customers</c>, op de partitiesleutel van de klant, naast het klantdocument,
/// het contract, de toegangsregels en de urenregels — met een eigen <c>kind</c>. Dat is dezelfde
/// keuze als bij <c>HourEntryDocument</c>, en de reden is dezelfde: één partitie per klant maakt
/// lezen en schrijven binnen één klant een gesloten geheel, en er is geen query in deze map die
/// buiten één partitiesleutel komt.</para>
///
/// <para><strong>De bedragen staan erin, en dat is het belangrijkste veld van dit document.</strong>
/// Er staat niet alleen dát er is gemaild maar ook <em>wat</em> er is gemaild. Zonder die bedragen is
/// de enige manier om te weten wat de klant heeft gekregen: het opnieuw uitrekenen. En dat levert
/// over een maand een ander getal op — de kostenmeting is dan bijgewerkt, de bundel is misschien
/// gewijzigd, er is een urencorrectie geplaatst. Bij een factuurdiscussie is "wat stond er in de
/// mail die u op 3 september kreeg" de vraag, en die hoort een document te kunnen beantwoorden in
/// plaats van een herberekening.</para>
///
/// <para><strong>Wat er níet in staat: de opgemaakte tekst van de mail.</strong> Die is uit de
/// bedragen en de vorm te herleiden en zou anders twee keer bestaan. Wat er wél in staat is de
/// onderwerpregel, want die is de enige tekst die de klant in zijn postbuslijst ziet en dus het
/// enige waarop hij de mail terugvindt.</para>
/// </remarks>
public sealed record StatementDocument
{
    /// <summary>Documentsleutel: <see cref="StatementDocumentKeys.Id(string)"/>.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Partitiesleutel. De klantslug.</summary>
    [JsonPropertyName("pk")]
    public required string PartitionKey { get; init; }

    /// <summary>Documentsoort. Altijd <see cref="StatementDocumentKeys.Kind"/>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = StatementDocumentKeys.Kind;

    /// <summary>De klantslug (<c>cid</c> in §6).</summary>
    [JsonPropertyName("cid")]
    public required string CustomerId { get; init; }

    /// <summary>De maand als <c>jjjj-MM</c>.</summary>
    /// <remarks>
    /// Staat er als eigen veld naast de sleutel, zodat een maandquery op <c>c.month</c> kan lopen
    /// net als die van de urenregels. Punt 7 van de fase-0-afwijkingen: <c>jjjj-MM</c> en niets
    /// anders, want Cosmos vergelijkt tijdvelden als tekst.
    /// </remarks>
    [JsonPropertyName("month")]
    public required string Month { get; init; }

    /// <summary>De verzendtoestand. Zie <see cref="StatementSendState"/>.</summary>
    [JsonPropertyName("state")]
    public required StatementSendState State { get; init; }

    /// <summary>Wanneer de poging is begonnen, in UTC.</summary>
    /// <remarks>
    /// Het moment van de claim, dus vóór de aanroep naar Communication Services. Bij een document
    /// dat op <see cref="StatementSendState.Unknown"/> is blijven staan, is dit het enige moment dat
    /// er is — en daarmee het antwoord op "sinds wanneer weten we het niet".
    /// </remarks>
    [JsonPropertyName("attemptedAt")]
    public required DateTimeOffset AttemptedAt { get; init; }

    /// <summary>Welke operator de verzending heeft gestart.</summary>
    [JsonPropertyName("attemptedBy")]
    public string? AttemptedBy { get; init; }

    /// <summary>Wanneer Communication Services het bericht heeft aangenomen, of <c>null</c>.</summary>
    [JsonPropertyName("sentAt")]
    public DateTimeOffset? SentAt { get; init; }

    /// <summary>
    /// De operatie-id van Communication Services, of <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Het enige bewijs buiten ons systeem. Hiermee is een verzending in Azure terug te vinden, en
    /// dat is bij een klant die zegt niets te hebben gekregen het verschil tussen "wij hebben het
    /// verstuurd" en "wij denken dat wij het hebben verstuurd".
    /// </remarks>
    [JsonPropertyName("operationId")]
    public string? OperationId { get; init; }

    /// <summary>
    /// De ontvangers, in de vorm waarin ze zijn geadresseerd.
    /// </summary>
    /// <remarks>
    /// Vastgelegd en niet opnieuw opgezocht. Een toegangsregel kan later worden ingetrokken of aan
    /// een ander adres worden gegeven, en dan is niet meer te zien naar wie de mail van augustus is
    /// gegaan. Dat is precies de vraag die je wilt kunnen beantwoorden als er iets bij de verkeerde
    /// terecht is gekomen.
    /// </remarks>
    [JsonPropertyName("recipients")]
    public IReadOnlyList<string> Recipients { get; init; } = [];

    /// <summary>De onderwerpregel zoals hij is verstuurd.</summary>
    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    /// <summary>Wanneer de kostenmeting achter deze bedragen is gedaan, in UTC.</summary>
    [JsonPropertyName("measuredAt")]
    public DateTimeOffset? MeasuredAt { get; init; }

    /// <summary>Het door te belasten Azure-bedrag dat in de mail stond.</summary>
    [JsonPropertyName("azure")]
    public decimal? AzureAmount { get; init; }

    /// <summary>Het bedrag voor uren boven bundel dat in de mail stond.</summary>
    [JsonPropertyName("extraHoursAmount")]
    public decimal? ExtraHoursAmount { get; init; }

    /// <summary>Het totaal dat in de mail stond.</summary>
    [JsonPropertyName("total")]
    public decimal? Total { get; init; }

    /// <summary>
    /// Waarom er niets is verstuurd, of <see cref="StatementRefusal.None"/>.
    /// </summary>
    /// <remarks>Een enum en geen tekst; zie <see cref="StatementRefusal"/>.</remarks>
    [JsonPropertyName("refusal")]
    public StatementRefusal Refusal { get; init; } = StatementRefusal.None;

    /// <summary>Wanneer een mens de onbekende uitkomst heeft opgelost, in UTC.</summary>
    [JsonPropertyName("releasedAt")]
    public DateTimeOffset? ReleasedAt { get; init; }

    /// <summary>Welke operator dat heeft gedaan.</summary>
    [JsonPropertyName("releasedBy")]
    public string? ReleasedBy { get; init; }

    /// <summary>
    /// Wat die operator heeft vastgesteld, in zijn eigen woorden.
    /// </summary>
    /// <remarks>
    /// <para>Het enige vrije tekstveld op dit document, en het is er met opzet: over een half jaar is
    /// dit het antwoord op de vraag waarom er twee keer is gemaild, of waarom er over augustus geen
    /// overzicht is. Dezelfde rol als <c>rejectionReason</c> op een urenregel.</para>
    ///
    /// <para><strong>Deze tekst gaat nooit in een mail.</strong> Hij staat op het operatorscherm en
    /// nergens anders. Er is geen veld op enig mailtype waar hij in past, en dat is de vorm die dit
    /// portaal voor zulke tekst gebruikt (punt 12, 13, 14): wat er niet is kan niet lekken.</para>
    /// </remarks>
    [JsonPropertyName("releaseNote")]
    public string? ReleaseNote { get; init; }

    /// <summary>
    /// Hoeveel keer er voor deze maand een verzending is gestart.
    /// </summary>
    /// <remarks>
    /// Eén bij de eerste poging. Loopt op zodra een mens een onbekende uitkomst heeft vrijgegeven en
    /// er opnieuw wordt verstuurd. Staat er meer dan één, dan is dat op het scherm te zien: een klant
    /// die twee overzichten over dezelfde maand heeft gekregen, hoort niet iets te zijn dat je uit
    /// tijdstempels moet reconstrueren.
    /// </remarks>
    [JsonPropertyName("attempts")]
    public int Attempts { get; init; } = 1;

    /// <summary>De versie waarop de gelijktijdigheidscontrole loopt. Zie <see cref="Data.CustomerDocument.ETag"/>.</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; init; }
}

/// <summary>
/// Waarom er niets is verstuurd.
/// </summary>
/// <remarks>
/// <para>Een enum, om dezelfde reden als <see cref="StatementFigureGap"/>: de weigering wordt
/// opgeschreven en op een scherm gezet, en een reden die als string reist komt op een dag uit een
/// <c>catch</c>-blok. Punt 13 en 14 van de fase-0-afwijkingen, in de vorm waarin ze hier van
/// toepassing zijn.</para>
///
/// <para>De Nederlandse tekst bij elke waarde staat in <see cref="StatementText"/> en is met de hand
/// geschreven voor een operator. Niet voor een klant: hij komt in geen enkele mail.</para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<StatementRefusal>))]
public enum StatementRefusal
{
    /// <summary>Er is niet geweigerd.</summary>
    [JsonStringEnumMemberName("none")]
    None,

    /// <summary>Mailen is niet ingericht: er is geen endpoint of geen afzender.</summary>
    [JsonStringEnumMemberName("mailNotConfigured")]
    MailNotConfigured,

    /// <summary>Er zijn over deze maand geen bedragen gemeten.</summary>
    [JsonStringEnumMemberName("noFigures")]
    NoFigures,

    /// <summary>Een bedrag dat in de mail hoort is onbekend. Onbekend is niet nul.</summary>
    [JsonStringEnumMemberName("amountUnknown")]
    AmountUnknown,

    /// <summary>De kostenkant zegt dat de meting over deze maand nog niet volledig is.</summary>
    [JsonStringEnumMemberName("amountsIncomplete")]
    AmountsIncomplete,

    /// <summary>Er is geen contactpersoon met een e-mailadres bij deze klant.</summary>
    [JsonStringEnumMemberName("noRecipient")]
    NoRecipient,

    /// <summary>Een adres of een naam is niet als kop van een mail te gebruiken.</summary>
    [JsonStringEnumMemberName("recipientInvalid")]
    RecipientInvalid,

    /// <summary>De maand is er nog niet voorbij, of hij is geen maand.</summary>
    [JsonStringEnumMemberName("monthNotClosed")]
    MonthNotClosed,

    /// <summary>Communication Services heeft het bericht geweigerd. Er is niets verstuurd.</summary>
    [JsonStringEnumMemberName("rejected")]
    Rejected,
}
