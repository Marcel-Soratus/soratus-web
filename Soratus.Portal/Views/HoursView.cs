using Soratus.Portal.Data;

namespace Soratus.Portal.Views;

/// <summary>
/// De teksten van het urenscherm die geen gegeven zijn maar een mededeling.
/// </summary>
/// <remarks>
/// Eén plek, en het viewmodel draagt de tekst mee in plaats van dat de Razor hem verzint. Dezelfde
/// afspraak als bij <see cref="ContractNotice"/>, en hier met een extra reden: de klantteksten en de
/// operatorteksten van dit scherm mogen elkaar niet naderen. Een tekst die op beide schermen uit
/// dezelfde constante komt, is een tekst waarvan iemand hem op één plek kan aanpassen en op het
/// verkeerde scherm laten belanden.
/// </remarks>
public static class HoursNotice
{
    /// <summary>
    /// Waarom een klant hier niets kan invullen.
    /// </summary>
    /// <remarks>
    /// <para><strong>Deze tekst zegt niets over fiatteren, en dat is de hele opgave.</strong> De
    /// acceptatie van fase 3 is dat de klant niets van die stroom ziet. Een uitleg als "uren worden na
    /// akkoord van Soratus toegevoegd" zou aan alle eisen van eerlijkheid voldoen en precies de fout
    /// zijn: dan weet de klant dat er een wachtrij is, en dan is de volgende vraag hoe lang die is.
    /// </para>
    ///
    /// <para>Wat er wél staat is waar: Soratus houdt de uren bij, en wie iets ziet dat niet klopt kan
    /// dat melden. Dat is dezelfde vorm als <see cref="ContractNotice.ReadOnly"/>.</para>
    /// </remarks>
    public const string CustomerReadOnly =
        "Deze urenspecificatie wordt door Soratus bijgehouden en is hier alleen te lezen. Zie je een " +
        "regel die niet klopt, laat het ons weten.";

    /// <summary>
    /// Dat het maandtotaal de som is van de regels eronder.
    /// </summary>
    /// <remarks>
    /// Staat op beide schermen. Voor de klant is het de uitleg waarom het getal in de maandtabel te
    /// controleren is; voor de operator is het de regel waar hij zich aan houdt. Het is bovendien de
    /// enige zin die deze eigenschap uitspreekt — en een eigenschap die je nergens opschrijft, is een
    /// eigenschap die iemand met de beste bedoelingen sloopt.
    /// </remarks>
    public const string TotalIsTheSum =
        "Het maandtotaal is de som van de regels in de specificatie van die maand.";

    /// <summary>
    /// De vaste regel uit §5, voor de operator.
    /// </summary>
    public const string PendingRule =
        "Regels die via Claude Code (MCP) of Azure DevOps binnenkomen staan op 'te fiatteren' en " +
        "tellen pas mee in het maandtotaal en in de facturatie na akkoord van Soratus.";

    /// <summary>
    /// Wat een correctie is en wat hij niet is.
    /// </summary>
    /// <remarks>
    /// Dit is besluit 16 in één zin voor de operator. Zonder deze tekst is niet te zien waarom een
    /// correctie als rij in de specificatie opduikt, en dan gaat iemand hem "opruimen".
    /// </remarks>
    public const string CorrectionIsAnEntry =
        "Een correctie wordt vastgelegd als een extra gefiatteerde regel met de categorie " +
        "'Correctie', en mag negatief zijn. Het maandtotaal blijft daarmee de som van de regels, en " +
        "de correctie blijft terug te vinden met wie hem heeft gemaakt en waarom.";

    /// <summary>
    /// Dat fiatteren niet terug te draaien is.
    /// </summary>
    /// <remarks>
    /// Staat bij de fiatteerknop en niet ergens onderaan. Een operator die dit pas leest nadat hij
    /// heeft geklikt, leest het te laat.
    /// </remarks>
    public const string ApprovalIsFinal =
        "Fiatteren kan niet ongedaan worden gemaakt: een gefiatteerd uur blijft in het maandtotaal " +
        "staan. Moet het er toch af, zet er dan een correctie tegenover.";

