using System.Globalization;
using Soratus.Portal.Components.Shared;
using Soratus.Portal.Sprints;

namespace Soratus.Portal.Components.Pages.Klant;

/// <summary>
/// De opmaak van het sprintscherm: één plek voor elke tekst die uit een gegeven volgt (§3.4).
/// </summary>
/// <remarks>
/// <para>Dezelfde afspraak als <see cref="HourText"/> en <see cref="BillingText"/>: de Razor zet een
/// tekst neer en stelt hem niet samen. Wat hier staat is opmaak — de <em>mededelingen</em> staan op het
/// viewmodel (<see cref="Views.SprintNotice"/>), want die horen bij de gegevens en niet bij het scherm.
/// </para>
///
/// <para><strong>Elke methode die een getal opmaakt heeft een <c>decimal?</c>-variant die een streepje
/// geeft.</strong> Dat is niet gemak maar de kern van dit scherm: gemeten had géén van de zestien work
/// items op dit bord een waarde in <c>RemainingWork</c>, <c>CompletedWork</c> of <c>StoryPoints</c>. Er
/// hoort daar een streepje te staan en nooit een nul — en het enige moment waarop dat fout kan gaan is
/// hier, in de opmaak.</para>
/// </remarks>
public static class SprintText
{
    /// <summary>De Nederlandse cultuur, voor het decimaalteken.</summary>
    /// <remarks>Dezelfde als <see cref="ContractText"/> gebruikt, en om dezelfde reden.</remarks>
    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>Het streepje dat "niet ingevuld" betekent.</summary>
    /// <remarks>
    /// Een em-dash en geen <c>0</c>, en niet "onbekend": dezelfde constante en dezelfde reden als
    /// <see cref="HourText.Dash"/>. Op een getalkolom leest een woord als een waarde.
    /// </remarks>
    public const string Dash = "—";

    /// <summary>Het pad naar het sprintscherm van een klant.</summary>
    /// <param name="slug">De klantslug.</param>
    /// <returns>Het pad.</returns>
    /// <remarks>
    /// <see cref="Uri.EscapeDataString(string)"/> op de slug, net als in <see cref="HourText.Path"/>: een
    /// slug is gevalideerd, maar een pad dat een niet-gevalideerde waarde zou kunnen dragen hoort dat niet
    /// ongeëscaped te doen.
    /// </remarks>
    public static string Path(string slug) => $"/klant/{Uri.EscapeDataString(slug)}/sprint";

    /// <summary>Een aantal uren, of een streepje.</summary>
    /// <param name="hours">De uren, of <c>null</c>.</param>
    /// <returns>Bijvoorbeeld <c>3,5 u</c>, of een streepje.</returns>
    public static string Hours(decimal? hours) =>
        hours is { } value ? $"{value.ToString("0.##", Dutch)} u" : Dash;

    /// <summary>Een aantal punten, of een streepje.</summary>
    /// <param name="points">De punten, of <c>null</c>.</param>
    /// <returns>Bijvoorbeeld <c>8</c>, of een streepje.</returns>
    /// <remarks>
    /// Zonder eenheid: "punten" staat in de kolomkop. Story points zijn in de praktijk hele getallen maar
    /// DevOps staat een breuk toe, dus dezelfde <c>0.##</c> als bij uren — een half punt hoort niet stil te
    /// worden afgerond naar een heel punt.
    /// </remarks>
    public static string Points(decimal? points) =>
        points is { } value ? value.ToString("0.##", Dutch) : Dash;

