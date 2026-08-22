using System.ComponentModel.DataAnnotations;

namespace Soratus.Portal.Sprints;

/// <summary>
/// De configuratiesectie <c>PortalSprints</c>: hoe vaak en hoe voorzichtig de sprintcollector Azure
/// DevOps bevraagt.
/// </summary>
/// <remarks>
/// <para><strong>Anders dan bij <see cref="Data.AzureCostOptions"/> komt hier niet elke waarde uit een
/// meting, en dat is het eerste dat een lezer hoort te weten.</strong> Van Cost Management is de emmer
/// gemeten — vier aanroepen binnen elf seconden gaven vier 429's, en de headers stonden erbij. Van Azure
/// DevOps is dat <em>niet</em> gemeten en het kón hier niet: de enige weg naar die API in deze sessie was
/// een MCP-server, en die geeft het antwoord door zonder de responsheaders. De documentatie noemt een
/// grens per gebruiker per vijf minuten met <c>X-RateLimit-Remaining</c>, <c>X-RateLimit-Limit</c>,
/// <c>X-RateLimit-Reset</c> en <c>Retry-After</c>, en die headers wordt <em>wel</em> gelezen — maar dat
/// ze er zijn is niet nagemeten.</para>
///
/// <para>Daaruit volgt de kant waarop de standaardwaarden staan: <strong>bij twijfel te langzaam en niet
/// te snel</strong>, precies zoals bij de kosten, en met de aantekening dat het getal daar een gemeten
/// bovengrens is en hier een aanname. Wat er wél is gemeten is het aantal aanroepen per klant per ronde,
/// en dat is de som die iemand tegen de gepubliceerde grens hoort te leggen zodra die grens te meten is:
/// <strong>twee vaste aanroepen</strong> (de iteraties van het team, en de work item-nummers van de
/// huidige sprint), <strong>plus één per tweehonderd work items</strong> voor de veldenbatch,
/// <strong>plus één per werkitemsoort</strong> die in de sprint voorkomt. Gemeten op dit bord:
/// 1 + 1 + 1 + 2 = <strong>vijf</strong>. Bij een ronde per kwartier is dat twintig aanroepen per uur per
/// klant.</para>
///
/// <para><strong>Er staat géén <c>ValidateOnStart</c> op</strong>, om dezelfde reden als bij
/// <see cref="Data.PortalDataOptions"/> en <see cref="Data.AzureCostOptions"/>: een verkeerd ingestelde
/// collector is een inrichtingsfout, en een inrichtingsfout die het opstarten tegenhoudt neemt
/// <c>/healthz</c> mee en rolt daarmee de uitrol terug.</para>
/// </remarks>
public sealed class SprintOptions
{
    /// <summary>De naam van de configuratiesectie.</summary>
    public const string SectionName = "PortalSprints";