    /// <summary>
    /// Dat een afgewezen regel blijft staan.
    /// </summary>
    public const string RejectionIsKept =
        "Een afgewezen regel wordt niet verwijderd. Hij blijft met de reden erbij bewaard, telt niet " +
        "mee en is voor de klant niet zichtbaar.";

    /// <summary>
    /// Dat er geen urenbundel in het contract staat.
    /// </summary>
    /// <remarks>
    /// <para>Dit is de vierde stand uit <see cref="HourMonthStatus.NoBundleAgreed"/> in woorden, en
    /// de reden dat besluit 15 een tekst nodig heeft. Zonder deze zin staat er een streepje in de
    /// saldokolom en is niet te zien of dat betekent "nog niets berekend" of "niets om te berekenen".
    /// </para>
    ///
    /// <para>Voor beide rollen dezelfde zin. Een klant zonder afgesproken bundel hoort te weten dat
    /// zijn uren nergens tegen worden afgezet; dat is geen intern gegeven maar zijn eigen contract.
    /// </para>
    /// </remarks>
    public const string NoBundle =
        "In het contract staat geen urenbundel per maand. Er is daarom geen saldo te berekenen; de " +
        "geboekte uren staan er zonder tegoed tegenover.";
}

/// <summary>
/// De Nederlandse dag waarop een urenregel is vastgelegd.
/// </summary>
/// <remarks>
/// Eén functie, gebruikt door beide projecties. Zouden ze elk zelf omrekenen, dan bestaan er twee
/// definities van "welke dag was dat" en lopen die op de zomertijdgrens uiteen — hetzelfde patroon dat
/// bij de knip op <c>msg</c> al met drie kopieën van één regel is misgegaan. Opslag blijft UTC, weergave
/// is Nederlandse tijd; dat is punt 7 van de fase-0-afwijkingen.
/// </remarks>
internal static class HourDay
{
    /// <summary>
    /// De dag waarop deze regel is vastgelegd, in <see cref="PortalTimeZone.Display"/>.
    /// </summary>
    /// <param name="entry">De regel.</param>
    /// <returns>De dag.</returns>
    internal static DateOnly Of(HourEntryDocument entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(entry.CreatedAt, PortalTimeZone.Display).DateTime);
    }
}

/// <summary>
/// De labels van de bronnen van een urenregel (§3.6, §8).
/// </summary>
/// <remarks>
/// De teksten komen uit de mockup en uit §8, waar staat dat Portaal neutraal grijs is en MCP en
/// DevOps een merkvlak krijgen. Alleen de tekst staat hier; de kleuren staan bij de andere tokens in
/// <c>Components/Shared</c>, zodat er één plek is waar §8 wordt uitgevoerd.
/// </remarks>
public static class HourSourceLabels
{
    /// <summary>
    /// Het label van een bron, zoals het in de bronkolom hoort te staan.
    /// </summary>
    /// <param name="source">De bron.</param>
    /// <returns>Het label.</returns>
    public static string Of(HourEntrySource source) => source switch
    {
        HourEntrySource.Portal => "Portaal",
        HourEntrySource.Mcp => "MCP · Claude Code",
        HourEntrySource.DevOps => "Azure DevOps",
        _ => "Onbekend",
    };
}

