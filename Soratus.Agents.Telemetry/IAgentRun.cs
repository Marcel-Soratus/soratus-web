using Soratus.Agents.Contracts;

namespace Soratus.Agents.Telemetry;

/// <summary>
/// Eén lopende run. Bestaat tussen <see cref="ISoratusAgent.StartRunAsync"/> en het afsluiten.
/// </summary>
/// <remarks>
/// Gebruik altijd <c>await using</c>. Het afsluiten schrijft het definitieve
/// <see cref="RunRecord"/>; wordt de run nooit afgesloten, dan blijft hij op
/// <see cref="RunResult.Running"/> staan en is dat op het scherm zichtbaar als een run die
/// nooit is afgerond. Dat is een eerlijker uitkomst dan een verzonnen <c>ok</c>.
///
/// Zolang deze run leeft draagt elke logregel op dezelfde asynchrone stroom automatisch
/// <see cref="RunId"/>; de bouwer geeft nergens iets door.
/// </remarks>
public interface IAgentRun : IAsyncDisposable
{
    /// <summary>De runId, bijvoorbeeld <c>r-8f3c1a2b</c>. Ook de documentsleutel.</summary>
    string RunId { get; }

    /// <summary>Wanneer deze run begon.</summary>
    DateTimeOffset StartedAt { get; }

    /// <summary>Waardoor deze run is gestart.</summary>
    TriggerKind Trigger { get; }

    /// <summary>Hoeveel items tot nu toe zijn verwerkt.</summary>
    int ItemsProcessed { get; }

    /// <summary>Hoeveel items tot nu toe zijn afgekeurd of mislukt.</summary>
    int ItemsFailed { get; }

    /// <summary>
    /// Meldt dat er items zijn verwerkt. Wat een item is weet alleen de agent, dus dit is het
    /// enige dat de bouwer echt zelf moet zeggen.
    /// </summary>
    /// <param name="count">Aantal verwerkte items; standaard één.</param>
    void Processed(int count = 1);

    /// <summary>Meldt dat er items zijn afgekeurd of mislukt.</summary>
    /// <param name="count">Aantal mislukte items; standaard één.</param>
    void FailedItems(int count = 1);

    /// <summary>
    /// Meldt dat de transactie is teruggedraaid. Het foutscherm vertelt de klant dat er geen
    /// halve stand is weggeschreven; die bewering moet waar zijn, dus hij wordt gemeld en niet
    /// geraden.
    /// </summary>
    void MarkRolledBack();

    /// <summary>
    /// Meldt dat deze run is mislukt op een uitzondering. Vult <c>errorType</c> en
    /// <c>errorMessage</c> en schrijft één logregel <c>run.failed</c> met de stacktrace in
    /// <c>extra</c>.
    /// </summary>
    /// <param name="exception">De uitzondering die de run heeft laten mislukken.</param>
    /// <remarks>
    /// Draait de run via <see cref="ISoratusAgent.RunAsync"/> of via een
    /// <see cref="Scheduling.IScheduledAgent"/>, dan doet de bibliotheek dit zelf zodra een
    /// uitzondering ontsnapt. Alleen wie zelf een <c>await using</c>-blok schrijft en de
    /// uitzondering daar afvangt, roept dit met de hand aan.
    /// </remarks>
    void Fail(Exception exception);

    /// <summary>
    /// Meldt dat deze run is mislukt zonder dat er een uitzondering was, bijvoorbeeld omdat
    /// een externe partij een nette foutcode teruggaf.
    /// </summary>
    /// <param name="errorType">Korte typeaanduiding, bijvoorbeeld <c>Http502</c>.</param>
    /// <param name="errorMessage">Eén zin die uitlegt wat er misging.</param>
    void Fail(string errorType, string errorMessage);
}
