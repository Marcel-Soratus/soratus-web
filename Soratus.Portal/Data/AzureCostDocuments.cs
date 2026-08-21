using System.Text.Json.Serialization;

namespace Soratus.Portal.Data;

/// <summary>
/// De documentsoort en de sleutel van een maand Azure-verbruik.
/// </summary>
/// <remarks>
/// <para><strong>Deze twee constanten horen in <see cref="PortalDocumentKinds"/> en
/// <see cref="PortalDocumentIds"/>, en ze staan hier omdat er in deze repository meer dan één sessie
/// tegelijk werkt.</strong> Dat is een organisatorische reden en geen ontwerpreden, en hij hoort dus
/// een houdbaarheidsdatum te hebben: zodra dit werk is samengevoegd verhuizen ze naar die twee
/// klassen, want daar staat de regel die ze afdwingen — één plek voor een documentsleutel, zodat de
/// lees- en de schrijfkant niet elk hun eigen sleutel kunnen samenstellen. Een document dat onder
/// twee sleutels wordt aangesproken bestaat twee keer.</para>
///
/// <para>Er staat wél al één ding vast dat niet mag verschuiven: het voorvoegsel <c>azureCost-</c>
/// en de maand erachter. Zie <see cref="ForMonth"/>.</para>
/// </remarks>
public static class AzureCostDocumentKeys
{
    /// <summary>
    /// De documentsoort: één maand Azure-verbruik van één klant (§6 <c>AzureCost</c>).
    /// </summary>
    /// <remarks>
    /// camelCase, net als <see cref="PortalDocumentKinds.HourEntry"/>, en enkelvoud: één document is
    /// één maand en geen verzameling.
    /// </remarks>
    public const string Kind = "azureCost";

    /// <summary>
    /// De id van het verbruiksdocument van één maand, binnen de partitie van die klant.
    /// </summary>
    /// <param name="month">De maand als <c>yyyy-MM</c>. Zie <see cref="HourMonths"/>.</param>
    /// <returns>Bijvoorbeeld <c>azureCost-2026-08</c>.</returns>
    /// <remarks>
    /// <para><strong>Afgeleid en niet willekeurig, en om een andere reden dan bij een urenregel.</strong>
    /// Daar is de afgeleide sleutel er om een tweede regel te voorkomen (§ <c>HourEntry</c>). Hier is
    /// hij er om het tegenovergestelde mogelijk te maken: de verzameling van vandaag hoort die van
    /// gisteren over dezelfde maand te <em>vervangen</em>, want een maand heeft één bedrag en niet één
    /// bedrag per meetmoment. Met een willekeurige sleutel zou er per dag een document bijkomen en zou
    /// de leeskant moeten kiezen welke van de dertig de waarheid is — precies de keuze die
    /// <see cref="PortalDocumentIds"/> uitsluit.</para>
    ///
    /// <para>Dat betekent dat de collector een <em>upsert</em> doet en geen create, en dat is hier
    /// veilig omdat er niets wordt opgeteld: het document is een momentopname van een lezing en geen
    /// mutatie. Zie <see cref="AzureCostDocument.MeasuredAt"/>.</para>
    /// </remarks>
    public static string ForMonth(string month) => $"azureCost-{month}";

    /// <summary>
    /// De documentsoort van de dagclaim van de kostencollector.
    /// </summary>
    /// <remarks>
    /// camelCase en enkelvoud, net als <see cref="Kind"/>: één document is één dag en geen verzameling.
    /// Zie <see cref="AzureCostRunDocument"/> voor waarom dit document bestaat.
    /// </remarks>
    public const string RunKind = "costRun";