/// <summary>
/// Eén regel in de urenspecificatie zoals de klant hem ziet (§3.6).
/// </summary>
/// <remarks>
/// <para><strong>Dit type heeft geen <c>Status</c>, en dat is de kern van fase 3.</strong> De
/// acceptatie eist dat de klant niets van de fiatteringsstroom ziet. Een statusveld dat op elke
/// klantrij <c>Approved</c> zou zijn is geen informatie maar een verklikker: het woord staat dan in de
/// paginabron, en daaruit volgt dat er andere waarden bestaan. Dezelfde vorm als bij
/// <see cref="CustomerLogLine"/> (§12), <see cref="CustomerRunRow"/> (§14),
/// <see cref="CustomerAgentsView"/> (§9) en de contractmarge — voor de vijfde keer, en om dezelfde
/// reden: wat er niet op het type staat kan niet lekken, ook niet als iemand er over een half jaar een
/// tooltip bij zet.</para>
///
/// <para>Er staat ook geen etag op (dat is een schrijfvoorwaarde), geen <c>createdBy</c>, geen
/// <c>approvedBy</c> en geen afwijzingsreden. Van die laatste drie is <c>approvedBy</c> het
/// gevaarlijkste: die zou verraden dát er is gefiatteerd, en door wie.</para>
///
/// <para><strong>Wat er wél op staat en waar te twisten valt: de bron.</strong> §3.6 noemt de
/// bronkolom expliciet in de specificatie, en §2 zegt over "Koppelingen (MCP/DevOps-details)" nee
/// voor de klant. Die twee lijken te botsen. Ze doen het niet: §2 gaat over de koppelingenkaart uit
/// §3.6 — de servernaam, de toolsignatuur, de DevOps-mapping — en dat is iets anders dan het label
/// "MCP · Claude Code" bij een regel. Zie het rapport van fase 3, waar dit als leesbeslissing staat.
/// </para>
///
/// <para><strong>En de reden dat de bron er hoe dan ook moet staan:</strong> een correctie is een
/// gewone regel met categorie <see cref="HourCategories.Correction"/> (besluit 16). Zou de klant die
/// regel niet kunnen onderscheiden, of erger, helemaal niet zien, dan is de som van de regels op zijn
/// scherm niet meer het maandtotaal boven de tabel — en dat is precies de eigenschap die fase 3
/// oplevert.</para>
/// </remarks>
public sealed record CustomerHourRow
{
    /// <summary>
    /// De Nederlandse dag waarop de regel is vastgelegd.
    /// </summary>
    /// <remarks>
    /// <para>De kolomkop hoort <strong>Geboekt</strong> te zijn en niet "Datum". Dit is niet de dag
    /// waarop het werk is gedaan — die kent een urenregel niet, zie
    /// <see cref="HourEntryDocument.CreatedAt"/> — en "Datum" belooft dat wel. De werkperiode staat in
    /// <see cref="MonthLabel"/>.</para>
    ///
    /// <para>Een <see cref="DateOnly"/> en niet het volledige moment: het tijdstip waarop iemand een
    /// uur in de administratie zette zegt de klant niets, en een tooltip met 15:04:05 erin nodigt uit
    /// tot de aanname dat het over het werk gaat. De operator krijgt het moment wél; zie
    /// <see cref="OperatorHourRow.RecordedAt"/>.</para>
    /// </remarks>
    public required DateOnly RecordedOn { get; init; }

    /// <summary>De maand waarop de uren zijn geboekt, als <c>yyyy-MM</c>.</summary>
    public required string Month { get; init; }

    /// <summary>De maand als label, bijvoorbeeld <c>augustus 2026</c>.</summary>
    public required string MonthLabel { get; init; }

    /// <summary>De categorie.</summary>
    public required string Category { get; init; }

    /// <summary>
    /// De omschrijving, afgekapt op de eerste regelovergang.
    /// </summary>
    /// <remarks>
    /// Zie <see cref="HourEntryDocument.Note"/>: dit is vrije tekst die bij een MCP-regel door Claude
    /// Code is geschreven, en daarmee dezelfde soort veld als <c>msg</c> op een logregel. De knip
    /// gebeurt in <see cref="From"/> met dezelfde functie als daar, en niet met een eigen kopie.
    /// </remarks>
    public required string Note { get; init; }

    /// <summary>Wie de uren op zijn naam heeft.</summary>
    public required string By { get; init; }

    /// <summary>Het aantal uren. Negatief bij een correctie naar beneden.</summary>
    public required decimal Hours { get; init; }

    /// <summary>De bron.</summary>
    public required HourEntrySource Source { get; init; }

    /// <summary>Het label van de bron.</summary>
    public required string SourceLabel { get; init; }

    /// <summary>Of dit een handmatige correctie is.</summary>
    public bool IsCorrection =>
        string.Equals(Category, HourCategories.Correction, StringComparison.Ordinal);