    /// <summary>
    /// Of de collector draait.
    /// </summary>
    /// <remarks>
    /// <para><strong>Standaard aan, en in <c>appsettings.Development.json</c> uit.</strong> Die kant op en
    /// niet andersom: een standaard-uit vlag levert een storing op die zich voordoet als werkende
    /// functionaliteit — het portaal start, er staat nergens een fout, en er wordt stil nooit
    /// opgehaald.</para>
    ///
    /// <para><strong>En er zit al een tweede rem op die geen vlag is.</strong> De collector bevraagt
    /// alleen klanten met een vastgelegd DevOps-bord, en dat legt een operator met de hand vast; er is
    /// geen migratie die er zeven verzint. Een verse opslag levert dus nul aanroepen op zonder dat iemand
    /// iets hoeft uit te zetten.</para>
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Hoeveel minuten er tussen twee ronden zit.
    /// </summary>
    /// <remarks>
    /// <para>Vijftien, en dat is niet gekozen maar overgenomen: §4 van de spec zet <c>devops-sync</c> op
    /// "elke 15 min". Dat is dus een eis en geen instelling, en de instelbaarheid is er om hem <em>lager
    /// te kunnen zetten als de gemeten grens dat vraagt</em> en niet om hem sneller te maken.</para>
    ///
    /// <para><strong>Waarom er wordt verzameld en niet bij elke paginaweergave opgehaald</strong> staat op
    /// <see cref="SprintCollector"/>. Het kortste argument staat in §3.4 zelf: die vraagt het "tijdstip
    /// van laatste ophalen" op het scherm, en bij een ophaling per paginaweergave is dat tijdstip altijd
    /// "nu" en zegt het dus niets.</para>
    /// </remarks>
    [Range(1, 1440, ErrorMessage = "PortalSprints:IntervalMinutes hoort tussen 1 en 1440 te liggen.")]
    public int IntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Hoe lang een opgeslagen lezing als vers geldt, als deel van <see cref="IntervalMinutes"/>.
    /// </summary>
    /// <remarks>
    /// <para><strong>Dit is de wederzijdse uitsluiting tussen twee portaalinstanties, en hij is met opzet
    /// géén claimdocument.</strong> Bij de kosten is dat er wel een (punt 38): één document per dag, met
    /// een <c>CreateItemAsync</c>, zodat de tweede instantie een 409 krijgt. Dat werkt daar omdat het één
    /// document per dag is. Per kwartier zou het zesennegentig documenten per dag zijn in een container
    /// zonder TTL — rommel die niemand opruimt, voor een budget dat niet is gemeten.</para>
    ///
    /// <para>Wat er in de plaats staat: de collector leest vóór het ophalen de opgeslagen lezing en slaat
    /// deze klant over als die jonger is dan <see cref="IntervalMinutes"/> maal deze factor. Dat kost één
    /// puntlezing van ongeveer één RU en het laat niets achter. <strong>Wat het níet is, is een slot:</strong>
    /// twee instanties die binnen dezelfde seconde tikken komen er beide langs. De prijs daarvan is een
    /// verdubbeling van het aantal aanroepen en niet een verkeerd getal op het scherm — er wordt niets
    /// opgeteld en de tweede lezing overschrijft de eerste met dezelfde waarde. Blijkt er ooit een emmer te
    /// zijn die dat niet verdraagt, dan is de claimvorm van punt 38 de opwaardering en dat is een kleine
    /// wijziging.</para>
    ///
    /// <para>Vier vijfde en niet één: een ronde die een paar seconden na het kwartier begint zou zichzelf
    /// anders overslaan, en dan haalt het portaal om het kwartier niets op met een reden die niemand kan
    /// zien.</para>
    /// </remarks>
    [Range(0.1, 1.0, ErrorMessage = "PortalSprints:FreshnessFactor hoort tussen 0,1 en 1,0 te liggen.")]
    public double FreshnessFactor { get; set; } = 0.8;

