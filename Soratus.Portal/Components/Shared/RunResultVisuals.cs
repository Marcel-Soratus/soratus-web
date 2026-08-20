using Soratus.Agents.Contracts;

namespace Soratus.Portal.Components.Shared;

/// <summary>
/// De enige plek waar een <see cref="RunResult"/> wordt vertaald naar wat je op het scherm ziet:
/// glyph, woordlabel en classnaam.
/// </summary>
/// <remarks>
/// <para>Een eigen klasse naast <see cref="StatusVisuals"/> en niet een paar methodes erin. Een
/// runafloop en een agentstatus zijn twee verschillende dingen die alleen op elkaar lijken: een
/// agent met een mislukte run kan best <see cref="AgentStatus.Live"/> zijn omdat de run erna wel
/// slaagde. Zouden de twee door één afbeelding gaan, dan gaat iemand op een dag een run als
/// status behandelen.</para>
///
/// <para>De vlakken zijn wél dezelfde: een geslaagde run leent het live-vlak, een mislukte het
/// failed-vlak. Dat is bewust — dezelfde kleur betekent in dit portaal overal hetzelfde, en er
/// komt geen tweede groen bij.</para>
/// </remarks>
public static class RunResultVisuals
{
    /// <summary>De glyph. Altijd samen met het woordlabel tonen, nooit als enige drager.</summary>
    /// <param name="result">De afloop, of <c>null</c> zolang de run loopt.</param>
    /// <returns>✓ ✕ ○ of ▸.</returns>
    /// <remarks>
    /// ✓ en ✕ komen uit de mockup. ○ voor overgeslagen is dezelfde glyph als
    /// <see cref="AgentStatus.Idle"/>, want het betekent hetzelfde: er was niets te doen. ▸ voor
    /// een lopende run staat in geen van beide bronnen; hij leest als "in gang" en is bewust geen
    /// van de vier statusglyphs, zodat een lopende run niet op een afgeronde lijkt.
    ///
    /// <c>null</c> en <see cref="RunResult.Running"/> geven hetzelfde beeld. De runlijst gebruikt
    /// <c>null</c> voor een lopende run — "loopt nog" is geen afloop — en het contract kent
    /// daarnaast de waarde <c>running</c> in het document zelf. Beide horen op het scherm hetzelfde
    /// te zeggen.
    /// </remarks>
    public static string Glyph(RunResult? result) => result switch
    {
        RunResult.Ok => "✓",
        RunResult.Failed => "✕",
        RunResult.Skipped => "○",
        _ => "▸",
    };

    /// <summary>Het woordlabel dat als echte tekst in beeld komt.</summary>
    /// <param name="result">De afloop, of <c>null</c> zolang de run loopt.</param>
    /// <returns>Bijvoorbeeld <c>Mislukt</c>.</returns>
    public static string Label(RunResult? result) => result switch
    {
        RunResult.Ok => "Geslaagd",
        RunResult.Failed => "Mislukt",
        RunResult.Skipped => "Overgeslagen",
        _ => "Loopt",
    };

    /// <summary>De volledige classlijst voor de badge in de resultaatkolom.</summary>
    /// <param name="result">De afloop, of <c>null</c> zolang de run loopt.</param>
    /// <returns>Bijvoorbeeld <c>badge badge--failed</c>.</returns>
    /// <remarks>
    /// Een lopende run krijgt de kale <c>.badge</c>: neutraal grijs, dezelfde vorm als rang 0 in
    /// §8. Geen groen en geen amber, want er is nog niets afgelopen — een kleur zou een uitkomst
    /// suggereren die er niet is.
    /// </remarks>
    public static string BadgeClass(RunResult? result) => result switch
    {
        RunResult.Ok => "badge badge--live",
        RunResult.Failed => "badge badge--failed",
        RunResult.Skipped => "badge badge--idle",
        _ => "badge",
    };

    /// <summary>Of deze afloop de rij een tint geeft.</summary>
    /// <param name="result">De afloop, of <c>null</c> zolang de run loopt.</param>
    /// <returns>
    /// <see cref="AgentStatus.Failed"/> bij een mislukte run, anders <c>null</c>. Doorgeven aan
    /// <c>DataRow.Tint</c>, dat alleen die ene tint kent (§8).
    /// </returns>
    public static AgentStatus? Tint(RunResult? result) =>
        result == RunResult.Failed ? AgentStatus.Failed : null;
}
