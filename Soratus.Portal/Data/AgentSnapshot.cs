using Soratus.Agents.Contracts;

namespace Soratus.Portal.Data;

/// <summary>
/// Wat er van één agent bekend is: zijn registratie en zijn laatste afgeronde run.
/// </summary>
/// <param name="Registration">Wat de agent over zichzelf publiceert.</param>
/// <param name="LastCompletedRun">
/// De laatste run die niet meer op <see cref="RunResult.Running"/> staat, of <c>null</c> als er
/// nog geen run is afgerond.
/// </param>
/// <remarks>
/// Dit is precies het paar waar <see cref="AgentStatusCalculator"/> om vraagt, en dat is geen
/// toeval: de store levert feiten, het oordeel valt in de contractbibliotheek, en het scherm
/// leest alleen af. Zo kan het portaal niet tot een andere status komen dan de storingsmelder.
///
/// Er zit geen status in dit type. Zodra hij erin zou zitten, is er een tweede plek waar status
/// kan ontstaan en dus een tweede plek die uit de pas kan lopen.
/// </remarks>
public sealed record AgentSnapshot(AgentRegistration Registration, RunRecord? LastCompletedRun)
{
    /// <summary>De naam van de agent.</summary>
    public string AgentName => Registration.AgentName;

    /// <summary>De klant waar deze agent voor draait.</summary>
    public string CustomerId => Registration.CustomerId;

    /// <summary>
    /// Reduceert deze agent tot status plus laatste activiteit, via de contractbibliotheek.
    /// </summary>
    /// <param name="now">Het moment waarop wordt geoordeeld.</param>
    /// <returns>Het gereduceerde beeld.</returns>
    public AgentSeverity Severity(DateTimeOffset now) =>
        AgentSeverity.From(Registration, LastCompletedRun, now);
}
