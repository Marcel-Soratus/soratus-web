using Soratus.Agents.Contracts;

namespace Soratus.Portal.Views;

/// <summary>
/// De laatste run van een agent, klaar om af te drukken.
/// </summary>
/// <remarks>
/// Gedeeld tussen de klant- en de operatorweergave, en dat mag: §2 van de spec geeft de klant
/// leesrecht op agents, logs en runs van zijn eigen omgeving. De dingen die de klant níet mag
/// zien — te fiatteren urenregels, koppelingdetails, de Azure-uitsplitsing per dienst — zijn geen
/// eigenschap van een run, en daar horen dus ook geen velden voor op dit type te komen.
/// </remarks>
public sealed record AgentRunSummary
{
    /// <summary>De runId, bijvoorbeeld <c>r-8f3c</c>.</summary>
    public required string RunId { get; init; }

    /// <summary>Wanneer de run begon.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Wanneer de run klaar was.</summary>
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>
    /// Hoe lang de run duurde, of <c>null</c> als dat niet is meegeschreven.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>De afloop.</summary>
    public required RunResult Result { get; init; }

    /// <summary>Hoeveel items deze run verwerkte.</summary>
    public int ItemsProcessed { get; init; }

    /// <summary>Hoeveel items zijn afgekeurd of mislukt.</summary>
    public int ItemsFailed { get; init; }

    /// <summary>
    /// Of de transactie is teruggedraaid. Het foutscherm meldt dat er geen halve stand is
    /// weggeschreven; die bewering komt hiervandaan en wordt niet geraden.
    /// </summary>
    public bool RolledBack { get; init; }

    /// <summary>Het .NET-type van de uitzondering, als de run mislukte.</summary>
    public string? ErrorType { get; init; }

    /// <summary>De foutmelding, als de run mislukte.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>De agentversie die deze run draaide.</summary>
    public required string Version { get; init; }

    /// <summary>
    /// Zet een <see cref="RunRecord"/> om, of geeft <c>null</c> terug als er geen run is.
    /// </summary>
    /// <param name="run">De run.</param>
    /// <returns>De samenvatting.</returns>
    internal static AgentRunSummary? From(RunRecord? run) =>
        run is null
            ? null
            : new AgentRunSummary
            {
                RunId = run.Id,
                StartedAt = run.StartedAt,
                FinishedAt = run.FinishedAt,
                Duration = run.DurationMs is { } ms ? TimeSpan.FromMilliseconds(ms) : null,
                Result = run.Result,
                ItemsProcessed = run.ItemsProcessed,
                ItemsFailed = run.ItemsFailed,
                RolledBack = run.RolledBack,
                ErrorType = run.ErrorType,
                ErrorMessage = run.ErrorMessage,
                Version = run.Version,
            };
}
