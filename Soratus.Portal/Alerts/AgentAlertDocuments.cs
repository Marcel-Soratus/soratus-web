using System.Text.Json.Serialization;
using Soratus.Agents.Contracts;
using Soratus.Portal.Data;
using Soratus.Portal.Mail;

namespace Soratus.Portal.Alerts;

/// <summary>
/// De documentsoort en de sleutel van een ontdubbelmarkering.
/// </summary>
/// <remarks>
/// <para><strong>Waarom deze constanten hier staan en niet bij
/// <see cref="PortalDocumentKinds"/>.</strong> Om dezelfde reden als bij
/// <see cref="StatementDocumentKeys"/>: daar hóren ze, en dat is ook waar ze op een dag terecht moeten
/// komen, maar er werken meer sessies in <c>Data/</c> en een gedeeld bestand met vier schrijvers is de
/// gegarandeerde merge-botsing. Dat is een werkomstandigheid en geen ontwerp; het staat als punt van
/// twijfel in het rapport.</para>
///
/// <para>Wat de tussentijd veilig houdt is dezelfde test als daar: er is een test die alle
/// <c>kind</c>-waarden in het portaal verzamelt en op dubbelen controleert. Twee soorten met dezelfde
/// <c>kind</c> in dezelfde container is de fout die dit zou kunnen opleveren, en die is niet
/// zichtbaar — een query op <c>kind</c> levert dan documenten van het verkeerde type op en de
/// deserialisatie vult de ontbrekende velden met hun standaardwaarde.</para>
/// </remarks>
public static class AgentAlertDocumentKeys
{
    /// <summary>
    /// De documentsoort: één ontdubbelmarkering van één agent.
    /// </summary>
    /// <remarks>
    /// Enkelvoud en in het Engels, zoals de bestaande soorten. <c>agentAlert</c> en niet <c>alert</c>:
    /// er komt op een dag iets anders bij dat ook een melding heet, en dan is niet meer te zien
    /// waarover deze gaat.
    /// </remarks>
    public const string Kind = "agentAlert";

    /// <summary>
    /// De documentsleutel van de markering van één agent, in de gereserveerde partitie.
    /// </summary>
    /// <param name="customerId">De klantslug.</param>
    /// <param name="agentName">De technische naam van de agent.</param>
    /// <returns>Bijvoorbeeld <c>agentAlert-mbv-declaraties-import</c>.</returns>
    /// <remarks>
    /// <para><strong>De sleutel is afgeleid, en dat is het slot op een dubbele mail.</strong> Eén
    /// klant, één agent, één markering. Hij wordt met <c>CreateItemAsync</c> geschreven — geen upsert —
    /// vóórdat er iets wordt verstuurd, dus een tweede portaalinstantie die op hetzelfde moment
    /// hetzelfde besluit neemt krijgt een <c>409</c> en verstuurt niets. Dezelfde eigenschap en
    /// dezelfde reden als bij <see cref="StatementDocumentKeys.Id(string)"/>.</para>
    ///
    /// <para><strong>Leesbaar en geen hash</strong>, om dezelfde reden als daar: deze sleutel komt in
    /// een logregel terecht en dan is de vraag welke agent het was.</para>
    /// </remarks>
    public static string Id(string customerId, string agentName) =>
        $"{Kind}-{customerId}-{agentName}";
}

