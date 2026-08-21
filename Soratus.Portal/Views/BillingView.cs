using Soratus.Portal.Data;

namespace Soratus.Portal.Views;

/// <summary>
/// De teksten van het facturatiescherm die geen gegeven zijn maar een mededeling (§3.7).
/// </summary>
/// <remarks>
/// <para>Eén plek, en het viewmodel draagt de tekst mee in plaats van dat de Razor hem verzint.
/// Dezelfde afspraak als bij <see cref="HoursNotice"/> en <see cref="ContractNotice"/>.</para>
///
/// <para><strong>De klantteksten en de operatorteksten van dit scherm staan hier bewust als aparte
/// constanten en niet als één tekst met een variabele erin.</strong> Dat is hier scherper dan bij de
/// uren: de operatortekst noemt de <em>beheeropslag</em>, en dat woord staat in de lijst met woorden
/// die een klant nergens mag zien. Een gedeelde tekst met een <c>if</c> erin is één verschrijving
/// verwijderd van onze marge op het scherm van de klant.</para>
/// </remarks>
public static class BillingNotice
{
    /// <summary>
    /// Waarom een klant hier niets kan invullen.
    /// </summary>
    public const string CustomerReadOnly =
        "Deze bedragen worden door Soratus bijgehouden en zijn hier alleen te lezen. Ze worden "
        + "achteraf gefactureerd. Zie je een bedrag dat niet klopt, laat het ons weten.";

    /// <summary>
    /// Dat het bedrag van de lopende maand nog loopt (§3.7, "de lopende maand staat bovenaan als concept").
    /// </summary>
    /// <remarks>
    /// Voor beide rollen dezelfde zin. Een klant hoort te weten dat het bedrag dat hij vandaag ziet
    /// niet het bedrag is dat hij gaat betalen — en dat is geen intern gegeven maar het enige dat een
    /// lopende maand over zichzelf kan zeggen.
    /// </remarks>
    public const string RunningMonth =
        "De lopende maand is nog niet af: dit bedrag loopt tot het einde van de maand op en wordt "
        + "daarna vastgesteld.";

    /// <summary>
    /// Waarom een bedrag "onbekend" kan zijn en nooit € 0,00 (voor de operator).
    /// </summary>
    /// <remarks>
    /// <para>Dit is de gemeten kern van fase 4a in één alinea, en hij staat op het scherm en niet
    /// alleen in de documentatie. Zonder deze tekst leest een streepje in de kostenkolom als een
    /// storing, en dan gaat iemand het "oplossen" door er nul van te maken.</para>
    ///
    /// <para>Alleen voor de operator: de klant hoort niet te weten met welke API wij hier vechten.
    /// Wat hij nodig heeft staat in <see cref="CustomerAmountUnknown"/>.</para>
    /// </remarks>
    public const string OperatorAmountUnknown =
        "Een streepje is geen nul. Cost Management geeft voor een omgeving die niet bestaat en voor "
        + "een periode die nog niet geboekt is hetzelfde antwoord — geslaagd, en zonder regels — en "
        + "geeft af en toe een 404 die 'probeer opnieuw' betekent. Geen van die drie is € 0,00, dus "
        + "het portaal zet er geen bedrag neer. Staat er bij een maand 'geen regels', controleer dan "
        + "eerst of de bevraagde omgeving hieronder de juiste is.";

    /// <summary>
    /// Waarom een bedrag bij de klant kan ontbreken.
    /// </summary>
    /// <remarks>
    /// Dezelfde waarheid, zonder de techniek en zonder onze marge. "Nog niet vastgesteld" is precies
    /// wat er aan de hand is en het belooft niets.
    /// </remarks>
    public const string CustomerAmountUnknown =
        "Een streepje betekent dat het bedrag van die maand nog niet is vastgesteld. Het betekent "
        + "niet dat er niets is verbruikt.";

