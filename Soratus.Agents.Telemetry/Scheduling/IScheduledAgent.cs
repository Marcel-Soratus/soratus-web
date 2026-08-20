namespace Soratus.Agents.Telemetry.Scheduling;

/// <summary>
/// Eén methode, en de bibliotheek doet de rest: plannen op de cron-expressie uit
/// <c>SORATUS_AGENT__SCHEDULE</c>, een run openen, de afloop bepalen en de run afsluiten.
/// </summary>
/// <remarks>
/// Bewust een interface en geen basisklasse. Een basisklasse dwingt overerving af, bezet de
/// enige overervingsplek die C# heeft en maakt de agent een singleton-<c>BackgroundService</c>
/// waarin je geen scoped afhankelijkheden (een <c>DbContext</c>, een HTTP-client met
/// per-run-context) kunt injecteren. Deze interface wordt scoped geregistreerd: de bibliotheek
/// maakt per run een scope, haalt de agent daaruit op en gooit de scope daarna weg. De bouwer
/// schrijft daarmee alleen het werk zelf en niets over hosting, planning of foutafhandeling.
/// </remarks>
public interface IScheduledAgent
{
    /// <summary>
    /// Doet het werk van één run.
    /// </summary>
    /// <param name="run">De lopende run. Meld hier verwerkte items en een eventuele rollback.</param>
    /// <param name="cancellationToken">Afgebroken zodra de host afsluit.</param>
    /// <remarks>
    /// Gooi gewoon door wat er misgaat. De bibliotheek zet de run dan op <c>failed</c> met
    /// <c>errorType</c>, <c>errorMessage</c> en een logregel met de stacktrace, en de host blijft
    /// staan voor de volgende geplande run.
    /// </remarks>
    Task ExecuteRunAsync(IAgentRun run, CancellationToken cancellationToken);
}