/// <summary>
/// De markering dat er over één agent is gemeld: één document per klant per agent.
/// </summary>
/// <remarks>
/// <para><strong>Dit document is de ontdubbeling, en het is ook het slot.</strong> Twee dingen in één
/// vorm, en dat is met opzet:</para>
///
/// <list type="bullet">
///   <item><description>
///     <strong>Over de tijd.</strong> <see cref="AgentStatusCalculator.ShouldAlert"/> ontdubbelt met
///     opzet niet — het is de zuivere vraag "hoort hier een melding over" en niet "hebben we die al
///     gestuurd", en het scherm gebruikt diezelfde regel. Voor <see cref="AgentStatus.Failed"/> levert
///     dat elke aanroep <c>true</c>, dus een melder die elke minuut draait zou zestig keer per uur
///     over dezelfde mislukte run mailen. Wat hem tegenhoudt is <see cref="NotifiedAt"/> naast
///     <see cref="AgentAlertOptions.RepeatAfter"/>.
///   </description></item>
///   <item><description>
///     <strong>Tussen instanties.</strong> Het portaal kan meer dan één instantie hebben, en dan
///     draaien er twee melders. Het document wordt vóór de verzending geschreven met een
///     <c>CreateItemAsync</c> (de eerste keer) of met een etagcontrole (een herhaling), dus van twee
///     melders die op hetzelfde moment hetzelfde besluiten verstuurt er precies één.
///   </description></item>
/// </list>
///
/// <para><strong>De betekenis van deze claim is die van de mail en niet die van de kostenrun, en dat
/// verschil staat al opgeschreven.</strong> Punt 38 zegt het van de andere kant: de dagclaim van
/// <c>AzureCostCollector</c> is een <em>wederzijdse uitsluiting</em> op een schaars budget — een
/// kostenlezing is herhaalbaar, er gaat niets de deur uit, en daarom staat er geen toestand op dat
/// document en is er geen uitgang. Hier is het een slot op een <em>onherhaalbare handeling</em>: een
/// verstuurde mail is niet terug te halen. Vandaar dat er wél een toestand op staat
/// (<see cref="Delivery"/>) en dat de markering blijft staan als de verzending mislukt.</para>
///
/// <para><strong>In de gereserveerde partitie en niet bij de klant.</strong> Dit is Soratus-eigen
/// boekhouding over onze eigen meldingen — de klant heeft er niets mee te maken en ziet hem nergens.
/// Dezelfde plek en dezelfde reden als <c>AzureCostRunDocument</c>: een klantslug moet met een kleine
/// letter of cijfer beginnen (<see cref="PortalSlug"/>), dus deze partitie kan nooit met die van een
/// klant samenvallen. En het levert hier bovendien de goedkope lezing op: alle markeringen staan in één
/// partitie, dus één ronde vraagt één query binnen één partitie in plaats van een cross-partition
/// query of één query per klant.</para>
///
/// <para><strong>Wat er niet in geregeld is, eerlijk:</strong> deze documenten hebben geen verval. De
/// container <c>customers</c> staat in Bicep op <c>ttl: null</c>, dus een item-TTL doet daar niets. Het
/// zijn er hoogstens zoveel als er ooit agents met een storing zijn geweest — tientallen — dus het
/// kost niets meetbaars, maar de markering van een agent die is opgeruimd blijft staan. Gemeld,
/// dezelfde soort rommel als bij de dagclaims van de kostencollector.</para>
/// </remarks>
public sealed record AgentAlertDocument
{
    /// <summary>Documentsleutel: <see cref="AgentAlertDocumentKeys.Id(string,string)"/>.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Partitiesleutel. Altijd <see cref="PortalDocumentIds.ReservedPartitionKey"/>.</summary>
    [JsonPropertyName("pk")]
    public required string PartitionKey { get; init; }

    /// <summary>Documentsoort. Altijd <see cref="AgentAlertDocumentKeys.Kind"/>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = AgentAlertDocumentKeys.Kind;

    /// <summary>De klantslug.</summary>
    [JsonPropertyName("cid")]
    public required string CustomerId { get; init; }

    /// <summary>De technische naam van de agent.</summary>
    [JsonPropertyName("agentName")]
    public required string AgentName { get; init; }

