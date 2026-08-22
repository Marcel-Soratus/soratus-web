using System.Text.Json.Serialization;

namespace Soratus.Portal.Sprints;

/// <summary>De documentsoort en de sleutel van de sprintlezing van één klant.</summary>
/// <remarks>
/// <para><strong>Deze twee constanten horen in <c>PortalDocumentKinds</c> en <c>PortalDocumentIds</c>, en
/// ze staan hier om dezelfde organisatorische reden als bij <see cref="Data.AzureCostDocumentKeys"/>:</strong>
/// er werkt meer dan één sessie in deze repository en een nieuw bestand botst niet. Zodra dit werk is
/// samengevoegd horen ze naar die twee klassen te verhuizen, want daar staat de regel die ze afdwingen —
/// één plek voor een documentsleutel, zodat de lees- en de schrijfkant niet elk hun eigen sleutel kunnen
/// samenstellen. Een document dat onder twee sleutels wordt aangesproken bestaat twee keer.</para>
/// </remarks>
public static class SprintDocumentKeys
{
    /// <summary>De documentsoort: de laatste sprintlezing van één klant.</summary>
    /// <remarks>Enkelvoud en camelCase, net als de andere soorten in deze container.</remarks>
    public const string Kind = "sprint";

    /// <summary>
    /// De id van het sprintdocument, binnen de partitie van die klant.
    /// </summary>
    /// <remarks>
    /// <para><strong>Eén document per klant en niet één per sprint, en dat is een besluit.</strong> Een
    /// maand Azure-verbruik krijgt een eigen document met de maand in de sleutel, want het
    /// facturatieoverzicht is een lijst maanden en een afgesloten maand blijft betekenis houden. Een
    /// sprint niet: §3.4 vraagt <em>de</em> sprint — naam, periode, boardpad, statistieken en de items —
    /// en niet een historie van sprints. Het portaal is een operationeel scherm.</para>
    ///
    /// <para>De prijs, eerlijk: er is straks geen manier om te zien hoe augustus eruitzag toen hij liep.
    /// Dat is bewust, en het is goedkoop terug te draaien — een sleutel met de sprint erin en een
    /// bereikquery is dezelfde vorm die de kosten al hebben. Zolang er niemand is die die historie leest,
    /// zou hij een lijst zijn die per kwartier groeit en waarvan niemand de laatste versie kan
    /// aanwijzen.</para>
    ///
    /// <para>Vandaar ook een <em>upsert</em> en geen create: de lezing van dit kwartier hoort die van het
    /// vorige te vervangen. Er wordt niets opgeteld, dus dat is veilig — dezelfde afweging als bij
    /// <see cref="Data.AzureCostDocumentKeys.ForMonth"/>.</para>
    /// </remarks>
    public const string Id = "sprint";
}

/// <summary>
/// Wat we van de sprint van één klant werkelijk wéten.
/// </summary>
/// <remarks>
/// <para><strong>Zes waarden, en elke waarde vraagt een andere handeling van een mens.</strong> Dat is de
/// enige maat die dit portaal voor een enum aanhoudt (punt 30, punt 19, de drie Entra-toestanden): een
/// waarde die dezelfde handeling vraagt als zijn buur hoort er niet te zijn, en twee werkelijkheden onder
/// één waarde is de fout waar die punten over gaan.</para>
///
/// <para><strong>En dit is de plek waar de harde regel van deze lane leeft: de maand komt uit de datums
/// van een iteratie en nooit uit de naam.</strong> Gemeten op 22 augustus 2026 geeft de teamiteratielijst
/// van <c>MBVApp4 MAUI Team</c> een veld <c>timeFrame</c> mee, en dat veld <em>lijkt</em> het antwoord:
/// <c>2026-08 Augustus</c> stond op <c>1</c> (current) en de rest op <c>2</c> (future). Maar de drie
/// iteraties zónder datums — <c>Iteration 1</c> t/m <c>3</c>, met <c>startDate: null</c> en
/// <c>finishDate: null</c> — stonden óók op <c>2</c>. Dat veld kan "in de toekomst" dus niet van "heeft
/// geen datums" onderscheiden, en het is precies dat onderscheid waar deze enum om bestaat.
/// <c>timeFrame</c> wordt daarom gelezen noch gebruikt.</para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<SprintState>))]
public enum SprintState
{
    /// <summary>
    /// Er is niets bekend: de lezing is niet gelukt, of er is voor deze klant nooit gelezen.
    /// </summary>
    /// <remarks>
    /// <para>Met opzet de <em>eerste</em> waarde, zodat een niet-gezette waarde en een document uit een
    /// oudere vorm hier uitkomen en niet bij <see cref="Current"/>. Dezelfde keuze als bij
    /// <see cref="Data.AzureCostState.Unknown"/>.</para>
    ///
    /// <para>De afwezigheid van een document valt hier ook onder, en dat is de belangrijkste: <strong>geen
    /// document betekent geen sprint en niet een leeg sprintoverzicht.</strong> Een leeg overzicht leest
    /// als "er is geen werk", en dat is een andere mededeling dan "wij hebben niet gekeken".</para>
    /// </remarks>
    Unknown,

