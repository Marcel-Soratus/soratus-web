using Soratus.Agents.Contracts;
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
    /// Alleen het registratiedocument van één agent van deze klant, zonder zijn laatste run.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="agentName">De technische naam van de agent.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// De registratie, of <c>null</c> als de agent niet bestaat óf van een andere klant is. Die
    /// twee zijn bewust hetzelfde antwoord.
    /// </returns>
    /// <remarks>
    /// Bestaat naast <see cref="GetAgentAsync"/> omdat de tabbladen op het agentdetail alleen willen
    /// weten of deze agent zichtbaar mag zijn en op welke omgeving hij draait. De laatste afgeronde
    /// run erbij ophalen is voor die vraag een tweede query die niemand leest. Dit is één point read
    /// — de goedkoopste leesactie die Cosmos kent — en daarmee is de zichtbaarheidscontrole per
    /// tabblad goedkoop genoeg om hem niet over te slaan.
    /// </remarks>
    Task<AgentRegistration?> GetRegistrationAsync(
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
    /// Telt de logregels van één agent per niveau, voor de filterchips.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="agentName">De technische naam van de agent.</param>
    /// <param name="query">
    /// Hetzelfde filter als aan <see cref="GetLogsAsync"/> is meegegeven.
    /// <see cref="LogQuery.Levels"/> wordt genegeerd — zie de opmerkingen.
    /// </param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De telling per niveau.</returns>
    /// <remarks>
    /// <para><strong>Het niveaufilter wordt bewust genegeerd.</strong> Zou het meedoen, dan telt
    /// elke chip alleen zichzelf: met alleen "error" aan zou er "info 0, warn 0, error 3" staan en
    /// zou de lezer niet kunnen zien dat er iets te vinden is als hij "warn" aanzet. De zoekterm,
    /// de runId en de bovengrens doen wél mee, want die gelden voor de hele tabel. Het resultaat is
    /// dat het aanzetten van een chip precies het aantal regels oplevert dat erop stond.</para>
    ///
    /// <para>Eén query met <c>GROUP BY c.level</c>, en niet drie tellingen. Gemeten op de echte
    /// opslag: 3,30 RU tegen 3,12 RU voor één losse telling — drie losse tellingen zouden dus ruim
    /// het drievoudige kosten en bovendien drie momenten zijn.</para>
    ///
    /// <para>Dit is een telling over álle bewaarde regels binnen het filter, niet over de
    /// zichtbare pagina. Bij de bewaartermijn van dertig dagen groeit de kost daarvan mee met wat
    /// een agent in die dertig dagen heeft geschreven; wordt dat een probleem, dan is een
    /// tijdvenster op de hele logweergave het antwoord en niet een telling die iets anders
    /// omvat dan de lijst.</para>
    /// </remarks>
    Task<LogLevelTally> CountLogLevelsAsync(
        CustomerScope scope,
        string agentName,
        LogQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// De logregels die er ná de cursor bij zijn gekomen. Dit is de live tail.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="agentName">De technische naam van de agent.</param>
    /// <param name="query">De cursor en dezelfde filters als de tabel; zie <see cref="LogQuery.Tail"/>.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// De nieuwe regels, oudste eerst, met de doorgeschoven cursor. Bij niets nieuws een leeg
    /// antwoord met dezelfde cursor erin — nooit <c>null</c>.
    /// </returns>
    /// <remarks>
    /// <para><strong>Geen regel twee keer.</strong> De cursor is een paar van tijdstempel en ULID,
    /// en de vergelijking is <c>ts &gt; @since OR (ts = @since AND id &gt; @sinceId)</c>. Met
    /// alleen een tijdstempel moet je kiezen tussen een regel overslaan en er eentje dubbel tonen;
    /// dit is precies gemeten tegen de echte opslag, waar een cursor op de tijd alleen zijn eigen
    /// regel opnieuw meelevert.</para>
    ///
    /// <para><strong>Geen regel overgeslagen.</strong> De query sorteert oplopend en de cursor
    /// schuift door naar de laatste regel die daadwerkelijk is meegegeven. Zie de opmerkingen bij
    /// <see cref="LogTail"/> voor waarom aflopend sorteren hier regels zou laten verdwijnen.</para>
    /// </remarks>
    Task<LogTail> TailLogsAsync(
        CustomerScope scope,
        string agentName,
        LogTailQuery query,
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
