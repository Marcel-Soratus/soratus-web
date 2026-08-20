namespace Soratus.Mcp.Uren;

/// <summary>
/// De knoppen van de MCP-server <c>soratus-uren</c>.
/// </summary>
/// <remarks>
/// <para>Er staat bewust geen sleutel, wachtwoord of connection string in. De server praat met het
/// portaal over een bearer-token dat via <c>DefaultAzureCredential</c> op de identiteit van de
/// aanroeper wordt opgehaald; op de accounts staat local auth uit, dus er is geen sleutel om op
/// terug te vallen en er hoort er ook geen te zijn.</para>
///
/// <para>Alles wordt gelezen uit omgevingsvariabelen met het voorvoegsel <c>SORATUS_UREN__</c>,
/// dezelfde vorm als <c>SORATUS_AGENT__</c> en <c>SORATUS_TELEMETRY__</c> in
/// <c>Soratus.Agents.Telemetry</c>. Eén conventie in de repo, zodat niemand hoeft te raden of het
/// nu een sectie of een platte sleutel is.</para>
/// </remarks>
public sealed class UrenOptions
{
    /// <summary>
    /// De basis-URL van het portaal, bijvoorbeeld <c>https://portal.soratus.com</c>.
    /// </summary>
    /// <remarks>
    /// Zonder pad en zonder querystring. Een querystring op een basis-URL is bijna altijd een
    /// SAS-token of een sleutel die iemand erin heeft geplakt, en dat is precies wat hier niet
    /// hoort; <see cref="UrenConfiguration"/> weigert het daarom.
    /// </remarks>
    public Uri? PortalBaseAddress { get; set; }

    /// <summary>
    /// De Entra-scope waarvoor een token wordt opgehaald, bijvoorbeeld
    /// <c>api://soratus-portal/.default</c>.
    /// </summary>
    /// <remarks>
    /// Moet op <c>/.default</c> eindigen: dan vraagt de aanmelding alles waarvoor deze client
    /// statisch toestemming heeft, en is de toestemming een eigenschap van de registratie in plaats
    /// van van de aanroep. Een losse scope (<c>…/Uren.Boeken</c>) zou hier ook kunnen, maar dan
    /// bepaalt configuratie op een machine welk recht er wordt gevraagd, en dat is precies de knop
    /// die je niet buiten de tenant wilt hebben.
    /// </remarks>
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// De app-id van de eigen public-client-registratie waarmee wordt aangemeld.
    /// </summary>
    /// <remarks>
    /// Een eigen client, en expliciet niet die van de Azure CLI. Zie <see cref="UrenCredentials"/>
    /// voor waarom dat verschil de hele reden van deze eigenschap is.
    /// </remarks>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>De tenant waarin wordt aangemeld.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Waar de bewaarde aanmelding staat.
    /// </summary>
    /// <remarks>
    /// Dit bestand is geen token en geen geheim — het draagt de gebruikersnaam, de tenant en de
    /// account-id, zodat de credential weet welk account hij in de versleutelde tokencache moet
    /// zoeken. De tokens zelf staan in die cache, die door het besturingssysteem wordt versleuteld.
    /// </remarks>
    public string AuthenticationRecordPath { get; set; } = DefaultRecordPath();

    /// <summary>
    /// De klanten waarvoor deze installatie mag boeken, of leeg voor "geen extra beperking".
    /// </summary>
    /// <remarks>
    /// <para><strong>Dit is geen veiligheidsgrens.</strong> Deze lijst staat op de machine van de
    /// aanroeper en is dus door de aanroeper te wijzigen. De echte grens is de app-rol
    /// <c>Operator</c> die het portaal op het bearer-token controleert — zie
    /// <see cref="PortalUrenClient"/>.</para>
    ///
    /// <para>Waarvoor hij dan wél is: het beperkt de schade van een verkeerd geraden of verkeerd
    /// getypte klantslug, en van een klantnaam die uit een gelezen bestand of een webpagina in een
    /// gesprek is beland. Een tool die voor élke klant kan boeken is een ander risico dan een die
    /// aan één omgeving hangt, en op een ontwikkelmachine is dat verschil gratis.</para>
    /// </remarks>
    public IReadOnlyList<string> AllowedCustomers { get; set; } = [];

    /// <summary>Hoe lang op het portaal wordt gewacht voordat het verzoek wordt opgegeven.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Proefdraaien: valideren en tonen wat er verstuurd zou worden, zonder iets te versturen.
    /// </summary>
    /// <remarks>
    /// Bedoeld om de server te kunnen draaien voordat het endpoint op het portaal bestaat, en om
    /// een aanroep te kunnen nakijken zonder een regel achter te laten die iemand moet opruimen.
    /// De melding die de aanroeper terugkrijgt begint in dit geval met <c>PROEFDRAAI</c>; een
    /// proefdraai die eruitziet als een boeking zou erger zijn dan geen proefdraai.
    ///
    /// <para><strong>Proefdraaien slaat het tokenpad over, en dat is een gat dat apart gedicht is.</strong>
    /// Zou dit de enige stand zijn waarin de server kan draaien voordat het endpoint bestaat, dan is
    /// de aanmelding het enige stuk dat nooit heeft gelopen — en dat is precies het stuk dat bij de
    /// eerste echte poging faalt. Daarvoor bestaat <c>soratus-uren controleer</c>: dat haalt een
    /// echt token en zegt wat erin staat, zonder iets te boeken.</para>
    /// </remarks>
    public bool DryRun { get; set; }

    /// <summary>
    /// De standaardplek van de bewaarde aanmelding, in de gebruikersmap.
    /// </summary>
    /// <returns>Het volledige pad.</returns>
    private static string DefaultRecordPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "soratus-uren",
        "aanmelding.json");
}
