using Soratus.Portal.Data;

namespace Soratus.Portal.Components.Shared;

/// <summary>
/// De enige plek waar een urenstand wordt vertaald naar wat je op het scherm ziet: glyph,
/// woordlabel en classnaam (§3.6, §8).
/// </summary>
/// <remarks>
/// <para><strong>Een eigen klasse naast <see cref="StatusVisuals"/> en niet een paar methodes
/// erin.</strong> Dat is dezelfde afweging die <see cref="RunResultVisuals"/> al maakt, en om
/// dezelfde reden: een maand die boven zijn bundel loopt is geen agent die het niet meer doet.
/// Zouden de twee door één afbeelding gaan, dan gaat iemand op een dag een urenstand aan
/// <c>StatusBadge</c> geven of een <see cref="HourMonthStatus"/> in de ernstsortering van het
/// klantoverzicht laten meedoen.</para>
///
/// <para>Punt 19 van <c>docs/agent-portal/fase-0-afwijkingen.md</c> vraagt de regel voor
/// <see cref="HourMonthStatus.NoBundleAgreed"/> in <c>StatusVisuals</c>. Hij staat hier in plaats
/// daarvan, met de bovenstaande reden; wat er niet verandert is wat die regel zegt — rang 0
/// hergebruiken, geen nieuwe kleur en geen nieuwe rang.</para>
///
/// <para><strong>De vlakken zijn wél dezelfde vier uit §8.</strong> Binnen bundel leent het
/// live-vlak, boven bundel het degraded-vlak, niets geboekt het idle-vlak, en geen bundel de kale
/// <c>.badge</c> die rang 0 draagt. Dezelfde kleur betekent in dit portaal overal hetzelfde, en er
/// komt geen tweede groen bij.</para>
///
/// <para><strong>Boven bundel is amber en niet rood.</strong> Er is niets stuk: uren boven de
/// bundel zijn een afspraak (§3.5) en worden achteraf gefactureerd (§3.7). Rood zou zeggen dat er
/// iets fout is gegaan, en dan gaat een klant bellen over een factuurregel die hij zelf heeft
/// aangevraagd.</para>
/// </remarks>
public static class HourStatusVisuals
{
    /// <summary>De glyph van een maandstand. Altijd samen met het woordlabel tonen.</summary>
    /// <param name="status">De stand.</param>
    /// <returns>● ◐ ○ of –.</returns>
    public static string Glyph(HourMonthStatus status) => status switch
    {
        HourMonthStatus.WithinBundle => "●",
        HourMonthStatus.OverBundle => "◐",
        HourMonthStatus.NothingBooked => "○",
        _ => "–",
    };

    /// <summary>Het woordlabel van een maandstand, zoals §3.6 hem noemt.</summary>
    /// <param name="status">De stand.</param>
    /// <returns>Bijvoorbeeld <c>Boven bundel</c>.</returns>
    /// <remarks>
    /// De eerste drie staan letterlijk in §3.6. De vierde is punt 19 in woorden: er is niets mis,
    /// er is alleen niets om aan te toetsen — dus geen "Boven bundel" en ook geen "Binnen bundel",
    /// want beide zouden een afspraak noemen die niet bestaat.
    /// </remarks>
    public static string Label(HourMonthStatus status) => status switch
    {
        HourMonthStatus.WithinBundle => "Binnen bundel",
        HourMonthStatus.OverBundle => "Boven bundel",
        HourMonthStatus.NothingBooked => "Niets geboekt",
        _ => "Geen bundel",
    };

    /// <summary>De volledige classlijst voor de badge in de statuskolom van de maandtabel.</summary>
    /// <param name="status">De stand.</param>
    /// <returns>Bijvoorbeeld <c>badge badge--degraded</c>.</returns>
    public static string BadgeClass(HourMonthStatus status) => status switch
    {
        HourMonthStatus.WithinBundle => "badge badge--live",
        HourMonthStatus.OverBundle => "badge badge--degraded",
        HourMonthStatus.NothingBooked => "badge badge--idle",
        _ => "badge",
    };

