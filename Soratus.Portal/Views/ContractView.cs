using Soratus.Portal.Data;

namespace Soratus.Portal.Views;

/// <summary>
/// De teksten van het contractscherm die geen gegeven zijn maar een mededeling.
/// </summary>
/// <remarks>
/// Eén plek, en de viewmodels dragen de tekst mee in plaats van dat het scherm hem verzint. Dezelfde
/// afspraak als bij <see cref="AgentConfigurationNotice"/>: dit zijn beweringen over wat het portaal
/// kán en over wat een toegangsaanduiding betekent, en die horen te veranderen op het moment dat het
/// portaal verandert — niet op het moment dat iemand een Razor-bestand herschrijft.
/// </remarks>
public static class ContractNotice
{
    /// <summary>
    /// Waarom een klant hier niets kan wijzigen.
    /// </summary>
    /// <remarks>
    /// <para>§2 geeft de klant op contract en toegang lezen, de operator lezen + bewerken. De
    /// openstaande vraag uit §9 — mag een beheerder van de klant zelf toegang geven — is beantwoord
    /// met "alleen Soratus". Deze tekst zegt dat, in plaats van dat de klant naar een uitgegrijsde
    /// knop kijkt en zich afvraagt welk recht hij mist.</para>
    ///
    /// <para>De tekst begon met "Read-only.". Dat is het enige Engels in een portaal dat verder
    /// consequent Nederlands is, en het stond als voetregel onder de contractkaart van een
    /// <em>klant</em> — niet in een operatorscherm waar een technische term thuishoort. De naam van
    /// de constante blijft <c>ReadOnly</c>: dat is code en geen copy.</para>
    /// </remarks>
    public const string ReadOnly =
        "Deze gegevens zijn hier alleen te lezen. Contract en portaaltoegang worden door Soratus " +
        "beheerd; laat het ons weten als er iets moet wijzigen.";

    /// <summary>
    /// Dat de twee toegangsaanduidingen precies hetzelfde recht geven.
    /// </summary>
    /// <remarks>
    /// <para>Zonder deze regel is "Beheerder klant" een naam die een bevoegdheid belooft die niet
    /// bestaat. De namen komen uit §3.5 en blijven staan; wat er niet in de spec staat is dat ze
    /// gelijkwaardig zijn, en dat is precies wat een lezer hier nodig heeft.</para>
    ///
    /// <para><strong>Het woord "rol" staat er niet in, en dat is de hele reden dat deze tekst is
    /// herschreven.</strong> "Beheerder klant" en "Lezer" zijn identiek in rechten, want alleen
    /// Soratus deelt toegang uit. Een rol belooft rechten, en zo'n belofte belandt op een dag als
    /// aanname in code — een <c>if</c> op de rolnaam die iets toestaat wat er nooit was. Het portaal
    /// noemt dit daarom nergens een rol: het is een aanduiding van wie we aanspreken. De kolomkop op
    /// beide contractschermen heet om dezelfde reden "Aanduiding".</para>
    /// </remarks>
    public const string AccessLabelsAreEqual =
        "Beide aanduidingen geven hetzelfde leesrecht. Ze zeggen wie we aanspreken en niet wat " +
        "iemand mag; toegang geven en intrekken doet Soratus.";

    /// <summary>
    /// Dat "vastgelegd" nog niet "kan inloggen" betekent, en dat het portaal het tweede niet weet.
    /// </summary>
    /// <remarks>
    /// <para>Toegang bestaat uit twee toestanden: vastgelegd in de platformdata (dat doet dit
    /// scherm) en actief in Entra ID (dat doet een mens). Het portaal krijgt geen recht om app-rollen
    /// toe te kennen — er is precies één Graph-permissie die dat kan, die is niet tot één app te
    /// beperken, en een gecompromitteerd portaal zou daarmee de tenant kunnen overnemen.</para>
    ///
    /// <para>Zolang het portaal ook geen <em>lees</em>recht op Entra heeft, is de tweede toestand
    /// onbekend. Dat staat er dan ook zo. Suggereren dat iemand kan inloggen omdat zijn regel in de
    /// lijst staat, is precies de stille onwaarheid die dit ontwerp probeert te vermijden.</para>
    /// </remarks>
    public const string EntraStateUnknown =
        "Een regel in deze lijst geeft leesrecht in het portaal zodra de persoon ook in Entra ID is " +
        "uitgenodigd. Die uitnodiging is een handmatige stap bij Soratus; dit portaal kan niet zien " +
        "of hij al is gedaan.";
}