    /// <summary>
    /// Waar de beheeropslag wordt afgesproken (operator-only).
    /// </summary>
    /// <remarks>
    /// §3.7 vraagt een "instelbare beheeropslag %". Instellen gebeurt op het contractscherm en niet
    /// hier, want het is een afspraak en geen meting — zie <see cref="IPortalCostsStore"/>. Deze tekst
    /// zegt dat, zodat een operator die hier een invulveld verwacht weet waar hij moet zijn, in plaats
    /// van te concluderen dat het niet kan.
    /// </remarks>
    public const string SurchargeIsOnTheContract =
        "Het opslagpercentage is een contractafspraak en wordt op het contractscherm vastgelegd. Het "
        + "geldt voor alle maanden; er is geen percentage per maand, want dan zouden er twee "
        + "afspraken over hetzelfde bestaan.";

    /// <summary>
    /// Dat de uitsplitsing uit de API komt en niet uit een lijst (operator-only).
    /// </summary>
    /// <remarks>
    /// §3.7 noemt vijf diensten met naam en de werkelijke namen zijn andere. Deze tekst staat er zodat
    /// een operator die "Container Apps" zoekt niet denkt dat er iets ontbreekt.
    /// </remarks>
    public const string ServicesComeFromAzure =
        "De diensten komen uit Cost Management en staan niet in een lijst in het portaal: een nieuwe "
        + "dienst hoort in het subtotaal te belanden zonder dat iemand er iets voor doet. De namen "
        + "zijn daarom die van Azure zelf. Bedragen zijn exclusief btw.";

    /// <summary>
    /// Dat het subtotaal de exacte som is en niet de som van de afgeronde regels (operator-only).
    /// </summary>
    /// <remarks>
    /// Nodig omdat de echte bedragen tot ver achter de komma lopen: <c>Key Vault € 0,000242</c> over
    /// een hele maand. Die regel als <c>€ 0,00</c> tonen zou een dienst opleveren die niets kost, en
    /// dat is dezelfde onwaarheid als € 0,00 voor een onbekend bedrag. Hij staat er als
    /// <c>&lt; € 0,01</c>, en dan telt de kolom zichtbaar niet op — vandaar deze zin.
    /// </remarks>
    public const string SubtotalIsExact =
        "Het subtotaal is de som van de onafgeronde bedragen. Een dienst die minder dan een cent "
        + "kost staat er als '< € 0,01', dus de regels tellen op het scherm niet precies op tot het "
        + "subtotaal.";
}

/// <summary>
/// Waarom een klant voor een maand geen bedrag te zien krijgt (§3.7).
/// </summary>
/// <remarks>
/// <para><strong>Dit is de klantvariant van <see cref="MonthlyChargeGap"/>, en hij bestaat omdat die
/// enum niet naar buiten mag.</strong> Daar heet een waarde <c>NoSurchargeAgreed</c>, en de
/// mededeling "we hebben nog geen opslag afgesproken" vertelt een klant dat er een opslag ís. De
/// beheeropslag is onze marge en §2 zet hem dicht voor de klant; een enum die hem noemt en op een
/// klantrij staat, staat in de paginabron en in elke mail die van die rij wordt opgemaakt.</para>
///
/// <para><strong>Een enum en geen tekst, en dat is de eis en niet de smaak.</strong> Een reden die als
/// <see cref="string"/> reist, is een reden die ergens uit een <c>catch</c>-blok kan komen — en dan
/// staat de tekst van een uitzondering in de inbox van een klant. Dat is de fout van de punten 13 en
/// 14 van de fase-0-afwijkingen (een technisch veld dat naar buiten lekt) voor de derde keer, nu in
/// een mail in plaats van op een scherm. Met een enum bestaat die weg niet: er zijn vier waarden en
/// ze staan hier alle vier opgeschreven.</para>
///
/// <para><strong>Vlaggen en geen enkele waarde,</strong> want er kunnen er twee tegelijk gelden: een
/// klant zonder contract mist zowel de afspraken als, in de eerste maand, de meting. Zie
/// <see cref="MonthlyChargeGap"/> voor hetzelfde argument aan de operatorkant.</para>
///
/// <para><strong>Drie gaten van de operator vallen hier op één waarde.</strong> Geen opslag, geen
/// bundel en geen tarief worden alle drie <see cref="ContractIncomplete"/>. Dat is geen
/// informatieverlies dat de klant iets kost: het zijn alle drie contractafspraken, en de handeling die
/// erop volgt is voor alle drie dezelfde — Soratus legt ze vast. Welke van de drie het is, is onze
/// administratie.</para>
/// </remarks>
[Flags]
public enum CustomerChargeGap
{
    /// <summary>Er is een bedrag. Er staat niets in de weg.</summary>
    None = 0,