    /// <summary>Het bord is gelezen en het team heeft geen enkele iteratie.</summary>
    /// <remarks>
    /// Handeling: iteraties aanmaken en aan het team toewijzen. Dit is niet hetzelfde als
    /// <see cref="NoDatedIterations"/> — daar staan er wél, en dan is de handeling datums invullen in
    /// plaats van iteraties aanmaken.
    /// </remarks>
    NoIterations,

    /// <summary>
    /// Het team heeft iteraties, en geen enkele heeft een begin- en een einddatum.
    /// </summary>
    /// <remarks>
    /// <para><strong>Dit was de werkelijke toestand van dit bord tot 21 augustus 2026</strong>, en hij was
    /// stil kapot: de teaminstelling staat op <c>@currentIteration</c>, die macro wordt door datums
    /// bepaald, en er was dus helemaal geen huidige sprint. Een weergave die deze toestand niet kan
    /// uitdrukken zou een leeg scherm tonen dat op "geen werk" lijkt.</para>
    ///
    /// <para>Handeling: datums invullen op de iteraties. Er staat werk op dat bord — het valt alleen in
    /// geen enkele maand.</para>
    /// </remarks>
    NoDatedIterations,

    /// <summary>
    /// Er zijn iteraties met datums, en vandaag valt in geen van hun periodes.
    /// </summary>
    /// <remarks>
    /// Een gat in de kalender: de vorige sprint is afgelopen en de volgende is nog niet begonnen. Op een
    /// bord met maandsprintjes gebeurt dat niet, en juist daarom hoort deze waarde te bestaan — als hij
    /// er stond zou dat betekenen dat er een maand mist, en dat is een bevinding en geen lege pagina.
    /// Handeling: de volgende sprint aanmaken of zijn datums nakijken.
    /// </remarks>
    NoCurrentSprint,

    /// <summary>
    /// Meer dan één iteratie met datums bevat vandaag.
    /// </summary>
    /// <remarks>
    /// <para><strong>Er wordt dan géén sprint gekozen, en dat is de hele reden dat deze waarde
    /// bestaat.</strong> Twee overlappende periodes zijn twee antwoorden op "welke sprint loopt nu", en
    /// stilletjes de eerste of de kortste kiezen is een verzonnen antwoord dat op het scherm niet van een
    /// juist antwoord te onderscheiden is. Dat is dezelfde soort keuze als bij een geslaagd leeg antwoord
    /// van Cost Management: een ambiguïteit die niet op te lossen is hoort zichtbaar te zijn in plaats
    /// van weggerekend (punt 30).</para>
    ///
    /// <para>Handeling: de periodes op het bord corrigeren. De overlappende iteraties staan met naam op
    /// het operatorscherm, want zonder die namen is de melding niet te gebruiken.</para>
    /// </remarks>
    Ambiguous,

    /// <summary>Er is precies één iteratie met datums waarin vandaag valt. Dit is de sprint.</summary>
    Current,
}

/// <summary>
/// De fase waarin een work item staat, volgens de <em>categorie</em> van zijn state.
/// </summary>
/// <remarks>
/// <para><strong>Dit type bestaat omdat §3.4 vijf statenamen voorschrijft die op dit bord niet bestaan.</strong>
/// De spec noemt <c>New/Active/Blocked/Resolved/Closed</c> — dat is de dummydata van de mockup. Gemeten op
/// 22 augustus 2026 heeft het werkitemtype <c>Task</c> van <c>MBVApp4 MAUI</c> er vier: <c>New</c>
/// (categorie <c>Proposed</c>), <c>Active</c> (<c>InProgress</c>), <c>Closed</c> (<c>Completed</c>) en
/// <c>Removed</c> (<c>Removed</c>). Geen <c>Blocked</c> en geen <c>Resolved</c>.</para>
///
/// <para>Statenamen zijn dus per proces en per werkitemsoort anders, en een vaste lijst van vijf zou op
/// het eerste bord met een eigen procestemplate stil de verkeerde kleur geven. Wat Azure DevOps wél
/// garandeert is de <em>categorie</em>: die is een gesloten verzameling en hij is opvraagbaar per
/// werkitemsoort. De statenaam gaat ongewijzigd naar het scherm — dat is wat er op het bord staat en wat
/// een mens herkent — en de categorie bepaalt wat het portaal ermee rekent.</para>
///
/// <para><strong>Anders dan <see cref="WorkItemOrigin"/> wordt deze waarde opgeslagen en niet bij het
/// lezen afgeleid.</strong> Dat is geen inconsistentie: de herkomst is uit het document zelf af te leiden
/// (het draagt de aanmaker), en de fase niet — daarvoor is de procesmetadata van DevOps nodig, en die per
/// klant meeslaan zou een tweede kopie zijn van iets dat DevOps bezit en dat verandert zonder dat wij het
/// merken.</para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<WorkItemStage>))]
public enum WorkItemStage
{
    /// <summary>
    /// De categorie is niet vastgesteld.
    /// </summary>
    /// <remarks>
    /// Eerste waarde, zodat een niet-gezette waarde hier uitkomt. Hij hoort in de praktijk niet voor te
    /// komen — de lezing wordt <see cref="SprintAnswerKind.Unreadable"/> zodra de categorie van een state
    /// niet te bepalen is, want een item dat niet te classificeren is maakt de statistiek "afgerond" te
    /// laag, en een te laag getal is onzichtbaar.
    /// </remarks>
    Unknown,