    /// <summary>Het adres van Azure DevOps.</summary>
    /// <remarks>
    /// Instelbaar voor een test die de client tegen een eigen server laat lopen, en om geen andere reden.
    /// Er is geen tweede DevOps in beeld, en een Server-installatie op eigen hardware heeft een ander
    /// padvoorvoegsel en zou dus meer zijn dan een instelling.
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "PortalSprints:Endpoint ontbreekt.")]
    public string Endpoint { get; set; } = "https://dev.azure.com";

    /// <summary>De api-version van de DevOps-API.</summary>
    /// <remarks>
    /// <c>7.1</c>. Alle metingen van 22 augustus 2026 zijn via een MCP-server gedaan die zijn eigen
    /// versie kiest, dus <strong>dit getal is niet nagemeten</strong> — wat er is nagemeten zijn de
    /// veldnamen en de vorm van het antwoord. Instelbaar zodat een nieuwe versie te proberen is zonder
    /// uitrol, met dezelfde aantekening als bij de kosten: een versiewissel is een meting en geen
    /// instelling.
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "PortalSprints:ApiVersion ontbreekt.")]
    public string ApiVersion { get; set; } = "7.1";

    /// <summary>
    /// Hoeveel keer één aanroep hoogstens wordt gedaan binnen één ronde.
    /// </summary>
    /// <remarks>
    /// Drie, en dus één meer dan bij de kosten. Dat verschil heeft een reden: daar is gemeten dat élke
    /// respons budget kost, ook een 429, dus een derde poging kost de volgende klant zijn meting. Hier is
    /// dat niet gemeten en is de gepubliceerde grens ruimer dan wat vijf aanroepen per kwartier
    /// verbruiken. Wat er gebeurt als de pogingen op zijn is hetzelfde: er wordt niets weggeschreven en de
    /// vorige lezing blijft staan met haar eigen tijdstip erbij.
    /// </remarks>
    [Range(1, 5, ErrorMessage = "PortalSprints:MaxAttempts hoort tussen 1 en 5 te liggen.")]
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// De eigen ondergrens, in seconden, voor het wachten na een geweigerd verzoek.
    /// </summary>
    /// <remarks>
    /// De <c>Retry-After</c>-hint wordt gelezen als hij er is en deze waarde is de vloer eronder — de vorm
    /// van <see cref="Data.AzureCostOptions.BackoffSeconds"/>, waar gemeten is dat de hint te kort kan
    /// zijn. Twintig seconden en niet tweehonderdveertig: de kostenemmer is gemeten schaars en deze is dat
    /// voor zover bekend niet, en een ronde die per klant vijf keer vier minuten wacht haalt het volgende
    /// kwartier niet.
    /// </remarks>
    [Range(1, 3600, ErrorMessage = "PortalSprints:BackoffSeconds hoort tussen 1 en 3600 te liggen.")]
    public int BackoffSeconds { get; set; } = 20;

    /// <summary>
    /// Hoeveel work items er hoogstens uit één sprint worden gelezen.
    /// </summary>
    /// <remarks>
    /// <para><strong>Deze grens is er zodat een te lang antwoord een fout wordt en geen halve
    /// lijst.</strong> Precies de reden van <c>MaximumPages</c> in de kostenclient: een lezer die een
    /// pagina laat liggen heeft een aantal dat te laag is, en van de twee mogelijke fouten — geen aantal
    /// of een te laag aantal — is alleen de eerste zichtbaar. Raakt hij op, dan is de lezing
    /// <see cref="SprintAnswerKind.Unreadable"/> en verschijnt er geen sprint met vijfhonderd van de
    /// zeshonderd items.</para>
    ///
    /// <para>Vijfhonderd, en dat is ruim: gemeten had de grootste iteratie op dit bord zestien work items.
    /// Een sprint van vijfhonderd items is geen sprint meer, dus deze grens raken is zelf een bevinding.
    /// De veldenbatch van DevOps neemt er volgens de documentatie tweehonderd per aanroep, dus dit worden
    /// hoogstens drie batchaanroepen — dat is meegerekend in de vijf van deze klasse en het pad met meer
    /// dan één batch is <strong>niet gemeten</strong>.</para>
    /// </remarks>
    [Range(1, 5000, ErrorMessage = "PortalSprints:MaxWorkItems hoort tussen 1 en 5000 te liggen.")]
    public int MaxWorkItems { get; set; } = 500;

    /// <summary>
    /// Hoeveel verschillende werkitemsoorten er in één sprint mogen voorkomen.
    /// </summary>
    /// <remarks>
    /// <para><strong>Deze grens bestaat omdat elke soort een eigen aanroep kost.</strong> De categorie van
    /// een state is per werkitemsoort opvraagbaar en niet per project in één keer, dus een sprint met
    /// twintig soorten kost twintig aanroepen. Gemeten had de grootste iteratie op dit bord twee soorten
    /// (<c>User Story</c> en <c>Task</c>).</para>
    ///
    /// <para><strong>En bij het overschrijden wordt de lezing onleesbaar en niet gedeeltelijk.</strong> Dat
    /// is dezelfde keuze als bij <see cref="MaxWorkItems"/>: zonder de categorie van een state is niet te
    /// zeggen of een item afgerond is, en een statistiek "afgerond" die de items van twee soorten niet
    /// meetelt is een getal dat te laag is — en van de twee mogelijke fouten is alleen "geen getal"
    /// zichtbaar.</para>
    /// </remarks>
    [Range(1, 100, ErrorMessage = "PortalSprints:MaxWorkItemTypes hoort tussen 1 en 100 te liggen.")]
    public int MaxWorkItemTypes { get; set; } = 20;

    /// <summary>
    /// Het woord waarmee een work item als geblokkeerd geldt: als tag of als statenaam.
    /// </summary>
    /// <remarks>
    /// <para><strong>Dit veld bestaat omdat "geblokkeerd" op dit bord geen toestand is.</strong> §3.4
    /// vraagt de statistiek "geblokkeerd" en noemt <c>Blocked</c> tussen de states. Gemeten op 22 augustus
    /// 2026 heeft het werkitemtype <c>Task</c> van dit project vier states — <c>New</c>, <c>Active</c>,
    /// <c>Closed</c> en <c>Removed</c> — en géén <c>Blocked</c>, en in de veldenlijst van dat type staat
    /// ook geen blokkadeveld. Op dít bord kan een blokkade dus alleen een tag zijn.</para>
    ///
    /// <para><strong>Het heet <c>Marker</c> en niet <c>Tag</c>, en dat is geen woordkeuze.</strong> Een
    /// ander project met een eigen procestemplate heeft die state misschien wél, en een controle die
    /// alleen naar tags kijkt zou daar precies de statistiek te laag maken die §3.4 vraagt — terwijl de
    /// statenaam voluit op het scherm staat. Van de twee mogelijke fouten is alleen "geen getal"
    /// zichtbaar. Eén woord, twee plekken waar het kan staan, één vraag; zie
    /// <see cref="SprintJudgement.IsBlocked"/>.</para>
    ///
    /// <para><strong>En dan is nul een echte nul.</strong> Dat is het onderscheid dat de kostenlane duur
    /// heeft geleerd, hier op zijn kop: bij een bedrag is nul een onwaarheid zodra er geen regels zijn,
    /// maar "geen van de items die we hebben gelezen draagt dit woord" is een gemeten uitkomst en die is
    /// nul. Het verschil is dat de items er wél zijn — de keerzijde van punt 30, waar nul mét regels ook
    /// een echte nul is.</para>
    ///
    /// <para>Leeg zetten schakelt de statistiek niet uit maar zet hem op nul, en dat is met opzet: een
    /// statistiek die verdwijnt is een statistiek waarvan niemand weet dat hij er was.</para>
    /// </remarks>
    public string BlockedMarker { get; set; } = "Blocked";

    /// <summary>
    /// De identiteiten die als agent gelden, voor de herkomst van een work item (§3.4).
    /// </summary>
    /// <remarks>
    /// <para><strong>Leeg is de standaard en dat betekent "we kunnen het niet zien".</strong> §3.4 vraagt
    /// per work item de herkomst: "aangemaakt door agent of handmatig". Gemeten is dat er in DevOps
    /// vandaag niets staat wat dat onderscheid draagt — élk work item op dit bord is door een mens
    /// aangemaakt en <c>System.CreatedBy</c> is het enige spoor dat er is. Met een lege lijst komt élk
    /// item dus op <see cref="WorkItemOrigin.Unknown"/> uit en niet op
    /// <see cref="WorkItemOrigin.Manual"/>, want "handmatig" zou een bewering zijn die niemand heeft
    /// gemeten. Dat is punt 15 op een enum in plaats van op een bedrag.</para>
    ///
    /// <para><strong>De identiteit en niet een tag, en die keuze is echt.</strong> Een tag
    /// <c>agent</c> zou goedkoper zijn en zou vandaag al werken, maar een tag is door een mens te zetten
    /// en te verwijderen — en dan is "aangemaakt door een agent" een bewering van wie het laatst op het
    /// bord heeft geklikt. <c>System.CreatedBy</c> is door DevOps gezet bij het aanmaken en door niemand
    /// te wijzigen. Zodra <c>devops-sync</c> (§4) zijn eigen service principal heeft, hoort die hier te
    /// staan en klopt de kolom vanaf dat moment voor de items die hij aanmaakt — en voor de items van
    /// daarvóór blijft hij eerlijk leeg.</para>
    ///
    /// <para>Vergeleken wordt hoofdletterongevoelig op het volledige <c>uniqueName</c>, en anders op de
    /// weergavenaam. Een e-mailadres of de naam van een service principal is niet hoofdlettergevoelig, en
    /// een lijst die dat wél is levert een kolom op die stil op "onbekend" springt na een hernoeming in
    /// Entra.</para>
    /// </remarks>
    public string[] AgentIdentities { get; set; } = [];

    /// <summary>De tijd tussen twee ronden.</summary>
    public TimeSpan Interval => TimeSpan.FromMinutes(IntervalMinutes);

    /// <summary>De ondergrens van de backoff.</summary>
    public TimeSpan Backoff => TimeSpan.FromSeconds(BackoffSeconds);

    /// <summary>
    /// Hoe oud een opgeslagen lezing mag zijn om als vers te gelden.
    /// </summary>
    /// <remarks>Zie <see cref="FreshnessFactor"/> voor waarom dit geen slot is.</remarks>
    public TimeSpan Freshness => TimeSpan.FromMinutes(IntervalMinutes * FreshnessFactor);
}
