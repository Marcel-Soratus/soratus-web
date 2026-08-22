using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soratus.Portal.Support;

/// <summary>
/// De documentsoort en de sleutels van de berichten in de supportdraad (§3.8).
/// </summary>
/// <remarks>
/// <para><strong>Waarom deze constanten hier staan en niet bij
/// <see cref="Data.PortalDocumentKinds"/>.</strong> Daar hóren ze, en daar moeten ze op een dag
/// terechtkomen: één plek voor de documentsoorten van de container <c>customers</c>. Ze staan hier om
/// dezelfde werkomstandigheid als bij <see cref="Mail.StatementDocumentKeys"/> — er werken andere
/// sessies in <c>Data/</c>, en een gedeeld bestand met drie schrijvers is de gegarandeerde
/// merge-botsing. Dat is een werkomstandigheid en geen ontwerp; het staat als punt van twijfel in het
/// rapport.</para>
///
/// <para>Wat de tussentijd veilig houdt is dezelfde test die <c>statement</c> dekt: er is een
/// controle die alle <c>kind</c>-waarden in het portaal verzamelt en op dubbelen toetst. Twee soorten
/// met dezelfde <c>kind</c> in dezelfde container is de fout die dit zou kunnen opleveren, en die is
/// niet zichtbaar — een query op <c>kind</c> levert dan documenten van het verkeerde type op en de
/// deserialisatie vult de ontbrekende velden met hun standaardwaarde.</para>
/// </remarks>
public static class SupportDocumentKeys
{
    /// <summary>
    /// De documentsoort: één bericht in de draad tussen klant en Soratus.
    /// </summary>
    /// <remarks>
    /// Enkelvoud en in het Engels, zoals de vijf bestaande soorten (<c>customer</c>, <c>contract</c>,
    /// <c>access</c>, <c>hourEntry</c>, <c>statement</c>). <c>supportMessage</c> en niet
    /// <c>message</c>: §6 noemt het type <c>Message</c>, maar dat woord is in een container die ook
    /// mail- en logbegrippen kent te breed om er een query op te durven zetten.
    /// </remarks>
    public const string Kind = "supportMessage";

    /// <summary>
    /// De documentsleutel van één bericht, binnen de partitie van die klant.
    /// </summary>
    /// <param name="createdAt">Het moment van vastleggen, in UTC.</param>
    /// <param name="fingerprint">
    /// De inhoud die dit bericht onderscheidt: de afzender en de tekst.
    /// </param>
    /// <returns>Bijvoorbeeld <c>supportMessage-20260822T134501123-4f1a9c02</c>.</returns>
    /// <remarks>
    /// <para><strong>Deze sleutel sorteert chronologisch, en daarin wijkt hij bewust af van
    /// <see cref="Data.PortalDocumentIds.HourEntry(string)"/>.</strong> Daar staat met zoveel woorden
    /// dat de sleutel géén ordening mag suggereren, omdat er nooit op gesorteerd wordt en een
    /// id-vorm die dat wel suggereert uitnodigt tot een query die erop leunt. Bij een berichtendraad
    /// is de ordening juist het hele ding: een draad ís een volgorde. De sleutel draagt hem daarom
    /// wél, zodat twee berichten binnen dezelfde milliseconde een vaste onderlinge volgorde hebben
    /// en niet per lezing van plaats wisselen.</para>
    ///
    /// <para><strong>Wat hij verder oplevert: bescherming tegen een dubbele verzending.</strong>
    /// Hetzelfde als bij een urenregel. Het portaal is static SSR, dus er is geen JavaScript dat de
    /// knop uitzet nadat erop is geklikt; twee keer klikken levert twee POST's met dezelfde inhoud
    /// op, die vallen binnen dezelfde milliseconde, krijgen dezelfde sleutel, en de tweede loopt op
    /// een <c>409</c>. Wat er níet mee wordt opgelost is iemand die binnen dezelfde milliseconde
    /// werkelijk twee keer hetzelfde wil zeggen. Die kan niet, en dat is eerlijker dan een dubbele
    /// bubbel in een gesprek.</para>
    ///
    /// <para>De hash is een verkorting en geen beveiliging: hij hoeft alleen twee verschillende
    /// berichten binnen dezelfde milliseconde te onderscheiden. SHA-256 afgekapt op vier bytes is
    /// daarvoor genoeg.</para>
    /// </remarks>
    public static string Id(DateTimeOffset createdAt, string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Kind}-{createdAt.UtcDateTime:yyyyMMdd'T'HHmmssfff}-{Convert.ToHexString(digest.AsSpan(0, 4)).ToLowerInvariant()}");
    }
}