    /// <summary>Voorgesteld: er is nog niet aan begonnen (categorie <c>Proposed</c>).</summary>
    Proposed,

    /// <summary>In behandeling (categorie <c>InProgress</c>).</summary>
    InProgress,

    /// <summary>Opgelost maar niet afgesloten (categorie <c>Resolved</c>).</summary>
    /// <remarks>
    /// Bestaat niet op dit bord en staat er toch, want hij bestaat in het Agile-proces van Azure DevOps
    /// voor <c>Bug</c> en <c>User Story</c>. <strong>Dit telt niet als afgerond.</strong> §3.4 zet
    /// <c>Resolved</c> en <c>Closed</c> in de mockup op dezelfde groene kleur; voor de statistiek
    /// "afgerond" is dat verkeerd — opgelost is niet gedaan, en een sprint die op grond daarvan als klaar
    /// wordt gelezen is een sprint waarvan niemand het restwerk ziet.
    /// </remarks>
    Resolved,

    /// <summary>Afgerond (categorie <c>Completed</c>). Dit is wat §3.4 "afgerond" noemt.</summary>
    Completed,

    /// <summary>
    /// Verwijderd (categorie <c>Removed</c>).
    /// </summary>
    /// <remarks>
    /// Een verwijderd item is geen werk. Het telt daarom niet mee in het aantal work items van de sprint
    /// en staat als eigen getal naast de statistieken — weglaten zou het aantal onverklaarbaar maken voor
    /// wie het bord ernaast openzet.
    /// </remarks>
    Removed,
}

/// <summary>
/// Of een work item door een agent of door een mens is aangemaakt (§3.4, "herkomst").
/// </summary>
/// <remarks>
/// <para><strong>Drie waarden en niet twee, en de eerste is de belangrijkste.</strong> §3.4 vraagt
/// "aangemaakt door agent of handmatig" en dat lijkt een <c>bool</c>. Dat kan het niet zijn: gemeten is
/// dat er in DevOps vandaag <em>niets</em> staat dat dit onderscheid draagt. Elk work item op dit bord is
/// door een mens gemaakt, maar dat weten we niet uit het bord — we weten alleen wie het heeft aangemaakt,
/// en of die iemand een agent is hangt af van een lijst die wij bijhouden. Is die lijst leeg, dan is het
/// antwoord "onbekend" en niet "handmatig".</para>
///
/// <para>Dat is punt 15 op een enum: een waarde die "onbekend" moet kunnen uitdrukken kan dat niet met een
/// <c>bool</c> die ook een geldig antwoord is. En de prijs van het verkeerd doen is niet klein — "door een
/// mens aangemaakt" bij een item dat een agent heeft gemaakt zet een operator op het verkeerde been over
/// wie er verantwoordelijk is voor de inhoud.</para>
///
/// <para>Zie <see cref="SprintOptions.AgentIdentities"/> voor hoe de lijst wordt vergeleken en waarom het
/// een identiteit is en geen tag.</para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<WorkItemOrigin>))]
public enum WorkItemOrigin
{
    /// <summary>Niet vast te stellen. De standaardwaarde, en de waarde van elk item zolang er geen agentidentiteit bekend is.</summary>
    Unknown,

    /// <summary>Aangemaakt door iemand die niet als agent bekend staat.</summary>
    Manual,

    /// <summary>Aangemaakt door een agent.</summary>
    Agent,
}

