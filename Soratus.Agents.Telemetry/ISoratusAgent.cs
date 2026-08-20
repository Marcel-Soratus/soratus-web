using Soratus.Agents.Contracts;

namespace Soratus.Agents.Telemetry;

/// <summary>
/// Het aanspreekpunt van de telemetriebibliotheek. Injecteer dit en je bent klaar.
/// </summary>
/// <remarks>
/// Er zit geen methode op om status te melden. Een agent die om is kan niet melden dat hij om
/// is, dus status wordt in het portaal afgeleid uit hartslag en runs. Wat een agent wél zelf
/// weet is of hij bewust op werk wacht — daarvoor is <see cref="ReportLifecycle"/>.
/// </remarks>
public interface ISoratusAgent
{
    /// <summary>Wat deze agent over zichzelf publiceert.</summary>
    AgentIdentity Identity { get; }

    /// <summary>De runId van de run op de huidige asynchrone stroom, of <c>null</c>.</summary>
    string? CurrentRunId { get; }

    /// <summary>
    /// De eerstvolgende geplande run, of <c>null</c> bij een agent zonder schema. Dit is
    /// hetzelfde tijdstip waarop de bibliotheek daadwerkelijk gaat draaien.
    /// </summary>
    DateTimeOffset? NextRunAt { get; }

    /// <summary>
    /// Opent een run. Schrijft direct een <see cref="RunRecord"/> met
    /// <see cref="RunResult.Running"/> en zet de runId op de huidige asynchrone stroom.
    /// </summary>
    /// <param name="trigger">Waardoor deze run start.</param>
    /// <param name="cancellationToken">Wordt niet gebruikt; aanwezig zodat de aanroep meegaat
    /// met de gebruikelijke vorm en de bibliotheek later mag blokkeren zonder de API te breken.</param>
    /// <returns>De run, af te sluiten met <c>await using</c>.</returns>
    Task<IAgentRun> StartRunAsync(TriggerKind trigger, CancellationToken cancellationToken = default);

    /// <summary>
    /// Draait <paramref name="body"/> binnen een run en zorgt zelf voor de afloop: ontsnapt er
    /// een uitzondering, dan is het resultaat <see cref="RunResult.Failed"/> met
    /// <c>errorType</c> en <c>errorMessage</c> gevuld, en daarna wordt de uitzondering
    /// doorgegooid.
    /// </summary>
    /// <param name="trigger">Waardoor deze run start.</param>
    /// <param name="body">Het werk van de run.</param>
    /// <param name="cancellationToken">Afbreken bij afsluiten van de host.</param>
    /// <remarks>
    /// Dit is de betrouwbaarste vorm: de bibliotheek staat om het werk heen en ziet dus precies
    /// welke uitzondering is ontsnapt. Gebruik <see cref="StartRunAsync"/> alleen als je zelf
    /// de foutafhandeling wilt schrijven.
    /// </remarks>
    Task RunAsync(
        TriggerKind trigger,
        Func<IAgentRun, CancellationToken, Task> body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Meldt wat de agent over zijn eigen levenscyclus zegt. De bibliotheek zet
    /// <see cref="AgentLifecycle.Running"/> bij starten en
    /// <see cref="AgentLifecycle.StoppedCleanly"/> bij netjes afsluiten; alleen
    /// <see cref="AgentLifecycle.IdleWaiting"/> kan de agent zelf weten, want een leeg
    /// wachtinterval ziet er van buiten hetzelfde uit als een vastgelopen lus.
    /// </summary>
    /// <param name="lifecycle">De te melden levenscyclus.</param>
    void ReportLifecycle(AgentLifecycle lifecycle);
}
