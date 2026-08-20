using System.Text.Json.Serialization;

namespace Soratus.Portal.Data;

/// <summary>
/// De documentsoorten in de container <c>customers</c>.
/// </summary>
/// <remarks>
/// <para>Klant, contract en toegang staan in dezelfde container met dezelfde partitiesleutel (de
/// klantslug) en worden onderscheiden door het veld <c>kind</c>. Dat is wat een klant aanmaken
/// atomair maakt: <c>TransactionalBatch</c> werkt binnen één partitiesleutel en niet daarbuiten.
/// </para>
///
/// <para><strong>Het veld heet <c>kind</c> en niet <c>type</c>, en dat is geen smaak.</strong> §6
/// van de spec geeft <c>Contract</c> een veld <c>type</c> — de soort contract, "Agent-abonnement +
/// doorontwikkeling". Dat is de naam die de mockup gebruikt en die op het scherm terugkomt. Een
/// discriminator die óók <c>type</c> heet zou dat veld overschrijven op het enige documenttype waar
/// het voorkomt. Van de twee namen is die uit de spec de vaste; de discriminator is van ons.</para>
/// </remarks>
public static class PortalDocumentKinds
{
    /// <summary>De klantregistratie. Eén per klant.</summary>
    public const string Customer = "customer";

    /// <summary>Het contract. Eén per klant, of geen zolang het niet is vastgelegd.</summary>
    public const string Contract = "contract";

    /// <summary>Eén portaaltoegang: een e-mailadres met een naam en een rol.</summary>
    public const string Access = "access";

    /// <summary>
    /// Eén urenregel (§6 <c>HourEntry</c>). Er zijn er onbeperkt veel per klant.
    /// </summary>
    /// <remarks>
    /// <para><strong>Dit is de eerste soort die onbeperkt doorgroeit, en hij staat toch in dezelfde
    /// container.</strong> Zie <see cref="HourEntryDocument"/> voor de afweging en voor de deur die
    /// dat besluit sluit.</para>
    ///
    /// <para>De naam is camelCase waar de andere drie één woord zijn. Dat volgt de veldnamen van de
    /// documenten zelf (<c>envFull</c>, <c>telemetryEndpoint</c>, <c>isInternal</c>) en de naam die
    /// §6 aan het type geeft. <c>hours</c> zou een verzameling suggereren; één document is één
    /// regel.</para>
    /// </remarks>
    public const string HourEntry = "hourEntry";

    /// <summary>De markering dat de eenmalige migratie uit de configuratie heeft gelopen.</summary>
    public const string Bootstrap = "bootstrap";
}

/// <summary>
/// De sleutels waarmee de documenten in de container worden aangeduid.
/// </summary>
/// <remarks>
/// Eén plek, zodat de lees- en de schrijfkant niet elk hun eigen sleutel kunnen samenstellen. Een
/// document dat onder twee sleutels wordt aangesproken bestaat twee keer.
/// </remarks>
public static class PortalDocumentIds
{
    /// <summary>
    /// De partitiesleutel van de markeerdocumenten die niet bij een klant horen.
    /// </summary>
    /// <remarks>
    /// Een klantslug moet met een letter of cijfer beginnen (zie <see cref="PortalSlug"/>), dus
    /// deze partitie kan nooit met die van een klant samenvallen. Het alternatief — de markering
    /// weglaten en op "staan er al klanten" varen — zou een klant die iemand bewust heeft
    /// verwijderd bij de volgende herstart terugzetten.
    /// </remarks>
    public const string ReservedPartitionKey = "$portal";

    /// <summary>De id van het klantdocument, binnen de partitie van die klant.</summary>
    public const string Customer = "customer";

    /// <summary>De id van het contractdocument, binnen de partitie van die klant.</summary>
    public const string Contract = "contract";

    /// <summary>De id van het markeerdocument van de migratie.</summary>
    public const string Bootstrap = "bootstrap";

    /// <summary>De id van een toegangsdocument, binnen de partitie van die klant.</summary>
    /// <param name="email">Het e-mailadres, al genormaliseerd naar kleine letters.</param>
    /// <returns>De id.</returns>
    public static string Access(string email) => $"access-{email}";