/// <summary>
/// Wie een bericht heeft geschreven (§6, <c>from</c>: klant / soratus / ai).
/// </summary>
/// <remarks>
/// <para><strong>Dit veld bepaalt met wélke stem een tekst op het scherm van de klant staat, en dat
/// maakt de standaardwaarde belangrijk.</strong> Een document met een leeg, hernoemd of onleesbaar
/// <c>author</c>-veld leest als de eerste waarde van de enum. Dat is hier <see cref="Unknown"/>, en de
/// twee alternatieven zijn allebei erger:</para>
/// <list type="bullet">
///   <item><description>
///     stond <see cref="Customer"/> op nul, dan zou een antwoord van ons als vraag van de klant
///     terugkomen;
///   </description></item>
///   <item><description>
///     stond <see cref="Soratus"/> op nul, dan zou de tekst van de klant in een witte
///     Soratus-bubbel staan — dus met ónze stem, met onze autoriteit, terug naar de klant. Dat is de
///     ergste van de drie: wat een klant schrijft is vrije tekst, en een deel van dit ontwerp bestaat
///     ervoor dat die tekst nooit als onze bewering kan worden gelezen.
///   </description></item>
/// </list>
///
/// <para>De klantprojectie laat een bericht met <see cref="Unknown"/> daarom wég, en de
/// operatorprojectie toont het als niet toe te wijzen. Dat is dezelfde asymmetrie als bij punt 12 en
/// punt 14 van de fase-0-afwijkingen: de operator ziet er méér, niet minder.</para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<SupportAuthor>))]
public enum SupportAuthor
{
    /// <summary>
    /// Niet toe te wijzen. Geen waarde die iemand schrijft, maar wat een beschadigd document leest.
    /// </summary>
    [JsonStringEnumMemberName("unknown")]
    Unknown,

    /// <summary>De klant. Vrije tekst die wij later lezen.</summary>
    [JsonStringEnumMemberName("klant")]
    Customer,

    /// <summary>Een mens van Soratus. Vrije tekst die de klant leest.</summary>
    [JsonStringEnumMemberName("soratus")]
    Soratus,

    /// <summary>
    /// De AI-eerstelijnsagent (§3.8).
    /// </summary>
    /// <remarks>
    /// <para><strong>Een bericht met deze afzender kan niet bestaan zonder een grondslag of een
    /// escalatiereden, en dat is geen afspraak maar de vorm van het schrijfpad.</strong> Zie
    /// <see cref="ISupportStore.RecordFirstLineAsync"/>: dat is de enige methode die deze waarde zet,
    /// en zij neemt een <see cref="SupportAnswer"/>. Er is geen overload waarmee een aanroeper hier
    /// vrije tekst neerzet.</para>
    ///
    /// <para>De waarde heet <c>ai</c> in de opslag, want zo noemt §6 hem. In de code heet hij
    /// <c>FirstLine</c>: "AI" zegt met welke techniek het antwoord is gemaakt en "eerstelijn" zegt
    /// welke rol het vervult, en de rol is wat de code onderscheidt.
    /// </para>
    /// </remarks>
    [JsonStringEnumMemberName("ai")]
    FirstLine,
}

