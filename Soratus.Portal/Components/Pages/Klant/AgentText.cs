using Soratus.Agents.Contracts;
using Soratus.Portal.Components.Shared;
using Soratus.Portal.Views;

namespace Soratus.Portal.Components.Pages.Klant;

/// <summary>
/// De woorden die de agentschermen delen: labels bij enums, de statusmelding en de manier
/// waarop een stilte in een zin komt te staan.
/// </summary>
/// <remarks>
/// <para>Dit is presentatie, geen rekenwerk. Elk getal komt uit een viewmodel; hier wordt het
/// alleen in de juiste woorden gezet. De klasse staat in de paginamap en niet bij de gedeelde
/// componenten, omdat het geen component is en alleen deze twee schermen hem gebruiken —
/// <c>Agents.razor</c> en <c>AgentDetail.razor</c>.</para>
///
/// <para>Waarom hij bestaat: zonder hem staat "acceptatie" op het ene scherm en "Acceptatie" op
/// het andere, en dat is precies de soort tegenspraak die niemand meldt maar iedereen ziet.</para>
/// </remarks>
internal static class AgentText
{
    /// <summary>De omgeving van een agent in woorden.</summary>
    /// <param name="environment">De omgeving.</param>
    /// <returns>Bijvoorbeeld <c>acceptatie</c>.</returns>
    public static string Environment(AgentEnvironment environment) => environment switch
    {
        AgentEnvironment.Production => "productie",
        AgentEnvironment.Acceptance => "acceptatie",
        _ => "ontwikkeling",
    };

    /// <summary>Waardoor een agent aan het werk gaat, in woorden.</summary>
    /// <param name="kind">De triggersoort.</param>
    /// <returns>Bijvoorbeeld <c>timer</c>.</returns>
    public static string Trigger(TriggerKind kind) => kind switch
    {
        TriggerKind.Timer => "timer",
        TriggerKind.Queue => "queue",
        TriggerKind.Http => "HTTP",
        TriggerKind.Webhook => "webhook",
        TriggerKind.Blob => "blob",
        _ => "handmatig",
    };

    /// <summary>Wat de agent over zijn eigen levenscyclus meldt, in woorden.</summary>
    /// <param name="lifecycle">De levenscyclus.</param>
    /// <returns>Bijvoorbeeld <c>wacht op werk</c>.</returns>
    /// <remarks>
    /// <c>StoppedCleanly</c> heet hier "netjes gestopt" en niet "geschaald naar nul". Op App
    /// Service bestaat schalen naar nul niet; zie <c>fase-0-afwijkingen.md</c> §4.
    /// </remarks>
    public static string Lifecycle(AgentLifecycle lifecycle) => lifecycle switch
    {
        AgentLifecycle.Running => "draait",
        AgentLifecycle.IdleWaiting => "wacht op werk",
        _ => "netjes gestopt",
    };

    /// <summary>
    /// Een stilte zoals hij in een lopende zin thuishoort: "3 minuten", "ruim 4 uur".
    /// </summary>
    /// <param name="silence">Hoe lang de agent al zwijgt.</param>
    /// <returns>De duur in woorden.</returns>
    /// <remarks>
    /// Bewust niet <see cref="TimeFormat.Duration"/>. Die is voor een tabelcel en levert
    /// "142,00 s" of "3 m 0 s" — precies en tabulair, en dat hoort ook zo in een kolom. In een
    /// zin leest dat als een meetwaarde in plaats van als een mededeling. Twee verschillende
    /// doelen, dus twee verschillende functies; het getal komt uit dezelfde bron.
    /// </remarks>
    public static string SilenceWords(TimeSpan silence)
    {
        var minutes = (int)Math.Round(silence.TotalMinutes);

        if (minutes < 1)
        {
            return "minder dan een minuut";
        }

        if (minutes < 60)
        {
            return minutes == 1 ? "een minuut" : $"{minutes} minuten";
        }

        var hours = (int)silence.TotalHours;

        if (hours < 24)
        {
            return hours == 1 ? "ruim een uur" : $"ruim {hours} uur";
        }

        var days = (int)silence.TotalDays;

        return days == 1 ? "ruim een dag" : $"ruim {days} dagen";
    }

