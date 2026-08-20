namespace Soratus.Agents.Contracts;

/// <summary>
/// Ordent klanten zoals het overzicht ze toont: ernstigste status eerst, bij gelijke status de
/// meest recente activiteit eerst.
/// </summary>
/// <remarks>
/// De vergelijking is omgekeerd aan de natuurlijke ordening van de getallen: een hogere
/// <see cref="AgentStatus"/> komt eerder. Daarom is dit een aparte comparer en geen
/// <see cref="IComparable{T}"/> op <see cref="CustomerSeverity"/> — een natuurlijke ordening
/// waarin "failed" kleiner is dan "idle" verrast iedereen die hem per ongeluk gebruikt.
///
/// Een klant zonder activiteit komt achteraan binnen zijn statusgroep. Dat is de eerlijke plek:
/// bij gelijke ernst zegt "wanneer gebeurde er voor het laatst iets" iets over urgentie, en "nooit
/// iets" is dan het minst urgent.
///
/// Idle (rang 1) tilt een klant nooit boven live (rang 2) uit, en een klant zonder agents (rang 0)
/// nooit boven een klant met agents. Dat volgt rechtstreeks uit de enum-waarden; wijzig die
/// getallen niet zonder deze sortering opnieuw te beoordelen.
/// </remarks>
public sealed class CustomerSeverityComparer : IComparer<CustomerSeverity>
{
    /// <summary>
    /// Vergelijkt twee klantbeelden voor de schermvolgorde.
    /// </summary>
    /// <param name="x">Het eerste klantbeeld.</param>
    /// <param name="y">Het tweede klantbeeld.</param>
    /// <returns>
    /// Negatief als <paramref name="x"/> hoger in de lijst hoort, positief als hij lager hoort,
    /// nul als de twee op ernst en recentheid niet te scheiden zijn.
    /// </returns>
    public int Compare(CustomerSeverity x, CustomerSeverity y)
    {
        var bySeverity = y.Status.CompareTo(x.Status);
        if (bySeverity != 0)
        {
            return bySeverity;
        }

        return (x.LastActivityAt, y.LastActivityAt) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            var (left, right) => right!.Value.CompareTo(left!.Value),
        };
    }
}