    /// <summary>
    /// Projecteert een urenregel naar de klantvariant.
    /// </summary>
    /// <param name="entry">De regel uit de opslag.</param>
    /// <returns>De rij zonder enig spoor van de fiatteringsstroom.</returns>
    /// <remarks>
    /// Een expliciete projectie en geen automatische mapping, om dezelfde reden als bij
    /// <see cref="CustomerRunRow.From"/>: komt er morgen een veld bij op
    /// <see cref="HourEntryDocument"/>, dan komt het hier niet stilzwijgend mee. Iemand moet er een
    /// regel voor schrijven, en dat is het moment waarop de vraag "mag de klant dit zien" hoort te
    /// vallen.
    /// </remarks>
    internal static CustomerHourRow From(HourEntryDocument entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new CustomerHourRow
        {
            RecordedOn = HourDay.Of(entry),
            Month = entry.Month,
            MonthLabel = HourMonths.Label(entry.Month),
            Category = entry.Category,
            Note = CustomerMessage.FirstLine(entry.Note),
            By = entry.By,
            Hours = entry.Hours,
            Source = entry.Source,
            SourceLabel = HourSourceLabels.Of(entry.Source),
        };
    }
}

/// <summary>
/// Eén regel in de urenspecificatie zoals de operator hem ziet en beoordeelt (§3.6).
/// </summary>
/// <remarks>
/// Dit is de variant met de fiatteringsstand, de etag en de sporen van wie wat wanneer deed. Er is
/// bewust geen gemeenschappelijk basistype met <see cref="CustomerHourRow"/>: bij de runs kon dat
/// (§14) omdat de kolommen daar identiek zijn en alleen één tooltip verschilt, maar hier verschillen de
/// tabellen echt — de operatortabel heeft een actiekolom, een amberkleurige rij en een tweede lijst
/// eronder. Dat is dezelfde situatie als bij de logs, waar §12 om die reden twee losse typen heeft.
/// </remarks>
public sealed record OperatorHourRow
{
    /// <summary>De documentsleutel. Gaat mee bij fiatteren en afwijzen.</summary>
    public required string EntryId { get; init; }

    /// <summary>De Nederlandse dag waarop de regel is vastgelegd. Kolomkop <strong>Geboekt</strong>.</summary>
    public required DateOnly RecordedOn { get; init; }

    /// <summary>
    /// Het exacte moment van vastleggen, in UTC.
    /// </summary>
    /// <remarks>
    /// Alleen op het operatortype, voor de tooltip bij de dag (§1: relatieve tijd in beeld, absolute in
    /// de tooltip). Bij een te fiatteren regel is dit de leeftijd van de wachtrij, en dat is de
    /// informatie waar de operator naar handelt.
    /// </remarks>
    public required DateTimeOffset RecordedAt { get; init; }

    /// <summary>De maand waarop de uren zijn geboekt, als <c>yyyy-MM</c>.</summary>
    public required string Month { get; init; }

    /// <summary>De maand als label.</summary>
    public required string MonthLabel { get; init; }

    /// <summary>De categorie.</summary>
    public required string Category { get; init; }

    /// <summary>
    /// De omschrijving, onbewerkt.
    /// </summary>
    /// <remarks>
    /// Niet geknipt, anders dan bij <see cref="CustomerHourRow.Note"/>. Dezelfde keuze als bij een
    /// logregel: de operator hoort te lezen wat er werkelijk in het document staat, ook als de
    /// koppeling zich niet aan "één zin" heeft gehouden — dat is juist de informatie waarmee hij die
    /// koppeling kan laten repareren.
    /// </remarks>
    public required string Note { get; init; }

    /// <summary>Wie de uren op zijn naam heeft.</summary>
    public required string By { get; init; }

    /// <summary>Het aantal uren.</summary>
    public required decimal Hours { get; init; }

    /// <summary>De bron.</summary>
    public required HourEntrySource Source { get; init; }

    /// <summary>Het label van de bron.</summary>
    public required string SourceLabel { get; init; }

    /// <summary>De fiatteringsstand.</summary>
    public required HourEntryStatus Status { get; init; }

