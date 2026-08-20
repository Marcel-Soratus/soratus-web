using Soratus.Portal.Security;

namespace Soratus.Portal.Data;

/// <summary>
/// De enige toegang tot de telemetrie.
/// </summary>
/// <remarks>
/// <para><strong>Elke methode begint met een scope.</strong> Geen enkele neemt een losse
/// <c>string customerId</c> aan. Een aanroeper die geen scope heeft kan hier niets, en een
/// aanroeper die er wel een heeft, heeft hem via <see cref="ICustomerScopeResolver"/> gekregen en
/// dus met een oordeel erachter. Dat is de reden dat er in deze interface nergens een
/// autorisatievraag staat: die is al beantwoord voordat je hier kon komen.</para>
///
/// <para><strong>De scope bepaalt ook wáár gelezen wordt.</strong> Elke klant krijgt zijn eigen
/// Cosmos-account; de endpoint zit aan <see cref="CustomerScope.Telemetry"/> vast. Er is dus geen
/// aanroep waarmee je met de scope van klant A in de opslag van klant B kijkt. De implementatie
/// filtert daarnáást in elke query op <c>customerId</c> — niet uit wantrouwen, maar omdat in fase 0
/// alle klanten nog één account delen en het filter dan het enige is wat er tussen zit.</para>
///
/// <para>Er is precies één implementatie: <see cref="CosmosAgentTelemetryStore"/>. Geen
/// seed-variant, geen in-memory variant, geen tweede DI-registratie. Seed-data wordt door een
/// apart consoleproject in dezelfde Cosmos gezet, in dezelfde documentvorm; het portaal weet niet
/// dat het seed is en hoort dat ook niet te kunnen weten. Een mocklaag die blijft hangen wordt
/// vanzelf de plek waar het verschil tussen demo en werkelijkheid gaat zitten.</para>
/// </remarks>
public interface IAgentTelemetryStore
{
    /// <summary>
    /// Alle agents van één klant, elk met zijn laatste afgeronde run.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant, en zijn opslag.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De agents, in willekeurige volgorde. Sorteren doet de weergave.</returns>
    /// <remarks>
    /// Bevat alle omgevingen. Het filteren op productie voor de klantweergave gebeurt in de
    /// projectie naar het klantviewmodel, niet hier — anders kan de operator zijn acceptatie-agents
    /// niet zien.
    ///
    /// Werpt als de opslag niet antwoordt. Op een klantpagina is dat het juiste gedrag: één klant
    /// die niet te lezen is, is daar het hele scherm. Voor het overzicht, waar dat juist niet mag,
    /// is er <see cref="GetOverviewAsync"/>.
    /// </remarks>
    Task<IReadOnlyList<AgentSnapshot>> GetAgentsAsync(
        CustomerScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Eén agent van deze klant.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="agentName">De technische naam van de agent.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// De agent, of <c>null</c> als hij niet bestaat óf van een andere klant is. Die twee zijn
    /// bewust hetzelfde antwoord; het scherm hoort er 404 van te maken.
    /// </returns>
    Task<AgentSnapshot?> GetAgentAsync(
        CustomerScope scope,
        string agentName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// De runs van één agent, nieuwste eerst, gepagineerd.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="agentName">De technische naam van de agent.</param>
    /// <param name="pageSize">Hoeveel runs per pagina, of <c>null</c> voor de standaard.</param>
    /// <param name="continuationToken">Het vervolgtoken van de vorige pagina.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De pagina runs.</returns>
    Task<RunPage> GetRunsAsync(
        CustomerScope scope,
        string agentName,
        int? pageSize = null,
        string? continuationToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// De logregels van één agent, nieuwste eerst.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="agentName">De technische naam van de agent.</param>
    /// <param name="query">Niveaufilter, zoekterm, cursor en paginering.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De pagina logregels, met de cursor voor de live tail.</returns>
    Task<LogPage> GetLogsAsync(
        CustomerScope scope,
        string agentName,
        LogQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Telt per agent van deze klant hoeveel runs er in elk tijdblok waren, voor de sparkline.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="window">Het tijdvenster en de blokindeling.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// Per agentnaam een lijst van <see cref="HistogramWindow.BlockCount"/> blokken, oudste eerst.
    /// Agents zonder runs in het venster ontbreken; de aanroeper vult ze aan met lege blokken.
    /// </returns>
    /// <remarks>
    /// <para><strong>Eén query voor de hele klant, niet één per agent.</strong> Dat is het hele
    /// punt van deze methode. Bij twintig agents zou een query per agent twintig query's per
    /// paginaweergave betekenen, en dat is precies de vorm die later pijn doet.</para>
    ///
    /// <para>Het aggregeren gebeurt in Cosmos en niet hier: de query groepeert per agent en per
    /// heel uur, zodat het aantal rijen dat over de lijn komt begrensd is door
    /// <em>agents × 24</em> en niet door het aantal runs. Een agent die elke minuut draait levert
    /// daarmee evenveel rijen op als een agent die eens per uur draait. Een platte projectie van
    /// alle runs is bij de huidige seed-data goedkoper, maar schaalt lineair met het aantal runs en
    /// loopt bij een minuutagent op naar duizenden rijen per dag.</para>
    /// </remarks>
    Task<IReadOnlyDictionary<string, IReadOnlyList<RunBucket>>> GetRunHistogramAsync(
        CustomerScope scope,
        HistogramWindow window,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Telt de runs van één klant vanaf een moment, uitgesplitst naar afloop.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="since">Het begin van het venster.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De telling.</returns>
    Task<RunTally> CountRunsAsync(
        CustomerScope scope,
        DateTimeOffset since,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Haalt voor elke klant zijn agents en runtellingen op, voor het Soratus-overzicht.
    /// </summary>
    /// <param name="scope">Het operatorrecht, met daarin een leesrecht per klant.</param>
    /// <param name="todayStartedAt">Het begin van "vandaag" op het scherm.</param>
    /// <param name="last24HoursStartedAt">Het begin van het venster van 24 uur.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Eén resultaat per klant uit <see cref="OperatorScope.Customers"/>.</returns>
    /// <remarks>
    /// <para>Dit is een fan-out over evenveel Cosmos-accounts als er klanten zijn, met begrensde
    /// parallelliteit en een tijdslimiet per klant. <strong>Een klant die niet antwoordt verdwijnt
    /// niet</strong>: hij komt terug met <see cref="CustomerTelemetry.Unavailable"/> gevuld, zodat
    /// het scherm hem als "status onbekend" kan tonen. Een overzicht met een gat erin is beter dan
    /// een overzicht dat een storing verbergt.</para>
    ///
    /// <para>De tellingen komen mee in dezelfde fan-out in plaats van uit een aparte aanroep, zodat
    /// de KPI-rij en de klantenlijst uit één en dezelfde leesactie komen. Twee leesacties zouden
    /// twee momenten zijn, en twee momenten spreken elkaar op een dag tegen.</para>
    /// </remarks>
    Task<IReadOnlyList<CustomerTelemetry>> GetOverviewAsync(
        OperatorScope scope,
        DateTimeOffset todayStartedAt,
        DateTimeOffset last24HoursStartedAt,
        CancellationToken cancellationToken = default);
}