/// <summary>
/// Of deze persoon in Entra ID daadwerkelijk kan aanmelden.
/// </summary>
/// <remarks>
/// De tweede van de twee toestanden van een toegangsregel. Vandaag altijd
/// <see cref="Unknown"/>: het portaal heeft geen leesrecht op Entra. De andere twee waarden staan er
/// omdat de controle erbij te zetten is zodra dat recht bestaat — als lezing op het moment van
/// renderen, niet als veld in het document.
///
/// <para>Er is bewust geen <c>bool IsInvited</c>. Twee toestanden zouden "onbekend" en "niet
/// uitgenodigd" op één waarde laten vallen, en dat zijn twee verschillende mededelingen: de eerste
/// zegt dat wíj het niet weten, de tweede dat de persoon niet naar binnen kan.</para>
/// </remarks>
public enum AccessEntraState
{
    /// <summary>Het portaal kan het niet zien. Dat is nu de enige waarde die voorkomt.</summary>
    Unknown,

    /// <summary>De rol staat in Entra: deze persoon kan aanmelden.</summary>
    Active,

    /// <summary>De rol staat niet in Entra: vastgelegd, maar aanmelden kan nog niet.</summary>
    Missing,
}

/// <summary>
/// De contractkaart en het toegangsoverzicht zoals de klant ze mag zien (§3.5).
/// </summary>
/// <remarks>
/// <para><strong>Wat er niet op staat, staat er niet als leeg veld.</strong> Dezelfde regel als bij
/// <see cref="CustomerAgentConfigurationView"/> en om dezelfde reden: een ontbrekende property kan
/// niet lekken, ook niet als iemand er over een half jaar een rij bij zet en het <c>@if</c>
/// vergeet.</para>
///
/// <para>Concreet ontbreekt hier het <em>opslagpercentage op de Azure-kosten</em>. Dat staat wel op
/// het contractdocument — §3.9 vraagt het bij het aanmaken van een klant — maar §2 zegt over
/// "Facturatie: Azure per dienst + beheeropslag" onomwonden <strong>nee</strong> voor de klant. Dat
/// is onze marge; die hoort niet op het scherm van degene die hem betaalt. Ook de etags ontbreken:
/// dat zijn schrijfvoorwaarden, en de klant schrijft niet.</para>
///
/// <para>Het uurtarief staat er als getal, met <see cref="IsInternal"/> ernaast, en niet als de
/// kant-en-klare zin "€ 125 / uur buiten bundel". De mockup bewaart die zin als apart veld
/// <c>tarief</c> naast <c>uurTarief</c>; twee velden over hetzelfde bedrag kunnen elkaar
/// tegenspreken en dan is niet te zeggen welk van de twee de factuur haalt. De zin hoort dus bij het
/// opmaken, op één plek in de weergave.</para>
/// </remarks>
public sealed record CustomerContractView
{
    /// <summary>De klantslug.</summary>
    public required string CustomerId { get; init; }

    /// <summary>De klantnaam, voor de kop.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Wanneer deze weergave is opgebouwd.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>Of dit de interne beheerklant is. Bepaalt hoe het tarief te lezen is.</summary>
    public required bool IsInternal { get; init; }

    /// <summary>
    /// Korte omgevingsaanduiding, bijvoorbeeld <c>West-Europa</c>.
    /// </summary>
    /// <remarks>
    /// <para>Staat wél op de klantweergave, en dat is geen inconsistentie met
    /// <see cref="OperatorContractView.EnvironmentDetail"/>. §2 maakt
    /// infrastructuur<em>details</em> operator-only; de korte aanduiding is volgens fase 0 juist
    /// "het enige omgevingsveld dat een klant te zien krijgt", en <see cref="CustomerAgentsView"/>
    /// draagt hem om dezelfde reden. De grens loopt tussen "in welke regio staat mijn omgeving" en
    /// "in welke subscription en resource group".</para>
    ///
    /// <para>Sinds er een omgevingsblok op het operatorscherm staat, loopt die grens ook door de
    /// bewerkbaarheid heen: de operator wijzigt daar de korte aanduiding, de volledige omgeving en de
    /// opslaglocatie in één kaart, en van die vijf velden leest de klant er twee — deze en zijn naam.
    /// Dat is geen filter op dat scherm maar een gevolg van de twee typen: wat hier niet staat kan de
    /// klantweergave niet renderen.</para>
    /// </remarks>
    public string? Environment { get; init; }

