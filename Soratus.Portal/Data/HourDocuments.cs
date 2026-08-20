using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soratus.Portal.Data;

/// <summary>
/// Waar een urenregel vandaan komt (§6 <c>source</c>).
/// </summary>
/// <remarks>
/// <para><strong>Dit veld is niet cosmetisch: het bepaalt of de regel meteen meetelt.</strong> §5
/// legt de vaste regel vast — alles wat een agent of koppeling inschiet landt als te fiatteren en
/// telt pas mee na akkoord van Soratus. <see cref="Portal"/> is de enige bron waar een mens met een
/// operatorrol aan de andere kant zit, en dus de enige die meteen gefiatteerd kan zijn.</para>
///
/// <para>De waarden zijn die van §6 en niet die van de mockup. Die gebruikt <c>handmatig</c> voor een
/// portaalregel; §6 zegt <c>portaal</c>. Van de twee is de spec de vaste, en dit is dezelfde afweging
/// als bij <c>kind</c> tegenover <c>type</c> in <see cref="PortalDocumentKinds"/>.</para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<HourEntrySource>))]
public enum HourEntrySource
{
    /// <summary>
    /// Ingevoerd in het portaal door een operator (§3.6, "Uren boeken"). Ook de handmatige
    /// correctie.
    /// </summary>
    [JsonStringEnumMemberName("portaal")]
    Portal,

    /// <summary>
    /// Ingeschoten via de MCP-server <c>soratus-uren</c> uit Claude Code (§5).
    /// </summary>
    [JsonStringEnumMemberName("mcp")]
    Mcp,

    /// <summary>
    /// Afgeleid uit Completed Work in Azure DevOps door <c>devops-sync</c> (§4).
    /// </summary>
    [JsonStringEnumMemberName("devops")]
    DevOps,
}

/// <summary>
/// De fiatteringsstand van een urenregel (§6 <c>status</c>).
/// </summary>
/// <remarks>
/// <para><strong>Deze drie waarden zijn operator-begrippen en komen op geen enkel klanttype
/// voor.</strong> De acceptatie van fase 3 zegt dat de klant niets van de fiatteringsstroom ziet.
/// Zou een klantrij een statusveld dragen dat altijd <see cref="Approved"/> is, dan staat het woord
/// in de paginabron en verraadt het dat er andere waarden bestaan. Zie
/// <see cref="Views.CustomerHourRow"/>: dat type heeft dit veld niet.</para>
///
/// <para>Er is geen vierde waarde voor "gefactureerd". Die verleiding is er wel — dan zou een
/// gefactureerde regel niet meer te wijzigen zijn — maar facturatie is fase 4 en de factuurstand
/// komt uit SnelStart. Een tweede plek waar diezelfde waarheid staat gaat afwijken van de eerste.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<HourEntryStatus>))]
public enum HourEntryStatus
{
    /// <summary>
    /// Te fiatteren. Telt in geen enkele som mee en is voor de klant onzichtbaar.
    /// </summary>
    [JsonStringEnumMemberName("pending")]
    Pending,

    /// <summary>
    /// Gefiatteerd. Dit is de enige stand die meetelt in het maandtotaal en in de facturatie, en de
    /// enige die een klant te zien krijgt.
    /// </summary>
    [JsonStringEnumMemberName("approved")]
    Approved,

    /// <summary>
    /// Afgewezen. Telt niet mee, is voor de klant onzichtbaar, en blijft staan met een reden.
    /// </summary>
    /// <remarks>
    /// Zie <see cref="HourEntryDocument.RejectionReason"/> voor waarom een afgewezen regel niet
    /// wordt verwijderd.
    /// </remarks>
    [JsonStringEnumMemberName("rejected")]
    Rejected,
}

/// <summary>
/// De categorieën van een urenregel (§3.6, uit <c>DATA.categories</c> van de mockup).
/// </summary>
/// <remarks>
/// <para>Eén lijst, in de datalaag, om dezelfde reden als <see cref="PortalAccessRoles"/>: een
/// formulier of een koppeling die een categorie aanbiedt die de schrijfkant weigert, is een fout die
/// pas bij het opslaan blijkt.</para>
///
/// <para><strong><see cref="Correction"/> staat er wel en is niet boekbaar.</strong> Dat is de kern
/// van besluit 16 in <c>docs/agent-portal/fase-0-afwijkingen.md</c>: een handmatige correctie is
/// geen ander soort getal maar nóg een gefiatteerde urenregel, met deze categorie. Zou hij in
/// <see cref="Bookable"/> staan, dan kon iemand hem als gewone boeking kiezen en betekende de
/// categorie niets meer — dan is een correctie niet meer van een boeking te onderscheiden en is de
/// tooltip uit §3.6 niet te vullen.</para>
/// </remarks>
public static class HourCategories
{
    /// <summary>Werk aan de agents zelf.</summary>
    public const string Development = "Ontwikkeling";

