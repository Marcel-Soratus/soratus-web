using Soratus.Agents.Contracts;

namespace Soratus.Portal.Views;

/// <summary>
/// De laatste run van een agent, klaar om af te drukken.
/// </summary>
/// <remarks>
/// <para>Gedeeld tussen de klant- en de operatorweergave, en dat mag: §2 van de spec geeft de klant
/// leesrecht op agents, logs en runs van zijn eigen omgeving. De dingen die de klant níet mag
/// zien — te fiatteren urenregels, koppelingdetails, de Azure-uitsplitsing per dienst — zijn geen
/// eigenschap van een run, en daar horen dus ook geen velden voor op dit type te komen.</para>
///
/// <para><strong>Er stond hier één uitzondering op, en die is weg.</strong> Dit type droeg
/// <c>ErrorType</c> — de volledige .NET-typenaam van de uitzondering — en het zit via
/// <see cref="CustomerAgentRow.LastRun"/> op de klantweergave van de agentlijst en de agentkop. Geen
/// enkel scherm las dat veld: het werd geprojecteerd en nooit afgedrukt. Een veld dat niemand leest en
/// dat onze naamruimtestructuur bij de klant neerlegt hoort niet te bestaan, dus is het verwijderd in
/// plaats van gesplitst. De operator vindt de typenaam waar hij hem nodig heeft: op het runtabblad,
/// op <see cref="OperatorRunRow.ErrorType"/>. Zie <c>docs/agent-portal/fase-0-afwijkingen.md</c>
/// §14.</para>
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

    /// <summary>
    /// De foutmelding, als de run mislukte.
    /// </summary>
    /// <remarks>
    /// Dit is de zin die <c>AgentText.StatusNotice</c> onder de agentkop zet, dus hij komt bij een
    /// klant op het scherm. Hij gaat daarom door dezelfde knip als een klantzichtbaar logbericht; zie
    /// <see cref="From"/>.
    /// </remarks>
    public string? ErrorMessage { get; init; }

    /// <summary>De agentversie die deze run draaide.</summary>
    public required string Version { get; init; }

    /// <summary>
    /// Zet een <see cref="RunRecord"/> om, of geeft <c>null</c> terug als er geen run is.
    /// </summary>
    /// <param name="run">De run.</param>
    /// <returns>De samenvatting.</returns>
    /// <remarks>
    /// <see cref="ErrorMessage"/> gaat door <c>CustomerMessage.FirstLine</c>, en dat geldt hier voor
    /// beide rollen. De volledige boodschap raakt daarmee niemand kwijt: bij een uitzondering staat hij
    /// in de bijbehorende <c>run.failed</c>-logregel onder <c>extra</c>, en die is operator-only. Wat
    /// deze samenvatting doet is de eerste zin in een lopende melding zetten — daar hoort geen tweede
    /// regel in, en zeker geen stacktrace.
    /// </remarks>
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
                ErrorMessage = run.ErrorMessage is { } message
                    ? CustomerMessage.FirstLine(message)
                    : null,
                Version = run.Version,
            };
}