    /// <summary>
    /// De id van de dagclaim, binnen de gereserveerde partitie.
    /// </summary>
    /// <param name="day">De dag waarop de run hoort te lopen.</param>
    /// <returns>Bijvoorbeeld <c>costRun-2026-08-21</c>.</returns>
    /// <remarks>
    /// <para><strong>Afgeleid van de dag, en dat is het slot op twee collectors.</strong> Hetzelfde
    /// mechanisme als bij <c>StatementDocumentKeys.Id</c>: het document wordt geschreven vóór de
    /// handeling, met een <c>CreateItemAsync</c> en geen upsert, dus de tweede instantie krijgt een
    /// <c>409</c> en doet niets. Zie <see cref="AzureCostRunDocument"/> voor het belangrijke verschil
    /// met die mailclaim — daar is de claim een slot op een onherhaalbare handeling, hier is hij een
    /// slot op een schaars aanroepbudget.</para>
    ///
    /// <para>De dag staat er leesbaar in en niet als hash: deze sleutel komt in een logregel terecht en
    /// <c>costRun-2026-08-21</c> is daar het antwoord op de vraag welke dag het was.</para>
    /// </remarks>
    public static string ForDay(DateOnly day) =>
        $"{RunKind}-{day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Wat we van een maand Azure-verbruik werkelijk wéten.
/// </summary>
/// <remarks>
/// <para><strong>Dit type bestaat omdat € 0,00 op een factuur geen lege waarde is maar een verkeerd
/// bedrag.</strong> Het is dezelfde regel als besluit 15 — een bedrag dat ontbreekt is niet nul — maar
/// een verdieping lager, want hier is de bron een API die het verschil zelf niet maakt. Gemeten op
/// <c>Microsoft.CostManagement/query</c>, api-version 2023-11-01, 21 augustus 2026, scope
/// <c>resourceGroups/MBV</c>:</para>
///
/// <list type="bullet">
///   <item><description>
///     Een resource group die <strong>niet bestaat</strong> geeft <c>HTTP 200</c> met
///     <c>"rows": []</c>. Geen 404, geen fout: een geslaagd, leeg antwoord.
///   </description></item>
///   <item><description>
///     Een resource group die <strong>wel bestaat en elke dag € 1,88 kost</strong>, bevraagd over een
///     periode die nog niet is geboekt, geeft <strong>hetzelfde antwoord</strong>: <c>HTTP 200</c>,
///     <c>"rows": []</c>. Dat is niet een randgeval maar de gewone toestand van elke dag tussen
///     middernacht en ongeveer 08:00 UTC — en dus van de nieuwe maand op de 1e om 04:00, het moment
///     waarop de <c>kosten-collector</c> volgens het onderzoek draait.
///   </description></item>
///   <item><description>
///     En daarnaast bestaat er een <c>HTTP 404</c> (<c>GtmDimensionDataProvider…returns null</c>) die
///     "probeer opnieuw" betekent: tweemaal gezien in ruim twintig aanroepen, op een verzoek dat er
///     vlak ervoor en vlak erna 200 op gaf.
///   </description></item>
/// </list>
///
/// <para>Een gewone client rendert in alle drie de gevallen € 0,00. Dat is drie keer een onwaarheid,
/// en de gevaarlijkste is de tweede: die ziet uit als een antwoord. Daarom kan het subtotaal in dit
/// portaal <c>null</c> zijn (<see cref="AzureCostReading.Subtotal"/>) en zegt dít veld waarom.</para>
///
/// <para><strong>Vier waarden en niet twee, om dezelfde reden als bij de Entra-toestand en bij de
/// vierde urenstand:</strong> een <c>bool</c> "compleet ja/nee" kan het verschil tussen "de API zei
/// niets" en "de API zei nul regels" niet dragen, en die twee vragen een verschillende handeling. De
/// eerste is opnieuw proberen; de tweede is nakijken of we de juiste omgeving bevragen.</para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AzureCostState>))]
public enum AzureCostState
{
    /// <summary>
    /// Er is niets bekend: de lezing is niet gelukt, of er is voor deze maand nooit gemeten.
    /// </summary>
    /// <remarks>
    /// Dit is de waarde van een 429 die zijn pogingen heeft opgebruikt, van de 404 hierboven, van een
    /// timeout, en van de afwezigheid van een document. Die laatste is de belangrijkste: <strong>geen
    /// document betekent geen bedrag en niet nul</strong> — dezelfde regel als "geen document betekent
    /// geen status" (punt 2 van de fase-0-afwijkingen), en om precies dezelfde reden.
    ///
    /// Het is met opzet de <em>eerste</em> waarde van deze enum, zodat een niet-gezette waarde en een
    /// document uit een oudere vorm hier uitkomen en niet bij <see cref="Measured"/>.
    /// </remarks>
    Unknown,