    /// <summary>Onderhoud en operationeel werk.</summary>
    public const string Maintenance = "Beheer";

    /// <summary>Vragen en incidenten van de klant.</summary>
    public const string Support = "Support";

    /// <summary>Overleg en advies.</summary>
    public const string Advice = "Advies";

    /// <summary>
    /// Een handmatige correctie op het maandtotaal. Alleen te maken via
    /// <see cref="IPortalHoursStore.CorrectHoursAsync"/>.
    /// </summary>
    public const string Correction = "Correctie";

    /// <summary>
    /// De categorieën die op het boekformulier en in een koppeling gekozen mogen worden, in de
    /// volgorde van de mockup.
    /// </summary>
    public static IReadOnlyList<string> Bookable { get; } =
        [Development, Maintenance, Support, Advice];

    /// <summary>
    /// Of dit een categorie is die geboekt mag worden.
    /// </summary>
    /// <param name="category">De categorie uit het formulier of uit een koppeling.</param>
    /// <returns><c>true</c> als hij bestaat en niet <see cref="Correction"/> is.</returns>
    public static bool IsBookable(string? category) =>
        category is not null && Bookable.Contains(category, StringComparer.Ordinal);

    /// <summary>
    /// Of dit een bestaande categorie is, inclusief <see cref="Correction"/>.
    /// </summary>
    /// <param name="category">De categorie uit een document.</param>
    /// <returns><c>true</c> als de categorie bestaat.</returns>
    /// <remarks>
    /// Voor de leeskant. Een document met een onbekende categorie wordt niet geweigerd — het staat
    /// er al — maar de weergave kan er dan iets over zeggen in plaats van het stil te tonen.
    /// </remarks>
    public static bool IsKnown(string? category) =>
        IsBookable(category) || string.Equals(category, Correction, StringComparison.Ordinal);
}

/// <summary>
/// De maandsleutel <c>yyyy-MM</c> waarop urenregels worden gegroepeerd (§6 <c>month</c>).
/// </summary>
/// <remarks>
/// <para><strong>Tekst en geen (jaar, maand)-paar, en in ISO-vorm.</strong> Dat volgt punt 7 van de
/// fase-0-afwijkingen: Cosmos slaat dit als tekst op en vergelijkt lexicografisch. Op <c>yyyy-MM</c>
/// werkt een bereikfilter (<c>&gt;= '2026-01' AND &lt;= '2026-12'</c>) en op elke andere vorm — ook
/// op <c>MM-yyyy</c>, zoals de mockup de datums schrijft — filtert en sorteert diezelfde query stil
/// verkeerd.</para>
///
/// <para><strong>De maand is geen afleiding uit <see cref="HourEntryDocument.CreatedAt"/>, en dat is
/// de reden dat dit veld bestaat.</strong> §3.6 laat een operator de maand kiezen bij het boeken: werk
/// van 31 juli dat op 1 augustus wordt vastgelegd hoort op juli. Zou de maand uit het tijdstip van
/// vastleggen volgen, dan was die keuze niet vast te leggen — en dan verschuift werk elke maandgrens
/// naar de verkeerde factuur. Zie punt 20 van de fase-0-afwijkingen: een urenregel kent één tijdstip
/// (wanneer hij is vastgelegd) en één periode (deze maand), en dat zijn twee verschillende dingen.
/// </para>
/// </remarks>
public static class HourMonths
{
    /// <summary>
    /// De Nederlandse maandnamen, met een kleine letter zoals in de mockup (<c>maart 2026</c>).
    /// </summary>
    /// <remarks>
    /// Uitgeschreven en niet uit <c>CultureInfo("nl-NL")</c>. Dat is geen wantrouwen tegen ICU maar
    /// tegen de omgeving: in globalization-invariant mode <em>werpt</em> <c>GetCultureInfo</c> niet, hij
    /// levert stil de invariante cultuur op — en dan staat er "August 2026" op het scherm van een
    /// Nederlandse klant. Twaalf woorden die nooit veranderen zijn dat risico niet waard. Dezelfde
    /// afweging als bij <see cref="Views.PortalTimeZone"/>, dat om dezelfde reden een terugval heeft.
    /// </remarks>
    private static readonly string[] Names =
    [
        "januari", "februari", "maart", "april", "mei", "juni",
        "juli", "augustus", "september", "oktober", "november", "december",
    ];

