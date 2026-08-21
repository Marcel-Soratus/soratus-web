using Soratus.Agents.Contracts;

namespace Soratus.Agents.Telemetry.HostedAgents;

/// <summary>
/// Eén agent die in deze host woont. Het aanspreekpunt om een aanroep als run vast te leggen.
/// </summary>
/// <remarks>
/// <para>Als <see cref="ISoratusAgent"/>, maar per agent in plaats van per proces, en zonder
/// <c>ReportLifecycle</c>. Dat laatste is het verschil dat telt: bij een agent met een eigen lus
/// weet alleen de agent of hij bewust wacht, dus meldt hij het zelf. Bij een geherbergde agent
/// is "bewust wachten" precies "er loopt geen aanroep", en dat weet de bibliotheek zelf beter
/// dan de bouwer: zij opent en sluit de runs. Een <c>ReportLifecycle</c> hier zou een tweede,
/// afwijkende waarheid over dezelfde toestand toestaan.</para>
///
/// <para><strong>Wat een verse hartslag van deze agent wél en niet bewijst.</strong> De hartslag
/// komt van de host en niet van het werk. Hij bewijst dat het proces in leven is en dat de
/// koppeling naar de opslag werkt. Hij bewijst <em>niet</em> dat deze agent doet waarvoor hij
/// er is: een endpoint dat niemand meer aanroept, of dat achter een kapotte inlog zit, klopt
/// even trouw door als een endpoint dat de hele dag werk verzet. Het enige bewijs dat deze agent
/// werkt is zijn laatste geslaagde <see cref="RunRecord"/>.</para>
/// </remarks>
public interface ISoratusHostedAgent
{
    /// <summary>Wat deze agent over zichzelf publiceert.</summary>
    AgentIdentity Identity { get; }

    /// <summary>
    /// Hoeveel aanroepen van deze agent op dit moment lopen.
    /// </summary>
    /// <remarks>
    /// Dit getal bepaalt de gemelde levensfase: nul is <see cref="AgentLifecycle.IdleWaiting"/>,
    /// meer is <see cref="AgentLifecycle.Running"/>. Het staat hier zichtbaar zodat een test de
    /// afleiding kan meten in plaats van hem te moeten aannemen.
    /// </remarks>
    int RunsInFlight { get; }

    /// <summary>
    /// Opent een run voor deze agent. Schrijft direct een <see cref="RunRecord"/> met
    /// <see cref="RunResult.Running"/> en zet de runId op de huidige asynchrone stroom.
    /// </summary>
    /// <param name="trigger">Waardoor deze aanroep binnenkwam.</param>
    /// <param name="cancellationToken">Aanwezig voor de gebruikelijke vorm; wordt niet gebruikt.</param>
    /// <returns>De run, af te sluiten met <c>await using</c>.</returns>
    Task<IAgentRun> StartRunAsync(TriggerKind trigger, CancellationToken cancellationToken = default);

    /// <summary>
    /// Draait <paramref name="body"/> binnen een run en bepaalt zelf de afloop: ontsnapt er een
    /// uitzondering, dan is het resultaat <see cref="RunResult.Failed"/> en wordt de uitzondering
    /// daarna doorgegooid.
    /// </summary>
    /// <param name="trigger">Waardoor deze aanroep binnenkwam.</param>
    /// <param name="body">Het werk van deze aanroep.</param>
    /// <param name="cancellationToken">Afbreken bij afsluiten van de host.</param>
    Task RunAsync(
        TriggerKind trigger,
        Func<IAgentRun, CancellationToken, Task> body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schrijft één logregel op naam van deze agent, buiten een run om.
    /// </summary>
    /// <param name="level">Info, warn of error.</param>
    /// <param name="eventName">Puntgescheiden gebeurtenisnaam, bijvoorbeeld <c>host.started</c>.</param>
    /// <param name="message">
    /// Eén zin, in het Nederlands, leesbaar voor wie de code niet kent — dit veld leest de klant.
    /// </param>
    /// <param name="extra">Operator-only context. Mag een anoniem object zijn.</param>
    /// <remarks>
    /// <para>Voor het gewone werk binnen een aanroep is dit niet nodig: een gewone
    /// <c>ILogger</c>-aanroep binnen een lopende run belandt automatisch bij de juiste agent en
    /// met de juiste runId. Deze methode dekt het geval dat daar níet in past — een mededeling
    /// van de host zelf, buiten elke aanroep, die tóch aan een agent toegeschreven moet worden
    /// omdat een logregel zonder agentnaam nergens te vinden is.</para>
    ///
    /// <para>De reden dat dat geval bestaat: bij een geherbergde agent is er geen enkelvoudige
    /// agentnaam per proces. Een regel buiten een aanroep heeft dus geen vanzelfsprekende
    /// eigenaar, en de bibliotheek verzint er geen — zij vraagt de aanroeper te kiezen.</para>
    /// </remarks>
    void ReportEvent(LogLevel level, string eventName, string message, object? extra = null);
}