/// <summary>
/// Eén iteratie van het team, zoals Azure DevOps hem geeft.
/// </summary>
/// <remarks>
/// <para>Uit <c>GET .../{team}/_apis/work/teamsettings/iterations</c> en niet uit de iteratieboom van het
/// project: een iteratie bestaat in het project en wordt aan een team <em>toegewezen</em>, en een sprint
/// is het toegewezen ding. Zie <see cref="DevOpsScope"/>.</para>
///
/// <para><strong>De datums zijn <see cref="DateOnly"/> en geen momenten, en dat is gemeten.</strong> Er is
/// <c>31 augustus 23:59:59</c> naar DevOps verstuurd en <c>2026-08-31T00:00:00Z</c> teruggekomen: DevOps
/// laat de tijd van een iteratiedatum vallen. Ze als <see cref="DateTimeOffset"/> behandelen zou
/// betekenen dat de laatste dag van een sprint om middernacht eindigt in plaats van hem te bevatten, en
/// dan is de laatste dag van elke maand geen sprintdag. Vandaar dat de vergelijking in
/// <see cref="SprintSelection"/> op dagen loopt en inclusief is aan beide kanten.</para>
/// </remarks>
public sealed record DevOpsIteration
{
    /// <summary>De identifier van de iteratie: een guid.</summary>
    /// <remarks>
    /// <para><strong>Hierop wordt naar de work items gevraagd, en niet op het pad.</strong> Dat is de harde
    /// regel van deze lane doorgetrokken naar de query: het pad bevat de naam, en een iteratie die tussen
    /// twee aanroepen wordt hernoemd levert dan een pad op dat niets meer vindt. Gemeten (22 augustus
    /// 2026) werkt <c>GET .../teamsettings/iterations/{guid}/workitems</c> en gaf hij voor
    /// <c>Iteration 1</c> zestien items.</para>
    ///
    /// <para>Let op dat dit een <em>andere</em> id is dan <c>System.IterationId</c> op een work item: die
    /// is een geheel getal (gemeten: <c>493</c> voor <c>Iteration 1</c>) en komt uit de iteratieboom van
    /// het project. De teamlijst geeft alleen de guid, en die is genoeg — er wordt niet in WIQL
    /// gefilterd.</para>
    /// </remarks>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>De naam, bijvoorbeeld <c>2026-08 Augustus</c>. Voor mensen.</summary>
    /// <remarks>
    /// <strong>Hier wordt niets uit afgeleid.</strong> Niet de maand, niet de periode, niet de volgorde.
    /// <c>2026-08 Augustus</c> hernoemen naar <c>Augustus</c> mag geen enkel getal in dit portaal
    /// verschuiven. De naam staat op het scherm omdat een mens hem op het bord ook ziet.
    /// </remarks>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Het boardpad, bijvoorbeeld <c>MBVApp4 MAUI\2026-08 Augustus</c> (§3.4, "boardpad").
    /// </summary>
    /// <remarks>
    /// Gemeten geeft de teamlijst hem zonder het <c>\Iteration\</c>-knooppunt dat de projectboom er wél in
    /// zet (<c>\MBVApp4 MAUI\Iteration\2026-08 Augustus</c>). Twee vormen van hetzelfde pad uit twee
    /// endpoints; wat hier staat is de vorm van het endpoint dat we lezen, en er wordt niets aan
    /// gerepareerd — een pad dat wij hebben bijgewerkt is geen pad dat op het bord staat.
    /// </remarks>
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    /// <summary>De eerste dag van de iteratie, of <c>null</c> als er geen datum staat.</summary>
    [JsonPropertyName("start")]
    public DateOnly? Start { get; init; }

    /// <summary>De laatste dag van de iteratie, of <c>null</c> als er geen datum staat.</summary>
    /// <remarks>Inclusief: de sprint bevat deze dag. Zie de toelichting bij dit type.</remarks>
    [JsonPropertyName("finish")]
    public DateOnly? Finish { get; init; }

    /// <summary>
    /// Of deze iteratie een begin- én een einddatum heeft, en dus in een periode kan vallen.
    /// </summary>
    /// <remarks>
    /// <para><strong>Eén van de twee is niet genoeg, en dat is geen strengheid.</strong> Een iteratie met
    /// alleen een begindatum heeft geen einde, dus "vandaag valt erin" is voor elke dag na het begin waar
    /// — ook over drie jaar. Een iteratie met alleen een einddatum heeft hetzelfde probleem de andere kant
    /// op. Beide zijn in de DevOps-gebruikersinterface niet te maken, maar de API kan ze leveren en dan is
    /// het antwoord op "welke sprint loopt nu" onzin in plaats van leeg.</para>
    /// </remarks>
    [JsonIgnore]
    public bool IsDated => Start is not null && Finish is not null;
}