    /// <summary>De lengte van een maandsleutel.</summary>
    private const int Length = 7;

    /// <summary>
    /// De maandsleutel van een moment.
    /// </summary>
    /// <param name="moment">Het moment. De zone wordt genomen zoals hij is.</param>
    /// <returns>De sleutel, bijvoorbeeld <c>2026-08</c>.</returns>
    public static string Of(DateTimeOffset moment) =>
        moment.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>
    /// De maandsleutel van een datum.
    /// </summary>
    /// <param name="date">De datum.</param>
    /// <returns>De sleutel.</returns>
    public static string Of(DateOnly date) =>
        date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>
    /// Controleert een maandsleutel.
    /// </summary>
    /// <param name="month">De sleutel uit een formulier of een document.</param>
    /// <returns><c>null</c> als hij klopt, anders de melding voor het formulier.</returns>
    public static string? Validate(string? month)
    {
        if (string.IsNullOrWhiteSpace(month))
        {
            return "Kies een maand.";
        }

        return month.Trim().Length == Length && Parse(month) is not null
            ? null
            : "Een maand heeft de vorm jjjj-mm, bijvoorbeeld 2026-08.";
    }

    /// <summary>
    /// De eerste dag van deze maand, of <c>null</c> als de sleutel niet klopt.
    /// </summary>
    /// <param name="month">De sleutel.</param>
    /// <returns>De eerste dag, of <c>null</c>.</returns>
    public static DateOnly? Parse(string? month) =>
        DateOnly.TryParseExact(
            month?.Trim(),
            "yyyy-MM",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;

    /// <summary>
    /// Het jaar van deze maand, of <c>null</c> als de sleutel niet klopt.
    /// </summary>
    /// <param name="month">De sleutel.</param>
    /// <returns>Het jaartal, of <c>null</c>.</returns>
    public static int? YearOf(string? month) => Parse(month)?.Year;

    /// <summary>
    /// Het label zoals het op het scherm hoort te staan, bijvoorbeeld <c>augustus 2026</c>.
    /// </summary>
    /// <param name="month">De sleutel.</param>
    /// <returns>Het label, of de sleutel zelf als die niet te lezen is.</returns>
    /// <remarks>
    /// Nederlands en met een kleine letter, zoals de mockup (<c>maart 2026</c>). Staat hier en niet in
    /// de weergave omdat zowel het maandoverzicht als de regel in de specificatie hem nodig heeft, en
    /// twee kopieën van "hoe heet een maand" gaan uit de pas lopen zodra iemand er een afkorting van
    /// maakt.
    /// </remarks>
    public static string Label(string month) =>
        Parse(month) is { } date
            ? string.Create(CultureInfo.InvariantCulture, $"{Names[date.Month - 1]} {date.Year:D4}")
            : month;

    /// <summary>
    /// De maandsleutels van één jaar, oudste eerst.
    /// </summary>
    /// <param name="year">Het jaartal.</param>
    /// <returns>Twaalf sleutels.</returns>
    /// <remarks>
    /// Bestaat zodat het maandoverzicht een maand kan tonen waarin niets is geboekt. Zou het scherm
    /// de maanden uit de gevonden regels afleiden, dan verdwijnt een maand zonder uren van het
    /// overzicht — precies de maand waarover de vraag "is hier niets gebeurd of is er niets geboekt"
    /// gaat, en dan is de status "Niets geboekt" uit §3.6 nergens te zien.
    /// </remarks>
    public static IReadOnlyList<string> InYear(int year) =>
        [.. Enumerable.Range(1, 12).Select(month => $"{year:D4}-{month:D2}")];
}

/// <summary>
/// Stelt de sleutel van een urenregel samen. Zie <see cref="PortalDocumentIds.HourEntry(string)"/>.
/// </summary>
/// <remarks>
/// Eén plek, en de reden is dezelfde als bij <see cref="PortalDocumentIds"/>: een sleutel die op twee
/// plekken wordt samengesteld levert twee documenten op — en bij uren betekent dat een dubbel
/// gefactureerd uur.
/// </remarks>
public static class HourEntryKeys
{
    /// <summary>
    /// De sleutel van een regel die in het portaal is ingevoerd.
    /// </summary>
    /// <param name="createdAt">Het moment van invoeren, in UTC.</param>
    /// <param name="fingerprint">De inhoud die de regel onderscheidt: bron, omschrijving, uren, boeker.</param>
    /// <returns>De sleutel.</returns>
    /// <remarks>
    /// <para>Tijdstempel plus een korte hash van de inhoud. Het tijdstempel maakt de sleutel leesbaar
    /// in de opslag; de hash maakt hem uniek binnen dezelfde milliseconde.</para>
    ///
    /// <para><strong>Wat dit oplevert: bescherming tegen een dubbele verzending.</strong> Het portaal
    /// is static SSR, dus er is geen JavaScript dat de knop uitzet nadat erop is geklikt. Twee keer
    /// klikken levert twee POST's op met dezelfde inhoud; die vallen binnen dezelfde milliseconde en
    /// krijgen dus dezelfde sleutel, en de tweede loopt op een 409. Wat er níet mee wordt opgelost is
    /// twee identieke boekingen die iemand echt twee keer wil maken in dezelfde milliseconde — die
    /// kan niet, en dat is een eerlijker uitkomst dan een dubbele regel.</para>
    ///
    /// <para>De hash is geen beveiliging maar een verkorting, dus SHA-256 afgekapt op vier bytes is
    /// hier genoeg: hij hoeft alleen twee verschillende regels in dezelfde milliseconde te
    /// onderscheiden.</para>
    /// </remarks>
    public static string ForPortal(DateTimeOffset createdAt, string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{createdAt.UtcDateTime:yyyyMMddHHmmssfff}-{Convert.ToHexString(digest.AsSpan(0, 4)).ToLowerInvariant()}");
    }