    /// <summary>Wie de regel heeft weggeschreven: een operator of de naam van de koppeling.</summary>
    public string? CreatedBy { get; init; }

    /// <summary>Wanneer de regel is weggeschreven.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Wanneer hij is gefiatteerd, of <c>null</c>.</summary>
    public DateTimeOffset? ApprovedAt { get; init; }

    /// <summary>Welke operator hem heeft gefiatteerd, of <c>null</c>.</summary>
    public string? ApprovedBy { get; init; }

    /// <summary>Wanneer hij is afgewezen, of <c>null</c>.</summary>
    public DateTimeOffset? RejectedAt { get; init; }

    /// <summary>Welke operator hem heeft afgewezen, of <c>null</c>.</summary>
    public string? RejectedBy { get; init; }

    /// <summary>Waarom hij is afgewezen, of <c>null</c>.</summary>
    public string? RejectionReason { get; init; }

    /// <summary>
    /// De idempotentiesleutel van de koppeling, of <c>null</c> bij een portaalregel.
    /// </summary>
    /// <remarks>
    /// Zichtbaar voor de operator omdat dit het enige is waarmee een regel aan een MCP-aanroep of een
    /// work item te knopen is bij de vraag "waar komt dit uur vandaan".
    /// </remarks>
    public string? ExternalId { get; init; }

    /// <summary>
    /// De etag. Gaat mee bij fiatteren en afwijzen, zodat er niets wordt beoordeeld wat intussen is
    /// veranderd.
    /// </summary>
    public string? ETag { get; init; }

    /// <summary>Of dit een handmatige correctie is.</summary>
    public bool IsCorrection =>
        string.Equals(Category, HourCategories.Correction, StringComparison.Ordinal);

    /// <summary>Of deze regel meetelt in het maandtotaal.</summary>
    public bool Counts => Status == HourEntryStatus.Approved;

    /// <summary>
    /// Of er bij deze regel een fiatteerknop hoort te staan.
    /// </summary>
    /// <remarks>
    /// Uit <see cref="HourEntryTransitions"/> en niet uit een eigen vergelijking. Anders staat er een
    /// knop die een melding oplevert, of ontbreekt er een bij iets wat wel mag — en dat verschil zou
    /// dan pas bij het klikken blijken.
    /// </remarks>
    public bool CanApprove => HourEntryTransitions.CanApprove(Status);

    /// <summary>Of er bij deze regel een afwijsknop hoort te staan.</summary>
    public bool CanReject => HourEntryTransitions.CanReject(Status);

    /// <summary>
    /// Projecteert een urenregel naar de operatorvariant.
    /// </summary>
    /// <param name="entry">De regel uit de opslag.</param>
    /// <returns>De rij met alles erop.</returns>
    internal static OperatorHourRow From(HourEntryDocument entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new OperatorHourRow
        {
            EntryId = entry.Id,
            RecordedOn = HourDay.Of(entry),
            RecordedAt = entry.CreatedAt,
            Month = entry.Month,
            MonthLabel = HourMonths.Label(entry.Month),
            Category = entry.Category,
            Note = entry.Note,
            By = entry.By,
            Hours = entry.Hours,
            Source = entry.Source,
            SourceLabel = HourSourceLabels.Of(entry.Source),
            Status = entry.Status,
            CreatedBy = entry.CreatedBy,
            CreatedAt = entry.CreatedAt,
            ApprovedAt = entry.ApprovedAt,
            ApprovedBy = entry.ApprovedBy,
            RejectedAt = entry.RejectedAt,
            RejectedBy = entry.RejectedBy,
            RejectionReason = entry.RejectionReason,
            ExternalId = entry.ExternalId,
            ETag = entry.ETag,
        };
    }
}