/// <summary>
/// Eén work item in de sprint, met precies de velden die DevOps ervan gaf (§3.4).
/// </summary>
/// <remarks>
/// <para><strong>Elk optioneel veld hier is <c>null</c> als DevOps het niet meestuurde, en nooit
/// nul.</strong> Dat is de belangrijkste eigenschap van dit type en hij is gemeten: in het antwoord van
/// <c>workitemsbatch</c> staat een leeg veld <em>niet</em> in het woordenboek. Van de zestien gemeten
/// items had géén enkel item <c>Microsoft.VSTS.Scheduling.RemainingWork</c>,
/// <c>…CompletedWork</c>, <c>…StoryPoints</c> of <c>System.Tags</c> — die sleutels ontbraken gewoon, en
/// twee items hadden ook geen <c>System.AssignedTo</c>. Een lezer die daar <c>0</c> van maakt zet
/// "openstaande uren: 0" op een scherm waar "geen uren ingevuld" hoort te staan, en dat is punt 15
/// letterlijk: nul is een afspraak en niet-ingevuld is er geen.</para>
///
/// <para><strong>En er staat geen enkel afgeleid veld op.</strong> Niet <c>isBlocked</c>, niet
/// <c>origin</c>: die twee volgen uit <see cref="Tags"/> en <see cref="CreatedByUniqueName"/> plus een
/// instelling, en ze worden bij het lezen afgeleid (<see cref="SprintJudgement"/>). Een opgeslagen
/// afgeleide waarde naast de gegevens waaruit hij volgt is een tweede waarheid, en de verkeerde van de
/// twee zou degene zijn die niemand bijwerkt — hetzelfde argument waarom er geen subtotaal op een
/// verbruiksdocument staat, en waarom het opslagpercentage op het contract blijft (punt 34). De praktische
/// winst is dat een gewijzigde <see cref="SprintOptions.BlockedMarker"/> of een nieuwe agentidentiteit
/// meteen klopt, ook voor lezingen die er al liggen.</para>
///
/// <para><see cref="Stage"/> is de uitzondering en de reden staat bij <see cref="WorkItemStage"/>.</para>
/// </remarks>
public sealed record SprintWorkItem
{
    /// <summary>Het nummer van het work item, bijvoorbeeld <c>4566</c>.</summary>
    [JsonPropertyName("id")]
    public required int Id { get; init; }

    /// <summary>De werkitemsoort zoals DevOps hem noemt, bijvoorbeeld <c>Task</c>.</summary>
    /// <remarks>
    /// Uit de API en niet uit een lijst in onze code, om dezelfde reden als bij een dienstnaam op een
    /// verbruiksregel: een project met een eigen procestemplate heeft eigen soorten, en een vaste lijst
    /// zou die stil buiten het overzicht laten vallen.
    /// </remarks>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>De titel.</summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>De statenaam zoals hij op het bord staat, bijvoorbeeld <c>Active</c>.</summary>
    /// <remarks>
    /// Ongewijzigd. Er wordt niet naar Nederlands vertaald en niet naar de vijf namen van §3.4 gedwongen:
    /// dit is het woord dat een mens op het bord ziet, en een portaal dat er een ander woord van maakt
    /// laat twee mensen over hetzelfde item verschillende dingen zeggen.
    /// </remarks>
    [JsonPropertyName("state")]
    public required string State { get; init; }

    /// <summary>De categorie van <see cref="State"/>. Hierop rekent het portaal.</summary>
    [JsonPropertyName("stage")]
    public required WorkItemStage Stage { get; init; }

    /// <summary>De tags, gesplitst.</summary>
    /// <remarks>
    /// DevOps levert ze als één tekenreeks met "; " ertussen, of stuurt het veld helemaal niet mee.
    /// Gesplitst opgeslagen omdat een tag een waarde is en geen zin: de blokkadecontrole vergelijkt met
    /// één tag en niet met een deel van een tekst, want <c>Not-Blocked</c> bevat <c>Blocked</c>.
    /// </remarks>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>De weergavenaam van de aanmaker, of <c>null</c>.</summary>
    [JsonPropertyName("createdByName")]
    public string? CreatedByName { get; init; }

