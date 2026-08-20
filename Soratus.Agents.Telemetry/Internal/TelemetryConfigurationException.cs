namespace Soratus.Agents.Telemetry.Internal;

/// <summary>
/// De opslag klopt niet: een ontbrekende database of container, of ontbrekende rechten.
/// </summary>
/// <remarks>
/// Apart van gewone schrijffouten, omdat dit nooit vanzelf overgaat. Opnieuw proberen heeft
/// geen zin en zou de echte oorzaak begraven onder drie identieke waarschuwingen.
/// </remarks>
internal sealed class TelemetryConfigurationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