    /// <summary>
    /// De lezing is gelukt en de API gaf nul regels terug.
    /// </summary>
    /// <remarks>
    /// <para><strong>Dit is niet € 0,00.</strong> Achter dit ene antwoord zitten drie werkelijkheden
    /// die de API niet van elkaar onderscheidt: er is werkelijk niets verbruikt, de periode is nog
    /// niet geboekt, of we bevragen een scope die niet bestaat. Alleen de eerste is nul.</para>
    ///
    /// <para>De code kan die drie niet uit elkaar halen — dat is gemeten, zie de toelichting bij deze
    /// enum — en dus doet ze dat niet. Wat er in plaats daarvan gebeurt is dat de bevraagde scope op
    /// het scherm komt te staan (<see cref="AzureCostDocument.Scope"/>), zodat een mens de derde
    /// mogelijkheid kan uitsluiten. Een ambiguïteit die niet op te lossen is, hoort zichtbaar te zijn
    /// in plaats van weggerekend.</para>
    ///
    /// <para>Let op het onderscheid met een maand die wél regels heeft die tot nul optellen: in de
    /// echte uitvoer staan <c>Bandwidth € 0,0000</c> en <c>Microsoft Entra € 0,0000</c> als gewone
    /// regels. Dát is een gemeten nul, en die krijgt <see cref="Measured"/> met een subtotaal van
    /// nul. Het verschil tussen een som die nul is en een som die niet bestaat is precies wat dit
    /// type draagt.</para>
    /// </remarks>
    NoLines,

    /// <summary>
    /// De lezing is gelukt, er zijn regels, maar de maand is nog niet volledig geboekt.
    /// </summary>
    /// <remarks>
    /// <para>Dit is de gewone toestand van de lopende maand, en §3.7 vraagt die ook zo te tonen: "de
    /// lopende maand staat bovenaan als concept met live berekende bedragen". Het bedrag is dan een
    /// ondergrens en geen bedrag.</para>
    ///
    /// <para>Voor een <em>afgesloten</em> maand betekent deze waarde iets anders en zwaarder: er valt
    /// nog niet op te factureren. Zie <see cref="AzureCostCompleteness"/> voor de regel waarmee dat
    /// wordt vastgesteld en waarom die op datums rust en niet op een percentage.</para>
    /// </remarks>
    Partial,

    /// <summary>
    /// De maand is volledig geboekt en gemeten. Dit is het enige bedrag waarop gefactureerd mag worden.
    /// </summary>
    Measured,
}

/// <summary>
/// Eén dienst met zijn bedrag binnen een maand (§3.7, "per dienst").
/// </summary>
/// <remarks>
/// <para><strong>De dienstnaam komt uit de API en staat niet in een lijst in onze code.</strong> §3.7
/// noemt Container Apps, Azure OpenAI, Storage, Log Analytics en Key Vault. De werkelijke
/// <c>ServiceName</c>-waarden zijn gemeten en het zijn andere: <c>Azure App Service</c>,
/// <c>Azure Cosmos DB</c>, <c>Bandwidth</c>, <c>Key Vault</c>, <c>Microsoft Entra</c>. Een vaste
/// lijst zou vandaag al de helft missen, en — erger — hij laat op de dag dat er een dienst bijkomt
/// stil geld buiten het subtotaal vallen. Daarom is dit een regel in een lijst en geen veld in een
/// type met een naam per dienst.</para>
///
/// <para>De valuta staat op het document en niet op de regel: de API geeft hem per rij mee, maar
/// binnen één antwoord is hij overal gelijk, en twee valuta's in één subtotaal is geen subtotaal.
/// Zie <see cref="AzureCostDocument.Currency"/>.</para>
/// </remarks>
public sealed record AzureCostLine
{
    /// <summary>
    /// De naam van de dienst zoals Azure hem geeft, bijvoorbeeld <c>Azure App Service</c>.
    /// </summary>
    [JsonPropertyName("dienst")]
    public required string Service { get; init; }