    /// <summary>
    /// De unieke naam van de aanmaker — meestal een e-mailadres — of <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <para><strong>Operator-only, en dat is een typeverschil en geen filter:</strong> dit veld staat op
    /// <c>OperatorSprintRow</c> en niet op de klantvariant. §2 zet "Koppelingen (MCP/DevOps-details)" dicht
    /// voor de klant, en dit is bovendien het e-mailadres van een medewerker van Soratus of van de klant —
    /// een persoonsgegeven dat op een klantscherm niets te zoeken heeft zolang niemand erom heeft
    /// gevraagd.</para>
    ///
    /// <para>Wat de klant er wél van ziet is de <em>herkomst</em>: of dit item van een agent komt. Dat is
    /// de vraag die §3.4 stelt, en die is te beantwoorden zonder een adres te noemen.</para>
    /// </remarks>
    [JsonPropertyName("createdByUniqueName")]
    public string? CreatedByUniqueName { get; init; }

    /// <summary>De weergavenaam van de toegewezen persoon, of <c>null</c> als het item niet is toegewezen.</summary>
    /// <remarks>
    /// <c>null</c> en niet "Niet toegewezen": dat woord is een schermtekst en hoort in de weergavelaag.
    /// Gemeten kwamen twee van de zestien items zonder <c>System.AssignedTo</c> terug.
    /// </remarks>
    [JsonPropertyName("assignedToName")]
    public string? AssignedToName { get; init; }

    /// <summary>De unieke naam van de toegewezen persoon, of <c>null</c>. Operator-only.</summary>
    /// <remarks>Zelfde grens en zelfde reden als <see cref="CreatedByUniqueName"/>.</remarks>
    [JsonPropertyName("assignedToUniqueName")]
    public string? AssignedToUniqueName { get; init; }

    /// <summary>De openstaande uren (<c>RemainingWork</c>), of <c>null</c> als het veld niet is ingevuld.</summary>
    [JsonPropertyName("remainingWork")]
    public decimal? RemainingWork { get; init; }

    /// <summary>De gedane uren (<c>CompletedWork</c>), of <c>null</c> als het veld niet is ingevuld.</summary>
    [JsonPropertyName("completedWork")]
    public decimal? CompletedWork { get; init; }

    /// <summary>De story points, of <c>null</c> als het veld niet is ingevuld.</summary>
    [JsonPropertyName("storyPoints")]
    public decimal? StoryPoints { get; init; }
}

/// <summary>
/// Een verwijzing naar een iteratie: net genoeg om hem op een scherm te kunnen benoemen.
/// </summary>
/// <remarks>
/// Gebruikt voor twee lijsten die elk om een eigen reden bestaan: de iteraties <em>zonder datums</em>
/// (<see cref="SprintDocument.Undated"/>) en de iteraties die <em>allemaal vandaag bevatten</em>
/// (<see cref="SprintDocument.Overlapping"/>). Geen datums op dit type, want geen van beide lijsten heeft
/// ze nodig — de eerste heeft ze per definitie niet, en bij de tweede is het aanwijzen van de namen genoeg
/// om ze te kunnen corrigeren.
/// </remarks>
public sealed record SprintIterationRef
{
    /// <summary>De naam van de iteratie.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Het boardpad. Operator-only.</summary>
    /// <remarks>
    /// Het pad is een DevOps-detail (§2) en het staat daarom alleen op de operatorvariant. De klant leest
    /// dat er iteraties zonder datums zijn en hoeveel; wélke dat zijn is boordhygiëne en die repareert
    /// Soratus.
    /// </remarks>
    [JsonPropertyName("path")]
    public required string Path { get; init; }
}

