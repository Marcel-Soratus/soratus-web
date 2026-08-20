using Soratus.Agents.Contracts;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// Bouwstenen voor de tests: een registratiedocument en een run, met alleen die velden gezet die
/// de test daadwerkelijk gebruikt.
/// </summary>
/// <remarks>
/// Geen enkele methode hier leest de klok. Elk moment komt als parameter binnen, want een test die
/// <c>DateTimeOffset.UtcNow</c> gebruikt is niet reproduceerbaar en gaat een keer 's nachts rood.
/// </remarks>
internal static class Testgegevens
{
    /// <summary>Het vaste referentiemoment van de tests: 19 augustus 2026, 09:22:31 UTC.</summary>
    /// <remarks>
    /// Eén moment voor alle tests, zodat een falende assertie altijd hetzelfde getal laat zien.
    /// Deze waarde komt uit de voorbeeldtabel in de opdracht en valt in de zomertijd, dus hij
    /// prikt meteen door een verwisseling van UTC en Nederlandse tijd heen.
    /// </remarks>
    public static readonly DateTimeOffset Nu = new(2026, 8, 19, 9, 22, 31, TimeSpan.Zero);

    /// <summary>
    /// Een registratiedocument met een hartslag op een gekozen moment.
    /// </summary>
    /// <param name="lastHeartbeatAt">Het moment van de laatste hartslag.</param>
    /// <param name="lifecycle">Wat de agent over zijn levenscyclus meldt.</param>
    /// <param name="agentName">De technische naam.</param>
    /// <param name="customerId">De klant-slug.</param>
    /// <param name="environment">De omgeving.</param>
    /// <returns>Het registratiedocument.</returns>
    public static AgentRegistration Registratie(
        DateTimeOffset lastHeartbeatAt,
        AgentLifecycle lifecycle = AgentLifecycle.Running,
        string agentName = "factuur-intake",
        string customerId = "acme-logistiek",
        AgentEnvironment environment = AgentEnvironment.Production) =>
        new()
        {
            Id = agentName,
            PartitionKey = agentName,
            CustomerId = customerId,
            AgentName = agentName,
            DisplayType = "Document-intake",
            Version = "1.4.2",
            StartedAt = lastHeartbeatAt - TimeSpan.FromHours(6),
            LastHeartbeatAt = lastHeartbeatAt,
            Lifecycle = lifecycle,
            TriggerKind = TriggerKind.Timer,
            Environment = environment,
        };

    /// <summary>
    /// Een afgeronde run met een gekozen afloop.
    /// </summary>
    /// <param name="result">De afloop.</param>
    /// <param name="finishedAt">Wanneer de run afliep, of <c>null</c> voor een lopende run.</param>
    /// <param name="agentName">De technische naam van de agent.</param>
    /// <param name="customerId">De klant-slug.</param>
    /// <returns>De run.</returns>
    public static RunRecord Run(
        RunResult result,
        DateTimeOffset? finishedAt = null,
        string agentName = "factuur-intake",
        string customerId = "acme-logistiek")
    {
        var started = (finishedAt ?? Nu) - TimeSpan.FromSeconds(12);

        return new RunRecord
        {
            Id = "r-8f3c",
            PartitionKey = RunRecord.BuildPartitionKey(agentName, started),
            CustomerId = customerId,
            AgentName = agentName,
            StartedAt = started,
            FinishedAt = result == RunResult.Running ? null : finishedAt ?? Nu,
            DurationMs = result == RunResult.Running ? null : 12_000,
            Result = result,
            Trigger = TriggerKind.Timer,
            Version = "1.4.2",
            ErrorType = result == RunResult.Failed ? "System.TimeoutException" : null,
            ErrorMessage = result == RunResult.Failed ? "De bron antwoordde niet binnen 30 seconden." : null,
        };
    }
}