    /// <summary>
    /// Het bedrag over de gemeten periode, exclusief btw en zonder beheeropslag.
    /// </summary>
    /// <remarks>
    /// <para>Niet nullable, en dat is hier juist: een regel bestáát omdat de API er een bedrag bij
    /// gaf. Een regel zonder bedrag komt niet voor — een <em>maand</em> zonder bedrag wel, en die
    /// heeft geen regels. Dat verschil is de reden dat <see cref="AzureCostReading.Subtotal"/>
    /// nullable is en dit veld niet.</para>
    ///
    /// <para>Onafgerond zoals de API hem geeft. De echte waarden hebben vijftien cijfers achter de
    /// komma (<c>37,4563985414928</c>) en het afronden gebeurt één keer, op het bedrag dat wordt
    /// doorbelast; zie <see cref="MonthlyChargeCalculator"/>. Hier afronden zou betekenen dat het
    /// subtotaal de som van afgeronde regels is, en dan wijkt het af van wat Azure factureert.</para>
    /// </remarks>
    [JsonPropertyName("bedrag")]
    public required decimal Amount { get; init; }
}

/// <summary>
/// Het Azure-verbruik van één klant in één maand, zoals het in de opslag staat (§6 <c>AzureCost</c>).
/// </summary>
/// <remarks>
/// <para><strong>Dit document wordt door het portaal alleen gelezen.</strong> Het wordt geschreven
/// door de beheeragent <c>kosten-collector</c> (§4), dagelijks, en die bestaat nog niet. De vorm
/// staat hier vast zodat die agent niets hoeft te verzinnen — dezelfde afspraak als bij
/// <see cref="HourEntryKeys.ForIntegration"/>, waar de documentvorm er ook eerder was dan de
/// koppeling die hem vult.</para>
///
/// <para><strong>Waarom het bedrag in de opslag staat en niet bij het bekijken wordt opgehaald.</strong>
/// Drie gemeten redenen, in gewicht:</para>
///
/// <list type="number">
///   <item><description>
///     <strong>Het aanroepbudget verdraagt geen pageview.</strong> Gemeten op 21 augustus 2026:
///     vier aanroepen binnen elf seconden leverden vier keer een 429, en een geslaagde aanroep
///     vroeg ongeveer dertig tot veertig seconden stilte ervoor. Het budget hangt aan de aanroeper
///     en niet aan de scope (de header heet <c>clienttype-retry-after</c>), dus meer klanten of
///     meer abonnementen maken het niet ruimer. Eén operator die twee klanten naast elkaar opent,
///     trekt de emmer leeg.
///   </description></item>
///   <item><description>
///     <strong>Het lege antwoord is alleen met historie te wegen.</strong> "Nul regels" betekent iets
///     anders als er gisteren wél regels waren; zie <see cref="AzureCostState.NoLines"/>. Die
///     vergelijking vraagt een bewaarde reeks, dus er is hoe dan ook opslag nodig.
///   </description></item>
///   <item><description>
///     <strong>Wat er op het scherm hoort te staan als de verzameling van vandaag is mislukt, is de
///     lezing van gisteren met het tijdstip erbij</strong> — en niet een verse mislukking. Het
///     bewaarde getal is werkelijk gemeten; de mislukte aanroep heeft niets gemeten. Van die twee is
///     het eerste het eerlijkere antwoord, zolang erbij staat wanneer het is gemeten. Dat is
///     <see cref="MeasuredAt"/>, en die staat op het scherm en niet alleen in dit document.
///   </description></item>
/// </list>
///
/// <para>De prijs, eerlijk: het scherm loopt tot een etmaal achter op wat Cost Management weet. Dat
/// is voor een maandbedrag dat achteraf wordt gefactureerd geen bezwaar — de gegevens van Cost
/// Management lopen zelf al zeven tot tien uur achter (gemeten: op 21 augustus 06:55 UTC stond de
/// 20e op 95,97% van een volle dag en de 21e ontbrak nog helemaal), dus "live" bestaat hier niet.
/// Het portaal zou een verse onnauwkeurigheid ruilen tegen een oude, en daar een aanroepbudget voor
/// opbranden.</para>
///
/// <para><strong>Er staat geen subtotaal op dit document.</strong> Dat is bewust en het is dezelfde
/// keuze als bij het maandtotaal van de uren: een opgeslagen som die de regels tegenspreekt is een
/// tweede waarheid, en de verkeerde van de twee zou degene zijn die niemand bijwerkt. Het subtotaal
/// wordt gerekend uit <see cref="Lines"/>; zie <see cref="AzureCostReading.Subtotal"/>.</para>
///
/// <para><strong>En er staat geen opslagpercentage op, terwijl §6 het hier zet.</strong> Dat is een
/// afwijking met een reden: het portaal heeft geen scherm waarop een percentage per maand wordt
/// vastgelegd, en de agent die dit document schrijft heeft geen mening over onze marge. Een veld dat
/// niets ooit vult is een stille onwaarheid — dezelfde afweging als bij
/// <see cref="AccessDocument"/>, waar om die reden geen "uitnodiging verstuurd"-veld staat. Het
/// percentage komt uit <see cref="ContractDocument.AzureSurchargePercentage"/>, want dat is waar de
/// afspraak wordt gemaakt.</para>
/// </remarks>
public sealed record AzureCostDocument
{
    /// <summary>Documentsleutel: <see cref="AzureCostDocumentKeys.ForMonth"/>.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Partitiesleutel. De klantslug.</summary>
    [JsonPropertyName("pk")]
    public required string PartitionKey { get; init; }