/// <summary>
/// De opslagvorm van <see cref="SupportAuthor"/>, gelezen uit de serializer in plaats van
/// overgeschreven.
/// </summary>
/// <remarks>
/// Zelfde constructie en dezelfde reden als <c>HourJsonValues</c>: een <c>QueryDefinition</c>-parameter
/// gaat als tekst naar Cosmos en komt niet langs de converter van het documenttype. Wie in een
/// <c>WHERE</c>-clausule <c>author.ToString()</c> gebruikt, zoekt op <c>"Customer"</c> terwijl er
/// <c>"klant"</c> staat — en dan levert de query nul berichten op in plaats van een fout. Een draad
/// die er leeg uitziet terwijl er berichten in staan is precies het soort stille fout dat dit portaal
/// niet nog een keer moet hebben.
/// </remarks>
internal static class SupportJsonValues
{
    private static readonly Dictionary<SupportAuthor, string> Authors =
        Enum.GetValues<SupportAuthor>().ToDictionary(author => author, Text);

    /// <summary>De opslagvorm van een afzender.</summary>
    /// <param name="author">De afzender.</param>
    /// <returns>De tekst, bijvoorbeeld <c>klant</c>.</returns>
    internal static string Of(SupportAuthor author) => Authors[author];

    private static string Text<T>(T value)
        where T : struct, Enum =>
        JsonSerializer.Serialize(value).Trim('"');
}

/// <summary>
/// Eén bericht in de supportdraad, zoals het in de opslag staat (§6 <c>Message</c>).
/// </summary>
/// <remarks>
/// <para><strong>Waar dit document staat: in de container <c>customers</c>, op de partitiesleutel van
/// de klant, met <c>kind: "supportMessage"</c>.</strong> Dat is dezelfde plek als de urenregels, en de
/// afweging is hier opnieuw gemaakt en niet gekopieerd. De drie argumenten van
/// <see cref="Data.HourEntryDocument"/> gaan hier alle drie op, en er komt één bij:</para>
/// <list type="number">
///   <item><description>
///     <strong>Bewaartermijn.</strong> <c>infra/portal/portal-rg.bicep</c> geeft precies één reden om
///     containers te splitsen: verschillende bewaartermijnen. Een supportbericht verloopt niet — de
///     vraag "wat hebben jullie mij in maart geantwoord" komt in september, net als bij een
///     factuurdiscussie. Geen TTL, dus geen eigen container.
///   </description></item>
///   <item><description>
///     <strong>Partitiesleutel.</strong> Een draad wordt altijd van één klant gelezen en nooit over
///     klanten heen. De klantslug is dus precies de goede sleutel, en er bestaat geen query die er
///     buiten hoeft.
///   </description></item>
///   <item><description>
///     <strong>Eén partitie betekent één lezing.</strong> De escalatie moet de SLA noemen (§3.8), en
///     de SLA staat op <see cref="Data.ContractDocument.Sla"/> — in dezelfde partitie. Het scherm
///     leest de draad en de SLA dus zonder fan-out, en er hoeft nergens een tweede kopie van de SLA
///     te staan.
///   </description></item>
///   <item><description>
///     <strong>En het argument dat hier de kant op valt die bij uren de prijs was.</strong>
///     Cosmos-rollen op het dataplane zijn per container te scopen. Zolang dit document in
///     <c>customers</c> staat, kan geen tweede identiteit schrijfrecht op de draad krijgen zonder óók
///     schrijfrecht op de <see cref="Data.AccessDocument"/>'s — en dat is de autorisatiebron van het
///     portaal. Bij uren was dat een prijs: de MCP-server en <c>devops-sync</c> moeten daardoor via
///     het portaal schrijven. Hier is het precies wat we willen. De AI-eerstelijnsagent hoort niet bij
///     Cosmos te kunnen: hij hoort een antwoord terug te geven aan het portaal, en het portaal hoort
///     te beslissen of daar een bericht van wordt. Zie <see cref="ISupportStore"/>.
///   </description></item>
/// </list>
///
/// <para><strong>De veldnamen komen uit §6</strong> (<c>cid</c>, <c>from</c>, <c>who</c>, <c>at</c>,
/// <c>text</c>, <c>context</c>). Eén afwijking: §6 noemt <c>context</c>, en dat veld staat hier niet
/// als vrije brok. §3.8 zegt waarom — de klantcontext wordt niet als paneel getoond, want dat is
/// simpelweg alles wat we van de klant weten. Wat er in de plaats staat is de <em>grondslag</em> van
/// één antwoord: <c>groundKind</c> en <c>groundKey</c>. Dat is geen context maar een bron, en het
/// verschil is dat een bron aanwijsbaar is.</para>
/// </remarks>
public sealed record SupportMessageDocument
{
    /// <summary>Documentsleutel: <see cref="SupportDocumentKeys.Id"/>.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Partitiesleutel. De klantslug.</summary>
    [JsonPropertyName("pk")]
    public required string PartitionKey { get; init; }

