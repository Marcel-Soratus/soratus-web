namespace Soratus.Agents.Contracts;

/// <summary>
/// Leidt de status van een agent af uit gepubliceerde feiten.
/// </summary>
/// <remarks>
/// Dit is de belangrijkste regel van het systeem: <em>een agent publiceert nooit zijn eigen
/// status</em>. Een agent die om is kan niet melden dat hij om is, dus elke zelfgemelde status
/// is precies onbetrouwbaar op het moment dat het ertoe doet. Wat een agent wél publiceert zijn
/// feiten — een hartslag, een levenscyclus, een afgeronde run — en die feiten worden hier,
/// buiten de agent om, tot een oordeel gemaakt.
///
/// De regels, in volgorde. De eerste die past wint:
/// <list type="number">
///   <item><description>geen registratiedocument → <see cref="AgentStatus.Unknown"/></description></item>
///   <item><description>laatste afgeronde run is <see cref="RunResult.Failed"/> → <see cref="AgentStatus.Failed"/></description></item>
///   <item><description>levenscyclus is <see cref="AgentLifecycle.IdleWaiting"/> of <see cref="AgentLifecycle.StoppedCleanly"/> én de hartslag is vers → <see cref="AgentStatus.Idle"/></description></item>
///   <item><description>stilte langer dan <see cref="AgentStatusThresholds.Degraded"/> → <see cref="AgentStatus.Degraded"/></description></item>
///   <item><description>anders → <see cref="AgentStatus.Live"/></description></item>
/// </list>
///
/// Die volgorde is niet willekeurig: hij loopt van ernstig naar mild, zodat de ernstigste
/// waarheid wint. Een mislukte run bij een stokkende hartslag levert
/// <see cref="AgentStatus.Failed"/> (rang 4) en niet <see cref="AgentStatus.Degraded"/>
/// (rang 3) — er is aantoonbaar iets misgegaan, en dat is een hardere mededeling dan "hij meldt
/// zich niet". Omgekeerd kan een agent zich niet met <see cref="AgentLifecycle.IdleWaiting"/>
/// uit een mislukte run praten.
///
/// Geen enkele methode in deze klasse leest de klok. <c>now</c> komt altijd als parameter
/// binnen; anders is een drempel van twee minuten niet te testen zonder twee minuten te wachten.
/// </remarks>
public static class AgentStatusCalculator
{
    /// <summary>
    /// Bepaalt de status van één agent.
    /// </summary>
    /// <param name="registration">
    /// Het registratiedocument van de agent, of <c>null</c> als de agent niets publiceert.
    /// </param>
    /// <param name="lastCompletedRun">
    /// De laatste <em>afgeronde</em> run, of <c>null</c> als er nog geen run is afgerond. Een run
    /// die nog op <see cref="RunResult.Running"/> staat hoort hier niet in: die is niet afgerond
    /// en zegt dus nog niets over slagen of falen.
    /// </param>
    /// <param name="now">Het moment waarop wordt geoordeeld, in UTC of met zone.</param>
    /// <returns>De afgeleide status.</returns>
    public static AgentStatus Calculate(
        AgentRegistration? registration,
        RunRecord? lastCompletedRun,
        DateTimeOffset now)
    {
        // 1. Zonder registratie weten we niets. Dat is geen storing en geen "live", en het is
        //    eerlijker dan een van beide verzinnen.
        if (registration is null)
        {
            return AgentStatus.Unknown;
        }

        // 2. Falen wint van alles wat daarna komt. Alleen Failed telt: Ok, Skipped en Running
        //    zijn geen fout, en Skipped ("niets te doen gehad") is dat nadrukkelijk ook niet.
        if (lastCompletedRun is { Result: RunResult.Failed })
        {
            return AgentStatus.Failed;
        }

        var silence = Silence(registration, now);

        // 3. Bewust wachten of netjes gestopt, mits we dat nog recent gehoord hebben.
        if (IsResting(registration.Lifecycle) && silence <= AgentStatusThresholds.Degraded)
        {
            return AgentStatus.Idle;
        }

        // 4. Te lang stil. Let op: dit is óók waar de agent belandt die "netjes gestopt" meldde
        //    en daarna zweeg — zie de opmerkingen bij IsHeartbeatFresh.
        if (silence > AgentStatusThresholds.Degraded)
        {
            return AgentStatus.Degraded;
        }

        // 5. Meldt zich op tijd en de laatste afgeronde run ging goed.
        return AgentStatus.Live;
    }

    /// <summary>
    /// Hoe lang deze agent al zwijgt, gerekend vanaf zijn laatste hartslag.
    /// </summary>
    /// <param name="registration">
    /// Het registratiedocument, of <c>null</c> als de agent niets publiceert.
    /// </param>
    /// <param name="now">Het moment waarop wordt gerekend.</param>
    /// <returns>
    /// De duur van de stilte, of <c>null</c> als er geen registratie is — dan is er geen stilte
    /// van bekende lengte, want er is nooit iets geweest om stil van te vallen. Nooit negatief:
    /// een hartslag die door klokverschil in de toekomst ligt telt als nul.
    /// </returns>
    /// <remarks>
    /// Bestaat zodat het scherm de melding kan laten meeschalen met de stilte. Status alleen is
    /// daarvoor te grof: "meldt zich 3 minuten niet" en "meldt zich 4 uur niet, vermoedelijk
    /// gestopt" zijn allebei <see cref="AgentStatus.Degraded"/>, maar het zijn voor de lezer
    /// twee verschillende berichten. De grens tussen die twee zinnen is een presentatiekeuze en
    /// hoort in het scherm, niet hier; dit levert alleen het getal.
    /// </remarks>
    public static TimeSpan? SilenceFor(AgentRegistration? registration, DateTimeOffset now) =>
        registration is null ? null : Silence(registration, now);

