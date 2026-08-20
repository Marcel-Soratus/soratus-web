using Soratus.Agents.Contracts;

namespace Soratus.Portal.Views;

/// <summary>
/// Hoeveel agents er in elke status staan.
/// </summary>
/// <param name="Live">Draait, meldt zich op tijd, laatste run geslaagd.</param>
/// <param name="Degraded">Meldt zich langer dan de drempel niet.</param>
/// <param name="Failed">Laatste afgeronde run is mislukt.</param>
/// <param name="Idle">Draait en heeft niets te doen. Geen storing.</param>
/// <param name="Unknown">Geen telemetrie. We weten niets.</param>
/// <remarks>
/// Dit type bestaat om regel 7 af te dwingen: getallen mogen elkaar tussen schermen niet
/// tegenspreken. De KPI-rij bovenaan het overzicht is niet apart geteld maar
/// <see cref="Combine"/> over precies de rijen die eronder staan. "13 live, 1 degraded" en de
/// lijst waar je die veertien agents in ziet zijn dus dezelfde telling; ze kunnen niet uit elkaar
/// lopen zonder dat de lijst zelf verandert.
/// </remarks>
public readonly record struct AgentStatusBreakdown(
    int Live,
    int Degraded,
    int Failed,
    int Idle,
    int Unknown)
{
    /// <summary>Geen agents.</summary>
    public static AgentStatusBreakdown Empty { get; }

    /// <summary>Het totaal aantal agents in deze telling.</summary>
    public int Total => Live + Degraded + Failed + Idle + Unknown;

    /// <summary>
    /// Het aantal agents dat aandacht vraagt: failed plus degraded.
    /// </summary>
    public int Attention => Failed + Degraded;

    /// <summary>
    /// Telt een reeks statussen op.
    /// </summary>
    /// <param name="statuses">De statussen.</param>
    /// <returns>De telling.</returns>
    public static AgentStatusBreakdown FromStatuses(IEnumerable<AgentStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);

        var breakdown = Empty;

        foreach (var status in statuses)
        {
            breakdown = status switch
            {
                AgentStatus.Live => breakdown with { Live = breakdown.Live + 1 },
                AgentStatus.Degraded => breakdown with { Degraded = breakdown.Degraded + 1 },
                AgentStatus.Failed => breakdown with { Failed = breakdown.Failed + 1 },
                AgentStatus.Idle => breakdown with { Idle = breakdown.Idle + 1 },
                _ => breakdown with { Unknown = breakdown.Unknown + 1 },
            };
        }

        return breakdown;
    }

    /// <summary>
    /// Telt tellingen bij elkaar op.
    /// </summary>
    /// <param name="parts">De deeltellingen, bijvoorbeeld één per klant.</param>
    /// <returns>De som.</returns>
    public static AgentStatusBreakdown Combine(IEnumerable<AgentStatusBreakdown> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var total = Empty;

        foreach (var part in parts)
        {
            total = new AgentStatusBreakdown(
                total.Live + part.Live,
                total.Degraded + part.Degraded,
                total.Failed + part.Failed,
                total.Idle + part.Idle,
                total.Unknown + part.Unknown);
        }

        return total;
    }
}