    /// <summary>Een aantal, altijd als getal.</summary>
    /// <param name="count">Het aantal.</param>
    /// <returns>Het getal.</returns>
    /// <remarks>
    /// <strong>Een aantal krijgt nooit een streepje, en dat is het spiegelbeeld van
    /// <see cref="Hours(decimal?)"/>.</strong> "Hoeveel van deze items zijn afgerond" heeft altijd een
    /// antwoord zodra we de items hebben gelezen, en dat antwoord kan nul zijn. Of we hebben gelezen staat
    /// in <see cref="SprintState"/> en niet in dit getal. Zie <see cref="SprintTally"/>.
    /// </remarks>
    public static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);

    /// <summary>Het nummer van een work item, als verwijzing.</summary>
    /// <param name="id">Het nummer.</param>
    /// <returns>Bijvoorbeeld <c>#4566</c>.</returns>
    /// <remarks>
    /// Met een hekje ervoor, want een los getal in een kolom naast andere getallen leest als een waarde.
    /// Er wordt <strong>niet</strong> naar DevOps gelinkt: een link naar een bord waar de klant geen
    /// toegang tot heeft is een link naar een inlogscherm, en dat is een belofte die niet wordt
    /// nagekomen.
    /// </remarks>
    public static string Number(int id) => $"#{id.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// De periode van een sprint in woorden (§3.4, "periode").
    /// </summary>
    /// <param name="start">De eerste dag, of <c>null</c>.</param>
    /// <param name="finish">De laatste dag, of <c>null</c>.</param>
    /// <returns>Bijvoorbeeld <c>1 t/m 31 augustus 2026</c>, of een streepje.</returns>
    /// <remarks>
    /// <para><strong>"t/m" en niet een gedachtestreep, en dat is geen opmaakvoorkeur.</strong> De
    /// einddatum is inclusief: gemeten geeft DevOps <c>2026-08-31T00:00:00Z</c> terug op een verzoek waarin
    /// <c>31 augustus 23:59:59</c> stond, dus het zijn datums en de laatste dag hoort bij de sprint. Een
    /// streepje laat open of de laatste dag meedoet; "t/m" niet.</para>
    ///
    /// <para>Eén van de twee ontbreekt: dan staat er geen halve periode maar een streepje. Een sprint met
    /// alleen een begindatum is geen sprint — zie <see cref="DevOpsIteration.IsDated"/>.</para>
    /// </remarks>
    public static string Period(DateOnly? start, DateOnly? finish)
    {
        if (start is not { } from || finish is not { } through)
        {
            return Dash;
        }

        // Dezelfde maand en hetzelfde jaar: de maand en het jaar één keer noemen. "1 t/m 31 augustus
        // 2026" is wat een mens schrijft, en op een bord met maandsprints is dat elke sprint.
        if (from.Year == through.Year && from.Month == through.Month)
        {
            return $"{from.Day.ToString(CultureInfo.InvariantCulture)} t/m "
                + through.ToString("d MMMM yyyy", Dutch);
        }

        return from.Year == through.Year
            ? $"{from.ToString("d MMMM", Dutch)} t/m {through.ToString("d MMMM yyyy", Dutch)}"
            : $"{from.ToString("d MMMM yyyy", Dutch)} t/m {through.ToString("d MMMM yyyy", Dutch)}";
    }

    /// <summary>
    /// De classnamen van de badge bij een fase (§8).
    /// </summary>
    /// <param name="stage">De fase.</param>
    /// <param name="isBlocked">Of het item geblokkeerd is.</param>
    /// <returns>De classnamen, bijvoorbeeld <c>badge badge--brand</c>.</returns>
    /// <remarks>
    /// <para>§8 geeft de kleuren: New = idle-grijs, Active = merkvlak, Blocked = degraded-amber,
    /// Resolved/Closed = live-groen. Die vier vlakken bestaan al als <c>.badge--*</c> in
    /// <c>patterns.css</c>, dus er komt geen kleur en geen klasse bij — §8 is uitdrukkelijk: verzin geen
    /// nieuwe kleuren.</para>
    ///
    /// <para><strong>Blokkade wint van de fase, en dat is een besluit.</strong> Een geblokkeerd item is in
    /// behandeling én geblokkeerd; op één badge past één vlak, en van die twee is de blokkade wat een mens
    /// moet zien. De statenaam blijft als label staan, dus er verdwijnt geen informatie — en er staat
    /// bovendien een aparte blokkademarkering naast. §8: nooit kleur zonder label.</para>
    /// </remarks>
    public static string StageBadgeClass(WorkItemStage stage, bool isBlocked) => isBlocked
        ? "badge badge--degraded"
        : stage switch
        {
            WorkItemStage.InProgress => "badge badge--brand",
            WorkItemStage.Resolved or WorkItemStage.Completed => "badge badge--live",
            _ => "badge badge--idle",
        };

    /// <summary>
    /// De glyph bij een fase (§1: status nooit alleen door kleur).
    /// </summary>
    /// <param name="stage">De fase.</param>
    /// <param name="isBlocked">Of het item geblokkeerd is.</param>
    /// <returns>De glyph.</returns>
    /// <remarks>
    /// Dezelfde vier glyphs die §8 voor de agentstatussen geeft, want ze betekenen hier hetzelfde soort
    /// ding: ○ nog niets, ◐ bezig of gestremd, ● klaar, ✕ weg. Ze staan <c>aria-hidden</c> op het scherm —
    /// een schermlezer zou bij ✕ "vermenigvuldigingsteken" voorlezen, en het woordlabel staat ernaast.
    /// </remarks>
    public static string StageGlyph(WorkItemStage stage, bool isBlocked) => isBlocked
        ? "◐"
        : stage switch
        {
            WorkItemStage.InProgress => "◐",
            WorkItemStage.Resolved or WorkItemStage.Completed => "●",
            WorkItemStage.Removed => "✕",
            WorkItemStage.Unknown => "–",
            _ => "○",
        };

    /// <summary>
    /// De uitleg bij een fase, voor de tooltip op de badge.
    /// </summary>
    /// <param name="stage">De fase.</param>
    /// <param name="state">De statenaam zoals hij op het bord staat.</param>
    /// <returns>De tekst.</returns>
    /// <remarks>
    /// <para><strong>Deze tooltip bestaat omdat de statenaam van het bord komt en niet van ons.</strong>
    /// Gemeten heeft dit bord <c>New</c>, <c>Active</c>, <c>Closed</c> en <c>Removed</c>; een ander project
    /// met een eigen procestemplate heeft andere woorden. Het label is dat woord — dat is wat een mens op
    /// het bord ziet — en deze tekst zegt in welke fase het portaal het plaatst. Zonder hem is niet te zien
    /// waarom een item dat "Closed" heet meetelt in "afgerond" en een item dat "Resolved" heet niet.</para>
    /// </remarks>
    public static string StageTitle(WorkItemStage stage, string state) => stage switch
    {
        WorkItemStage.Proposed => $"'{state}' is voorgesteld werk: er is nog niet aan begonnen",
        WorkItemStage.InProgress => $"'{state}' is werk in behandeling",
        WorkItemStage.Resolved =>
            $"'{state}' is opgelost maar niet afgesloten, en telt dus niet mee in 'afgerond'",
        WorkItemStage.Completed => $"'{state}' is afgerond werk",
        WorkItemStage.Removed =>
            $"'{state}' is verwijderd en telt niet mee in het aantal work items",
        _ => $"de fase van '{state}' is niet vastgesteld",
    };

    /// <summary>
    /// De herkomst in woorden (§3.4).
    /// </summary>
    /// <param name="origin">De herkomst.</param>
    /// <returns>Het woord.</returns>
    /// <remarks>
    /// <para><strong>"Onbekend" en niet "handmatig" bij <see cref="WorkItemOrigin.Unknown"/>.</strong> Dat
    /// is de hele reden dat die waarde bestaat: er staat in DevOps vandaag niets dat het onderscheid draagt,
    /// dus "handmatig" zou een bewering zijn die niemand heeft gemeten. Zie
    /// <see cref="SprintJudgement.Origin"/>.</para>
    /// </remarks>
    public static string Origin(WorkItemOrigin origin) => origin switch
    {
        WorkItemOrigin.Agent => "agent",
        WorkItemOrigin.Manual => "mens",
        _ => "onbekend",
    };

    /// <summary>De uitleg bij de herkomst, voor de tooltip.</summary>
    /// <param name="origin">De herkomst.</param>
    /// <returns>De tekst.</returns>
    public static string OriginTitle(WorkItemOrigin origin) => origin switch
    {
        WorkItemOrigin.Agent => "dit item is door een agent aangemaakt",
        WorkItemOrigin.Manual => "dit item is door een mens aangemaakt",
        _ => "er is in DevOps niets dat zegt of een agent of een mens dit item heeft aangemaakt; "
            + "zolang er geen agentidentiteit bekend is, is dit voor elk item de uitkomst",
    };

    /// <summary>
    /// Hoe oud een lezing is, in woorden, of de mededeling dat er nooit is gelezen (§3.4).
    /// </summary>
    /// <param name="readAt">Wanneer er is gelezen, of <c>null</c>.</param>
    /// <param name="now">Nu.</param>
    /// <returns>Bijvoorbeeld <c>opgehaald 4 min geleden</c>.</returns>
    /// <remarks>
    /// <para>§3.4 vraagt het tijdstip van laatste ophalen, en §1 van de spec vraagt relatieve tijden in
    /// beeld met de absolute tijd in de tooltip. Dat is hier precies goed: "vier minuten geleden" is het
    /// antwoord op de vraag die een lezer heeft, en het exacte moment hoort erbij voor wie het naast een
    /// logregel legt.</para>
    ///
    /// <para><c>null</c> krijgt een woord en geen streepje: dit staat in de kop en niet in een getalkolom,
    /// en daar is "nooit opgehaald" leesbaarder dan een liggend streepje.</para>
    /// </remarks>
    public static string Age(DateTimeOffset? readAt, DateTimeOffset now) =>
        readAt is { } moment ? $"opgehaald {TimeFormat.Relative(moment, now)}" : "nooit opgehaald";
}