    /// <summary>
    /// Het verbruik van deze maand is nog niet vastgesteld.
    /// </summary>
    /// <remarks>
    /// Dekt alle drie de toestanden waarin Cost Management geen bruikbaar bedrag gaf — nooit gemeten,
    /// meting mislukt, en een geslaagde meting zonder regels. Voor de klant zijn dat niet drie
    /// mededelingen maar één, en geen ervan is € 0,00. Zie <see cref="Data.AzureCostState"/> voor het
    /// onderscheid dat de operator wél ziet.
    /// </remarks>
    ConsumptionUnknown = 1,

    /// <summary>Er staan nog contractafspraken open die het bedrag bepalen.</summary>
    ContractIncomplete = 2,

    /// <summary>
    /// Deze omgeving is intern beheer van Soratus en wordt niet doorbelast (§4).
    /// </summary>
    /// <remarks>
    /// Geen ontbrekend bedrag maar een bekend antwoord: er valt niets te factureren. Het staat toch
    /// tussen de gaten omdat het voor de lezer dezelfde vraag beantwoordt — waarom staat er geen
    /// bedrag — en omdat € 0,00 hier net zo verkeerd zou zijn: dat zou zeggen dat we een factuur van
    /// nul sturen.
    /// </remarks>
    NotCharged = 4,
}

/// <summary>
/// Eén maand op het facturatieoverzicht zoals de klant hem ziet (§3.7).
/// </summary>
/// <remarks>
/// <para><strong>Dit type heeft geen uitsplitsing, geen opslagpercentage en geen
/// <see cref="MonthlyChargeGap"/>.</strong> §2 zegt: "Facturatie: bedragen en status — ja" voor de
/// klant, en "Facturatie: Azure per dienst + beheeropslag — nee". Dat verschil is hier een
/// typeverschil en geen <c>@if</c>: er bestaat geen uitdrukking in de klantmarkup die onze marge op
/// het scherm zet, want het veld is er niet. Dezelfde vorm als bij <see cref="CustomerHourRow"/>,
/// <see cref="CustomerLogLine"/>, <see cref="CustomerRunRow"/> en de contractmarge — voor de zesde
/// keer, en om dezelfde reden.</para>
///
/// <para><strong>De <see cref="MonthlyChargeGap"/> staat er niet op, en dat is niet uit netheid.</strong>
/// Die vlaggen heten <c>NoSurchargeAgreed</c> — dat is onze marge, en de mededeling "we hebben nog
/// geen opslag afgesproken" vertelt een klant dat er een opslag ís. Wat de klant nodig heeft is dat
/// het bedrag nog niet vaststaat, en dat staat in <see cref="TotalNotice"/>.</para>
///
/// <para><strong>Dit is ook het type dat het maandoverzicht per mail nodig heeft.</strong> Zie
/// <see cref="IBillingViews.BuildMonthAsync(Security.CustomerScope, string, CancellationToken)"/>: de mail gaat naar de contactpersoon van de klant, dus
/// hij hoort precies te bevatten wat op het klantscherm staat en niets meer. Een mail die uit het
/// operatortype wordt opgemaakt, is een mail waarin onze marge één veldverwijzing ver weg is.</para>
/// </remarks>
public sealed record CustomerChargeRow
{
    /// <summary>De maand als <c>yyyy-MM</c>.</summary>
    public required string Month { get; init; }

    /// <summary>Het maandlabel, bijvoorbeeld <c>augustus 2026</c>.</summary>
    public required string MonthLabel { get; init; }

