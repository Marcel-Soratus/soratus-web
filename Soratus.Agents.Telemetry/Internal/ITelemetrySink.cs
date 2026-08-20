using Soratus.Agents.Contracts;

namespace Soratus.Agents.Telemetry.Internal;

/// <summary>
/// De schrijfkant naar de opslag. Apart gezet zodat de bufferlaag getest kan worden zonder
/// Cosmos, en zodat een andere opslag later niets in de rest van de bibliotheek raakt.
/// </summary>
internal interface ITelemetrySink
{
    /// <summary>Overschrijft het registratiedocument van deze agent.</summary>
    Task UpsertRegistrationAsync(AgentRegistration registration, CancellationToken cancellationToken);

    /// <summary>Schrijft of overschrijft één run.</summary>
    Task UpsertRunAsync(RunRecord run, CancellationToken cancellationToken);

    /// <summary>Schrijft een batch logregels weg.</summary>
    Task WriteLogsAsync(IReadOnlyList<LogRecord> logs, CancellationToken cancellationToken);
}