    /// <summary>
    /// De sleutel van een regel die een koppeling heeft ingeschoten.
    /// </summary>
    /// <param name="source">De bron. <see cref="HourEntrySource.Portal"/> hoort hier niet.</param>
    /// <param name="externalId">
    /// Een sleutel die de koppeling bij een herhaling opnieuw kan produceren: het work item met de
    /// revisie van zijn Completed Work, bijvoorbeeld.
    /// </param>
    /// <returns>De sleutel.</returns>
    /// <remarks>
    /// <para><strong>Dit is de enige plek waar dubbel factureren wordt tegengehouden.</strong> Een
    /// koppeling die zijn aanroep herhaalt na een netwerkfout — en dat doet elke koppeling ooit —
    /// schrijft met deze sleutel hetzelfde document en krijgt een 409. Zonder een stabiele sleutel van
    /// de bron is er niets dat de tweede regel van een echte tweede regel onderscheidt, en dan is het
    /// antwoord niet "we verzinnen er een". Zie <see cref="HourEntryDocument.ExternalId"/>.</para>
    ///
    /// <para><strong>Niet elke koppeling heeft zo'n sleutel, en dat is geen reden om er een te
    /// bouwen.</strong> De MCP-server heeft hem niet: de JSON-RPC-request-id verandert bij een
    /// herhaling, en een sleutel over de inhoud (<c>cid|month|hours|category|note</c>) zou een tweede
    /// legitieme boeking van een uur op dezelfde dag met dezelfde omschrijving blokkeren. Zo'n
    /// koppeling krijgt dus géén idempotentie, en dat hoort dan ook zo te staan — bij de tool, als
    /// "een tweede poging levert een tweede regel op".</para>
    ///
    /// <para>Wat het risico in dat geval draagt is <see cref="HourEntryStatus.Pending"/>: een dubbele
    /// regel kan niet ongezien op een factuur komen, want iemand moet hem eerst fiatteren. Dat is
    /// zwakker dan een 409 en het is de bodem waar §5 voor bestaat.</para>
    ///
    /// <para><strong>Deze methode heeft vandaag geen aanroeper, en dat is met opzet.</strong> Het
    /// aannamepad van een koppeling hoort bij het portaalendpoint, en dat vraagt een eigen bewijstype
    /// voor een aanroeper die geen mens is. Dat type bestaat nog niet; zie het rapport van fase 3. De
    /// <em>sleutelregel</em> staat hier alvast, om dezelfde reden als bij
    /// <see cref="PortalDocumentIds"/>: een sleutel die op twee plekken wordt samengesteld levert twee
    /// documenten op, en bij uren is dat een dubbel gefactureerd uur.</para>
    ///
    /// <para>De bron zit in de sleutel omdat twee koppelingen dezelfde externe id kunnen gebruiken:
    /// een work item-nummer uit DevOps en een aanroep-id uit de MCP-server komen uit
    /// verschillende nummerreeksen en hoeven elkaar niet te ontwijken.</para>
    /// </remarks>
    public static string ForIntegration(HourEntrySource source, string externalId)
    {
        ArgumentNullException.ThrowIfNull(externalId);

        if (source == HourEntrySource.Portal)
        {
            throw new ArgumentOutOfRangeException(
                nameof(source),
                source,
                "Een portaalregel heeft geen externe sleutel. Gebruik ForPortal.");
        }

        return $"{Serialize(source)}-{externalId.Trim()}";
    }