    /// <summary>
    /// Het door te belasten Azure-bedrag, of <c>null</c> als het niet vaststaat.
    /// </summary>
    /// <remarks>
    /// Subtotaal plus beheeropslag, in één getal. De opbouw staat op
    /// <see cref="OperatorChargeRow"/>; hier staat alleen wat er wordt doorbelast. <c>null</c> is
    /// nooit nul; zie <see cref="AzureCostState"/>.
    /// </remarks>
    public decimal? AzureCharged { get; init; }

    /// <summary>De uren boven de bundel, of <c>null</c> als er geen bundel is vastgelegd.</summary>
    public decimal? OverBundleHours { get; init; }

    /// <summary>Wat die uren kosten, of <c>null</c>.</summary>
    public decimal? HoursAmount { get; init; }

    /// <summary>
    /// De gefiatteerde uren van deze maand.
    /// </summary>
    /// <remarks>
    /// <para>Exact het getal dat op het urenscherm van deze klant in de kolom "Besteed" staat: de som
    /// van de gefiatteerde regels, uit <see cref="Data.HourBalance.Booked"/> en niet uit een tweede
    /// telling. Zou het facturatiescherm of het maandoverzicht per mail zelf optellen, dan bestaat er
    /// een tweede definitie van "besteed" en dan noemt de mail een ander aantal dan het scherm.</para>
    ///
    /// <para>Staat hier omdat het maandoverzicht per mail het nodig heeft: een klant hoort zijn
    /// specificatie te kunnen laten optellen tot het bedrag dat hij betaalt, en dat kan hij niet met
    /// alleen de uren <em>boven</em> bundel. <c>null</c> als er voor deze maand geen urenstand is —
    /// dat is de maand die alleen op het overzicht staat omdat er een kostenmeting voor is.</para>
    /// </remarks>
    public decimal? UsedHours { get; init; }

    /// <summary>De urenbundel van deze maand, of <c>null</c> als er geen bundel is vastgelegd.</summary>
    /// <remarks>
    /// <c>null</c> is "niet afgesproken" en nul is "geen bundel" — punt 15 en punt 19, en dezelfde
    /// regel als bij <see cref="Data.ContractDocument.BundledHours"/>. Een klant zonder afspraak leest
    /// hier dus een streepje en geen nul.
    /// </remarks>
    public decimal? BundledHours { get; init; }

    /// <summary>Het totaal van deze maand, of <c>null</c> als het niet vaststaat (§3.7, "op één totaal").</summary>
    public decimal? Total { get; init; }

    /// <summary>Of dit de lopende maand is. Die staat bovenaan als concept (§3.7).</summary>
    public required bool IsRunningMonth { get; init; }

    /// <summary>Of dit bedrag definitief is.</summary>
    public required bool IsFinal { get; init; }

    /// <summary>
    /// Of het tijdvak van deze maand volledig geboekt is.
    /// </summary>
    /// <remarks>
    /// <para><strong>Klantveilig, en dat is een afweging die ik expliciet maak.</strong> Dit is een
    /// vlag en geen <see cref="Data.AzureCostState"/>: het zegt "de maand is af" en niet
    /// <em>waarom</em> hij dat niet is. Dat het tijdvak nog loopt weet de klant al — §3.7 zet de
    /// lopende maand als concept bovenaan en <see cref="Views.BillingNotice.RunningMonth"/> zegt het
    /// met zoveel woorden. Wat er níet doorheen komt is het onderscheid tussen "geen regels" en
    /// "meting mislukt", en dat is de bedrijfsvoering die operator-only blijft.</para>
    ///
    /// <para>Waarom hij náást <see cref="IsFinal"/> staat en niet in plaats daarvan: die twee kunnen
    /// onafhankelijk <c>false</c> zijn, en het maandoverzicht per mail moet weten welke van de twee.
    /// Zie <see cref="Data.MonthlyCharge.IsPeriodComplete"/> — daar staat de meting waaruit dat blijkt.
    /// </para>
    /// </remarks>
    public required bool IsPeriodComplete { get; init; }