    /// <summary>
    /// De status waarover het laatst is gemeld.
    /// </summary>
    /// <remarks>
    /// <para>Als tekst en niet als getal. De numerieke waarde van <see cref="AgentStatus"/> <em>is</em>
    /// de ernstrang uit §3.1, en die is bedoeld om te sorteren en niet om op te slaan: schuift die rang
    /// ooit, dan zou een bewaard getal een andere status gaan betekenen zonder dat er iets verandert
    /// aan het document.</para>
    ///
    /// <para>De omzetting komt van de serializer, met een converter op déze eigenschap en niet met een
    /// attribuut op de enum. Dat type staat in <c>Soratus.Agents.Contracts</c> — het agentcontract — en
    /// hoe het portaal zijn eigen boekhouding wegschrijft is geen eigenschap van dat contract.</para>
    ///
    /// <para><strong>Dit veld is het verschil tussen "nog steeds stuk" en "erger geworden".</strong>
    /// Een status die verandert meldt meteen, ook binnen het herhaalvenster; zie
    /// <see cref="AgentAlertDecision"/>.</para>
    /// </remarks>
    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter<AgentStatus>))]
    public required AgentStatus Status { get; init; }

    /// <summary>Wanneer de melding is aangeboden, in UTC.</summary>
    /// <remarks>
    /// Het moment van de claim, dus vóór de aanroep naar Communication Services. Hierop staat het
    /// herhaalvenster; zie <see cref="AgentAlertOptions.RepeatAfter"/>.
    /// </remarks>
    [JsonPropertyName("notifiedAt")]
    public required DateTimeOffset NotifiedAt { get; init; }

    /// <summary>Wanneer deze storingsperiode voor het eerst is gemeld, in UTC.</summary>
    /// <remarks>
    /// Blijft staan bij een herhaling. Dit is het antwoord op "sinds wanneer weten we hiervan", en dat
    /// is niet uit <see cref="NotifiedAt"/> te halen zodra er één keer is herhaald.
    /// </remarks>
    [JsonPropertyName("firstNotifiedAt")]
    public required DateTimeOffset FirstNotifiedAt { get; init; }

    /// <summary>Hoeveel meldingen er in deze storingsperiode zijn aangeboden.</summary>
    [JsonPropertyName("notifications")]
    public int Notifications { get; init; } = 1;

    /// <summary>Welke instantie de melding heeft aangeboden.</summary>
    /// <remarks>
    /// Alleen om na te zoeken, dezelfde rol als <c>AzureCostRunDocument.ClaimedBy</c>. Dit is het enige
    /// veld waaraan te zien is dat er meer dan één instantie draait.
    /// </remarks>
    [JsonPropertyName("notifiedBy")]
    public string? NotifiedBy { get; init; }

    /// <summary>
    /// Hoe de laatste verzending is afgelopen.
    /// </summary>
    /// <remarks>
    /// <para><see cref="MailDelivery.Unknown"/> is de standaardwaarde en dat is de veilige: een
    /// document dat is aangemaakt en waarop daarna niets meer is geschreven — omdat het proces omviel
    /// tussen de claim en de verzending — zegt daarmee "onbekend" en niet "aangenomen".</para>
    ///
    /// <para><strong>Een mislukte verzending zet de markering niet terug, en dat is een keuze met een
    /// prijs.</strong> Bij <see cref="MailDelivery.Refused"/> weten we zeker dat er niets is verstuurd,
    /// dus opnieuw proberen zou mogen. Het gebeurt niet: een <c>4xx</c> is hier vrijwel altijd een
    /// inrichtingsfout — een ontvanger die niet klopt, een afzender die niet is geverifieerd — en die
    /// gaat niet over binnen een minuut. Elke minuut opnieuw proberen zou een storing in het melden
    /// verergeren tot een storing bij de dienstverlener. Wat er in de plaats komt is een
    /// <c>error</c>-regel: dat het melden zelf stuk is, is niet te melden met een mail, dus het log is
    /// de enige plek waar het kan staan.</para>
    /// </remarks>
    [JsonPropertyName("delivery")]
    [JsonConverter(typeof(JsonStringEnumConverter<MailDelivery>))]
    public MailDelivery Delivery { get; init; } = MailDelivery.Unknown;

    /// <summary>De operatie-id van Communication Services, of <c>null</c>.</summary>
    [JsonPropertyName("operationId")]
    public string? OperationId { get; init; }

    /// <summary>
    /// Wanneer deze agent weer in orde was, of <c>null</c> zolang de storing staat.
    /// </summary>
    /// <remarks>
    /// <para><strong>Het document wordt niet verwijderd maar afgesloten, en dat levert twee dingen
    /// op.</strong> Het is het antwoord op "hoe lang duurde die storing", en het maakt een terugkeer
    /// meteen weer meldbaar: een afgesloten markering geldt als geen markering, dus een agent die
    /// gisteren omviel, herstelde en vandaag opnieuw omvalt, wordt vandaag meteen gemeld en niet pas
    /// na het herhaalvenster.</para>
    ///
    /// <para>Er gaat géén mail uit bij herstel. Dat is geen vergeten geval: §7 vraagt om te mailen bij
    /// <c>failed</c> en <c>degraded</c>, en een tweede mail per storing verdubbelt het volume om iets
    /// te melden dat op het scherm staat.</para>
    /// </remarks>
    [JsonPropertyName("clearedAt")]
    public DateTimeOffset? ClearedAt { get; init; }

    /// <summary>De versie waarop de gelijktijdigheidscontrole loopt.</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; init; }
}

/// <summary>
/// Of er over deze agent nú een melding hoort te gaan, gegeven wat er al is gemeld.
/// </summary>
/// <remarks>
/// Vier uitkomsten en geen <c>bool</c>, om dezelfde reden als overal in dit portaal: het log en de
/// tests horen te kunnen zien <em>waarom</em> er wordt gemeld. "Voor het eerst" en "opnieuw na zes
/// uur" zijn bij het nazoeken twee verschillende gebeurtenissen.
/// </remarks>
internal enum AlertDue
{
    /// <summary>Er is niets over deze agent gemeld, of de vorige storing was afgesloten.</summary>
    First,