    /// <summary>De opslagvorm van een bron, zoals hij in de sleutel en in het document staat.</summary>
    /// <param name="source">De bron.</param>
    /// <returns>De tekst.</returns>
    public static string Serialize(HourEntrySource source) => HourJsonValues.Of(source);
}

/// <summary>
/// De opslagvorm van de twee opsommingen, gelezen uit de serializer in plaats van overgeschreven.
/// </summary>
/// <remarks>
/// <para><strong>Dit bestaat om één stille fout onmogelijk te maken.</strong> Een
/// <c>QueryDefinition</c>-parameter gaat als tekst naar Cosmos en komt niet langs de converter van het
/// documenttype. Wie in een <c>WHERE</c>-clausule <c>status.ToString()</c> gebruikt, zoekt op
/// <c>"Approved"</c> terwijl er <c>"approved"</c> in het document staat — en dan levert de klantquery
/// <em>nul regels</em> op in plaats van een fout. Het scherm zegt dan dat er niets is geboekt, en de
/// factuur is te laag. Er is geen uitzondering die opgaat en geen log die afgaat.</para>
///
/// <para>Een handgeschreven <c>switch</c> met de drie teksten erin lost dat op tot het moment dat
/// iemand een waarde hernoemt of een converter met een andere naamgeving toevoegt; dan staan er twee
/// definities en gaan ze uiteen. Daarom komt de tekst hier uit
/// <see cref="JsonSerializer"/> met precies de attributen van het type zelf. Er is dus letterlijk één
/// bron: het document en de query kunnen niet meer verschillen, ook niet na een hernoeming.</para>
///
/// <para>Eén keer uitgerekend en gecachet. Serialiseren per query zou werken maar is een allocatie op
/// een pad dat per paginaweergave loopt, en de uitkomst verandert niet tijdens de rit.</para>
/// </remarks>
internal static class HourJsonValues
{
    private static readonly Dictionary<HourEntryStatus, string> Statuses =
        Enum.GetValues<HourEntryStatus>().ToDictionary(status => status, Text);

    private static readonly Dictionary<HourEntrySource, string> Sources =
        Enum.GetValues<HourEntrySource>().ToDictionary(source => source, Text);

    /// <summary>De opslagvorm van een stand, zoals hij in het document staat.</summary>
    /// <param name="status">De stand.</param>
    /// <returns>De tekst, bijvoorbeeld <c>approved</c>.</returns>
    internal static string Of(HourEntryStatus status) => Statuses[status];

    /// <summary>De opslagvorm van een bron, zoals hij in het document staat.</summary>
    /// <param name="source">De bron.</param>
    /// <returns>De tekst, bijvoorbeeld <c>portaal</c>.</returns>
    internal static string Of(HourEntrySource source) => Sources[source];

