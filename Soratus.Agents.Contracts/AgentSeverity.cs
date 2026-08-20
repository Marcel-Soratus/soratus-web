namespace Soratus.Agents.Contracts;

/// <summary>
/// Wat het overzicht van één agent nodig heeft: zijn status en wanneer hij voor het laatst iets
/// van zich liet horen.
/// </summary>
/// <param name="Status">De afgeleide status van deze agent.</param>
/// <param name="LastActivityAt">
/// Het laatste moment waarop deze agent iets deed of zich meldde, of <c>null</c> als er niets
/// bekend is.
/// </param>
/// <remarks>
/// Bewust geen kopie van <see cref="AgentRegistration"/>. Dit is het gereduceerde beeld waarop
/// het overzicht sorteert; alles wat je hier extra in zet, moet het overzicht ophalen zonder het
/// te gebruiken.
/// </remarks>
public readonly record struct AgentSeverity(AgentStatus Status, DateTimeOffset? LastActivityAt)
{
    /// <summary>
    /// Reduceert de gepubliceerde feiten van één agent tot status plus laatste activiteit.
    /// </summary>
    /// <param name="registration">Het registratiedocument, of <c>null</c>.</param>
    /// <param name="lastCompletedRun">De laatste afgeronde run, of <c>null</c>.</param>
    /// <param name="now">Het moment waarop wordt geoordeeld.</param>
    /// <returns>Het gereduceerde beeld van deze agent.</returns>
    /// <remarks>
    /// "Laatste activiteit" is het jongste van de hartslag en het einde van de laatste run. De
    /// hartslag telt mee omdat een agent die niets te doen had wél leeft, en het einde van de run
    /// telt mee omdat een run die net klaar is recenter kan zijn dan de hartslag ervoor. Loopt de
    /// run nog, dan geldt zijn starttijd — die is een echt moment, in tegenstelling tot een
    /// eindtijd die er nog niet is.
    /// </remarks>
    public static AgentSeverity From(
        AgentRegistration? registration,
        RunRecord? lastCompletedRun,
        DateTimeOffset now)
    {
        var status = AgentStatusCalculator.Calculate(registration, lastCompletedRun, now);
        DateTimeOffset? runActivity = lastCompletedRun is null
            ? null
            : lastCompletedRun.FinishedAt ?? lastCompletedRun.StartedAt;

        return new AgentSeverity(status, Latest(registration?.LastHeartbeatAt, runActivity));
    }

    /// <summary>Het jongste van twee momenten, waarbij <c>null</c> niets bijdraagt.</summary>
    internal static DateTimeOffset? Latest(DateTimeOffset? left, DateTimeOffset? right) =>
        (left, right) switch
        {
            (null, null) => null,
            (null, var r) => r,
            (var l, null) => l,
            var (l, r) => l > r ? l : r,
        };
}
