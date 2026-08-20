using Soratus.Agents.Contracts;

namespace Soratus.Portal.Components.Shared;

/// <summary>
/// De enige plek waar een <see cref="AgentStatus"/> wordt vertaald naar wat je op het scherm
/// ziet: glyph, woordlabel, classnaam en kleurvariabele.
/// </summary>
/// <remarks>
/// Badge, stip, verdelingsbalk, legenda en rijtint gebruiken allemaal deze afbeelding. Zodra
/// zo'n mapping op twee plekken staat, lopen ze uit de pas en toont het ene scherm amber waar
/// het andere rood toont. Daarom staat hij hier en niet in een component.
///
/// De waarden komen 1:1 uit §8 van <c>agent-portal-spec.md</c>; de classnamen bestaan al in
/// <c>wwwroot/css/patterns.css</c>. Voeg hier niets toe zonder dat beide bronnen meebewegen.
///
/// <para><strong>Rang 0 heeft drie woordlabels, en dat is geen slordigheid.</strong>
/// <see cref="AgentStatus.Unknown"/> zegt maar één ding: wij hebben niets gemeten. Waaróm we
/// niets hebben gemeten verschilt per context, en dat verschil is precies wat de lezer wil
/// weten. Op een agentrij ontbreekt de telemetrie van een agent die wél bestaat
/// (<see cref="UnknownAgentLabel"/>); op een klantrij zonder agents is er nog niets ingericht
/// (<see cref="UnknownCustomerLabel"/>); bij een klant met alleen acceptatie- of
/// ontwikkelagents is er wél iets, maar niet in productie
/// (<see cref="UnknownNonProductionLabel"/>). Voeg ze niet samen: elk van de drie is in de
/// andere twee contexten een onwaarheid.</para>
/// </remarks>
public static class StatusVisuals
{
    /// <summary>
    /// Het woordlabel voor <see cref="AgentStatus.Unknown"/> op een agentrij: wij ontvangen
    /// geen telemetrie van een agent die wél bestaat.
    /// </summary>
    public const string UnknownAgentLabel = "Geen telemetrie";

    /// <summary>
    /// Het woordlabel voor <see cref="AgentStatus.Unknown"/> op een klantrij: de klant heeft
    /// nog geen agents, dus er valt niets te meten.
    /// </summary>
    /// <remarks>
    /// Gebruik dit label alleen op klantniveau. "Geen agents" op een agentrij zou een onwaarheid
    /// zijn — die agent bestaat, hij meldt zich alleen niet.
    /// </remarks>
    public const string UnknownCustomerLabel = "Geen agents";

    /// <summary>
    /// Het woordlabel voor <see cref="AgentStatus.Unknown"/> op een klantrij van een klant die
    /// wél agents heeft, maar geen enkele in productie.
    /// </summary>
    /// <remarks>
    /// De derde variant van rang 0; zie de opmerking bij deze klasse over waarom er drie zijn.
    /// "Geen agents" zou hier onwaar zijn — er staan agents, alleen op acceptatie of
    /// ontwikkeling, en die tellen niet mee in de ernst. Gebruik dit label uitsluitend op een
    /// klantrij waarvan <c>HasOnlyNonProductionAgents</c> geldt.
    /// </remarks>
    public const string UnknownNonProductionLabel = "Geen in productie";

    /// <summary>De glyph uit §8. Altijd samen met het woordlabel tonen, nooit als enige drager.</summary>
    /// <param name="status">De status.</param>
    /// <returns>● ◐ ✕ ○ of –.</returns>
    public static string Glyph(AgentStatus status) => status switch
    {
        AgentStatus.Live => "●",
        AgentStatus.Degraded => "◐",
        AgentStatus.Failed => "✕",
        AgentStatus.Idle => "○",
        _ => "–",
    };