    /// <summary>Of deze klant intern is en dus niet wordt doorbelast (§4).</summary>
    public required bool IsInternal { get; init; }

    /// <summary>
    /// Wanneer het verbruik van deze maand is opgehaald, of <c>null</c> als er nooit is gemeten.
    /// </summary>
    /// <remarks>
    /// §3.7 vraagt "read-only, met tijdstip van ophalen". Dat tijdstip staat op de <em>rij</em> en niet
    /// op de weergave, want elke maand heeft zijn eigen laatste meting: een afgesloten maand is één
    /// keer gemeten en verandert niet meer, en de lopende maand is vannacht gemeten. Eén tijdstip boven
    /// de tabel zou voor elke rij behalve één onwaar zijn.
    /// </remarks>
    public DateTimeOffset? MeasuredAt { get; init; }

    /// <summary>
    /// Waarom er geen totaal is. <see cref="CustomerChargeGap.None"/> als er wel een is.
    /// </summary>
    /// <remarks>
    /// De klantvariant van <see cref="MonthlyChargeGap"/> — vier waarden die onze marge niet noemen, en
    /// een enum zodat er geen tekst uit een <c>catch</c>-blok in kan belanden. Zie
    /// <see cref="CustomerChargeGap"/>. Dit is het veld waarop het maandoverzicht per mail zijn
    /// mededeling hoort te baseren.
    /// </remarks>
    public required CustomerChargeGap Gap { get; init; }

    /// <summary>
    /// Waarom er geen totaal is, in gewone taal, of <c>null</c> als er wel een is.
    /// </summary>
    /// <remarks>
    /// <para>De zin die bij <see cref="Gap"/> hoort, voorgeformuleerd. Hij staat op het viewmodel en
    /// niet in de Razor om dezelfde reden als de teksten in <see cref="HoursNotice"/>: de markup hoort
    /// een mededeling neer te zetten en hem niet samen te stellen.</para>
    ///
    /// <para>De vlaggen staan er náást en zijn niet vervangen door deze tekst. Een tweede lezer — het
    /// maandoverzicht per mail — heeft een eigen toon en een eigen lengte, en die hoort te kunnen
    /// beslissen op grond van het gegeven en niet op grond van onze schermtekst. Wie op de tekst
    /// beslist, beslist op iets wat morgen anders geformuleerd is.</para>
    /// </remarks>
    public string? TotalNotice { get; init; }
}

/// <summary>
/// Eén maand op het facturatieoverzicht zoals de operator hem ziet (§3.7).
/// </summary>
/// <remarks>
/// De variant met de uitsplitsing per dienst, het opslagpercentage, het opslagbedrag, de bevraagde
/// scope en de reden waarom een bedrag ontbreekt. Er is bewust geen gemeenschappelijk basistype met
/// <see cref="CustomerChargeRow"/>: de tabellen verschillen echt — er komen vier kolommen bij en een
/// uitklapbare uitsplitsing eronder — en dat is dezelfde situatie als bij de logs (§12) en de uren,
/// waar om die reden twee losse typen staan.
/// </remarks>
public sealed record OperatorChargeRow
{
    /// <summary>De maand als <c>yyyy-MM</c>.</summary>
    public required string Month { get; init; }

    /// <summary>Het maandlabel.</summary>
    public required string MonthLabel { get; init; }

    /// <summary>Wat er van het Azure-verbruik bekend is.</summary>
    public required AzureCostState AzureState { get; init; }

    /// <summary>
    /// De diensten met hun bedragen, hoogste bedrag eerst. Operator-only (§2).
    /// </summary>
    /// <remarks>
    /// Leeg zodra er niets is gemeten. Een lege lijst hoort op het scherm géén tabel met nullen op te
    /// leveren maar de mededeling uit <see cref="AzureCostState"/>.
    /// </remarks>
    public IReadOnlyList<AzureCostLine> Lines { get; init; } = [];

    /// <summary>Het Azure-subtotaal, afgerond, of <c>null</c>. Operator-only.</summary>
    public decimal? AzureSubtotal { get; init; }