    /// <summary>
    /// Of de hartslag van deze agent vers genoeg is om iets over hem te durven zeggen.
    /// </summary>
    /// <param name="registration">Het registratiedocument, of <c>null</c>.</param>
    /// <param name="now">Het moment waarop wordt gerekend.</param>
    /// <returns><c>false</c> zonder registratie; anders of de stilte binnen de drempel valt.</returns>
    /// <remarks>
    /// "Vers" is hier dezelfde grens als voor degraded: <see cref="AgentStatusThresholds.Degraded"/>.
    /// Dat is een bewuste keuze en geen luiheid.
    ///
    /// Een tweede, ruimere grens voor idle klinkt aantrekkelijk — een wachtende agent doet immers
    /// niets — maar hij schrijft wél hartslagen: die komen van de telemetriebibliotheek en niet
    /// van de werklus, dus een wachtende agent klopt net zo hard door als een werkende. Zwijgt
    /// hij tóch, dan is er iets mis met het proces zelf, en juist dan wil je niet dat zijn eigen
    /// laatste mededeling ("ik wacht even") hem langer groen houdt dan verdiend. Eén grens
    /// betekent bovendien één zin uitleg op het scherm in plaats van twee.
    ///
    /// Het gevolg is expliciet en gewenst: een agent die <see cref="AgentLifecycle.StoppedCleanly"/>
    /// meldde en daarna zweeg staat na de drempel op <see cref="AgentStatus.Degraded"/>, niet
    /// eeuwig op <see cref="AgentStatus.Idle"/>. Hij ís immers weg. Het scherm kan met
    /// <see cref="SilenceFor"/> laten zien dat dat al uren zo is en dat de agent vermoedelijk
    /// bewust is gestopt.
    /// </remarks>
    public static bool IsHeartbeatFresh(AgentRegistration? registration, DateTimeOffset now) =>
        registration is not null && Silence(registration, now) <= AgentStatusThresholds.Degraded;

    /// <summary>
    /// Of de storingsmelder over deze agent een bericht hoort te sturen.
    /// </summary>
    /// <param name="registration">Het registratiedocument, of <c>null</c>.</param>
    /// <param name="lastCompletedRun">De laatste afgeronde run, of <c>null</c>.</param>
    /// <param name="now">Het moment waarop wordt geoordeeld.</param>
    /// <returns><c>true</c> als er gemeld moet worden.</returns>
    /// <remarks>
    /// Scherm en storingsmelder delen deze functie, en dat is een harde eis: lopen ze uiteen, dan
    /// mailt de melder over iets dat het scherm niet toont, of andersom, en dan spreken twee
    /// schermen elkaar tegen.
    ///
    /// De drempel verschilt per status, want de twee statussen zijn verschillend van aard:
    /// <list type="bullet">
    ///   <item><description>
    ///     <see cref="AgentStatus.Failed"/> meldt meteen. Een mislukte run is een afgerond feit;
    ///     die wordt niet vanzelf beter door tien minuten te wachten.
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="AgentStatus.Degraded"/> meldt pas na <see cref="AgentStatusThresholds.Alert"/>.
    ///     Een gemiste hartslag tijdens een uitrol of een korte hapering is geen storing, en een
    ///     melder die dáárover mailt wordt binnen een week weggefilterd.
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="AgentStatus.Unknown"/> meldt niet. We weten niets van deze agent; dat is
    ///     een uitrolvraag, geen storing, en anders mailt de melder over elke agent die nog niet
    ///     bestaat.
    ///   </description></item>
    /// </list>
    /// </remarks>
    public static bool ShouldAlert(
        AgentRegistration? registration,
        RunRecord? lastCompletedRun,
        DateTimeOffset now)
    {
        var status = Calculate(registration, lastCompletedRun, now);

        return status switch
        {
            AgentStatus.Failed => true,
            AgentStatus.Degraded => SilenceFor(registration, now) > AgentStatusThresholds.Alert,
            _ => false,
        };
    }

    /// <summary>Levenscycli waarin niets doen normaal is.</summary>
    private static bool IsResting(AgentLifecycle lifecycle) =>
        lifecycle is AgentLifecycle.IdleWaiting or AgentLifecycle.StoppedCleanly;

    /// <summary>Stilte sinds de laatste hartslag, afgekapt op nul bij klokverschil.</summary>
    private static TimeSpan Silence(AgentRegistration registration, DateTimeOffset now)
    {
        var elapsed = now - registration.LastHeartbeatAt;
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }
}