    /// <summary>Documentsoort. Altijd <see cref="SupportDocumentKeys.Kind"/>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = SupportDocumentKeys.Kind;

    /// <summary>De klantslug (<c>cid</c> in §6).</summary>
    [JsonPropertyName("cid")]
    public required string CustomerId { get; init; }

    /// <summary>Wie het bericht heeft geschreven (<c>from</c> in §6).</summary>
    [JsonPropertyName("from")]
    public required SupportAuthor Author { get; init; }

    /// <summary>
    /// De naam van de schrijver zoals hij op het scherm hoort te staan (<c>who</c> in §6), of
    /// <c>null</c> bij de eerstelijnsagent.
    /// </summary>
    /// <remarks>
    /// <para><strong>Bij een klantbericht is dit de aangemelde gebruiker en niet iets uit het
    /// formulier.</strong> Het staat daarom niet op <see cref="SupportQuestionForm"/> — het formulier
    /// dat de POST bindt heeft alleen een tekstveld. Zou de naam bindbaar zijn, dan zou een
    /// zelfgemaakte POST hem kunnen zetten, en dan staat er in de draad van een klant een bericht op
    /// naam van iemand die het niet heeft geschreven.</para>
    ///
    /// <para>Bij een Soratus-bericht is dit <see cref="Security.PortalWriteScope.Actor"/>, en ook dat
    /// is geen parameter.</para>
    ///
    /// <para><strong>Bij de eerstelijnsagent is dit <c>null</c>, en dat is opzet.</strong> Er staat
    /// geen modelnaam en geen versie. Twee redenen: het is een configuratiewaarde en die hoort niet in
    /// een document dat een klant leest, en het antwoord van de eerstelijn is geen bewering van een
    /// model maar een verwijzing naar een grondslag van het portaal — zie
    /// <see cref="SupportAnswer"/>. Wie welk model heeft gedraaid hoort in de logregel, bij de
    /// operator.</para>
    /// </remarks>
    [JsonPropertyName("who")]
    public string? Who { get; init; }