    /// <summary>
    /// De id van een urenregel, binnen de partitie van die klant.
    /// </summary>
    /// <param name="key">
    /// De sleutel binnen de klant: zie <see cref="HourEntryKeys"/>. Voor een regel uit het portaal
    /// een tijdstempel met een korte hash; voor een regel uit een koppeling de bron met de
    /// idempotentiesleutel van die koppeling.
    /// </param>
    /// <returns>De id.</returns>
    /// <remarks>
    /// <para><strong>De sleutel is geen willekeurig getal, en dat is de hele bedoeling.</strong> Een
    /// urenregel wordt geld: een dubbel weggeschreven regel is een dubbel gefactureerd uur. Met een
    /// herleidbare id levert een herhaalde schrijfactie een 409 op in plaats van een tweede regel.
    /// Dat dekt twee gevallen die anders geen van beide zichtbaar zijn — een dubbele verzending van
    /// het boekformulier (static SSR, dus geen JavaScript dat de knop uitzet) en een koppeling die
    /// zijn aanroep herhaalt na een netwerkfout.</para>
    ///
    /// <para>De id is bewust <em>niet</em> chronologisch sorteerbaar over alle bronnen heen. Er
    /// wordt nergens op id gesorteerd: de maandquery filtert op <c>month</c> en sorteert op
    /// <c>date</c>. Een id-vorm die suggereert dat hij een ordening draagt nodigt uit tot een query
    /// die daarop leunt, en die breekt zodra de eerste regel uit een koppeling binnenkomt.</para>
    /// </remarks>
    public static string HourEntry(string key) => $"hourEntry-{key}";
}

/// <summary>
/// De rollen binnen een klant, uit §3.5.
/// </summary>
/// <remarks>
/// <para><strong>Beide rollen mogen precies hetzelfde: lezen.</strong> Er is geen klantrol die iets
/// mag wijzigen — alleen Soratus deelt toegang uit en alleen Soratus bewerkt het contract (§2,
/// rolmatrix). "Beheerder klant" is dus geen bevoegdheid maar een aanduiding van wie de
/// contactpersoon is.</para>
///
/// <para>Dat de twee rollen niets van elkaar onderscheiden is een <em>gemeld</em> punt en geen
/// vergissing: zie het rapport bij fase 2. Een rol die "Beheerder" heet en niets mag beheren nodigt
/// uit tot de aanname dat hij wél iets mag, en die aanname belandt op een dag in code. De naam
/// staat zo in de spec en in de mockup, dus hij blijft staan tot iemand hem verandert.</para>
///
/// <para>Deze rol is <em>niet</em> de app-rol uit Entra. Die bepaalt of je klant of operator bent en
/// komt uit het token; zie <see cref="Security.PortalRoles"/>. Een toegangsdocument met de rol
/// "Soratus-operator" — zoals de mockup die voor de interne klant heeft — wordt daarom geweigerd:
/// operator worden is geen portaalgegeven.</para>
/// </remarks>
public static class PortalAccessRoles
{
    /// <summary>De contactpersoon van de klant. Leesrecht, net als <see cref="Reader"/>.</summary>
    public const string Administrator = "Beheerder klant";

    /// <summary>Leesrecht.</summary>
    public const string Reader = "Lezer";

    /// <summary>De rollen die een operator kan uitdelen, in de volgorde van het formulier.</summary>
    public static IReadOnlyList<string> All { get; } = [Administrator, Reader];

    /// <summary>
    /// Of dit een bestaande rol is.
    /// </summary>
    /// <param name="role">De rol uit het formulier of uit een document.</param>
    /// <returns><c>true</c> als de rol bestaat.</returns>
    public static bool IsKnown(string? role) =>
        role is not null && All.Contains(role, StringComparer.Ordinal);
}