    /// <summary>Het woordlabel dat als echte tekst in beeld komt.</summary>
    /// <param name="status">De status.</param>
    /// <param name="unknownLabel">
    /// Het label voor <see cref="AgentStatus.Unknown"/>. Laat leeg voor
    /// <see cref="UnknownAgentLabel"/>; geef op een klantrij <see cref="UnknownCustomerLabel"/> mee.
    /// </param>
    /// <returns>Het label, bijvoorbeeld <c>Degraded</c>.</returns>
    public static string Label(AgentStatus status, string? unknownLabel = null) => status switch
    {
        AgentStatus.Live => "Live",
        AgentStatus.Degraded => "Degraded",
        AgentStatus.Failed => "Failed",
        AgentStatus.Idle => "Idle",
        _ => string.IsNullOrWhiteSpace(unknownLabel) ? UnknownAgentLabel : unknownLabel,
    };

    /// <summary>De volledige classlijst voor een status-badge.</summary>
    /// <param name="status">De status.</param>
    /// <returns>Bijvoorbeeld <c>badge badge--live</c>.</returns>
    /// <remarks>
    /// <see cref="AgentStatus.Unknown"/> krijgt geen modifier: de kale <c>.badge</c> in
    /// patterns.css draagt al de neutrale "geen agents"-kleuren uit §8.
    /// </remarks>
    public static string BadgeClass(AgentStatus status) =>
        Modifier(status) is { } modifier ? $"badge badge--{modifier}" : "badge";

    /// <summary>De volledige classlijst voor een statusstip.</summary>
    /// <param name="status">De status.</param>
    /// <returns>Bijvoorbeeld <c>status-dot status-dot--failed</c>.</returns>
    public static string DotClass(AgentStatus status) =>
        Modifier(status) is { } modifier ? $"status-dot status-dot--{modifier}" : "status-dot";

    /// <summary>De classnaam die de rijtint zet, of <c>null</c> als de rij neutraal blijft.</summary>
    /// <param name="status">De status.</param>
    /// <returns><c>data-row--failed</c> bij een mislukking, anders <c>null</c>.</returns>
    /// <remarks>
    /// Alleen <see cref="AgentStatus.Failed"/> heeft een rijtint (§8). Een amber of groene rij
    /// zou de tabel tot een kleurenveld maken en de storing juist minder zichtbaar.
    /// </remarks>
    public static string? RowClass(AgentStatus status) =>
        status == AgentStatus.Failed ? "data-row--failed" : null;

    /// <summary>
    /// De kleur van de statusstip als CSS-verwijzing, voor vlakken die geen eigen classnaam
    /// hebben — de statusverdelingsbalk op het klantoverzicht.
    /// </summary>
    /// <param name="status">De status.</param>
    /// <returns>Bijvoorbeeld <c>var(--status-live-ink)</c>.</returns>
    /// <remarks>
    /// Gebruik dit uitsluitend als <c>background</c> van een segment dat naast een tekstuele
    /// verdeling staat ("2 live · 1 failed"). Een balk zonder die tekst is kleur zonder label.
    /// </remarks>
    public static string DotColorVar(AgentStatus status) => status switch
    {
        AgentStatus.Live => "var(--status-live-ink)",
        AgentStatus.Degraded => "var(--status-degraded-ink)",
        AgentStatus.Failed => "var(--status-failed-ink)",
        AgentStatus.Idle => "var(--status-idle-dot)",
        _ => "var(--status-none-dot)",
    };

    /// <summary>De modifier-naam achter <c>--</c>, of <c>null</c> voor de neutrale basisvorm.</summary>
    /// <param name="status">De status.</param>
    /// <returns><c>live</c>, <c>degraded</c>, <c>failed</c>, <c>idle</c> of <c>null</c>.</returns>
    public static string? Modifier(AgentStatus status) => status switch
    {
        AgentStatus.Live => "live",
        AgentStatus.Degraded => "degraded",
        AgentStatus.Failed => "failed",
        AgentStatus.Idle => "idle",
        _ => null,
    };

    /// <summary>
    /// De vijf statussen in legendavolgorde: eerst wat werkt, dan wat mis is, dan wat rust,
    /// en als laatste wat wij niet weten.
    /// </summary>
    public static IReadOnlyList<AgentStatus> All { get; } =
    [
        AgentStatus.Live,
        AgentStatus.Degraded,
        AgentStatus.Failed,
        AgentStatus.Idle,
        AgentStatus.Unknown,
    ];
}