    /// <summary>Er is gemeld, maar over een andere status. Dit is nieuwe informatie.</summary>
    Changed,

    /// <summary>Dezelfde storing, en het herhaalvenster is voorbij.</summary>
    Repeat,

    /// <summary>Dezelfde storing, binnen het herhaalvenster. Er gaat niets uit.</summary>
    Suppressed,
}

/// <summary>
/// De ontdubbelregel, apart en zonder klok van zichzelf.
/// </summary>
/// <remarks>
/// <para><strong>Deze regel staat hier en niet in <see cref="AgentStatusCalculator"/>, en dat is een
/// besluit dat al vastlag.</strong> <c>ShouldAlert</c> is de zuivere vraag "hoort hier een melding
/// over" en niet "hebben we die al gestuurd" — en het scherm gebruikt diezelfde functie. Zou de
/// ontdubbeling daar zitten, dan verdwijnt een agent van het scherm omdat er over hem gemaild is.
/// </para>
///
/// <para><strong>Wanneer een melding herhaald mag worden, en waarom dat een keuze is.</strong> Er is
/// geen goed antwoord dat uit de spec volgt. Wat er wél volgt is dat beide uitersten fout zijn: elke
/// minuut melden maakt de melder waardeloos, en één keer melden over een storing die drie dagen duurt
/// is een storing waarvan niemand meer weet dat hij er is. De keuze is daarom een venster
/// (<see cref="AgentAlertOptions.RepeatAfter"/>, standaard zes uur) met twee uitzonderingen die niet
/// wachten:</para>
///
/// <list type="bullet">
///   <item><description>
///     <strong>Een veranderde status meldt meteen.</strong> Een agent die van
///     <see cref="AgentStatus.Degraded"/> naar <see cref="AgentStatus.Failed"/> gaat is nieuwe
///     informatie, en wachten zou die informatie tot zes uur oud maken. Ook de andere kant op:
///     <c>Failed</c> naar <c>Degraded</c> meldt ook meteen, want het is een ander beeld en de operator
///     hoort niet uit een oude mail te concluderen wat er nu aan de hand is.
///   </description></item>
///   <item><description>
///     <strong>Een afgesloten markering geldt als geen markering.</strong> Een storing die weg was en
///     terugkomt is een nieuwe storing, ook al is het dezelfde agent en dezelfde status.
///   </description></item>
/// </list>
///
/// <para>Wat dat kost: een agent die om het uur heen en weer flappert tussen twee statussen levert
/// elke keer een melding op. Dat is bewust niet gedempt. Zo'n agent <em>is</em> een storing, en de
/// dempening die dat zou tegenhouden — een venster op "er is over deze agent iets gemeld", ongeacht
/// wat — zou ook de escalatie van <c>Degraded</c> naar <c>Failed</c> tegenhouden. Van die twee is de
/// tweede duurder. Gemeld als punt van twijfel.</para>
/// </remarks>
internal static class AgentAlertDecision
{
    /// <summary>
    /// Of er nu gemeld hoort te worden.
    /// </summary>
    /// <param name="marker">Wat er over deze agent is vastgelegd, of <c>null</c>.</param>
    /// <param name="status">De status van nu.</param>
    /// <param name="now">Het moment waarop wordt geoordeeld.</param>
    /// <param name="repeatAfter">Na hoeveel tijd een onveranderde storing opnieuw wordt gemeld.</param>
    /// <returns>De uitkomst.</returns>
    internal static AlertDue Judge(
        AgentAlertDocument? marker,
        AgentStatus status,
        DateTimeOffset now,
        TimeSpan repeatAfter)
    {
        if (marker is null || marker.ClearedAt is not null)
        {
            return AlertDue.First;
        }

        if (marker.Status != status)
        {
            return AlertDue.Changed;
        }

        // Groter dan en niet groter-of-gelijk: dezelfde vorm als AgentStatusCalculator, waar een
        // stilte precies op de drempel nog geen storing is. Een klok die achteruit is gezet levert een
        // negatief verschil op en daarmee Suppressed — de veilige kant, want de andere kant is een
        // mail per ronde zolang de klok achterloopt.
        return now - marker.NotifiedAt > repeatAfter ? AlertDue.Repeat : AlertDue.Suppressed;
    }
}