    /// <summary>
    /// De glyph van een fiatteringsstand.
    /// </summary>
    /// <param name="status">De stand.</param>
    /// <returns>● ◐ of ✕.</returns>
    /// <remarks>
    /// <strong>Alleen voor het operatorscherm.</strong> <see cref="HourEntryStatus"/> komt op geen
    /// enkel klanttype voor (zie <c>Views.CustomerHourRow</c>), dus een klantscherm heeft geen
    /// waarde om hier in te stoppen. Dat is geen afspraak die deze klasse afdwingt maar een die het
    /// typesysteem afdwingt, en dat is de bedoeling.
    /// </remarks>
    public static string Glyph(HourEntryStatus status) => status switch
    {
        HourEntryStatus.Approved => "●",
        HourEntryStatus.Pending => "◐",
        _ => "✕",
    };

    /// <summary>Het woordlabel van een fiatteringsstand.</summary>
    /// <param name="status">De stand.</param>
    /// <returns>Bijvoorbeeld <c>Te fiatteren</c>.</returns>
    public static string Label(HourEntryStatus status) => status switch
    {
        HourEntryStatus.Approved => "Gefiatteerd",
        HourEntryStatus.Pending => "Te fiatteren",
        _ => "Afgewezen",
    };

    /// <summary>De volledige classlijst voor de badge in de standkolom van de specificatie.</summary>
    /// <param name="status">De stand.</param>
    /// <returns>Bijvoorbeeld <c>badge badge--degraded</c>.</returns>
    /// <remarks>
    /// Afgewezen krijgt de kale <c>.badge</c> en niet het failed-vlak. Rood is in §8 voor een
    /// storing, en een afgewezen regel is geen storing maar een besluit van een mens (punt 17).
    /// Amber voor "te fiatteren" is wél op zijn plek: daar moet iemand iets mee.
    /// </remarks>
    public static string BadgeClass(HourEntryStatus status) => status switch
    {
        HourEntryStatus.Approved => "badge badge--live",
        HourEntryStatus.Pending => "badge badge--degraded",
        _ => "badge",
    };

    /// <summary>
    /// De classlijst van het bronlabel in de specificatie.
    /// </summary>
    /// <param name="source">De bron.</param>
    /// <returns><c>badge</c> of <c>badge badge--brand</c>.</returns>
    /// <remarks>
    /// <para>§8, laatste regel over uren: "Bronnen urenregels: Portaal = neutraal grijs, MCP/Claude
    /// Code en Azure DevOps = merkvlak <c>#eef2ff</c> met <c>#1B1F8C</c>." Dat is precies
    /// <c>.badge</c> tegenover <c>.badge--brand</c>; er komt hier dus geen kleur bij.</para>
    ///
    /// <para><strong>Een badge en geen chip.</strong> §8 reserveert de pilvorm (radius 999px) voor
    /// suggestiechips en geeft al het andere 4 of 6px. Dit is een label bij een gegeven, geen
    /// filter waar je op kunt klikken.</para>
    ///
    /// <para>Er staat bewust geen glyph bij een bron. Glyphs zijn in §8 de tweede drager van
    /// <em>status</em>; een bron is geen status, en een glyph erbij zou suggereren dat de ene bron
    /// beter is dan de andere.</para>
    /// </remarks>
    public static string SourceClass(HourEntrySource source) =>
        source == HourEntrySource.Portal ? "badge" : "badge badge--brand";

    /// <summary>
    /// De vier maandstanden in de volgorde waarin ze op een legenda horen.
    /// </summary>
    /// <remarks>
    /// Eerst wat goed gaat, dan wat aandacht vraagt, dan wat leeg is, en als laatste wat we niet
    /// kunnen beoordelen. Dezelfde ordening als <see cref="StatusVisuals.All"/>.
    /// </remarks>
    public static IReadOnlyList<HourMonthStatus> AllMonths { get; } =
    [
        HourMonthStatus.WithinBundle,
        HourMonthStatus.OverBundle,
        HourMonthStatus.NothingBooked,
        HourMonthStatus.NoBundleAgreed,
    ];
}
