using Soratus.Agents.Contracts;
using Soratus.Portal.Data;

namespace Soratus.Portal.Views;

/// <summary>
/// Het agentdetail zoals de klant het ziet (§3.3).
/// </summary>
/// <remarks>
/// Dezelfde regel als bij <see cref="CustomerAgentsView"/>: wat de klant niet mag zien staat er
/// niet als leeg veld, het staat er helemaal niet. Er is geen <c>Environment</c>, geen
/// subscription, geen resource group en geen contractversie.
///
/// Deze weergave wordt alleen opgebouwd voor een agent in de productieomgeving. Vraagt een klant
/// het detail van een acceptatie-agent op, dan komt er <c>null</c> uit en hoort het scherm 404 te
/// geven — hetzelfde antwoord als bij een agent die niet bestaat.
/// </remarks>
public sealed record CustomerAgentDetailView
{
    /// <summary>De slug van de klant.</summary>
    public required string CustomerId { get; init; }

    /// <summary>De klantnaam.</summary>
    public required string CustomerDisplayName { get; init; }

    /// <summary>Wanneer deze weergave is opgebouwd.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>De agent zelf, in dezelfde vorm als in de lijst.</summary>
    public required CustomerAgentRow Agent { get; init; }
}

/// <summary>
/// Het agentdetail zoals de operator het ziet.
/// </summary>
/// <remarks>
/// Een apart type, om dezelfde reden als <see cref="OperatorCustomerAgentsView"/>: het verschil
/// tussen de rollen is een verschil tussen typen, niet tussen vlaggen op één type.
/// </remarks>
public sealed record OperatorAgentDetailView
{
    /// <summary>De slug van de klant.</summary>
    public required string CustomerId { get; init; }

    /// <summary>De klantnaam.</summary>
    public required string CustomerDisplayName { get; init; }

    /// <summary>Korte omgevingsaanduiding van de klant.</summary>
    public string? Environment { get; init; }

    /// <summary>
    /// De volledige omgeving van de klant, bijvoorbeeld <c>sub-soratus-acme · rg-acme-prod</c>.
    /// Operator-only.
    /// </summary>
    public string? EnvironmentDetail { get; init; }

    /// <summary>Wanneer deze weergave is opgebouwd.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>De agent zelf, in dezelfde vorm als in de operatorlijst.</summary>
    public required OperatorAgentRow Agent { get; init; }

    /// <summary>
    /// Waar de telemetrie van deze klant staat, als tekst voor de read-only configuratiekaart
    /// (§3.3). Alleen de endpoint en de database; er is geen sleutel om te tonen.
    /// </summary>
    public required string TelemetryLocation { get; init; }

    /// <summary>
    /// Hoe lang logregels bewaard blijven, voor op de configuratiekaart.
    /// </summary>
    /// <remarks>
    /// Doorverwijzing naar <see cref="TelemetryRetention"/> en geen tweede getal: het
    /// configuratietabblad leest dezelfde waarde, en twee plekken met een bewaartermijn zijn twee
    /// plekken die uit de pas kunnen gaan lopen.
    /// </remarks>
    public static TimeSpan LogRetention => TelemetryRetention.Logs;

    /// <summary>Hoe lang runs bewaard blijven.</summary>
    public static TimeSpan RunRetention => TelemetryRetention.Runs;

    /// <summary>
    /// De drempel waarboven stilte als <see cref="AgentStatus.Degraded"/> geldt, voor de
    /// statusspecifieke melding.
    /// </summary>
    public static TimeSpan DegradedThreshold => AgentStatusThresholds.Degraded;
}