/// <summary>
/// De klantregistratie zoals hij in de opslag staat.
/// </summary>
/// <remarks>
/// <para>Volgt §6 (<c>Customer</c>): id, naam, intern?, env, envFull. <strong>Zonder
/// <c>agents[]</c></strong> — dat veld staat in de spec en in het <c>DATA</c>-object van de mockup,
/// maar agents zijn telemetrie. Ze hier ook opslaan zou betekenen dat er twee lijsten van agents
/// bestaan die op een dag niet meer hetzelfde zeggen, en de verkeerde van de twee zou de lijst zijn
/// die niemand bijwerkt. De agents van een klant komen uit <see cref="IAgentTelemetryStore"/>.
/// </para>
///
/// <para>De veldnamen zijn die van de spec (<c>cid</c>, <c>env</c>, <c>envFull</c>) en niet die van
/// het agentcontract (<c>customerId</c>). Zie het rapport: §6 gebruikt <c>cid</c> voor alles wat
/// portaaleigen is, het agentcontract gebruikt <c>customerId</c> voor alles wat telemetrie is, en
/// die twee families staan in verschillende containers.</para>
/// </remarks>
public sealed record CustomerDocument
{
    /// <summary>Documentsleutel. Altijd <see cref="PortalDocumentIds.Customer"/>.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Partitiesleutel. Gelijk aan <see cref="CustomerId"/>.</summary>
    [JsonPropertyName("pk")]
    public required string PartitionKey { get; init; }

    /// <summary>Documentsoort. Altijd <see cref="PortalDocumentKinds.Customer"/>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = PortalDocumentKinds.Customer;

    /// <summary>De klantslug, gelijk aan <c>customerId</c> in de telemetrie en aan het pad in de URL.</summary>
    [JsonPropertyName("cid")]
    public required string CustomerId { get; init; }

    /// <summary>De naam zoals hij op het scherm hoort te staan.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Of dit de interne klant "Soratus — intern beheer" is (§4).</summary>
    [JsonPropertyName("isInternal")]
    public bool IsInternal { get; init; }

    /// <summary>Korte omgevingsaanduiding. Het enige omgevingsveld dat een klant ziet.</summary>
    [JsonPropertyName("env")]
    public string? Environment { get; init; }

    /// <summary>De volledige omgeving (subscription · resource group). Operator-only.</summary>
    [JsonPropertyName("envFull")]
    public string? EnvironmentDetail { get; init; }

    /// <summary>
    /// De Cosmos-endpoint van de telemetrie van déze klant, of leeg voor de standaard.
    /// </summary>
    /// <remarks>
    /// Staat hier en niet in configuratie, want dat is precies de acceptatie van fase 2: een klant
    /// met zijn eigen account inrichten mag geen uitrol vragen.
    /// </remarks>
    [JsonPropertyName("telemetryEndpoint")]
    public string? TelemetryEndpoint { get; init; }

    /// <summary>De databasenaam bij <see cref="TelemetryEndpoint"/>, of leeg voor de standaard.</summary>
    [JsonPropertyName("telemetryDatabase")]
    public string? TelemetryDatabase { get; init; }

    /// <summary>Wanneer deze klant is aangemaakt, in UTC.</summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Wie hem heeft aangemaakt: de <c>oid</c> of naam van de operator.</summary>
    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; init; }

    /// <summary>Wanneer hij voor het laatst is gewijzigd, in UTC.</summary>
    [JsonPropertyName("changedAt")]
    public DateTimeOffset? ChangedAt { get; init; }

    /// <summary>Wie hem voor het laatst heeft gewijzigd.</summary>
    [JsonPropertyName("changedBy")]
    public string? ChangedBy { get; init; }

    /// <summary>
    /// De versie die Cosmos zelf bijhoudt. Hierop loopt de gelijktijdigheidscontrole.
    /// </summary>
    /// <remarks>
    /// Wordt nooit door ons gezet: Cosmos vult hem bij elke schrijfactie. Hij gaat als
    /// <c>If-Match</c> mee bij een wijziging, zodat twee operators elkaar niet stil overschrijven.
    /// </remarks>
    [JsonPropertyName("_etag")]
    public string? ETag { get; init; }
}

/// <summary>
/// Het contract van één klant, zoals het in de opslag staat.
/// </summary>
/// <remarks>
/// <para>Volgt §6 (<c>Contract</c>) en de contractkaart uit §3.5. De veldnamen komen uit het
/// <c>DATA</c>-object van de mockup, zodat de kaart één op één te vullen is.</para>
///
/// <para><strong>Wat er bewust níet in staat: <c>tarief</c>.</strong> De mockup heeft naast
/// <c>uurTarief</c> (een getal) ook <c>tarief</c> (de tekst "€ 125 / uur buiten bundel"). Twee
/// velden over hetzelfde bedrag kunnen elkaar tegenspreken, en dan is niet te zeggen welk van de
/// twee de factuur haalt. De tekst wordt afgeleid bij het opmaken; voor de interne klant, waar de
/// mockup "intern — niet doorbelast" heeft staan, volgt die tekst uit
/// <see cref="CustomerDocument.IsInternal"/>.</para>
///
/// <para><c>looptijd</c> en <c>opzeg</c> zijn tekst en geen aantal maanden. Dat is geen luiheid: de
/// interne klant heeft looptijd "doorlopend" en opzegtermijn "n.v.t.", en die passen in geen
/// getal.</para>
/// </remarks>
public sealed record ContractDocument
{
    /// <summary>Documentsleutel. Altijd <see cref="PortalDocumentIds.Contract"/>.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Partitiesleutel. De klantslug.</summary>
    [JsonPropertyName("pk")]
    public required string PartitionKey { get; init; }

