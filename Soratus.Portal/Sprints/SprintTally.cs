namespace Soratus.Portal.Sprints;

/// <summary>
/// De twee oordelen die niet uit DevOps komen maar uit een instelling van ons.
/// </summary>
/// <remarks>
/// <para><strong>Deze klasse bestaat omdat de herkomst en de blokkade afgeleid zijn en niet
/// opgeslagen.</strong> Ze volgen uit gegevens die wél in het document staan — de tags en de aanmaker —
/// plus een instelling. Ze meeslaan zou een tweede waarheid zijn naast de gegevens waaruit ze volgen, en
/// de verkeerde van de twee zou degene zijn die niemand bijwerkt. Hetzelfde argument als waarom er geen
/// subtotaal op een verbruiksdocument staat en waarom het opslagpercentage op het contract blijft
/// (punt 34).</para>
///
/// <para>De praktische winst is dat een gewijzigde <see cref="SprintOptions.BlockedMarker"/> of een nieuwe
/// agentidentiteit meteen klopt, ook voor lezingen die er al liggen. Zou het opgeslagen zijn, dan zou de
/// hele lijst pas na de volgende ronde kloppen en zou een operator die net een instelling heeft gewijzigd
/// een kwartier naar het oude antwoord kijken zonder te weten waarom.</para>
///
/// <para>Puur en zonder afhankelijkheden, om dezelfde reden als <see cref="SprintSelection"/>: dit is de
/// plek waar de regel staat, en een regel die alleen via een HTTP-antwoord te bereiken is, is een regel
/// die niet wordt getest.</para>
/// </remarks>
public static class SprintJudgement
{
    /// <summary>
    /// Of dit work item geblokkeerd is (§3.4, statistiek "geblokkeerd").
    /// </summary>
    /// <param name="item">Het work item.</param>
    /// <param name="marker">
    /// Het woord dat blokkade betekent, uit <see cref="SprintOptions.BlockedMarker"/>. Leeg betekent dat
    /// niets geblokkeerd is.
    /// </param>
    /// <returns><c>true</c> als het item een tag met dit woord heeft, of als zijn state zo heet.</returns>
    /// <remarks>
    /// <para><strong>Twee plekken waar dat woord kan staan, en dat is één vraag en niet twee.</strong>
    /// §3.4 noemt <c>Blocked</c> tussen de states, en gemeten heeft dit bord die state niet — het
    /// werkitemtype <c>Task</c> van <c>MBVApp4 MAUI</c> heeft <c>New</c>, <c>Active</c>, <c>Closed</c> en
    /// <c>Removed</c>, en in zijn veldenlijst staat geen blokkadeveld. Op dit bord kan een blokkade dus
    /// alleen een tag zijn. Een ander project met een eigen procestemplate heeft die state misschien wél,
    /// en dan zou een controle die alleen naar tags kijkt precies de statistiek te laag maken die §3.4
    /// vraagt — en een te laag getal is onzichtbaar.</para>
    ///
    /// <para><strong>Gelijkheid en geen <c>Contains</c>.</strong> <c>Not-Blocked</c> bevat <c>Blocked</c>,
    /// en een tag is een waarde en geen zin. Hoofdletterongevoelig, want een tag in DevOps is dat ook.</para>
    /// </remarks>
    public static bool IsBlocked(SprintWorkItem item, string? marker)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (string.IsNullOrWhiteSpace(marker))
        {
            return false;
        }

        var woord = marker.Trim();