    /// <summary>
    /// De statusspecifieke melding onder de agentkop (§3.3): waarom staat deze agent zo, en
    /// wat betekent dat voor het werk.
    /// </summary>
    /// <param name="status">De afgeleide status.</param>
    /// <param name="silence">Hoe lang de agent al zwijgt.</param>
    /// <param name="lastRun">De laatste afgeronde run, voor de details bij een mislukking.</param>
    /// <returns>De melding, of <c>null</c> als er niets te melden is.</returns>
    /// <remarks>
    /// <para><see cref="AgentStatus.Live"/> levert <c>null</c>. §3.3 vraagt een melding bij
    /// degraded, failed en idle en noemt live niet — terecht: een groene balk bij elke gezonde
    /// agent is ruis, en ruis maakt de balk bij de één die stuk is minder zichtbaar.</para>
    ///
    /// <para>De tekst bij <see cref="AgentStatus.Failed"/> beweert alleen wat de run zelf
    /// meldt. "De transactie is teruggedraaid" staat er uitsluitend als
    /// <c>RolledBack</c> waar is; anders staat het er niet, ook niet als vermoeden.</para>
    /// </remarks>
    public static string? StatusNotice(
        AgentStatus status,
        TimeSpan? silence,
        AgentRunSummary? lastRun) => status switch
    {
        AgentStatus.Failed => FailedNotice(lastRun),
        AgentStatus.Degraded => DegradedNotice(silence),
        AgentStatus.Idle =>
            "De agent draait en heeft niets te doen. Dit is normaal en geen storing: er is geen "
            + "werk aangeboden sinds de laatste run.",
        AgentStatus.Unknown =>
            "Wij ontvangen geen telemetrie van deze agent. Hij is nog niet uitgerold of hij meldt "
            + "zich niet, dus wij weten niet hoe hij ervoor staat — en dat is iets anders dan dat "
            + "het goed gaat.",
        _ => null,
    };

    private static string FailedNotice(AgentRunSummary? lastRun)
    {
        var parts = new List<string>
        {
            "De laatste run is niet afgerond. Werk blijft liggen tot dit is opgelost.",
        };

        if (lastRun is { RolledBack: true })
        {
            parts.Add(
                "De transactie is teruggedraaid, dus er staat geen halve stand in de "
                + "doelsystemen.");
        }

        if (lastRun is { ItemsFailed: > 0 } failed)
        {
            parts.Add($"{failed.ItemsFailed} items zijn afgekeurd.");
        }

        if (lastRun is { ErrorMessage: { Length: > 0 } message })
        {
            parts.Add($"Melding van de agent: {message}");
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// De melding bij degraded, meeschalend met de stilte.
    /// </summary>
    /// <remarks>
    /// De grens is <see cref="AgentStatusThresholds.Alert"/> (tien minuten) en geen zelf
    /// gekozen getal. Dat is precies het moment waarop de storingsmelder ons al heeft gemaild
    /// (spec §9). Vóór die grens is een gemiste hartslag meestal een hik — een herstart, een
    /// uitrol, een trage minuut — en loopt het werk door. Ná die grens hebben wij het zelf al
    /// een storing genoemd, en dan mag het scherm niet zachter zijn dan de mail die er al uit
    /// is. Eén grens, twee formuleringen, en geen derde drempel om uit te leggen.
    /// </remarks>
    private static string DegradedNotice(TimeSpan? silence)
    {
        if (silence is not { } value)
        {
            return "Deze agent meldt zich niet op tijd. Soratus kijkt ernaar.";
        }

        var words = SilenceWords(value);

        return value < AgentStatusThresholds.Alert
            ? $"Deze agent meldt zich {words} niet, terwijl wij hem elke "
              + $"{(int)AgentStatusThresholds.HeartbeatInterval.TotalSeconds} seconden verwachten. "
              + "Het proces draait vermoedelijk nog en werk kan gewoon doorlopen; Soratus kijkt ernaar."
            : $"Deze agent meldt zich {words} niet. Zo lang stil betekent bijna altijd dat het "
              + "proces is gestopt — ga ervan uit dat er geen werk meer wordt opgepakt. Soratus "
              + "heeft hier automatisch een melding van gekregen.";
    }
}