    /// <summary>Documentsoort. Altijd <see cref="PortalDocumentKinds.Contract"/>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = PortalDocumentKinds.Contract;

    /// <summary>De klantslug.</summary>
    [JsonPropertyName("cid")]
    public required string CustomerId { get; init; }

    /// <summary>Contractnummer, bijvoorbeeld <c>SOR-2026-003</c>.</summary>
    [JsonPropertyName("nr")]
    public string? Number { get; init; }

    /// <summary>Soort contract, bijvoorbeeld <c>Agent-abonnement + doorontwikkeling</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>
    /// Ingangsdatum als <c>yyyy-MM-dd</c>.
    /// </summary>
    /// <remarks>
    /// Opslag is de kalenderdatum in ISO-vorm, ook al toont de mockup <c>01-11-2025</c>. Dat volgt
    /// punt 7 van de fase-0-afwijkingen: Cosmos vergelijkt tijdvelden als tekst, en op
    /// <c>dd-MM-yyyy</c> sorteert een lijst contracten stil verkeerd. Het is een datum en geen
    /// moment: een contract gaat in op een dag, niet op een tijdstip in een tijdzone.
    /// </remarks>
    [JsonPropertyName("start")]
    public string? StartsOn { get; init; }

    /// <summary>Looptijd als tekst, bijvoorbeeld <c>24 maanden</c> of <c>doorlopend</c>.</summary>
    [JsonPropertyName("looptijd")]
    public string? Term { get; init; }

    /// <summary>Opzegtermijn als tekst, bijvoorbeeld <c>2 maanden</c> of <c>n.v.t.</c>.</summary>
    [JsonPropertyName("opzeg")]
    public string? NoticePeriod { get; init; }

    /// <summary>De SLA in één regel, bijvoorbeeld <c>Reactie 4 werkuren · herstel 1 werkdag</c>.</summary>
    [JsonPropertyName("sla")]
    public string? Sla { get; init; }

    /// <summary>
    /// Urenbundel per maand, of <c>null</c> als er geen bundel is vastgelegd.
    /// </summary>
    /// <remarks>
    /// <para><strong><c>decimal?</c> en geen <c>decimal</c>.</strong> Met een niet-nullable
    /// <c>decimal</c> zijn "nul" en "niet ingevuld" dezelfde waarde, en dan moet de leeskaart kiezen
    /// welke van de twee hij toont. Een klant in onboarding heeft nog geen bundel vastgelegd, en een
    /// contract met een bundel van nul uur is een andere mededeling dan een leeg veld — het eerste
    /// zegt "alle uren gaan per uur", het tweede zegt "we hebben het nog niet afgesproken".</para>
    ///
    /// <para>Dat is dezelfde regel die in dit portaal al twee keer is toegepast: bij de statussen
    /// (geen document betekent <c>Unknown</c> en geen verzonnen groen, punt 2 van de
    /// fase-0-afwijkingen) en bij <see cref="Views.AccessEntraState"/> (drie waarden en geen
    /// <c>bool</c>, want "onbekend" en "niet uitgenodigd" zijn verschillende mededelingen).</para>
    ///
    /// <para>Documenten van vóór deze wijziging hebben <c>"bundelUren": 0</c> staan en lezen dus als
    /// nul en niet als <c>null</c>. Dat is de eerlijke uitkomst: van zo'n document is niet te weten
    /// of de nul een afspraak was of een ontbrekende invoer, en achteraf <c>null</c> van maken zou
    /// een afspraak weggooien die er misschien wel was.</para>
    /// </remarks>
    [JsonPropertyName("bundelUren")]
    public decimal? BundledHours { get; init; }