    /// <summary>
    /// Of er een contract is vastgelegd.
    /// </summary>
    /// <remarks>
    /// <c>false</c> is een gewone toestand: een klant in onboarding heeft nog geen contract. Het
    /// scherm hoort dat te zeggen — een kaart met elf streepjes suggereert dat er gegevens
    /// ontbreken, terwijl er nog niets ís.
    /// </remarks>
    public required bool HasContract { get; init; }

    /// <summary>Contractnummer.</summary>
    public string? Number { get; init; }

    /// <summary>Soort contract.</summary>
    public string? Type { get; init; }

    /// <summary>Ingangsdatum, of <c>null</c> als die niet is vastgelegd.</summary>
    public DateOnly? StartsOn { get; init; }

    /// <summary>Looptijd, als tekst.</summary>
    public string? Term { get; init; }

    /// <summary>Opzegtermijn, als tekst.</summary>
    public string? NoticePeriod { get; init; }

    /// <summary>De SLA in één regel.</summary>
    public string? Sla { get; init; }

    /// <summary>
    /// Urenbundel per maand, of <c>null</c> als er niets is vastgelegd.
    /// </summary>
    /// <remarks>
    /// Zie <see cref="ContractDocument.BundledHours"/>: <c>null</c> is "niet vastgelegd" en nul is
    /// "geen bundel", en dat zijn twee verschillende mededelingen.
    /// </remarks>
    public decimal? BundledHours { get; init; }

    /// <summary>
    /// Uurtarief buiten de bundel, of <c>null</c> als er niets is vastgelegd.
    /// </summary>
    /// <remarks>Zie <see cref="ContractDocument.HourlyRate"/>.</remarks>
    public decimal? HourlyRate { get; init; }

    /// <summary>Indexatie.</summary>
    public string? Indexation { get; init; }

    /// <summary>Contactpersoon bij de klant.</summary>
    public string? Contact { get; init; }

    /// <summary>Beheerd door.</summary>
    public string? ManagedBy { get; init; }

    /// <summary>Wie er namens deze klant toegang heeft.</summary>
    public IReadOnlyList<CustomerAccessRow> Access { get; init; } = [];

    /// <summary>
    /// Waarom er op dit scherm niets te wijzigen valt.
    /// </summary>
    /// <remarks>
    /// De rolmatrix (§2) geeft de klant lezen en de operator lezen + bewerken, en de openstaande
    /// vraag uit §9 — mag een beheerder van de klant zelf toegang geven — is met "alleen Soratus"
    /// beantwoord. Er staat dus een melding en geen uitgegrijsde knop: een knop die niets doet
    /// belooft dat het wél kan.
    /// </remarks>
    public required string ReadOnlyNotice { get; init; }

    /// <summary>
    /// Dat het portaal niet kan zien of iemand in Entra is uitgenodigd. Zie
    /// <see cref="ContractNotice.EntraStateUnknown"/>.
    /// </summary>
    /// <remarks>
    /// Staat ook op de klantweergave, en dat is bewust: als een collega in deze lijst staat en niet
    /// kan inloggen, is dit de uitleg. Zwijgen zou die klant naar ons laten bellen met een vraag
    /// waarop het scherm het antwoord had.
    /// </remarks>
    public required string AccessStateNotice { get; init; }

    /// <summary>
    /// Dat de twee toegangsaanduidingen hetzelfde recht geven. Zie
    /// <see cref="ContractNotice.AccessLabelsAreEqual"/>.
    /// </summary>
    /// <remarks>
    /// <para>Staat op beide typen, want beide rollen zien dezelfde kolom. Zonder deze zin belooft
    /// "Beheerder klant" aan de klant een recht dat niet bestaat — en de klant is juist de lezer die
    /// dat woord op zichzelf betrekt.</para>
    ///
    /// <para><strong>Waarom als veld en niet als constante uit de markup.</strong> Het scherm haalde
    /// de constante rechtstreeks uit de Razor. Dat werkt, maar het breekt de afspraak dat het
    /// viewmodel de tekst draagt, en die afspraak is de reden dat het rolverschil in dit portaal een
    /// typeverschil is en geen <c>@if</c>: wat een rol te zien krijgt staat op het type van die rol.
    /// Een constante in de markup zet die grens buiten het bereik van de compiler — dan is een
    /// melding die op één van de twee schermen ontbreekt niet meer op te merken, en dat is precies
    /// wat hier was gebeurd.</para>
    /// </remarks>
    public required string AccessLabelNotice { get; init; }
}

