namespace Soratus.Agents.Contracts;

/// <summary>
/// De drempels waarop stilte van een agent als probleem geldt.
/// </summary>
/// <remarks>
/// Deze staan op één plek omdat het scherm en de storingsmelder dezelfde grens moeten
/// hanteren. Lopen ze uiteen, dan meldt de melder iets dat het scherm niet toont, of
/// andersom — precies de tegenspraak tussen schermen die regel 7 verbiedt.
/// </remarks>
public static class AgentStatusThresholds
{
    /// <summary>
    /// Vanaf deze stilte geldt een agent als <see cref="AgentStatus.Degraded"/>.
    /// </summary>
    public static readonly TimeSpan Degraded = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Vanaf deze stilte stuurt de storingsmelder een bericht. Ruimer dan
    /// <see cref="Degraded"/>, zodat een korte hapering geen mail oplevert.
    /// </summary>
    public static readonly TimeSpan Alert = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Hoe vaak de telemetriebibliotheek een hartslag wegschrijft. Ruim onder
    /// <see cref="Degraded"/>, zodat één gemiste schrijfactie nog geen storing is.
    /// </summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
}
