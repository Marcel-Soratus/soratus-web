using System.ComponentModel.DataAnnotations;

namespace Soratus.Portal.Data;

/// <summary>
/// De configuratiesectie <c>Telemetry</c>: waar de telemetrie standaard staat.
/// </summary>
/// <remarks>
/// <para>Dit is de <em>standaard</em>locatie, niet dé locatie. Elke klant mag in de sectie
/// <c>Portal:Customers</c> een eigen <c>TelemetryEndpoint</c> opgeven; wie dat niet doet valt hier
/// op terug. In fase 0 bestaat er één account en staat er dus alleen hier iets. Zodra klanten hun
/// eigen Cosmos-account krijgen, groeit de configuratie mee en verandert er geen regel leescode.
/// </para>
///
/// <para>Er staat bewust geen sleutel en geen connection string in. Op de accounts is local auth
/// uitgeschakeld — accountsleutels bestaan niet — en de verbinding loopt via de user-assigned
/// managed identity van de app, die <c>DefaultAzureCredential</c> oppikt uit
/// <c>AZURE_CLIENT_ID</c>.</para>
///
/// <para>Containernamen staan hier niet: die liggen vast in het contract (<c>agents</c>,
/// <c>runs</c>, <c>logs</c>, partitiesleutelpad <c>/pk</c>) en zijn geen knop. Zie
/// <see cref="CosmosContainerNames"/>.</para>
/// </remarks>
public sealed class PortalTelemetryOptions
{
    /// <summary>De naam van de configuratiesectie.</summary>
    public const string SectionName = "Telemetry";

    /// <summary>
    /// De standaard Cosmos-endpoint, bijvoorbeeld
    /// <c>https://cosmos-soratus-prod.documents.azure.com:443/</c>. Mag leeg zijn zodra elke klant
    /// zijn eigen endpoint heeft.
    /// </summary>
    public string? AccountEndpoint { get; set; }

    /// <summary>De standaard databasenaam.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Telemetry:Database ontbreekt.")]
    public string Database { get; set; } = "telemetry";

    /// <summary>
    /// Hoeveel logregels en runs één pagina bevat als de aanroeper niets opgeeft.
    /// </summary>
    [Range(1, 1000)]
    public int DefaultPageSize { get; set; } = 100;

    /// <summary>
    /// Hoeveel klantopslagen het overzicht tegelijk bevraagt.
    /// </summary>
    /// <remarks>
    /// Begrensd omdat elke klant straks een eigen account is: zonder grens opent het overzicht bij
    /// vijftig klanten vijftig gelijktijdige verbindingen, en dan is de traagste klant de snelheid
    /// van het scherm.
    /// </remarks>
    [Range(1, 64)]
    public int OverviewParallelism { get; set; } = 8;

    /// <summary>
    /// Hoe lang het overzicht op één klantopslag wacht voordat het die als onbereikbaar noteert.
    /// </summary>
    /// <remarks>
    /// Eén trage klant mag het hele overzicht niet ophouden. De klant verdwijnt daarbij niet uit
    /// de lijst — hij komt erin te staan als "status onbekend", met de reden erbij.
    /// </remarks>
    [Range(1, 120)]
    public int OverviewTimeoutSeconds { get; set; } = 10;
}

/// <summary>
/// De containernamen uit het contract.
/// </summary>
/// <remarks>
/// Vaste waarden en geen configuratie: de telemetriebibliotheek schrijft naar deze namen, het
/// seed-project schrijft naar deze namen, en een portaal dat ergens anders kijkt leest een lege
/// database in plaats van een fout te melden.
/// </remarks>
public static class CosmosContainerNames
{
    /// <summary>Eén <c>AgentRegistration</c> per agent.</summary>
    public const string Agents = "agents";

    /// <summary><c>RunRecord</c>-documenten, TTL 400 dagen.</summary>
    public const string Runs = "runs";

    /// <summary><c>LogRecord</c>-documenten, TTL 30 dagen.</summary>
    public const string Logs = "logs";
}