/// <summary>
/// De sprint van één klant zoals die in de opslag staat.
/// </summary>
/// <remarks>
/// <para><strong>Waarom de sprint in de opslag staat en niet bij het bekijken wordt opgehaald.</strong>
/// §3.4 zegt "het portaal haalt bij openen de laatste status op" en §4 zet <c>devops-sync</c> op "elke 15
/// min". Die twee spreken elkaar tegen en dit is de kant die het is geworden, met drie argumenten in
/// gewicht:</para>
///
/// <list type="number">
///   <item><description>
///     <strong>§3.4 vraagt zelf het tijdstip van laatste ophalen op het scherm.</strong> Bij een ophaling
///     per paginaweergave is dat tijdstip altijd "nu" en zegt het niets. Het veld heeft alleen betekenis
///     als de lezing ouder kan zijn dan de pagina — dus de spec vraagt met dat ene veld om een
///     momentopname, en niet om een live aanroep.
///   </description></item>
///   <item><description>
///     <strong>Bij een mislukte ophaling is de vorige lezing mét tijdstip eerlijker dan een verse
///     mislukking</strong>, want die heeft niets gemeten. Dat is de regel van de kostenlane (punt 32 en
///     39) en hij geldt hier om een eigen reden: de vraag die een klant op dit scherm stelt is "schiet
///     mijn werk op", en "veertien minuten oud" beantwoordt die vraag terwijl een foutmelding hem niet
///     beantwoordt. Zonder opslag ís er geen vorige lezing om te tonen.
///   </description></item>
///   <item><description>
///     <strong>Het aanroepbudget van DevOps is niet gemeten.</strong> Dat is geen reden om aan te nemen
///     dat het schaars is — de kostenlane heeft geleerd dat je in geen van beide richtingen mag aannemen
///     — maar het is wel een reden om de kant te kiezen waar het aantal aanroepen niet van het aantal
///     openstaande tabbladen afhangt. Eén operator met twee tabbladen trok de emmer van Cost Management
///     leeg; of dat hier kan is onbekend, en verzamelen maakt het onmogelijk in plaats van onwaarschijnlijk.
///   </description></item>
/// </list>
///
/// <para><strong>De prijs, eerlijk: het scherm loopt tot een kwartier achter.</strong> Voor de vraag die
/// dit scherm beantwoordt is dat weinig — een sprint verandert niet per minuut — maar het is meer dan nul,
/// en daarom staat <see cref="ReadAt"/> op het scherm en niet alleen in dit document. En het is minder
/// eerlijk dan bij de kosten: dáár loopt de bron zelf al acht uur achter, dus "live" bestaat er niet. Hier
/// bestaat live wél, en het portaal kiest er bewust tegen.</para>
///
/// <para><strong>Er wordt nooit teruggeschreven naar DevOps.</strong> Dat is §3.4 en het staat hier omdat
/// dit het enige type is waar de verleiding zou kunnen ontstaan: er staat een volledige work item-lijst in
/// een document dat wij bezitten, en dat ziet uit als iets wat je kunt bijwerken. DevOps is leidend. Dit
/// document is een <em>lezing</em> en geen kopie waarmee iets te doen valt; een veld erin wijzigen
/// verandert niets op het bord en zou over een kwartier weer overschreven zijn.</para>
/// </remarks>
public sealed record SprintDocument
{
    /// <summary>Documentsleutel. Altijd <see cref="SprintDocumentKeys.Id"/>.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Partitiesleutel. De klantslug.</summary>
    [JsonPropertyName("pk")]
    public required string PartitionKey { get; init; }

    /// <summary>Documentsoort. Altijd <see cref="SprintDocumentKeys.Kind"/>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = SprintDocumentKeys.Kind;

    /// <summary>De klantslug (<c>cid</c> in §6).</summary>
    [JsonPropertyName("cid")]
    public required string CustomerId { get; init; }

    /// <summary>Wat er van de sprint van deze klant bekend is.</summary>
    [JsonPropertyName("state")]
    public required SprintState State { get; init; }

    /// <summary>
    /// De scope waartegen is gelezen, bijvoorbeeld <c>/soratus/MBVApp4 MAUI/MBVApp4 MAUI Team</c>.
    /// </summary>
    /// <remarks>
    /// Operator-only, om dezelfde reden en met dezelfde grens als
    /// <see cref="Data.AzureCostDocument.Scope"/>: §2 wijst de koppelingsdetails aan de operator toe. En om
    /// dezelfde functie: een sprint die van een ander team blijkt te zijn is alleen te betrappen als er op
    /// het scherm staat wélk team er is bevraagd.
    /// </remarks>
    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    /// <summary>
    /// Wanneer deze lezing bij DevOps is opgehaald, in UTC (§3.4, "tijdstip van laatste ophalen").
    /// </summary>
    /// <remarks>
    /// Het moment van de <em>lezing</em> en niet van het schrijven, net als
    /// <see cref="Data.AzureCostDocument.MeasuredAt"/>. Die twee lopen uiteen zodra er een keer opnieuw
    /// moet worden geprobeerd, en van de twee is de lezing degene waar de gegevens bij horen.
    /// </remarks>
    [JsonPropertyName("readAt")]
    public required DateTimeOffset ReadAt { get; init; }

    /// <summary>De guid van de huidige sprint, of <c>null</c> als er geen is.</summary>
    [JsonPropertyName("sprintId")]
    public string? SprintId { get; init; }

    /// <summary>De naam van de huidige sprint, of <c>null</c> (§3.4, "sprintnaam").</summary>
    [JsonPropertyName("sprintName")]
    public string? SprintName { get; init; }

    /// <summary>Het boardpad van de huidige sprint, of <c>null</c> (§3.4, "boardpad").</summary>
    [JsonPropertyName("boardPath")]
    public string? BoardPath { get; init; }

    /// <summary>De eerste dag van de sprint als <c>jjjj-MM-dd</c>, of <c>null</c> (§3.4, "periode").</summary>
    /// <remarks>
    /// Als tekst in de opslag en als <see cref="DateOnly"/> in het geheugen. Dezelfde vorm en dezelfde
    /// reden als <see cref="Data.AzureCostDocument.CoversThrough"/>: een dag is geen moment, en een dag die
    /// als moment wordt opgeslagen krijgt een tijdzone die er niet bij hoort.
    /// </remarks>
    [JsonPropertyName("start")]
    public string? Start { get; init; }