/// <summary>
/// Eén maandrij op het operatorscherm: de stand plus wat er nog te fiatteren ligt (§3.6).
/// </summary>
/// <remarks>
/// <para><strong>Dit type bestaat alleen om <see cref="PendingHours"/> ergens neer te zetten.</strong>
/// §3.6 vraagt in de maandtabel "operator-only: + x u te fiatteren". Dat getal had een nullable veld op
/// <see cref="HourBalance"/> kunnen worden met een <c>@if</c> eromheen; dan staat het in het viewmodel
/// van de klant en gaat het mee over elke serialisatiegrens. Nu bestaat het alleen op het pad waar het
/// hoort.</para>
///
/// <para>De maandstand zelf komt uit <see cref="HourBalance"/> en is voor beide rollen exact hetzelfde
/// object uit dezelfde berekening. Dat is de andere helft van de acceptatie-eis: het maandtotaal dat de
/// operator ziet is niet een tweede telling naast die van de klant, het is dezelfde.</para>
/// </remarks>
public sealed record OperatorMonthRow
{
    /// <summary>De stand van deze maand: bundel, geboekt, saldo, status.</summary>
    public required HourBalance Balance { get; init; }

    /// <summary>Hoeveel uren er in deze maand nog te fiatteren liggen. Nul als er niets ligt.</summary>
    public required decimal PendingHours { get; init; }

    /// <summary>Uit hoeveel regels die uren komen.</summary>
    public required int PendingCount { get; init; }

    /// <summary>Of er in deze maand iets te fiatteren ligt.</summary>
    public bool HasPending => PendingCount > 0;
}

/// <summary>
/// Het urenscherm zoals de klant het ziet (§3.6).
/// </summary>
/// <remarks>
/// <para><strong>Wat er niet op staat: elk spoor van fiatteren.</strong> Geen aantal te fiatteren
/// regels, geen "wacht op akkoord", geen lege sectie waar iets zou kunnen staan, en geen veld dat nul
/// is omdat het toevallig nul is. De rijen zijn <see cref="CustomerHourRow"/>, en dat type heeft geen
/// stand; de maanden zijn <see cref="HourBalance"/>, en dat type heeft geen te-fiatteren-teller. Er is
/// dus geen plek in dit viewmodel waar die informatie in zou kunnen belanden, ook niet per ongeluk.
/// </para>
///
/// <para><strong>De specificatie en de maandtabel horen bij elkaar op te tellen.</strong>
/// <see cref="Entries"/> zijn precies de regels die de maanden in <see cref="Months"/> optellen (of,
/// bij een gekozen maand, die ene maand). Dat is geen toeval maar hetzelfde antwoord van dezelfde
/// query: de som en de rijen komen uit één lezing. Zou de tabel uit een aparte aggregatie komen, dan
/// bestaat het geval waarin het totaal en de rijen niet overeenkomen, en dan is er geen manier voor de
/// klant om te zien welke van de twee klopt.</para>
/// </remarks>
public sealed record CustomerHoursView
{
    /// <summary>De klantslug.</summary>
    public required string CustomerId { get; init; }

    /// <summary>De klantnaam, voor de kop.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Wanneer deze weergave is opgebouwd.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>Of dit de interne beheerklant is. Bepaalt hoe het tarief te lezen is.</summary>
    public required bool IsInternal { get; init; }

    /// <summary>
    /// De urenbundel per maand uit het contract, of <c>null</c> als er geen bundel is vastgelegd.
    /// </summary>
    /// <remarks><c>null</c> is niet nul; zie <see cref="ContractDocument.BundledHours"/>.</remarks>
    public decimal? BundledHours { get; init; }

    /// <summary>
    /// Het uurtarief buiten de bundel, of <c>null</c> als er geen tarief is vastgelegd.
    /// </summary>
    /// <remarks>
    /// Staat op de klantweergave omdat §3.5 het contract met tarief aan de klant leest, en omdat de
    /// mededeling "x u boven bundel" zonder tarief niet af te maken is. De <em>marge</em> op de
    /// Azure-kosten staat er niet en komt hier ook niet; dat is §2.
    /// </remarks>
    public decimal? HourlyRate { get; init; }

    /// <summary>
    /// De maanden in de weergave, oudste eerst. Bij de standaardweergave precies één.
    /// </summary>
    public required IReadOnlyList<HourBalance> Months { get; init; }

