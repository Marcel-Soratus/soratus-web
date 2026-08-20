namespace Soratus.Agents.Contracts;

/// <summary>
/// Het beeld van één klant op het overzicht: de ernstigste status van zijn agents en het meest
/// recente moment waarop een van die agents iets deed.
/// </summary>
/// <param name="Status">
/// De ernstigste status binnen de verzameling agents. <see cref="AgentStatus.Unknown"/> (rang 0)
/// voor een klant zonder agents.
/// </param>
/// <param name="LastActivityAt">
/// De jongste activiteit over alle agents heen, of <c>null</c> als er geen agent iets bekend
/// heeft gemaakt.
/// </param>
/// <param name="AgentCount">Het aantal agents dat is meegerekend.</param>
/// <remarks>
/// Deze rekenregel staat hier en niet in het portaal, omdat de tests en het portaal dezelfde
/// sortering moeten opleveren. "Ernstigste" is puur het maximum van de enum-waarde: die waarde
/// <em>is</em> de ernstrang, dus er is geen aparte tabel die uit de pas kan lopen met
/// <see cref="AgentStatus"/>.
/// </remarks>
public readonly record struct CustomerSeverity(
    AgentStatus Status,
    DateTimeOffset? LastActivityAt,
    int AgentCount)
{
    /// <summary>
    /// Het beeld van een klant zonder agents: niets bekend, geen activiteit, rang 0.
    /// </summary>
    /// <remarks>
    /// Bewust <see cref="AgentStatus.Unknown"/> en niet <see cref="AgentStatus.Idle"/>. Een net
    /// aangesloten klant is geen rustende klant, en rang 0 houdt hem onder elke klant waarover we
    /// wél iets weten — ook onder een klant die alleen maar idle agents heeft.
    /// </remarks>
    public static CustomerSeverity None { get; } = new(AgentStatus.Unknown, null, 0);

    /// <summary>
    /// Vat een verzameling agents samen tot één klantbeeld.
    /// </summary>
    /// <param name="agents">De agents van deze klant. Leeg mag.</param>
    /// <returns>Het samengevatte beeld, of <see cref="None"/> bij een lege verzameling.</returns>
    public static CustomerSeverity FromAgents(IEnumerable<AgentSeverity> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);

        var status = AgentStatus.Unknown;
        DateTimeOffset? lastActivity = null;
        var count = 0;

        foreach (var agent in agents)
        {
            count++;

            if (agent.Status > status)
            {
                status = agent.Status;
            }

            lastActivity = AgentSeverity.Latest(lastActivity, agent.LastActivityAt);
        }

        return count == 0 ? None : new CustomerSeverity(status, lastActivity, count);
    }

    /// <summary>
    /// Sorteert op ernst, dan op recentheid: het probleem dat aandacht vraagt komt bovenaan.
    /// </summary>
    /// <remarks>
    /// Zie <see cref="CustomerSeverityComparer"/> voor de precieze ordening en de motivatie.
    /// </remarks>
    public static IComparer<CustomerSeverity> SeverityFirst { get; } = new CustomerSeverityComparer();

    /// <summary>
    /// Sorteert willekeurige rijen op hun klantbeeld, ernst eerst.
    /// </summary>
    /// <typeparam name="T">Het rijtype van het overzicht.</typeparam>
    /// <param name="rows">De rijen.</param>
    /// <param name="severity">Hoe je uit een rij zijn klantbeeld haalt.</param>
    /// <returns>De rijen in schermvolgorde. Stabiel, dus rijen die volledig gelijk uitkomen
    /// houden hun oorspronkelijke onderlinge volgorde en springen niet van plek bij het
    /// verversen.</returns>
    public static IOrderedEnumerable<T> Sort<T>(
        IEnumerable<T> rows,
        Func<T, CustomerSeverity> severity)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(severity);

        return rows.OrderBy(severity, SeverityFirst);
    }
}