    /// <summary>De laatste dag van de sprint als <c>jjjj-MM-dd</c>, of <c>null</c>. Inclusief.</summary>
    [JsonPropertyName("finish")]
    public string? Finish { get; init; }

    /// <summary>De work items van deze sprint.</summary>
    /// <remarks>
    /// Leeg bij elke toestand behalve <see cref="SprintState.Current"/>, en ook dan kan hij leeg zijn — een
    /// sprint die net begint heeft geen items, en dat is een echte nul en geen onbekende. Het verschil met
    /// "wij hebben niet gelezen" zit in <see cref="State"/> en niet in de lengte van deze lijst.
    /// </remarks>
    [JsonPropertyName("items")]
    public IReadOnlyList<SprintWorkItem> Items { get; init; } = [];

    /// <summary>
    /// De iteraties van dit team zonder begin- én einddatum.
    /// </summary>
    /// <remarks>
    /// <para><strong>Deze lijst bestaat omdat weglaten hier liegen is.</strong> Er staan op dit bord drie
    /// iteraties zonder datums met werkitems erin (<c>Iteration 1</c> t/m <c>3</c>), met opzet niet
    /// aangeraakt: die items verplaatsen is een beslissing van een mens. Een iteratie zonder datums valt in
    /// geen enkele maand, dus die items komen in geen enkele sprintweergave voor — en een scherm dat het
    /// werk van de huidige maand toont zonder te zeggen dat er werk buiten valt, biedt een onvolledig beeld
    /// aan als volledig.</para>
    ///
    /// <para><strong>Hij staat er ook bij een gezonde <see cref="SprintState.Current"/>,</strong> en niet
    /// alleen als er geen sprint is. Dat is het punt: juist als er wél een sprint loopt, is de mededeling
    /// "er valt werk buiten elke maand" iets wat niemand anders zegt.</para>
    ///
    /// <para><strong>Wat er níet bij staat is hoeveel work items er in staan, en dat is een keuze met een
    /// prijs.</strong> Dat aantal kost een aanroep per iteratie, en de belangrijkste mededeling hangt er
    /// niet van af. Het alternatief was gevaarlijker dan het lijkt: een aantal dat bij veel iteraties
    /// gedeeltelijk wordt opgehaald is een aantal dat te laag is, en van de twee mogelijke fouten is alleen
    /// "geen aantal" zichtbaar. Het scherm zegt daarom uitdrukkelijk dat de items niet zijn geteld, want een
    /// ontbrekend aantal leest anders als nul.</para>
    /// </remarks>
    [JsonPropertyName("undated")]
    public IReadOnlyList<SprintIterationRef> Undated { get; init; } = [];

    /// <summary>
    /// De iteraties die vandaag allemaal bevatten, bij <see cref="SprintState.Ambiguous"/>.
    /// </summary>
    /// <remarks>
    /// Leeg bij elke andere toestand. Zonder deze namen is de melding "er lopen meerdere sprints" niet te
    /// gebruiken: de handeling erachter is de periodes corrigeren, en dan moet je weten welke. Operator-only,
    /// want overlappende periodes zijn boordhygiëne — de klant leest dat er geen sprint is aan te wijzen.
    /// </remarks>
    [JsonPropertyName("overlapping")]
    public IReadOnlyList<SprintIterationRef> Overlapping { get; init; } = [];

    /// <summary>Hoeveel iteraties van dit team wél datums hebben.</summary>
    /// <remarks>
    /// Nodig om <see cref="SprintState.NoCurrentSprint"/> te kunnen uitleggen: "er zijn vijf sprints met
    /// datums en vandaag valt in geen ervan" is een andere mededeling dan "er is er één en die is
    /// afgelopen".
    /// </remarks>
    [JsonPropertyName("datedCount")]
    public int DatedCount { get; init; }

    /// <summary>
    /// Waarom er niets bekend is, in gewone taal, of <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Alleen gevuld bij <see cref="SprintState.Unknown"/>. Geen statuscode en geen uitzonderingstekst;
    /// zie <see cref="Data.AzureCostDocument.Failure"/> voor de reden. Operator-only: de klant hoort niet
    /// te weten met welke API wij vechten, en een uitzonderingstekst kan een adres of een tokenfout
    /// bevatten.
    /// </remarks>
    [JsonPropertyName("failure")]
    public string? Failure { get; init; }

    /// <summary>De versie waarop de gelijktijdigheidscontrole loopt.</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; init; }
}
