using Soratus.Portal.Components.Shared;
using Soratus.Agents.Contracts;

namespace Soratus.Portal.Views;

/// <summary>
/// De klantweergave van de agentlijst (§3.2). Read-only, alleen productie.
/// </summary>
/// <remarks>
/// <para><strong>Zichtbaarheid zit in de vorm van dit type, niet in vlaggen.</strong> Wat een klant
/// niet mag zien staat hier niet als <c>null</c> met een <c>@if</c> eromheen — het staat er
/// helemaal niet. Dus geen <c>PendingHours</c>, geen fiatteeracties, geen boekformulier, geen
/// koppelingdetails, geen Azure-uitsplitsing per dienst, geen beheeropslag. Een ontbrekende
/// property kan niet lekken, ook niet als iemand er over een half jaar een kolom bij zet en het
/// <c>@if</c> vergeet.</para>
///
/// <para>Ook <c>Environment</c> per agent ontbreekt. Deze weergave bevat uitsluitend agents in de
/// productieomgeving — een acceptatie-agent die omvalt is geen storing voor de klant — en zonder
/// het veld valt er niets te verklappen over wat er nog op acceptatie staat.</para>
/// </remarks>
public sealed record CustomerAgentsView
{
    /// <summary>De slug van de klant.</summary>
    public required string CustomerId { get; init; }

    /// <summary>De klantnaam, voor de kop.</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Korte omgevingsaanduiding, bijvoorbeeld <c>West-Europa</c>. Bewust niet de subscription of
    /// de resource group.
    /// </summary>
    public string? Environment { get; init; }

    /// <summary>
    /// Het moment waarop deze weergave is opgebouwd, voor de "laatst bijgewerkt"-regel in de kop.
    /// </summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>De agents, ernstigste eerst, dan meest recente activiteit.</summary>
    public required IReadOnlyList<CustomerAgentRow> Agents { get; init; }

    /// <summary>
    /// De statusverdeling, afgeleid uit <see cref="Agents"/> en niet apart geteld.
    /// </summary>
    public required AgentStatusBreakdown Statuses { get; init; }

    /// <summary>
    /// Of deze klant nog geen agents in productie heeft. Het scherm hoort dan de lege staat te
    /// tonen (§3.2) en niet een lege tabel met koppen.
    /// </summary>
    public bool IsEmpty => Agents.Count == 0;
}

/// <summary>
/// Eén agent zoals de klant hem ziet.
/// </summary>
/// <remarks>
/// Plat en direct af te drukken: geen berekening meer nodig in de Razor-pagina. Wat het scherm nog
/// wél doet is opmaak — een relatieve tijd van <see cref="LastActivityAt"/>, een label en een
/// glyph bij <see cref="Status"/> — want dat is presentatie en geen rekenwerk.
/// </remarks>
public sealed record CustomerAgentRow
{
    /// <summary>De technische naam, bijvoorbeeld <c>factuur-intake</c>.</summary>
    public required string AgentName { get; init; }

    /// <summary>De typeaanduiding voor de typekolom, bijvoorbeeld <c>Document-intake</c>.</summary>
    public required string DisplayType { get; init; }

    /// <summary>
    /// De afgeleide status. Komt uit <see cref="AgentStatusCalculator"/>, dus dezelfde bron als de
    /// storingsmelder gebruikt.
    /// </summary>
    public required AgentStatus Status { get; init; }

    /// <summary>De versie die nu draait.</summary>
    public required string Version { get; init; }

    /// <summary>Sinds wanneer dit proces loopt.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>De laatste hartslag.</summary>
    public required DateTimeOffset LastHeartbeatAt { get; init; }

    /// <summary>
    /// Hoe lang deze agent al zwijgt. Laat het scherm de melding meeschalen met de stilte: "meldt
    /// zich 3 minuten niet" en "meldt zich 4 uur niet" zijn beide degraded maar twee verschillende
    /// berichten.
    /// </summary>
    public required TimeSpan? Silence { get; init; }

    /// <summary>
    /// Het jongste moment waarop deze agent iets deed of zich meldde.
    /// </summary>
    public required DateTimeOffset? LastActivityAt { get; init; }

    /// <summary>De cron-expressie, of <c>null</c> bij een agent die op een trigger draait.</summary>
    public string? Schedule { get; init; }

    /// <summary>Waardoor deze agent aan het werk gaat.</summary>
    public required TriggerKind TriggerKind { get; init; }

    /// <summary>Toelichting op de trigger, bijvoorbeeld <c>Blob-drop (inbox-facturen)</c>.</summary>
    public string? TriggerDetail { get; init; }

    /// <summary>
    /// De eerstvolgende geplande run, of <c>null</c> bij een agent die alleen op een trigger
    /// draait. Toon dan de trigger en niet een verzonnen tijdstip.
    /// </summary>
    public DateTimeOffset? NextRunAt { get; init; }

    /// <summary>De laatste afgeronde run, of <c>null</c> als er nog geen run is afgerond.</summary>
    public AgentRunSummary? LastRun { get; init; }

    /// <summary>
    /// De runs van de laatste 24 uur in twaalf blokken van twee uur, oudste eerst (§3.2).
    /// </summary>
    /// <remarks>
    /// Altijd precies twaalf blokken, ook voor een agent die niets deed — dan staan er twaalf lege
    /// in. Zo hoeft de pagina geen lege lijst af te vangen en houden alle sparklines dezelfde
    /// breedte, wat in een tabelkolom nogal uitmaakt.
    /// </remarks>
    public required IReadOnlyList<SparkBlock> Runs24Hours { get; init; }
}