/// <summary>
/// Eén toegangsregel zoals de klant hem ziet.
/// </summary>
/// <remarks>
/// Zonder etag (dat is een schrijfvoorwaarde) en zonder wie hem heeft uitgedeeld.
/// </remarks>
public sealed record CustomerAccessRow
{
    /// <summary>Het e-mailadres.</summary>
    public required string Email { get; init; }

    /// <summary>De naam, of <c>null</c> als die niet is vastgelegd.</summary>
    public string? Name { get; init; }

    /// <summary>De rol binnen de klant. Zie <see cref="PortalAccessRoles"/>.</summary>
    public required string Role { get; init; }

    /// <summary>
    /// Of deze persoon in Entra kan aanmelden. Vandaag altijd
    /// <see cref="AccessEntraState.Unknown"/>; zie <see cref="ContractNotice.EntraStateUnknown"/>.
    /// </summary>
    public required AccessEntraState EntraState { get; init; }
}

/// <summary>
/// De contractkaart, het klantbeheer en het toegangsoverzicht zoals de operator ze ziet en bewerkt.
/// </summary>
/// <remarks>
/// <para>Een apart type en niet een vlag op het klanttype, om dezelfde reden als bij de andere
/// schermen: het verschil tussen de rollen is een verschil tussen typen. Wat er hier bovenop komt is
/// precies wat §2 als operator-only aanmerkt, plus de etags die het formulier nodig heeft.</para>
///
/// <para><strong>De etags zijn geen technisch detail dat hier ongelukkig is beland.</strong> Ze
/// horen op dit type omdat het formulier ze moet terugsturen: dat is wat voorkomt dat twee operators
/// die dezelfde kaart openhebben elkaar stil overschrijven. Een formulier zonder etag is een
/// formulier waarvan de laatste verzender wint.</para>
/// </remarks>
public sealed record OperatorContractView
{
    /// <summary>De klantslug.</summary>
    public required string CustomerId { get; init; }

    /// <summary>De klantnaam.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Wanneer deze weergave is opgebouwd.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>Of dit de interne beheerklant is.</summary>
    public required bool IsInternal { get; init; }

    /// <summary>Of er een contract is vastgelegd.</summary>
    public required bool HasContract { get; init; }

    /// <summary>Contractnummer.</summary>
    public string? Number { get; init; }

    /// <summary>Soort contract.</summary>
    public string? Type { get; init; }

    /// <summary>Ingangsdatum.</summary>
    public DateOnly? StartsOn { get; init; }

    /// <summary>Looptijd, als tekst.</summary>
    public string? Term { get; init; }

    /// <summary>Opzegtermijn, als tekst.</summary>
    public string? NoticePeriod { get; init; }

    /// <summary>De SLA in één regel.</summary>
    public string? Sla { get; init; }

    /// <summary>Urenbundel per maand, of <c>null</c> als er niets is vastgelegd.</summary>
    /// <remarks>Zie <see cref="ContractDocument.BundledHours"/>.</remarks>
    public decimal? BundledHours { get; init; }

    /// <summary>Uurtarief buiten de bundel, of <c>null</c> als er niets is vastgelegd.</summary>
    /// <remarks>Zie <see cref="ContractDocument.HourlyRate"/>.</remarks>
    public decimal? HourlyRate { get; init; }

    /// <summary>Indexatie.</summary>
    public string? Indexation { get; init; }

    /// <summary>Contactpersoon bij de klant.</summary>
    public string? Contact { get; init; }

    /// <summary>Beheerd door.</summary>
    public string? ManagedBy { get; init; }

    /// <summary>
    /// Het opslagpercentage op de Azure-kosten, of <c>null</c> als het niet is vastgelegd.
    /// Operator-only (§2).
    /// </summary>
    /// <remarks>
    /// <c>null</c> is hier het gevaarlijkst van de drie: nul opslag en geen afspraak over opslag
    /// zien er in een berekening hetzelfde uit, en het verschil is onze marge. Zie
    /// <see cref="ContractDocument.AzureSurchargePercentage"/>.
    /// </remarks>
    public decimal? AzureSurchargePercentage { get; init; }

    /// <summary>Wanneer het contract voor het laatst is gewijzigd.</summary>
    public DateTimeOffset? ChangedAt { get; init; }

    /// <summary>Wie het voor het laatst heeft gewijzigd.</summary>
    public string? ChangedBy { get; init; }

