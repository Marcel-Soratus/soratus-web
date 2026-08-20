using Soratus.Agents.Contracts;

namespace Soratus.Portal.Views;

/// <summary>
/// Het tabblad Runs van het agentdetail (§3.3): gestart, duur, resultaat, aantal items, runId.
/// </summary>
/// <remarks>
/// Gedeeld tussen de klant- en de operatorweergave, om dezelfde reden als
/// <see cref="AgentRunSummary"/>: §2 geeft beide rollen leesrecht op de runs van de eigen omgeving,
/// en er is geen veld op een run dat de één wel en de ander niet mag zien.
/// </remarks>
public sealed record AgentRunsView
{
    /// <summary>De technische naam van de agent.</summary>
    public required string AgentName { get; init; }

    /// <summary>Wanneer deze weergave is opgebouwd.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>De runs, nieuwste eerst.</summary>
    public required IReadOnlyList<AgentRunRow> Runs { get; init; }

    /// <summary>
    /// Het vervolgtoken voor de volgende (oudere) pagina, of <c>null</c> als dit alles was. Niet
    /// geschikt voor een URL.
    /// </summary>
    public string? ContinuationToken { get; init; }

    /// <summary>Of er nog oudere runs zijn.</summary>
    public bool HasMore => ContinuationToken is not null;

    /// <summary>Of deze agent nog nooit gedraaid heeft.</summary>
    public bool IsEmpty => Runs.Count == 0;
}

/// <summary>
/// Eén run in de runlijst, plat en direct af te drukken.
/// </summary>
/// <remarks>
/// <para><strong>Een lopende run mist dingen, en dat staat er als "afwezig" en niet als nul.</strong>
/// <see cref="Duration"/>, <see cref="Outcome"/> en <see cref="ItemsProcessed"/> zijn alle drie
/// nullable, en bij een run die nog loopt zijn ze <c>null</c>. Het document in Cosmos zegt op dat
/// moment <c>durationMs: null</c>, <c>result: "running"</c> en <c>itemsProcessed: 0</c> — dat
/// laatste is een beginstand en geen uitkomst. Zou dit type die nul doorgeven, dan stond er op het
/// scherm "0 ms" en "0 items" bij een run die vrolijk aan het werk is, en dat is niet onvolledig
/// maar onwaar. Met <c>null</c> kan het scherm een streepje zetten.</para>
///
/// <para><strong>Waarom dit niet <see cref="AgentRunSummary"/> is.</strong> Dat type beschrijft de
/// laatste <em>afgeronde</em> run in de kop van het scherm; daar bestaat "loopt nog" niet, dus
/// staan <c>Result</c> en <c>ItemsProcessed</c> er terecht als niet-nullable in. Deze lijst bevat
/// juist álle runs, inclusief de lopende. Dat verschil in wat er kán ontbreken is precies het
/// verschil dat een apart type verdient; één type met "soms is dit veld zinloos" zou het bij beide
/// schermen aan de lezer overlaten om dat te weten.</para>
/// </remarks>
public sealed record AgentRunRow
{
    /// <summary>De runId, bijvoorbeeld <c>r-4a91d20c</c>.</summary>
    public required string RunId { get; init; }

    /// <summary>Wanneer de run begon.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Wanneer de run klaar was, of <c>null</c> zolang hij loopt.</summary>
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>
    /// Hoe lang de run duurde, of <c>null</c> zolang hij loopt. Toon dan een streepje en niet nul.
    /// </summary>
    /// <remarks>
    /// Bewust niet "nu min gestart" voor een lopende run. Dat is de tijd die hij al bezig is, niet
    /// zijn duur, en het zou in dezelfde kolom staan als de duur van de runs eronder — waarmee de
    /// kolom twee verschillende dingen betekent afhankelijk van de rij.
    /// </remarks>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// De afloop, of <c>null</c> zolang de run nog loopt.
    /// </summary>
    /// <remarks>
    /// <c>null</c> en niet <see cref="RunResult.Running"/>: "loopt nog" is geen afloop, en zolang
    /// het als vierde uitkomst in dezelfde opsomming zit, komt het vroeg of laat in een telling
    /// van geslaagd-tegen-mislukt terecht. Zie <see cref="IsRunning"/>.
    /// </remarks>
    public RunResult? Outcome { get; init; }

    /// <summary>
    /// Hoeveel items deze run verwerkte, of <c>null</c> zolang hij loopt.
    /// </summary>
    public int? ItemsProcessed { get; init; }

    /// <summary>
    /// Hoeveel items zijn afgekeurd of mislukt, of <c>null</c> zolang de run loopt.
    /// </summary>
    public int? ItemsFailed { get; init; }

    /// <summary>Of de transactie is teruggedraaid.</summary>
    public bool RolledBack { get; init; }

    /// <summary>Het .NET-type van de uitzondering, als de run mislukte.</summary>
    public string? ErrorType { get; init; }

    /// <summary>De foutmelding, als de run mislukte.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>De agentversie die deze run draaide.</summary>
    public required string Version { get; init; }

    /// <summary>Waardoor deze run startte.</summary>
    public required TriggerKind Trigger { get; init; }

    /// <summary>Of deze run op dit moment nog loopt.</summary>
    public bool IsRunning => Outcome is null;

    /// <summary>
    /// Zet een <see cref="RunRecord"/> om naar een rij.
    /// </summary>
    /// <param name="run">De run.</param>
    /// <returns>De rij.</returns>
    internal static AgentRunRow From(RunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var running = run.Result == RunResult.Running;

        return new AgentRunRow
        {
            RunId = run.Id,
            StartedAt = run.StartedAt,
            FinishedAt = run.FinishedAt,
            // Ook de duur gaat op null zolang de run loopt, en niet alleen als het document er geen
            // in heeft staan. Een agent die durationMs alvast meeschrijft op een run die nog bezig
            // is, levert anders een eindduur op iets wat geen einde heeft — en het scherm zet
            // ernaast de tooltip "de run is nog bezig". Dezelfde vraag als bij de andere drie
            // velden, dus hetzelfde antwoord.
            Duration = running || run.DurationMs is not { } ms ? null : TimeSpan.FromMilliseconds(ms),
            Outcome = running ? null : run.Result,
            ItemsProcessed = running ? null : run.ItemsProcessed,
            ItemsFailed = running ? null : run.ItemsFailed,
            RolledBack = run.RolledBack,
            ErrorType = run.ErrorType,
            ErrorMessage = run.ErrorMessage,
            Version = run.Version,
            Trigger = run.Trigger,
        };
    }
}