    /// <summary>
    /// De tekst van het bericht (<c>text</c> in §6).
    /// </summary>
    /// <remarks>
    /// <para><strong>Vrije tekst in twee richtingen, en dat is de kern van punt 13 en punt 14 van de
    /// fase-0-afwijkingen — hier voor het eerst beide kanten in hetzelfde veld.</strong> Wat de klant
    /// schrijft lezen wij later; wat wij schrijven leest een klant. Wat er aan beide zijden is
    /// afgesloten en wat niet staat bij <see cref="SupportBody"/>.</para>
    ///
    /// <para><strong>Bij <see cref="SupportAuthor.FirstLine"/> is deze tekst niet door het model
    /// geschreven.</strong> Hij wordt door <see cref="SupportText"/> samengesteld uit
    /// <see cref="GroundKind"/> en de bijbehorende grondslag. Het antwoordtype van de naad heeft geen
    /// tekstveld, dus er is geen pad waarlangs een gegenereerde zin hier terechtkomt.</para>
    /// </remarks>
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    /// <summary>
    /// Waar het antwoord van de eerstelijn op rust, of <c>null</c> bij elk ander bericht.
    /// </summary>
    /// <remarks>
    /// <para><strong>Bij <see cref="SupportAuthor.FirstLine"/> is óf dit veld gevuld, óf
    /// <see cref="Escalation"/> — nooit geen van beide.</strong> Dat is niet met een <c>required</c>
    /// af te dwingen, want de twee sluiten elkaar uit; het wordt afgedwongen door het schrijfpad, dat
    /// een <see cref="SupportAnswer"/> neemt en die kent precies twee vormen. En het wordt bij het
    /// lezen afgedwongen door de projectie: een eerstelijnbericht zonder grondslag en zonder
    /// escalatiereden levert géén bubbel op, want er is geen bubbeltype dat het kan dragen. Zie
    /// <see cref="SupportBubble"/>.</para>
    /// </remarks>
    [JsonPropertyName("groundKind")]
    public SupportGroundKind? GroundKind { get; init; }

    /// <summary>
    /// De aanduiding binnen de grondslag: een agentnaam, een maand als <c>jjjj-MM</c>, een
    /// factuurmaand. <c>null</c> als er geen grondslag is.
    /// </summary>
    /// <remarks>
    /// Hij staat er zodat de bronregel in de bubbel kan zeggen waaróver het ging, en zodat er een
    /// werkende verwijzing naar het scherm bij kan waar het getal vandaan komt. Dat is de tweede helft
    /// van de eis uit §3.8: niet alleen dát er een bron is, maar welke.
    /// </remarks>
    [JsonPropertyName("groundKey")]
    public string? GroundKey { get; init; }

    /// <summary>
    /// De reden dat de eerstelijn het niet zeker wist, of <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Een enum en geen tekst, om dezelfde reden als <see cref="Mail.StatementRefusal"/>: een reden
    /// die als string reist komt op een dag uit een <c>catch</c>-blok, en dit veld staat in de draad
    /// van een klant. De Nederlandse zin staat in <see cref="SupportText"/> en is met de hand
    /// geschreven.
    /// </remarks>
    [JsonPropertyName("escalation")]
    public SupportEscalation? Escalation { get; init; }

    /// <summary>
    /// Wanneer het bericht is vastgelegd, in UTC (<c>at</c> in §6). Dit is de ordening van de draad.
    /// </summary>
    /// <remarks>
    /// Eén moment en canoniek UTC; omzetten naar Nederlandse tijd gebeurt bij het weergeven. Dat is
    /// punt 7 en punt 25 van de fase-0-afwijkingen. <c>required</c>, want de draad sorteert hierop en
    /// een bericht zonder moment zou stil boven- of onderaan belanden.
    /// </remarks>
    [JsonPropertyName("at")]
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>De versie waarop de gelijktijdigheidscontrole loopt. Zie <see cref="Data.CustomerDocument.ETag"/>.</summary>
    /// <remarks>
    /// Staat er omdat elk document in deze container hem heeft, en niet omdat er iets mee gebeurt: een
    /// bericht wordt na het schrijven niet meer gewijzigd. Er is geen methode op
    /// <see cref="ISupportStore"/> die een bestaand bericht aanraakt — een draad is een verslag en
    /// geen bewerkbaar veld, en een antwoord dat achteraf verandert maakt van "dit heeft u ons
    /// geschreven" een bewering zonder bron.
    /// </remarks>
    [JsonPropertyName("_etag")]
    public string? ETag { get; init; }
}