    /// <summary>
    /// Het jaartotaal, of <c>null</c> zolang alleen de huidige maand wordt getoond.
    /// </summary>
    /// <remarks>
    /// <c>null</c> en niet een jaar met één maand erin. §3.6 laat het jaartotaal pas verschijnen bij
    /// "Alle maanden", en een jaartotaal dat één maand telt is geen jaartotaal maar een tweede plek
    /// waar hetzelfde getal staat.
    /// </remarks>
    public HourYear? Year { get; init; }

    /// <summary>
    /// De maand waarop de specificatie is gefilterd, of <c>null</c> voor de hele weergave (§3.6).
    /// </summary>
    public string? SelectedMonth { get; init; }

    /// <summary>De gefiatteerde regels, nieuwste eerst.</summary>
    public required IReadOnlyList<CustomerHourRow> Entries { get; init; }

    /// <summary>
    /// De som van de uren in <see cref="Entries"/>.
    /// </summary>
    /// <remarks>
    /// Staat er als veld omdat de voetregel van de tabel hem nodig heeft, en het is dezelfde som als
    /// het maandtotaal — bij een gefilterde maand exact <see cref="HourBalance.Booked"/> van die
    /// maand. Zou het scherm hem zelf optellen, dan bestaat er een tweede optelling naast die van de
    /// berekening.
    /// </remarks>
    public required decimal EntryHours { get; init; }

    /// <summary>Waarom er op dit scherm niets in te vullen valt.</summary>
    public required string ReadOnlyNotice { get; init; }

    /// <summary>Dat het maandtotaal de som van de regels is.</summary>
    public required string TotalNotice { get; init; }

    /// <summary>
    /// Dat er geen bundel is vastgelegd, of <c>null</c> als er wel een is.
    /// </summary>
    /// <remarks>
    /// Als tekst en niet als vlag, zodat de Razor hem niet zelf hoeft te formuleren; dezelfde afspraak
    /// als bij <see cref="CustomerContractView.AccessStateNotice"/>. <c>null</c> betekent dat er niets
    /// te melden is.
    /// </remarks>
    public string? NoBundleNotice { get; init; }
}

/// <summary>
/// Het urenscherm zoals de operator het ziet en bewerkt (§3.6).
/// </summary>
/// <remarks>
/// Alles wat §2 als operator-only aanmerkt, plus de etags die de formulieren moeten terugsturen, plus
/// de keuzelijsten waaruit die formulieren mogen kiezen. Die laatste komen uit de datalaag en niet uit
/// de Razor, zodat een formulier geen waarde kan aanbieden die de schrijfkant weigert — dezelfde regel
/// als bij <see cref="OperatorContractView.Roles"/>.
/// </remarks>
public sealed record OperatorHoursView
{
    /// <summary>De klantslug.</summary>
    public required string CustomerId { get; init; }

    /// <summary>De klantnaam.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Wanneer deze weergave is opgebouwd.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>Of dit de interne beheerklant is.</summary>
    public required bool IsInternal { get; init; }

    /// <summary>De urenbundel per maand, of <c>null</c> als er geen bundel is vastgelegd.</summary>
    public decimal? BundledHours { get; init; }

    /// <summary>Het uurtarief buiten de bundel, of <c>null</c>.</summary>
    public decimal? HourlyRate { get; init; }

    /// <summary>Of er een contract is vastgelegd.</summary>
    /// <remarks>
    /// <c>false</c> betekent dat er uren geboekt kunnen worden op een klant zonder contract. Dat mag —
    /// onboarding gaat in die volgorde — maar het scherm hoort te melden dat er dan niets is om tegen
    /// af te zetten, in plaats van overal streepjes te zetten.
    /// </remarks>
    public required bool HasContract { get; init; }

    /// <summary>De maanden in de weergave, oudste eerst, met wat er te fiatteren ligt.</summary>
    public required IReadOnlyList<OperatorMonthRow> Months { get; init; }

    /// <summary>Het jaartotaal, of <c>null</c> zolang alleen de huidige maand wordt getoond.</summary>
    public HourYear? Year { get; init; }

    /// <summary>De maand waarop de specificatie is gefilterd, of <c>null</c>.</summary>
    public string? SelectedMonth { get; init; }

