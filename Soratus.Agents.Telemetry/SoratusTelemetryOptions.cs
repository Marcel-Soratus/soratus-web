using Soratus.Agents.Contracts;

namespace Soratus.Agents.Telemetry;

/// <summary>
/// De knoppen van de telemetriebibliotheek. Alles heeft een werkbare standaardwaarde; een
/// agentbouwer hoeft hier normaal niets aan te raken.
/// </summary>
/// <remarks>
/// Er staat bewust geen sleutel of connection string in. De verbinding loopt via
/// <see cref="Endpoint"/> en een <c>TokenCredential</c> (managed identity), zodat er nooit een
/// geheim in configuratie of code terechtkomt.
///
/// De bibliotheek maakt database noch containers aan, en stuurt ook geen <c>ttl</c> mee.
/// Retentie is een eigenschap van de container (<c>DefaultTimeToLive</c>) en wordt centraal
/// ingericht. Een ontbrekende container is een inrichtingsfout die zichtbaar hoort te zijn,
/// niet iets dat een agent stilletjes repareert met de verkeerde bewaartermijn.
/// </remarks>
public sealed class SoratusTelemetryOptions
{
    /// <summary>De Cosmos-endpoint, bijvoorbeeld <c>https://xyz.documents.azure.com:443/</c>.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Naam van de database.</summary>
    public string Database { get; set; } = "telemetry";

    /// <summary>
    /// Container met één <c>AgentRegistration</c> per agent, partitiesleutelpad <c>/pk</c>,
    /// zonder TTL.
    /// </summary>
    public string AgentsContainer { get; set; } = "agents";

    /// <summary>
    /// Container met <c>RunRecord</c>-documenten, partitiesleutelpad <c>/pk</c>, TTL 400 dagen.
    /// </summary>
    public string RunsContainer { get; set; } = "runs";

    /// <summary>
    /// Container met <c>LogRecord</c>-documenten, partitiesleutelpad <c>/pk</c>, TTL 30 dagen.
    /// </summary>
    public string LogsContainer { get; set; } = "logs";

    /// <summary>
    /// Hoeveel logregels in de buffer passen. Loopt de buffer vol, dan vallen regels weg —
    /// een agent wordt nooit geblokkeerd door telemetrie.
    /// </summary>
    public int LogBufferCapacity { get; set; } = 10_000;

    /// <summary>
    /// Hoeveel runs en registraties in de buffer passen. Klein, want deze documenten zijn
    /// zeldzaam; ze wegen wel zwaarder, dus ze hebben een eigen buffer die niet door een
    /// logstorm kan worden volgedrukt.
    /// </summary>
    public int DocumentBufferCapacity { get; set; } = 1_000;

    /// <summary>
    /// Hoeveel logregels maximaal in één Cosmos-batch gaan. Wordt begrensd op 100, de
    /// harde grens van een transactional batch.
    /// </summary>
    public int LogBatchSize { get; set; } = 50;

    /// <summary>
    /// Hoe lang de schrijver op meer logregels wacht voordat hij een batch wegschrijft.
    /// Korter betekent snellere live tail en meer schrijfacties.
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Hoeveel keer een mislukte schrijfactie opnieuw wordt geprobeerd.</summary>
    public int WriteRetries { get; set; } = 3;

    /// <summary>De wachttijd voor de eerste nieuwe poging; verdubbelt daarna.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Hoe lang bij afsluiten op een lege buffer wordt gewacht. Kort: een uitrol mag niet
    /// blijven hangen op telemetrie.
    /// </summary>
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Ruime hygiënegrens op de lengte van <c>msg</c>, tegen één absurd lange ononderbroken regel.
    /// </summary>
    /// <remarks>
    /// Dit is <em>niet</em> het mechanisme dat interne details uit <c>msg</c> houdt. Dat doet de
    /// knip op de eerste regelovergang: <c>msg</c> is één zin en een zin bevat geen
    /// regelafbreking. Een lengtegrens is daar gemeten ongeschikt voor — de langste legitieme
    /// eerste regel in de opslag was 1417 tekens, dus elke grens die een stacktrace tegenhoudt
    /// verminkt ook geldig proza. Deze grens staat daarom ruim en gaat in de praktijk nooit af.
    /// Slaat hij toch toe, dan wordt er op een grafeemgrens geknipt en gaat de rest naar dezelfde
    /// sleutel in <c>extra</c>.
    /// </remarks>
    public int MaxMessageLength { get; set; } = MessageTruncation.DefaultMaxLength;

    /// <summary>Maximale lengte van de geserialiseerde <c>extra</c>-JSON.</summary>
    public int MaxExtraLength { get; set; } = 131_072;
}