    /// <summary>Het afgesproken opslagpercentage, of <c>null</c>. Operator-only.</summary>
    public decimal? SurchargePercentage { get; init; }

    /// <summary>Het opslagbedrag, of <c>null</c>. Operator-only.</summary>
    public decimal? SurchargeAmount { get; init; }

    /// <summary>Het door te belasten Azure-bedrag, of <c>null</c>.</summary>
    public decimal? AzureCharged { get; init; }

    /// <summary>De uren boven bundel, of <c>null</c>.</summary>
    public decimal? OverBundleHours { get; init; }

    /// <summary>Het uurtarief, of <c>null</c>.</summary>
    public decimal? HourlyRate { get; init; }

    /// <summary>Wat de uren boven bundel kosten, of <c>null</c>.</summary>
    public decimal? HoursAmount { get; init; }

    /// <summary>Het totaal van deze maand, of <c>null</c>.</summary>
    public decimal? Total { get; init; }

    /// <summary>Waarom er geen totaal is. Operator-only.</summary>
    public required MonthlyChargeGap Gap { get; init; }

    /// <summary>Of dit de lopende maand is.</summary>
    public required bool IsRunningMonth { get; init; }

    /// <summary>Of dit bedrag definitief is en dus gefactureerd kan worden.</summary>
    public required bool IsFinal { get; init; }

    /// <summary>Of deze klant intern is en dus niet wordt doorbelast (§4).</summary>
    public required bool IsInternal { get; init; }

    /// <summary>Wanneer het verbruik is opgehaald, of <c>null</c>.</summary>
    public DateTimeOffset? MeasuredAt { get; init; }

    /// <summary>De laatste dag waarover er bedragen zijn, of <c>null</c>.</summary>
    public DateOnly? CoversThrough { get; init; }

    /// <summary>
    /// De scope waartegen is gemeten, of <c>null</c>. Operator-only.
    /// </summary>
    /// <remarks>
    /// Staat op het scherm omdat een geslaagd, leeg antwoord niet te onderscheiden is van een
    /// verkeerde omgeving. Zie <see cref="AzureCostDocument.Scope"/> — dit veld is het enige
    /// gereedschap tegen een tikfout in een resource-groepnaam die jaren stil € 0,00 zou opleveren.
    /// </remarks>
    public string? Scope { get; init; }

    /// <summary>Waarom er niets bekend is, of <c>null</c>. Operator-only.</summary>
    public string? Failure { get; init; }
}

/// <summary>
/// Het facturatiescherm zoals de klant het ziet (§3.7).
/// </summary>
/// <remarks>
/// <para><strong>Wat er niet op staat: de uitsplitsing per dienst, de beheeropslag en de bevraagde
/// scope.</strong> De rijen zijn <see cref="CustomerChargeRow"/> en dat type draagt die drie niet, dus
/// er is geen plek in dit viewmodel waar ze in kunnen belanden — ook niet per ongeluk, ook niet als
/// iemand er over een half jaar een tooltip bij zet.</para>
///
/// <para><strong>Wat er ook niet op staat: een factuurnummer, een verzenddatum en een
/// betaalstatus.</strong> §3.7 vraagt die, en ze komen uit SnelStart. Er is geen SnelStart-koppeling
/// (zie <c>docs/agent-portal/fase-4-haalbaarheid.md</c> §1), dus die velden zouden altijd leeg zijn —
/// en een lege statuskolom leest als "niet verstuurd" in plaats van als "wij weten het niet". Dat is
/// dezelfde stille onwaarheid als het "uitnodiging verstuurd"-veld dat om die reden niet op
/// <see cref="Data.AccessDocument"/> staat. Ze komen erbij zodra er iets is dat ze vult.</para>
/// </remarks>
public sealed record CustomerBillingView
{
    /// <summary>De klantslug.</summary>
    public required string CustomerId { get; init; }

    /// <summary>De klantnaam, voor de kop.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Wanneer deze weergave is opgebouwd.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>Of dit de interne beheerklant is.</summary>
    public required bool IsInternal { get; init; }