    /// <summary>
    /// De specificatie: gefiatteerde en te fiatteren regels door elkaar, nieuwste eerst (§3.6).
    /// </summary>
    /// <remarks>
    /// Eén lijst en niet twee, want §3.6 zet de acties Fiatteren en Afwijzen bij de regel in de
    /// specificatie en niet in een aparte wachtrij. Een te fiatteren regel hoort in de rij te staan
    /// waar hij terecht zou komen, zodat te zien is wat hij met de maand zou doen. Afgewezen regels
    /// staan er níet in; die staan in <see cref="Rejected"/>.
    /// </remarks>
    public required IReadOnlyList<OperatorHourRow> Entries { get; init; }

    /// <summary>
    /// De afgewezen regels, nieuwste eerst.
    /// </summary>
    /// <remarks>
    /// <para><strong>Een eigen lijst, en dat is het antwoord op het enige echte bezwaar tegen het
    /// bewaren van afgewezen regels.</strong> Dat bezwaar is dat de specificatie onbruikbaar wordt als
    /// hij volloopt met regels die niet meetellen — en dat is waar. Het is hier opgelost waar het hoort:
    /// in de weergave, niet in de opslag. In de opslag blijven ze staan, met hun reden, omdat afwijzen
    /// anders geen besluit is maar een handeling die je bij elke run van de koppeling opnieuw doet
    /// (zie <see cref="IPortalHoursStore.RejectHoursAsync"/>).</para>
    ///
    /// <para>Leeg is het normale geval, en dan hoort er geen sectie te staan.</para>
    /// </remarks>
    public required IReadOnlyList<OperatorHourRow> Rejected { get; init; }

    /// <summary>De som van de gefiatteerde uren in <see cref="Entries"/>.</summary>
    public required decimal EntryHours { get; init; }

    /// <summary>De som van de te fiatteren uren in <see cref="Entries"/>.</summary>
    public required decimal PendingHours { get; init; }

    /// <summary>Hoeveel regels er in totaal te fiatteren liggen in deze weergave.</summary>
    public required int PendingCount { get; init; }

    /// <summary>
    /// De maanden waarop geboekt kan worden, nieuwste eerst.
    /// </summary>
    /// <remarks>
    /// De maanden van het jaar in beeld die binnen de contractperiode vallen en al zijn begonnen; zie
    /// <see cref="HourBalanceCalculator.MonthsInScope"/>. Bewust geen vrij tekstveld: een boeking op
    /// een maand die niet in de tabel staat is een boeking die niemand terugvindt.
    /// </remarks>
    public required IReadOnlyList<string> BookableMonths { get; init; }

    /// <summary>De maand die het boekformulier voorgeselecteerd heeft (§3.6, "default huidige").</summary>
    public required string DefaultMonth { get; init; }

    /// <summary>De categorieën die geboekt mogen worden.</summary>
    /// <remarks>
    /// Uit <see cref="HourCategories.Bookable"/>, dus zonder <see cref="HourCategories.Correction"/>:
    /// een correctie is een eigen aanroep en geen keuze in een lijst.
    /// </remarks>
    public IReadOnlyList<string> Categories { get; init; } = HourCategories.Bookable;

    /// <summary>De vaste regel dat ingeschoten regels pas na akkoord meetellen (§5).</summary>
    public required string PendingNotice { get; init; }

    /// <summary>Dat het maandtotaal de som van de gefiatteerde regels is.</summary>
    public required string TotalNotice { get; init; }

    /// <summary>Wat een correctie is en hoe hij wordt vastgelegd.</summary>
    public required string CorrectionNotice { get; init; }

    /// <summary>Dat fiatteren niet terug te draaien is.</summary>
    public required string ApprovalNotice { get; init; }

    /// <summary>Dat een afgewezen regel blijft staan. Alleen relevant als er afgewezen regels zijn.</summary>
    public required string RejectionNotice { get; init; }

    /// <summary>Dat er geen bundel is vastgelegd, of <c>null</c> als er wel een is.</summary>
    public string? NoBundleNotice { get; init; }
}