    /// <summary>
    /// Wanneer het klantdocument voor het laatst is gewijzigd, of <c>null</c> als er nog geen
    /// klantdocument is.
    /// </summary>
    /// <remarks>
    /// Een eigen paar naast <see cref="ChangedAt"/> en <see cref="ChangedBy"/>, want het zijn twee
    /// documenten met elk hun eigen geschiedenis. Het contract van vorige week zegt niets over de
    /// subscription die gisteren is verbeterd, en op een scherm waar twee operators kunnen botsen is
    /// juist dat de aanwijzing dat er iemand anders aan het werk is.
    /// </remarks>
    public DateTimeOffset? CustomerChangedAt { get; init; }

    /// <summary>Wie het klantdocument voor het laatst heeft gewijzigd.</summary>
    public string? CustomerChangedBy { get; init; }

    /// <summary>
    /// De etag van het contract, of <c>null</c> als er nog geen contract is.
    /// </summary>
    /// <remarks>
    /// Gaat mee als <see cref="ContractEdit.BasedOnETag"/>. <c>null</c> betekent "dit contract wordt
    /// aangemaakt" en niet "sla de controle over": ook dan levert een ander die net eerder was een
    /// conflict op.
    /// </remarks>
    public string? ContractETag { get; init; }

    /// <summary>Korte omgevingsaanduiding van de klant.</summary>
    public string? Environment { get; init; }

    /// <summary>De volledige omgeving (subscription · resource group). Operator-only.</summary>
    public string? EnvironmentDetail { get; init; }

    /// <summary>
    /// De Azure-scope waartegen de kosten worden gemeten, of <c>null</c>. Operator-only.
    /// </summary>
    /// <remarks>
    /// Staat hier om dezelfde reden als <see cref="TelemetryEndpoint"/>, en die reden weegt hier
    /// zwaarder: <see cref="Data.IPortalDataStore.SaveCustomerAsync"/> vervangt het hele klantdocument,
    /// dus een veld dat niet op het formulier staat wordt bij het eerste bewaren leeggemaakt. Zou dit
    /// veld hier ontbreken, dan zou een operator die de klantnaam verbetert de kostenmeting van die
    /// klant uitzetten — en dan staat er vanaf de volgende maand "niet ingericht" op het
    /// facturatiescherm zonder dat iemand iets heeft uitgezet. Zie <see cref="Data.AzureScope"/>.
    /// </remarks>
    public string? AzureScope { get; init; }

    /// <summary>
    /// Het DevOps-bord waarvan de sprint wordt gelezen, of <c>null</c>. Operator-only.
    /// </summary>
    /// <remarks>
    /// Staat hier om exact dezelfde reden als <see cref="AzureScope"/> hierboven, en het gevolg van
    /// vergeten is hetzelfde: een operator die de klantnaam verbetert zou de sprintweergave van die klant
    /// uitzetten. Operator-only omdat §2 de koppelingsdetails (MCP/DevOps) aan de operator toewijst en
    /// niet aan de klant; zie <see cref="Sprints.DevOpsScope"/>.
    /// </remarks>
    public string? DevOpsScope { get; init; }

    /// <summary>
    /// De Cosmos-endpoint van de telemetrie van déze klant, of <c>null</c> voor het
    /// standaardaccount. Operator-only.
    /// </summary>
    /// <remarks>
    /// <para>Staat hier omdat het omgevingsblok van het contractscherm hem moet kunnen tonen én
    /// terugsturen. Dat tweede is niet cosmetisch: <see cref="Data.IPortalDataStore.SaveCustomerAsync"/>
    /// vervangt het hele klantdocument, dus een veld dat niet op het formulier staat wordt bij het
    /// eerste bewaren leeggemaakt. Zou dit veld hier ontbreken, dan zou een operator die de klantnaam
    /// verbetert de telemetrie van die klant afsluiten — het overzicht zegt dan "status onbekend" en
    /// niemand weet waardoor.</para>
    ///
    /// <para>Geen geheim: op de accounts staat local auth uit, dus een endpoint is een adres en geen
    /// sleutel. Zie <see cref="Security.CustomerRecord"/>.</para>
    /// </remarks>
    public string? TelemetryEndpoint { get; init; }

    /// <summary>
    /// De databasenaam bij <see cref="TelemetryEndpoint"/>, of <c>null</c> voor de standaardnaam.
    /// Operator-only.
    /// </summary>
    /// <remarks>Zie <see cref="TelemetryEndpoint"/>: hij staat hier om dezelfde reden.</remarks>
    public string? TelemetryDatabase { get; init; }