    /// <summary>Documentsoort. Altijd <see cref="AzureCostDocumentKeys.Kind"/>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = AzureCostDocumentKeys.Kind;

    /// <summary>De klantslug (<c>cid</c> in §6).</summary>
    [JsonPropertyName("cid")]
    public required string CustomerId { get; init; }

    /// <summary>De maand als <c>yyyy-MM</c>.</summary>
    /// <remarks>
    /// Dezelfde vorm en dezelfde reden als bij een urenregel: Cosmos vergelijkt dit als tekst, en op
    /// <c>yyyy-MM</c> werkt een bereikfilter terwijl hij op elke andere vorm stil verkeerd sorteert.
    /// Zie <see cref="HourMonths"/>. Dat de maandsleutel van de uren en die van de kosten dezelfde
    /// vorm hebben is geen toeval maar de voorwaarde om ze op één totaal te kunnen zetten (§3.7).
    /// </remarks>
    [JsonPropertyName("month")]
    public required string Month { get; init; }

    /// <summary>Wat er van deze maand bekend is.</summary>
    [JsonPropertyName("state")]
    public required AzureCostState State { get; init; }

    /// <summary>
    /// De diensten met hun bedragen, zoals de API ze gaf.
    /// </summary>
    /// <remarks>
    /// Leeg bij <see cref="AzureCostState.NoLines"/> en bij <see cref="AzureCostState.Unknown"/>. De
    /// volgorde is die van de API; het scherm sorteert zelf op bedrag, want dat is de kolom waarop
    /// een operator kijkt.
    /// </remarks>
    [JsonPropertyName("lines")]
    public IReadOnlyList<AzureCostLine> Lines { get; init; } = [];