    /// <summary>
    /// Serialiseert één waarde en haalt de aanhalingstekens eraf.
    /// </summary>
    /// <remarks>
    /// De opties doen hier niet mee: de converter staat als attribuut op het type, en die gaat vóór de
    /// opties van de aanroeper. Dat is precies waarom dit werkt ongeacht met welke
    /// <see cref="JsonSerializerOptions"/> het document straks wordt weggeschreven.
    /// </remarks>
    private static string Text<T>(T value)
        where T : struct, Enum =>
        JsonSerializer.Serialize(value).Trim('"');
}

/// <summary>
/// Eén urenregel zoals hij in de opslag staat (§6 <c>HourEntry</c>).
/// </summary>
/// <remarks>
/// <para><strong>Waar dit document staat: in de container <c>customers</c>, op de partitiesleutel van
/// de klant, met <c>kind: "hourEntry"</c>.</strong> Niet in een eigen container, en dat is een keuze
/// met een prijs.</para>
///
/// <para>De reden om het niet te splitsen staat in <c>infra/portal/portal-rg.bicep</c> zelf: de drie
/// telemetriecontainers zijn gescheiden <em>omdat ze verschillende bewaartermijnen hebben</em>, en dat
/// is daar de enige reden die voor een splitsing wordt gegeven. Een urenregel verloopt net zo min als
/// een contract. "Groeit onbeperkt door" is geen tegenargument dat op deze schaal iets betekent: een
/// logische partitie mag 20 GB, dit document is een paar honderd bytes, en tien regels per dag per
/// klant gedurende tien jaar is enkele tientallen megabytes. Wat het wél oplevert is dat de bundel
/// (uit <see cref="ContractDocument"/>) en de urenregels in dezelfde partitie staan, dus dat het
/// urenscherm alles van één klant leest zonder fan-out.</para>
///
/// <para><strong>Wat dit besluit afsluit, en dat hoort erbij te staan.</strong> Cosmos-rollen op het
/// dataplane zijn per container te scopen. Zolang urenregels in <c>customers</c> staan, kan een
/// tweede identiteit géén schrijfrecht op uren krijgen zonder óók schrijfrecht op de
/// <see cref="AccessDocument"/>'s — en dat is de autorisatiebron van het portaal. Wie daar een regel
/// kan bijschrijven, verleent zichzelf toegang; zie reden 1 bij
/// <see cref="PortalDataLocation"/>, waar dat geen lek maar een rechtenverhoging wordt genoemd.
/// <strong>Het gevolg is dat de MCP-server en <c>devops-sync</c> hun uren via het portaal moeten
/// schrijven en niet rechtstreeks naar Cosmos.</strong> Dat is met die keuze een eis geworden en geen
/// voorkeur. Wordt daar ooit anders over besloten, dan hoort dit document naar een eigen container te
/// verhuizen, en dat is een Bicep-wijziging plus een kopieerslag.</para>
///
/// <para><strong>De veldnamen komen uit §6</strong> (<c>cid</c>, <c>date</c>, <c>month</c>,
/// <c>category</c>, <c>note</c>, <c>hours</c>, <c>source</c>, <c>by</c>, <c>status</c>). Wat daarnaast
/// staat is van ons: de sporen van wie wat wanneer heeft gedaan, en de sleutel van de koppeling.</para>
/// </remarks>
public sealed record HourEntryDocument
{
    /// <summary>Documentsleutel: <see cref="PortalDocumentIds.HourEntry(string)"/>.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Partitiesleutel. De klantslug.</summary>
    [JsonPropertyName("pk")]
    public required string PartitionKey { get; init; }

    /// <summary>Documentsoort. Altijd <see cref="PortalDocumentKinds.HourEntry"/>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = PortalDocumentKinds.HourEntry;

    /// <summary>De klantslug (<c>cid</c> in §6).</summary>
    [JsonPropertyName("cid")]
    public required string CustomerId { get; init; }

    /// <summary>
    /// De maand waarop de uren worden geboekt, als <c>yyyy-MM</c>.
    /// </summary>
    /// <remarks>
    /// De werkperiode, en niet af te leiden uit <see cref="CreatedAt"/>: werk van 31 juli dat op
    /// 1 augustus wordt vastgelegd hoort op juli. Dit is het veld waarop de facturatie rust. Zie
    /// <see cref="HourMonths"/>.
    /// </remarks>
    [JsonPropertyName("month")]
    public required string Month { get; init; }

    /// <summary>De categorie. Zie <see cref="HourCategories"/>.</summary>
    [JsonPropertyName("category")]
    public required string Category { get; init; }

    /// <summary>
    /// De omschrijving: één regel, leesbaar voor de klant.
    /// </summary>
    /// <remarks>
    /// <para><strong>Dit is vrije tekst uit een koppeling en de klant leest hem.</strong> Daarmee is
    /// het dezelfde soort veld als <c>msg</c> op een logregel en <c>errorMessage</c> op een run, en
    /// die twee zijn precies de velden waar in punt 13 en 14 van de afwijkingennotitie stacktraces en
    /// naamruimtes uit klantschermen zijn gehaald. De omschrijving van een MCP-regel wordt geschreven
    /// door Claude Code tijdens het werk; daar staat "Voorraad-sync: validatie locatieCode" in, maar
    /// er kan net zo goed een pad of een klassenaam in staan.</para>
    ///
    /// <para>De klantprojectie knipt daarom op de eerste regelovergang, met dezelfde
    /// <c>MessageTruncation.Cut</c> als de logregels. Zie <see cref="Views.CustomerHourRow"/>.</para>
    /// </remarks>
    [JsonPropertyName("note")]
    public required string Note { get; init; }

    /// <summary>
    /// Het aantal uren. Positief bij een boeking, en mogelijk negatief bij een correctie.
    /// </summary>
    /// <remarks>
    /// <para><strong>Niet-nullable, en dat is geen inconsistentie met §15.</strong> Die regel —
    /// een ontbrekend contractbedrag is niet nul — gaat over een <em>afspraak</em> die nog niet is
    /// gemaakt. Hier is het getal het bestaansrecht van het document: een urenregel zonder uren is
    /// geen halfleeg document maar een ongeldig document, en die wordt geweigerd in plaats van als
    /// nul opgeslagen.</para>
    ///
    /// <para>Negatief mag alleen op een correctie (<see cref="HourCategories.Correction"/>). Dat is
    /// wat een correctie naar beneden mogelijk maakt zonder een gefiatteerde regel te wijzigen; zie
    /// <see cref="HourCorrection"/>.</para>
    /// </remarks>
    [JsonPropertyName("hours")]
    public required decimal Hours { get; init; }