    /// <summary>
    /// De etag van het klantdocument, of <c>null</c> als deze klant nog niet is gemigreerd.
    /// </summary>
    /// <remarks>
    /// Gaat mee als <see cref="Data.CustomerEdit.BasedOnETag"/>, en om dezelfde reden als
    /// <see cref="ContractETag"/>: het formulier stuurt terug wat er stond toen de operator begon te
    /// typen. Een verse lezing vlak vóór het schrijven zou de wijziging van een ander binnenhalen en
    /// er precies overheen schrijven.
    /// </remarks>
    public string? CustomerETag { get; init; }

    /// <summary>
    /// Of deze klant alleen uit de configuratie komt en nog geen document in de opslag heeft.
    /// </summary>
    /// <remarks>
    /// Zichtbaar op het scherm, want het verklaart waarom er geen wijzigingsgeschiedenis is en het
    /// zegt dat de eenmalige migratie nog niet heeft gelopen. Stil laten zou de operator laten
    /// denken dat de klant een gewone klant is, waarna zijn eerste wijziging het document alsnog
    /// aanmaakt — dat werkt, maar hij hoort te weten wat er gebeurt.
    /// </remarks>
    public required bool IsFromConfigurationOnly { get; init; }

    /// <summary>Wie er namens deze klant toegang heeft.</summary>
    public IReadOnlyList<OperatorAccessRow> Access { get; init; } = [];

    /// <summary>De aanduidingen die te kiezen zijn in het toegangsformulier.</summary>
    /// <remarks>
    /// Komt uit <see cref="PortalAccessRoles.All"/> en niet uit een lijst in de Razor, zodat het
    /// formulier geen waarde kan aanbieden die de schrijfkant weigert.
    /// </remarks>
    public IReadOnlyList<string> Roles { get; init; } = PortalAccessRoles.All;

    /// <summary>
    /// Dat de twee toegangsaanduidingen hetzelfde recht geven. Zie
    /// <see cref="ContractNotice.AccessLabelsAreEqual"/>.
    /// </summary>
    /// <remarks>
    /// Staat er als tekst omdat het anders een verrassing is. "Beheerder klant" klinkt als een
    /// bevoegdheid en is er geen. Het veld heette <c>RoleNotice</c>; die naam gaf het woord terug
    /// dat de tekst zelf niet meer mag bevatten, en een veldnaam is waar de volgende ontwikkelaar
    /// de betekenis vandaan haalt. Zie <see cref="PortalAccessRoles"/>.
    /// </remarks>
    public required string AccessLabelNotice { get; init; }

    /// <summary>
    /// Dat een vastgelegde toegang pas werkt na de handmatige stap in Entra, en dat het portaal niet
    /// kan zien of die is gedaan. Zie <see cref="ContractNotice.EntraStateUnknown"/>.
    /// </summary>
    public required string AccessStateNotice { get; init; }
}

/// <summary>
/// Eén toegangsregel zoals de operator hem ziet en kan intrekken.
/// </summary>
public sealed record OperatorAccessRow
{
    /// <summary>Het e-mailadres.</summary>
    public required string Email { get; init; }

    /// <summary>De naam, of <c>null</c>.</summary>
    public string? Name { get; init; }

    /// <summary>De rol binnen de klant.</summary>
    public required string Role { get; init; }

    /// <summary>Wanneer deze toegang is vastgelegd.</summary>
    public required DateTimeOffset GrantedAt { get; init; }

    /// <summary>Welke operator hem heeft gegeven.</summary>
    public string? GrantedBy { get; init; }

    /// <summary>
    /// Of deze persoon in Entra kan aanmelden.
    /// </summary>
    /// <remarks>
    /// Vandaag altijd <see cref="AccessEntraState.Unknown"/>. Het portaal verstuurt de uitnodiging
    /// niet en gaat dat ook niet doen — zie <see cref="ContractNotice.EntraStateUnknown"/> — en het
    /// heeft (nog) geen leesrecht om te controleren of iemand anders het heeft gedaan. Deze toestand
    /// is dus blijvend onbekend tot dat leesrecht er komt, en niet tijdelijk leeg.
    /// </remarks>
    public required AccessEntraState EntraState { get; init; }

    /// <summary>
    /// De etag van deze regel. Gaat mee bij het intrekken, zodat er niets wordt verwijderd wat
    /// intussen is veranderd.
    /// </summary>
    public string? ETag { get; init; }
}