    /// <summary>
    /// Uurtarief buiten de bundel in euro, of <c>null</c> als er geen tarief is vastgelegd.
    /// </summary>
    /// <remarks>Zelfde afweging als bij <see cref="BundledHours"/>.</remarks>
    [JsonPropertyName("uurTarief")]
    public decimal? HourlyRate { get; init; }

    /// <summary>Indexatie, bijvoorbeeld <c>CBS-index per 1 januari</c>.</summary>
    [JsonPropertyName("indexatie")]
    public string? Indexation { get; init; }

    /// <summary>Contactpersoon bij de klant.</summary>
    [JsonPropertyName("contact")]
    public string? Contact { get; init; }

    /// <summary>Beheerd door, bijvoorbeeld <c>Soratus — accountteam</c>.</summary>
    [JsonPropertyName("eigenaar")]
    public string? ManagedBy { get; init; }

    /// <summary>
    /// Het opslagpercentage op de Azure-kosten, of <c>null</c> als er niets is afgesproken.
    /// </summary>
    /// <remarks>
    /// <para><strong>Operator-only.</strong> §2 zegt "Facturatie: Azure per dienst + beheeropslag:
    /// nee" voor de klant, en dit veld is die beheeropslag. Het staat op het contract omdat §3.9 het
    /// vraagt bij het aanmaken van een klant, terwijl §6 het bij <c>AzureCost</c> per maand zet.
    /// Beide kunnen: dit is de afspraak, een maand kan er later van afwijken. Zie het rapport.</para>
    ///
    /// <para>Dat dit veld hier staat is precies de reden dat de klantweergave van het contract een
    /// eigen type is dat het veld niet heeft, in plaats van een <c>@if</c> in de Razor.</para>
    ///
    /// <para><strong>Van de drie bedragen is <c>null</c> hier het belangrijkst.</strong> Nul procent
    /// opslag is een afspraak die we hebben gemaakt; geen opslag ingevuld is een afspraak die nog
    /// moet komen. Zodra fase 4 de Azure-uitsplitsing gaat rekenen scheelt dat verschil geld, en
    /// een niet-nullable <c>decimal</c> zou de tweede stil als de eerste laten doorrekenen. Zie
    /// verder <see cref="BundledHours"/>.</para>
    /// </remarks>
    [JsonPropertyName("opslag")]
    public decimal? AzureSurchargePercentage { get; init; }

    /// <summary>Wanneer dit contract voor het laatst is gewijzigd, in UTC.</summary>
    [JsonPropertyName("changedAt")]
    public DateTimeOffset? ChangedAt { get; init; }

    /// <summary>Wie het voor het laatst heeft gewijzigd.</summary>
    [JsonPropertyName("changedBy")]
    public string? ChangedBy { get; init; }

    /// <summary>De versie waarop de gelijktijdigheidscontrole loopt. Zie <see cref="CustomerDocument.ETag"/>.</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; init; }
}

/// <summary>
/// Eén portaaltoegang: een e-mailadres met een naam en een rol (§6 <c>Access</c>).
/// </summary>
/// <remarks>
/// <para><strong>De aanwezigheid van dit document is het toegangsrecht.</strong> Intrekken
/// verwijdert het document en zet geen vlag. Dat is met opzet: "wie mag hierbij" is dan de
/// aanwezigheid van een document en niet een veld dat iemand in een query kan vergeten. Een
/// vergeten filter op een vlag verleent toegang; een ontbrekend document kan dat niet.</para>
///
/// <para>De prijs staat in het rapport als open punt: er blijft geen spoor van een ingetrokken
/// toegang. Een audittrail hoort een eigen bewaartermijn te hebben en dus een eigen container, en
/// die keuze hangt aan het audit-besluit dat §9 nog openhoudt.</para>
///
/// <para><strong>Dit document is de ene helft van het toegangsrecht, en het portaal kent de andere
/// helft niet.</strong> Op de app-registratie staat <c>appRoleAssignmentRequired</c>, dus iemand
/// moet in Entra óók de rol <c>Klant</c> krijgen. Dat blijft handwerk, en niet bij gebrek aan tijd:
/// er is precies één Graph-permissie waarmee een app app-rollen kan toekennen, die is niet tot één
/// app te beperken, en een gecompromitteerd portaal zou daarmee de tenant kunnen overnemen. Het
/// portaal krijgt die permissie dus niet.</para>
///
/// <para><strong>Daarom staat er geen veld op dit document dat zegt of de uitnodiging is
/// verstuurd.</strong> Dat was de eerste opzet en het is een stille onwaarheid: niets in het portaal
/// zou dat veld ooit vullen, dus het scherm zou blijven zeggen "wacht op uitnodiging" ook nadat
/// iemand het had gedaan. De tweede toestand is <em>onbekend</em> zolang het portaal geen leesrecht
/// op Entra heeft (<c>Application.Read.All</c>, een tenantbrede toekenning), en onbekend hoort ook
/// als onbekend op het scherm te staan. Zie <see cref="Views.AccessEntraState"/>. Komt dat leesrecht
/// er, dan is de controle een lezing op het moment van renderen en geen veld in dit document — een
/// gekopieerde toestand die niemand bijwerkt is precies hoe die onwaarheid terugkomt.</para>
/// </remarks>
public sealed record AccessDocument
{
    /// <summary>Documentsleutel: <see cref="PortalDocumentIds.Access(string)"/>.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Partitiesleutel. De klantslug.</summary>
    [JsonPropertyName("pk")]
    public required string PartitionKey { get; init; }