        return string.Equals(item.State, woord, StringComparison.OrdinalIgnoreCase)
            || item.Tags.Any(tag => string.Equals(tag, woord, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Of dit work item door een agent of door een mens is aangemaakt (§3.4, "herkomst").
    /// </summary>
    /// <param name="item">Het work item.</param>
    /// <param name="agents">
    /// De identiteiten die als agent gelden, uit <see cref="SprintOptions.AgentIdentities"/>. Leeg betekent
    /// dat we het niet kunnen zien.
    /// </param>
    /// <returns>De herkomst.</returns>
    /// <remarks>
    /// <para><strong>Een lege lijst levert <see cref="WorkItemOrigin.Unknown"/> voor élk item, en dat is
    /// het hele punt van deze methode.</strong> "Handmatig" zou de bewering zijn dat we hebben nagekeken
    /// dat er geen agent bij was, en er is niets om na te kijken zolang er geen agentidentiteit bekend is.
    /// Punt 15 op een enum.</para>
    ///
    /// <para>Er wordt op <see cref="SprintWorkItem.CreatedByUniqueName"/> vergeleken en anders op
    /// <see cref="SprintWorkItem.CreatedByName"/>. Die tweede is een terugval en geen tweede sleutel: de
    /// unieke naam is wat een identiteit vastlegt, de weergavenaam is wat een mens leest en die kan bij
    /// twee identiteiten gelijk zijn. Ontbreekt de unieke naam — dat kan, DevOps stuurt een leeg veld
    /// helemaal niet mee — dan is de weergavenaam het beste dat er is, en dan is een verkeerd positief
    /// beter zichtbaar dan een stil "onbekend".</para>
    ///
    /// <para>Een item zonder aanmaker blijft <see cref="WorkItemOrigin.Unknown"/>, ook als de lijst gevuld
    /// is: er is dan niets vergeleken.</para>
    /// </remarks>
    public static WorkItemOrigin Origin(SprintWorkItem item, IReadOnlyList<string>? agents)
    {
        ArgumentNullException.ThrowIfNull(item);

        var namen = agents?.Where(naam => !string.IsNullOrWhiteSpace(naam)).ToArray() ?? [];

        if (namen.Length == 0)
        {
            return WorkItemOrigin.Unknown;
        }

        var identiteit = item.CreatedByUniqueName ?? item.CreatedByName;

        if (string.IsNullOrWhiteSpace(identiteit))
        {
            return WorkItemOrigin.Unknown;
        }

        return namen.Any(naam =>
            string.Equals(naam.Trim(), identiteit.Trim(), StringComparison.OrdinalIgnoreCase))
            ? WorkItemOrigin.Agent
            : WorkItemOrigin.Manual;
    }
}

/// <summary>
/// De statistieken van één sprint (§3.4, "work items, afgerond, openstaande uren, story points,
/// geblokkeerd").
/// </summary>
/// <param name="Items">Het aantal work items dat werk is: alles behalve verwijderd.</param>
/// <param name="Completed">Hoeveel daarvan afgerond zijn (categorie <c>Completed</c>).</param>
/// <param name="Blocked">Hoeveel daarvan geblokkeerd zijn. Zie <see cref="SprintJudgement.IsBlocked"/>.</param>
/// <param name="Removed">Hoeveel items er verwijderd zijn. Staat ernaast en telt niet mee in <paramref name="Items"/>.</param>
/// <param name="Unclassified">
/// Hoeveel items er een state hebben waarvan de categorie niet is vastgesteld. Hoort nul te zijn; zie de
/// toelichting bij dit type.
/// </param>
/// <param name="OpenHours">De som van de openstaande uren, of <c>null</c> als geen enkel item dat veld heeft.</param>
/// <param name="DoneHours">De som van de gedane uren, of <c>null</c> als geen enkel item dat veld heeft.</param>
/// <param name="StoryPoints">De som van de story points, of <c>null</c> als geen enkel item dat veld heeft.</param>
/// <remarks>
/// <para><strong>De drie sommen zijn <c>decimal?</c> en dat is de belangrijkste eigenschap van dit
/// type.</strong> Het is de invariant van <c>AzureCostReading.Subtotal</c> één niveau hoger: <em>een som
/// bestaat dan en slechts dan als er iets is om op te tellen.</em> Gemeten op 22 augustus 2026 had géén
/// van de zestien work items in <c>Iteration 1</c> een <c>RemainingWork</c>, een <c>CompletedWork</c> of
/// <c>StoryPoints</c> — die sleutels stonden niet in het antwoord van <c>workitemsbatch</c>. Een
/// implementatie die daar <c>Sum()</c> op doet, zet "openstaande uren: 0" op het scherm, en dat is een
/// getal dat er niet is: het betekent "niemand heeft uren ingevuld" en niet "er is geen werk over". Punt
/// 15, en punt 30 in de vorm die daar "een subtotaal bestaat dan en slechts dan als er regels zijn" heet.
/// </para>
///
/// <para><strong>En de keerzijde geldt hier net zo hard: nul mét waarden is een echte nul.</strong> Een
/// sprint waarin alle taken op nul resterende uren staan heeft nul openstaande uren, en dat mag als
/// <c>0</c> op het scherm. Het verschil tussen een som die nul is en een som die niet bestaat is precies
/// wat <c>decimal?</c> hier draagt.</para>
///
/// <para><strong>De aantallen zijn <c>int</c> en niet nullable, en dat is geen inconsistentie.</strong> Een
/// aantal is de uitkomst van een vraag over items die we hebben gelezen: "hoeveel van deze zestien dragen
/// de blokkadetag" heeft het antwoord nul, en dat is gemeten en niet ontbrekend. Of we überhaupt hebben
/// gelezen staat in <see cref="SprintState"/> en niet in deze getallen — dezelfde scheiding als bij de
/// kosten, waar de toestand op het document staat en niet in het bedrag.</para>
///
/// <para><paramref name="Unclassified"/> hoort nul te zijn: de lezing wordt onleesbaar verklaard zodra de
/// categorie van een state niet te bepalen is. Het getal staat er omdat het het gevolg van die fout
/// meetbaar maakt zonder de fout te hoeven uitlokken — en omdat een lezing uit een oudere documentvorm
/// items zonder categorie kan bevatten, en die horen dan niet stil als "niet afgerond" te gelden.</para>
/// </remarks>
public readonly record struct SprintTally(
    int Items,
    int Completed,
    int Blocked,
    int Removed,
    int Unclassified,
    decimal? OpenHours,
    decimal? DoneHours,
    decimal? StoryPoints)
{
    /// <summary>
    /// Telt de statistieken van een sprint op.
    /// </summary>
    /// <param name="items">De work items van de sprint. Mag leeg zijn.</param>
    /// <param name="blockedMarker">Het woord dat blokkade betekent, uit <see cref="SprintOptions.BlockedMarker"/>.</param>
    /// <returns>De statistieken.</returns>
    /// <remarks>
    /// Verwijderde items doen aan niets mee: niet aan het aantal, niet aan "afgerond", niet aan de sommen.
    /// Een verwijderd item is geen werk, en zijn resterende uren zijn geen werk dat nog moet gebeuren.
    /// </remarks>
    public static SprintTally Of(IReadOnlyList<SprintWorkItem> items, string? blockedMarker)
    {
        ArgumentNullException.ThrowIfNull(items);

        var werk = items.Where(item => item.Stage != WorkItemStage.Removed).ToArray();

        return new SprintTally(
            werk.Length,
            werk.Count(item => item.Stage == WorkItemStage.Completed),
            werk.Count(item => SprintJudgement.IsBlocked(item, blockedMarker)),
            items.Count - werk.Length,
            werk.Count(item => item.Stage == WorkItemStage.Unknown),
            Sum(werk, item => item.RemainingWork),
            Sum(werk, item => item.CompletedWork),
            Sum(werk, item => item.StoryPoints));
    }

    /// <summary>
    /// De som van een veld over de items die het hebben, of <c>null</c> als geen enkel item het heeft.
    /// </summary>
    /// <param name="items">De items.</param>
    /// <param name="veld">Het veld.</param>
    /// <returns>De som, of <c>null</c>.</returns>
    /// <remarks>
    /// <strong>Niet <c>items.Sum(veld) ?? 0</c> en niet <c>items.Where(…).Sum()</c>.</strong> Die eerste
    /// maakt van "niemand heeft het ingevuld" een nul; die tweede doet dat ook, want <c>Sum</c> over een
    /// lege verzameling is nul. Er is één plek in deze klasse waar dat verschil wordt bewaard en dit is
    /// hem.
    /// </remarks>
    private static decimal? Sum(
        IReadOnlyList<SprintWorkItem> items,
        Func<SprintWorkItem, decimal?> veld)
    {
        decimal? som = null;

        foreach (var item in items)
        {
            if (veld(item) is { } waarde)
            {
                som = (som ?? 0m) + waarde;
            }
        }

        return som;
    }
}
