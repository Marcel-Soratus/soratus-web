using Soratus.Portal.Components.Shared;
using Soratus.Agents.Contracts;

namespace Soratus.Portal.Views;

/// <summary>
/// De operatorweergave van de agentlijst van één klant: dezelfde weergave als de klant ziet, met
/// beheerinformatie erbovenop (§1).
/// </summary>
/// <remarks>
/// Een apart type en geen <see cref="CustomerAgentsView"/> met extra velden erop. Dat is de kern
/// van de opzet: het verschil tussen de twee rollen is een verschil tussen twee <em>typen</em>, en
/// de pagina die het klanttype krijgt kan het operatorveld niet renderen omdat het er niet is.
///
/// De velden die hier bij komen: alle omgevingen in plaats van alleen productie, de omgeving per
/// agent, de volledige omgevingsaanduiding (subscription en resource group), en de contractversie
/// van de telemetriebibliotheek.
/// </remarks>
public sealed record OperatorCustomerAgentsView
{
    /// <summary>De slug van de klant.</summary>
    public required string CustomerId { get; init; }

    /// <summary>De klantnaam.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Korte omgevingsaanduiding.</summary>
    public string? Environment { get; init; }

    /// <summary>
    /// De volledige omgeving, bijvoorbeeld <c>sub-soratus-acme · rg-acme-prod</c>. Operator-only.
    /// </summary>
    public string? EnvironmentDetail { get; init; }

    /// <summary>Of dit de interne beheerklant is (§4).</summary>
    public required bool IsInternal { get; init; }

    /// <summary>Wanneer deze weergave is opgebouwd.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>
    /// Alle agents van deze klant, ongeacht omgeving, ernstigste eerst.
    /// </summary>
    public required IReadOnlyList<OperatorAgentRow> Agents { get; init; }

    /// <summary>
    /// De statusverdeling over alle omgevingen, afgeleid uit <see cref="Agents"/>.
    /// </summary>
    public required AgentStatusBreakdown Statuses { get; init; }

    /// <summary>
    /// De statusverdeling van alleen de productie-agents — precies wat de klant zelf te zien
    /// krijgt.
    /// </summary>
    /// <remarks>
    /// Staat er zodat een operator in één oogopslag ziet of een probleem de klant raakt of alleen
    /// op acceptatie zit. Afgeleid uit dezelfde <see cref="Agents"/>-lijst, dus het kan de
    /// klantweergave niet tegenspreken.
    /// </remarks>
    public required AgentStatusBreakdown ProductionStatuses { get; init; }

    /// <summary>Of deze klant nog helemaal geen agents heeft.</summary>
    public bool IsEmpty => Agents.Count == 0;
}

/// <summary>
/// Eén agent zoals de operator hem ziet.
/// </summary>
/// <remarks>
/// De velden overlappen grotendeels met <see cref="CustomerAgentRow"/>, en die herhaling is de
/// bedoeling. Zouden de twee één type delen, dan is een veld dat aan de operatorkant nodig is
/// vanzelf ook aan de klantkant aanwezig, en dan is de scheiding weer een kwestie van discipline
/// in de markup. Twee typen mogen uit elkaar groeien; dat is precies wat je wil.
/// </remarks>
public sealed record OperatorAgentRow
{
    /// <summary>De technische naam.</summary>
    public required string AgentName { get; init; }

    /// <summary>De typeaanduiding.</summary>
    public required string DisplayType { get; init; }

    /// <summary>De afgeleide status.</summary>
    public required AgentStatus Status { get; init; }

    /// <summary>De versie die nu draait.</summary>
    public required string Version { get; init; }

    /// <summary>Sinds wanneer dit proces loopt.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>De laatste hartslag.</summary>
    public required DateTimeOffset LastHeartbeatAt { get; init; }

    /// <summary>Hoe lang deze agent al zwijgt.</summary>
    public required TimeSpan? Silence { get; init; }

    /// <summary>Het jongste moment waarop deze agent iets deed of zich meldde.</summary>
    public required DateTimeOffset? LastActivityAt { get; init; }

    /// <summary>Wat de agent over zijn eigen levenscyclus meldt.</summary>
    public required AgentLifecycle Lifecycle { get; init; }

    /// <summary>De cron-expressie, of <c>null</c>.</summary>
    public string? Schedule { get; init; }

    /// <summary>Waardoor deze agent aan het werk gaat.</summary>
    public required TriggerKind TriggerKind { get; init; }

    /// <summary>Toelichting op de trigger.</summary>
    public string? TriggerDetail { get; init; }

    /// <summary>De eerstvolgende geplande run, of <c>null</c>.</summary>
    public DateTimeOffset? NextRunAt { get; init; }

    /// <summary>De laatste afgeronde run.</summary>
    public AgentRunSummary? LastRun { get; init; }

    /// <summary>
    /// De runs van de laatste 24 uur in twaalf blokken van twee uur, oudste eerst.
    /// </summary>
    public required IReadOnlyList<SparkBlock> Runs24Hours { get; init; }

    /// <summary>
    /// Productie, acceptatie of ontwikkeling. Bestaat niet op <see cref="CustomerAgentRow"/>.
    /// </summary>
    public required AgentEnvironment AgentEnvironment { get; init; }

    /// <summary>De contractversie die deze agent publiceert.</summary>
    public required int ContractVersion { get; init; }

    /// <summary>
    /// Of deze agent op een oudere contractvorm is blijven staan en dus een uitrol mist.
    /// </summary>
    public bool IsContractVersionStale =>
        ContractVersion < AgentRegistration.CurrentContractVersion;
}