    /// <summary>Documentsoort. Altijd <see cref="PortalDocumentKinds.Access"/>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = PortalDocumentKinds.Access;

    /// <summary>De klantslug (<c>cid</c> in §6).</summary>
    [JsonPropertyName("cid")]
    public required string CustomerId { get; init; }

    /// <summary>
    /// Het e-mailadres, genormaliseerd naar kleine letters.
    /// </summary>
    /// <remarks>
    /// Eén vorm en niet twee. Entra vergelijkt hoofdletterongevoelig, en zou hier de ingetypte
    /// vorm staan met daarnaast een genormaliseerde, dan bestaat er een pad waarlangs
    /// "Jan@x.nl" en "jan@x.nl" twee toegangen zijn en er één van intrekken niets doet.
    /// </remarks>
    [JsonPropertyName("email")]
    public required string Email { get; init; }

    /// <summary>De naam, voor het toegangsoverzicht.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>De rol binnen de klant. Zie <see cref="PortalAccessRoles"/>.</summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>Wanneer deze toegang is vastgelegd, in UTC.</summary>
    [JsonPropertyName("grantedAt")]
    public DateTimeOffset GrantedAt { get; init; }

    /// <summary>Welke operator hem heeft gegeven.</summary>
    [JsonPropertyName("grantedBy")]
    public string? GrantedBy { get; init; }

    /// <summary>De versie waarop de gelijktijdigheidscontrole loopt.</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; init; }
}

/// <summary>
/// De markering dat de eenmalige migratie van <c>Portal:Customers</c> naar de opslag heeft gelopen.
/// </summary>
/// <remarks>
/// Staat in de gereserveerde partitie <see cref="PortalDocumentIds.ReservedPartitionKey"/>, want hij
/// hoort bij geen enkele klant. Zolang dit document er staat schrijft het portaal de
/// configuratielijst nooit meer weg — ook niet als er intussen klanten zijn verwijderd.
/// </remarks>
public sealed record BootstrapDocument
{
    /// <summary>Documentsleutel. Altijd <see cref="PortalDocumentIds.Bootstrap"/>.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Partitiesleutel. Altijd <see cref="PortalDocumentIds.ReservedPartitionKey"/>.</summary>
    [JsonPropertyName("pk")]
    public required string PartitionKey { get; init; }

    /// <summary>Documentsoort. Altijd <see cref="PortalDocumentKinds.Bootstrap"/>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = PortalDocumentKinds.Bootstrap;

    /// <summary>Wanneer de migratie liep, in UTC.</summary>
    [JsonPropertyName("ranAt")]
    public DateTimeOffset RanAt { get; init; }

    /// <summary>Hoeveel klanten er zijn weggeschreven.</summary>
    [JsonPropertyName("customers")]
    public int Customers { get; init; }

    /// <summary>De slugs die zijn weggeschreven, zodat na te zoeken is wat er gebeurde.</summary>
    [JsonPropertyName("slugs")]
    public IReadOnlyList<string> Slugs { get; init; } = [];
}