    /// <summary>Waar de regel vandaan komt. Zie <see cref="HourEntrySource"/>.</summary>
    [JsonPropertyName("source")]
    public required HourEntrySource Source { get; init; }

    /// <summary>
    /// Wie de uren op zijn naam heeft, bijvoorbeeld <c>Claude Code — Marcel</c> (§6 <c>by</c>).
    /// </summary>
    /// <remarks>
    /// Vrije tekst en geen verwijzing naar een gebruiker. De boeker van een MCP-regel is een mens die
    /// in Claude Code werkte en die geen portaalgebruiker hoeft te zijn; de boeker van een
    /// DevOps-regel is een work item. Een sleutel naar een gebruikerstabel zou beide gevallen
    /// uitsluiten, en de tabel bestaat niet.
    /// </remarks>
    [JsonPropertyName("by")]
    public required string By { get; init; }

    /// <summary>De fiatteringsstand. Zie <see cref="HourEntryStatus"/>.</summary>
    [JsonPropertyName("status")]
    public required HourEntryStatus Status { get; init; }

    /// <summary>
    /// De idempotentiesleutel van de koppeling, of <c>null</c> bij een portaalregel.
    /// </summary>
    /// <remarks>
    /// Staat naast de id waarin hij al verwerkt zit, en dat is met opzet: de id is een sleutel en de
    /// waarde is een gegeven. Wil je weten of een MCP-aanroep al een regel heeft opgeleverd, dan is
    /// dat een query op dit veld en niet een poging tot schrijven om te zien of hij faalt.
    /// </remarks>
    [JsonPropertyName("externalId")]
    public string? ExternalId { get; init; }

    /// <summary>
    /// Wanneer de regel is vastgelegd, in UTC. Dit is het enige tijdstip dat een urenregel over
    /// zichzelf kent.
    /// </summary>
    /// <remarks>
    /// <para><strong>§6 geeft <c>HourEntry</c> een veld <c>date</c>; dat veld staat hier niet, en dat is
    /// een besluit.</strong> Het is niet af te spreken wat het zou betekenen. De mockup laat het twee
    /// dingen zijn: bij de seed-regels staan datums verspreid over de maand (dat leest als de dag waarop
    /// het werk is gedaan) en bij een nieuwe boeking zet hij <c>DATA.now</c> (dat is de dag van
    /// vastleggen). Van die twee kan alleen de tweede voor élke bron waar zijn — §3.6 geeft het
    /// boekformulier geen datumveld, dus een operator kán geen werkdatum opgeven, en de MCP-tool uit §5
    /// heeft geen datumparameter.</para>
    ///
    /// <para><strong>En dan is <c>date</c> een duplicaat van dit veld, wat erger is dan een
    /// misleidende naam.</strong> Twee velden over hetzelfde moment, op verschillende korrel en in
    /// verschillende tijdzones, kunnen van elkaar gaan afwijken — en welke van de twee dan de
    /// specificatie haalt is niet te zeggen. Dat is precies de reden dat <c>tarief</c> niet naast
    /// <c>uurTarief</c> op <see cref="ContractDocument"/> staat, en dat een agent zijn eigen status niet
    /// publiceert (punt 2 van de afwijkingen). Eén moment, één veld, canoniek UTC.</para>
    ///
    /// <para>De specificatie toont hieruit de Nederlandse dag onder de kop <strong>Geboekt</strong> —
    /// niet "Datum", want dat woord belooft de werkdatum. Omzetten naar Nederlandse tijd gebeurt bij het
    /// weergeven en niet bij het opslaan; dat is punt 7 van de afwijkingen.</para>
    ///
    /// <para><strong>De werkperiode zit in <see cref="Month"/>, en dat is waarom dat veld
    /// bestaat.</strong> Werk van 31 juli dat op 1 augustus wordt vastgelegd heeft hier 1 augustus staan
    /// en juli als maand. Dat laatste is de vraag die de facturatie stelt. Bewaart een bron ooit wél een
    /// echte werkdatum — <c>devops-sync</c> zou dat kunnen, uit de revisie van het work item — dan is dat
    /// een tweede veld met een eigen naam, en nooit dit veld met een andere betekenis.</para>
    ///
    /// <para><c>required</c>, want de specificatie sorteert hierop. Een regel zonder tijdstip zou stil
    /// bovenaan of onderaan belanden.</para>
    /// </remarks>
    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Wie de regel heeft vastgelegd: de operator, of de naam van de koppeling.
    /// </summary>
    /// <remarks>
    /// Niet hetzelfde als <see cref="By"/>. Bij een MCP-regel is <c>by</c> de mens die het werk deed
    /// en <c>createdBy</c> de koppeling die de regel wegschreef; bij een correctie is <c>by</c> wie
    /// er verantwoordelijk voor is en <c>createdBy</c> wie hem intypte. Eén veld voor beide zou de
    /// vraag "wie heeft dit in de opslag gezet" onbeantwoordbaar maken, en dat is precies de vraag
    /// bij een factuurdiscussie.
    /// </remarks>
    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; init; }