    /// <summary>Het jaar dat in beeld is.</summary>
    public required int Year { get; init; }

    /// <summary>De maanden, nieuwste eerst. De lopende maand staat bovenaan (§3.7).</summary>
    public required IReadOnlyList<CustomerChargeRow> Months { get; init; }

    /// <summary>
    /// De som van de totalen van de maanden die een totaal hebben, of <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <para><strong><c>null</c> zodra één maand geen totaal heeft, en niet de som van de rest.</strong>
    /// Dat is dezelfde regel als bij <see cref="MonthlyCharge.Total"/>, een niveau hoger: een
    /// jaartotaal waarin twee van de twaalf maanden ontbreken is niet te onderscheiden van een
    /// jaartotaal dat compleet is, en het is lager. Van de twee mogelijke fouten — geen getal of een te
    /// laag getal — is alleen de eerste zichtbaar.</para>
    ///
    /// <para>Maanden waarvoor er nooit is gemeten omdat de klant toen niet bestond, tellen niet als
    /// ontbrekend: die staan niet in <see cref="Months"/>. Zie <see cref="IBillingViews"/>.</para>
    /// </remarks>
    public decimal? YearTotal { get; init; }

    /// <summary>Waarom er op dit scherm niets in te vullen valt.</summary>
    public required string ReadOnlyNotice { get; init; }

    /// <summary>Wat een streepje in een bedragkolom betekent.</summary>
    public required string UnknownNotice { get; init; }
}

/// <summary>
/// Het facturatiescherm zoals de operator het ziet (§3.7).
/// </summary>
/// <remarks>
/// Alles wat §2 als operator-only aanmerkt: de uitsplitsing per dienst, de beheeropslag, de bevraagde
/// scope en de reden waarom een bedrag ontbreekt.
/// </remarks>
public sealed record OperatorBillingView
{
    /// <summary>De klantslug.</summary>
    public required string CustomerId { get; init; }

    /// <summary>De klantnaam.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Wanneer deze weergave is opgebouwd.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>Of dit de interne beheerklant is.</summary>
    public required bool IsInternal { get; init; }

    /// <summary>Het jaar dat in beeld is.</summary>
    public required int Year { get; init; }

    /// <summary>De maanden, nieuwste eerst.</summary>
    public required IReadOnlyList<OperatorChargeRow> Months { get; init; }

    /// <summary>De som van de maandtotalen, of <c>null</c> zodra er één ontbreekt.</summary>
    public decimal? YearTotal { get; init; }

    /// <summary>
    /// Het opslagpercentage uit het contract, of <c>null</c> als er niets is afgesproken.
    /// </summary>
    /// <remarks>
    /// Staat naast de maanden omdat het voor alle maanden hetzelfde is; per maand staat het er ook,
    /// zodat een rij op zichzelf te lezen is. Twee plekken met hetzelfde getal is hier geen tweede
    /// waarheid: beide komen uit dezelfde <see cref="Data.ContractDocument"/> en er is geen pad waarlangs
    /// ze uiteen kunnen lopen.
    /// </remarks>
    public decimal? SurchargePercentage { get; init; }

    /// <summary>Of er een contract is vastgelegd.</summary>
    /// <remarks>
    /// <c>false</c> betekent dat er geen bundel, geen tarief en geen opslag is — en dus geen enkel
    /// totaal. Het scherm hoort dat één keer te melden met een verwijzing naar het contractscherm, in
    /// plaats van twaalf keer een streepje te zetten.
    /// </remarks>
    public required bool HasContract { get; init; }

    /// <summary>Wat een streepje in een bedragkolom betekent.</summary>
    public required string UnknownNotice { get; init; }

    /// <summary>Waar de beheeropslag wordt afgesproken.</summary>
    public required string SurchargeNotice { get; init; }

    /// <summary>Dat de dienstnamen uit Azure komen.</summary>
    public required string ServicesNotice { get; init; }

    /// <summary>Dat het subtotaal de exacte som is.</summary>
    public required string SubtotalNotice { get; init; }
}