    /// <summary>De valuta van de bedragen, bijvoorbeeld <c>EUR</c>.</summary>
    /// <remarks>
    /// <para><c>null</c> als er niets is gemeten, en dan is er ook geen bedrag. Niet standaard
    /// <c>"EUR"</c>: gemeten is dat de API hem meestuurt, en een verzonnen valuta naast een bedrag
    /// dat we niet hebben is een tweede onwaarheid op dezelfde regel.</para>
    ///
    /// <para>De bedragen zijn exclusief btw. Dat staat hier als opmerking en niet als veld, want het
    /// is een eigenschap van de API en geen gegeven dat per maand kan verschillen.</para>
    /// </remarks>
    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    /// <summary>
    /// De scope waartegen is gemeten, bijvoorbeeld
    /// <c>/subscriptions/501a66d2-…/resourceGroups/MBV</c>.
    /// </summary>
    /// <remarks>
    /// <para><strong>Dit veld bestaat om één gemeten reden: een resource group die niet bestaat geeft
    /// HTTP 200 met nul regels.</strong> Er is dus geen enkele manier om in code te zien of een leeg
    /// antwoord "niets verbruikt" of "verkeerde omgeving" betekent. Wat er dan overblijft is de vraag
    /// aan een mens stellen, en dat kan alleen als op het scherm staat wát er is bevraagd.</para>
    ///
    /// <para>Het is operator-only, en dat is geen extra regel: het staat op de operatorweergave omdat
    /// §2 de volledige omgeving (subscription · resource group) al aan de operator toewijst en niet
    /// aan de klant. Zie <see cref="CustomerDocument.EnvironmentDetail"/>, dat om dezelfde reden
    /// niet op <see cref="Security.CustomerScope"/> staat.</para>
    /// </remarks>
    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    /// <summary>
    /// Wanneer deze lezing bij Cost Management is opgehaald, in UTC.
    /// </summary>
    /// <remarks>
    /// <para>Dit is het tijdstip dat §3.7 bedoelt met "met tijdstip van ophalen", en het hoort bij
    /// élke toestand op het scherm te staan — juist bij <see cref="AzureCostState.Unknown"/>, want
    /// dan is het het antwoord op "hoe oud is wat ik hier zie".</para>
    ///
    /// <para>Het is het moment van de <em>lezing</em> en niet van het schrijven. Die twee lopen
    /// uiteen zodra de collector een keer opnieuw moet proberen, en van de twee is de lezing degene
    /// waar het bedrag bij hoort.</para>
    /// </remarks>
    [JsonPropertyName("measuredAt")]
    public required DateTimeOffset MeasuredAt { get; init; }

    /// <summary>
    /// De laatste dag waarover deze lezing bedragen bevat, als <c>yyyy-MM-dd</c>, of <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <c>null</c> als er geen regels zijn. Dit is het gegeven waarmee "gemeten tot en met de 20e" op
    /// het scherm komt, en het is de reden dat <see cref="AzureCostState.Partial"/> geen loze
    /// mededeling is: bij een ondergrens hoort te staan waar hij de grens van is.
    /// </remarks>
    [JsonPropertyName("coversThrough")]
    public string? CoversThrough { get; init; }

    /// <summary>
    /// Waarom er niets bekend is, in gewone taal, of <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Alleen gevuld bij <see cref="AzureCostState.Unknown"/>. Geen statuscode en geen
    /// uitzonderingstekst: dit komt op een scherm waar een operator naar kijkt, en "429 na 5
    /// pogingen" zegt hem minder dan "Cost Management liet ons vijf keer niet door". De technische
    /// vorm hoort in de logregel van de collector.
    /// </remarks>
    [JsonPropertyName("failure")]
    public string? Failure { get; init; }

    /// <summary>De versie waarop de gelijktijdigheidscontrole loopt. Zie <see cref="CustomerDocument.ETag"/>.</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; init; }
}