    /// <summary>Wanneer de regel is gefiatteerd, of <c>null</c>.</summary>
    [JsonPropertyName("approvedAt")]
    public DateTimeOffset? ApprovedAt { get; init; }

    /// <summary>
    /// Welke operator hem heeft gefiatteerd, of <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Bij een portaalregel is dit dezelfde persoon als <see cref="CreatedBy"/> en gelijk aan
    /// <see cref="CreatedAt"/>: boeken in het portaal ís fiatteren (§5 gaat over agents en
    /// koppelingen). Het veld staat er toch, zodat "wanneer is dit uur gaan meetellen" één antwoord
    /// heeft voor alle bronnen. Zonder dat zou de facturatie voor een portaalregel naar
    /// <c>createdAt</c> moeten kijken en voor een MCP-regel naar <c>approvedAt</c>, en dan bestaan er
    /// twee definities van hetzelfde moment.
    /// </remarks>
    [JsonPropertyName("approvedBy")]
    public string? ApprovedBy { get; init; }

    /// <summary>Wanneer de regel is afgewezen, of <c>null</c>.</summary>
    [JsonPropertyName("rejectedAt")]
    public DateTimeOffset? RejectedAt { get; init; }

    /// <summary>Welke operator hem heeft afgewezen, of <c>null</c>.</summary>
    [JsonPropertyName("rejectedBy")]
    public string? RejectedBy { get; init; }

    /// <summary>
    /// Waarom de regel is afgewezen. Verplicht bij <see cref="HourEntryStatus.Rejected"/>.
    /// </summary>
    /// <remarks>
    /// <para><strong>Een afgewezen regel wordt niet verwijderd, en dit veld is de reden dat dat
    /// werkt.</strong> De spec zegt er niets over; het alternatief — weggooien — is aantrekkelijk
    /// omdat een lijst vol afgewezen regels onbruikbaar wordt. Dat argument is opgelost in de
    /// weergave: afgewezen regels staan niet in de specificatie maar in een eigen lijst, zie
    /// <see cref="Views.OperatorHoursView.Rejected"/>.</para>
    ///
    /// <para>Wat niet in de weergave oplosbaar is, is het volgende. Een koppeling die zijn aanroep
    /// herhaalt schrijft dezelfde id (zie <see cref="HourEntryKeys.ForIntegration"/>). Is de
    /// afgewezen regel verwijderd, dan slaagt die herhaling en staat de regel opnieuw als te
    /// fiatteren in de lijst — en de operator wijst hem opnieuw af, elke keer dat de koppeling
    /// draait. Afwijzen zou daarmee geen besluit meer zijn maar een handeling die je blijft
    /// herhalen. Blijft het document staan, dan botst de herhaling erop en is het besluit
    /// blijvend.</para>
    ///
    /// <para>De tweede reden is de factuurdiscussie: "waarom staat dit niet op de factuur" is een
    /// vraag die maanden later komt, en zonder dit veld is het antwoord verdwenen.</para>
    /// </remarks>
    [JsonPropertyName("rejectReason")]
    public string? RejectionReason { get; init; }

    /// <summary>De versie waarop de gelijktijdigheidscontrole loopt. Zie <see cref="CustomerDocument.ETag"/>.</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; init; }

    /// <summary>
    /// Of deze regel meetelt in het maandtotaal en in de facturatie.
    /// </summary>
    /// <remarks>
    /// Eén plek, zodat "telt mee" niet in elke som opnieuw als <c>Status == Approved</c> wordt
    /// opgeschreven. Komt er ooit een vierde stand bij, dan valt de beslissing hier en niet op zes
    /// plekken waarvan iemand er vijf vindt.
    /// </remarks>
    [JsonIgnore]
    public bool Counts => Status == HourEntryStatus.Approved;
}
