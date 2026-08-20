namespace Soratus.Portal.Data;

/// <summary>
/// Hoe een maand ervoor staat tegenover de urenbundel (§3.6).
/// </summary>
/// <remarks>
/// <para>§3.6 noemt drie standen: Binnen bundel, Boven bundel, Niets geboekt. Er is een vierde nodig
/// en die staat niet in de spec: <see cref="NoBundleAgreed"/>. Zodra
/// <see cref="ContractDocument.BundledHours"/> <c>null</c> mag zijn — en dat is besluit 15 van de
/// fase-0-afwijkingen — bestaat de maand waarin uren staan terwijl er geen bundel is afgesproken.
/// Die is niet "binnen bundel" en niet "boven bundel", en hem als een van die twee tonen is precies
/// het stille misgaan dat besluit 15 wilde voorkomen: dan leest een klant zonder afspraak als een
/// klant met een bundel van nul, en die twee schelen geld.</para>
///
/// <para>Voor de kleur volgt de nieuwe stand het voorbeeld van punt 3 van de afwijkingen: geen nieuwe
/// kleur en geen nieuwe rang, maar rang 0 hergebruiken (neutraal grijs, glyph <c>–</c>). Er is niets
/// mis; er is alleen niets om aan te toetsen.</para>
/// </remarks>
public enum HourMonthStatus
{
    /// <summary>
    /// Er is deze maand niets gefiatteerd geboekt. De bundel doet dan niet mee.
    /// </summary>
    /// <remarks>
    /// Deze stand gaat vóór <see cref="NoBundleAgreed"/>, ook als er geen bundel is. De rij gaat over
    /// geboekte uren, en "niets geboekt" is dan de mededeling die de lezer zoekt; of er een bundel is
    /// afgesproken staat op de contractkaart.
    /// </remarks>
    NothingBooked,

    /// <summary>Er staan uren en ze passen in de bundel. Saldo is nul of positief.</summary>
    WithinBundle,

    /// <summary>Er staan meer uren dan de bundel. Het meerdere wordt achteraf gefactureerd (§3.7).</summary>
    OverBundle,

    /// <summary>
    /// Er staan uren, maar er is geen bundel vastgelegd. Er is dus geen saldo.
    /// </summary>
    NoBundleAgreed,
}

/// <summary>
/// Hoe één maand ervoor staat: bundel, gefiatteerde uren, saldo en stand (§3.6).
/// </summary>
/// <remarks>
/// <para><strong>Dit type wordt door beide rollen gebruikt en draagt daarom geen enkel spoor van de
/// fiatteringsstroom.</strong> Geen <c>PendingHours</c>, geen aantal te fiatteren regels, geen vlag
/// die zegt dat er nog iets ligt. Dat is niet weggelaten uit netheid maar omdat het de acceptatie van
/// fase 3 is: de klant ziet niets van die stroom. Wat er niet op het type staat, kan niet in de
/// paginabron belanden — dezelfde vorm als bij <c>CustomerLogLine</c>, <c>CustomerRunRow</c>,
/// <c>CustomerAgentsView</c> en de contractmarge. Het te fiatteren aantal hangt aan
/// <see cref="Views.OperatorMonthRow"/>, en dat type bestaat alleen op het operatorpad.</para>
///
/// <para><strong><see cref="Booked"/> is altijd de som van de gefiatteerde regels en nooit iets
/// anders.</strong> Dat is de acceptatie-eis van fase 3, en hij is hier waar door constructie: er is
/// geen veld waarin een afwijkend totaal past. Een handmatige correctie verandert deze som niet door
/// hem te overschrijven maar door er een gefiatteerde regel bij te leggen; zie
/// <see cref="CorrectionHours"/>.</para>
/// </remarks>
public sealed record HourBalance
{
    /// <summary>De maand, als <c>yyyy-MM</c>.</summary>
    public required string Month { get; init; }

    /// <summary>De maand zoals hij op het scherm hoort te staan, bijvoorbeeld <c>augustus 2026</c>.</summary>
    public required string MonthLabel { get; init; }

    /// <summary>
    /// De urenbundel van deze maand, of <c>null</c> als er geen bundel is vastgelegd.
    /// </summary>
    /// <remarks>
    /// <c>null</c> en niet nul. Zie <see cref="ContractDocument.BundledHours"/> en besluit 15: een
    /// bundel van nul uur is de afspraak "alles gaat per uur", geen bundel is "we hebben het nog niet
    /// afgesproken", en in een saldoberekening zien die twee er hetzelfde uit terwijl ze het niet
    /// zijn.
    /// </remarks>
    public decimal? BundledHours { get; init; }

    /// <summary>
    /// De som van de gefiatteerde uren van deze maand. Dit is het maandtotaal.
    /// </summary>
    /// <remarks>
    /// Nul als er niets is geboekt, en dat is hier wél een geldig getal: het is een uitkomst van een
    /// som en geen ontbrekende afspraak. Zie <see cref="HourEntryDocument.Hours"/> voor waarom dat
    /// verschil met §15 klopt.
    /// </remarks>
    public required decimal Booked { get; init; }

    /// <summary>
    /// Bundel minus geboekt, of <c>null</c> als er geen bundel is vastgelegd.
    /// </summary>
    /// <remarks>
    /// <para><strong>Dit is de plek waar besluit 15 stil misgaat als je niet oplet.</strong> Met een
    /// niet-nullable bundel is het saldo van een klant zonder afspraak <c>0 - geboekt</c>, dus
    /// negatief, dus "boven bundel" — en dan staat er op het scherm dat een klant zijn bundel
    /// overschrijdt die er nooit een had. Bij <c>null</c> valt er niets te salderen en staat er een
    /// streepje.</para>
    ///
    /// <para>Positief betekent uren over, negatief betekent uren boven de bundel. Exact nul is
    /// <see cref="HourMonthStatus.WithinBundle"/> en niet boven: precies de bundel opgebruiken is
    /// binnen de afspraak.</para>
    /// </remarks>
    public decimal? Balance { get; init; }

    /// <summary>
    /// De uren boven de bundel, of <c>null</c> als er geen bundel is vastgelegd.
    /// </summary>
    /// <remarks>
    /// Nul als de bundel niet is overschreden. Dit is het getal dat fase 4 tegen het uurtarief
    /// factureert (§3.7), en het staat hier apart van <see cref="Balance"/> omdat de facturatie geen
    /// negatief bedrag hoort te kunnen berekenen uit een positief saldo.
    /// </remarks>
    public decimal? OverBundleHours { get; init; }

    /// <summary>De stand van deze maand.</summary>
    public required HourMonthStatus Status { get; init; }

    /// <summary>
    /// Hoeveel van <see cref="Booked"/> uit handmatige correcties komt.
    /// </summary>
    /// <remarks>
    /// <para><strong>Dit veld is de oplossing van de tegenspraak in §3.6.</strong> Die vraagt om twee
    /// dingen van hetzelfde getal: het maandtotaal is de som van de gefiatteerde regels, én een
    /// handmatige correctie wordt als afwijking in de tooltip gemeld. Van één getal kan dat niet —
    /// een correctie die het totaal overschrijft maakt het geen som meer, en een som die de correctie
    /// negeert laat de correctie niets doen.</para>
    ///
    /// <para>Het antwoord is dat een correctie nóg een gefiatteerde regel is, met bron
    /// <see cref="HourEntrySource.Portal"/> en categorie <see cref="HourCategories.Correction"/>. Dan
    /// is het totaal een zuivere som, is de correctie een rij in de specificatie, en is er iets voor
    /// de tooltip: dit getal. Nul betekent dat er niet is gecorrigeerd. Zie besluit 16 in
    /// <c>docs/agent-portal/fase-0-afwijkingen.md</c>.</para>
    /// </remarks>
    public required decimal CorrectionHours { get; init; }

    /// <summary>Uit hoeveel gefiatteerde regels <see cref="Booked"/> is opgebouwd.</summary>
    /// <remarks>
    /// Voor de tooltip uit §3.6 ("x u uit n regels"). Alleen gefiatteerde regels, want alleen die
    /// zitten in de som — een aantal dat niet bij het getal ernaast hoort is erger dan geen aantal.
    /// </remarks>
    public required int EntryCount { get; init; }

    /// <summary>Of er in deze maand is gecorrigeerd.</summary>
    public bool HasCorrection => CorrectionHours != 0m;
}

/// <summary>
/// Hoe een jaar ervoor staat: de maanden, het jaartotaal en de uren boven bundel (§3.6).
/// </summary>
/// <remarks>
/// Draagt, net als <see cref="HourBalance"/>, geen enkel spoor van de fiatteringsstroom, en om
/// dezelfde reden.
/// </remarks>
public sealed record HourYear
{
    /// <summary>Het jaartal.</summary>
    public required int Year { get; init; }

    /// <summary>De maanden, oudste eerst.</summary>
    public required IReadOnlyList<HourBalance> Months { get; init; }

    /// <summary>De som van de gefiatteerde uren over alle maanden in <see cref="Months"/>.</summary>
    public required decimal Booked { get; init; }

    /// <summary>
    /// De bundels van die maanden bij elkaar, of <c>null</c> als er geen bundel is vastgelegd.
    /// </summary>
    /// <remarks>
    /// Alleen de maanden in <see cref="Months"/>, niet twaalf keer de bundel. Zie
    /// <see cref="HourBalanceCalculator.MonthsInScope"/>: een bundel voor een maand die nog niet is
    /// begonnen of die vóór de ingangsdatum van het contract ligt, is geen tegoed.
    /// </remarks>
    public decimal? BundledHours { get; init; }

    /// <summary>
    /// De uren boven bundel over het jaar, of <c>null</c> als er geen bundel is vastgelegd.
    /// </summary>
    /// <remarks>
    /// <para><strong>Dit is de som van de overschrijdingen per maand, en met opzet niet
    /// <see cref="BundledHours"/> minus <see cref="Booked"/>.</strong> Die twee zijn verschillende
    /// getallen en het verschil is geld.</para>
    ///
    /// <para>De bundel is een afspraak per maand (§3.5) en rolt niet door: een maand met vier uur over
    /// betaalt niet voor een maand met vier uur te veel. Wordt het jaarbedrag uit de jaartotalen
    /// berekend, dan salderen die twee maanden elkaar en verdwijnt de overschrijding uit de
    /// facturatie. De mockup doet dat wel (<c>max(0, totSpent - totBundel)</c>); dat is een fout die
    /// in dummy-data niet opvalt en in een factuur wel.</para>
    /// </remarks>
    public decimal? OverBundleHours { get; init; }

    /// <summary>De som van de handmatige correcties over het jaar.</summary>
    public required decimal CorrectionHours { get; init; }
}

/// <summary>
/// Rekent uren om naar maandtotalen, saldi en standen. Puur, en de enige plek waar dat gebeurt.
/// </summary>
/// <remarks>
/// <para><strong>Waarom dit één plek is.</strong> Hetzelfde argument als bij
/// <c>AgentStatusCalculator</c>: het scherm en de facturatie-agent moeten van hetzelfde getal
/// uitgaan. Zou het maandtotaal in de weergave worden opgeteld en in de conceptfactuur van
/// <c>maandfactuur-snelstart</c> opnieuw, dan bestaan er twee definities van "besteed" — en de eerste
/// keer dat ze verschillen is dat een factuur die niet overeenkomt met het portaal waar de klant naar
/// kijkt.</para>
///
/// <para><strong>Waarom hij tóch niet in <c>Soratus.Agents.Contracts</c> staat, waar
/// <c>AgentStatusCalculator</c> wel staat.</strong> Die bibliotheek bevat de documentvormen van het
/// <em>agentcontract</em>: registratie, run, logregel. Dat is telemetrie, gepubliceerd door agents.
/// Een urenregel is het tegendeel — Soratus-eigen administratie in de database <c>platform</c>, waar
/// een agent niets te zoeken heeft (zie <see cref="PortalDataLocation"/>). Deze functie daar zetten
/// betekent <see cref="HourEntryDocument"/> en <see cref="ContractDocument"/> meeverhuizen, en dan
/// bevat de agentbibliotheek het uurtarief en de marge.</para>
///
/// <para>De plek waar dit uiteindelijk hoort is een derde bibliotheek met de platformvormen, waar
/// zowel het portaal als de facturatie-agent van afhangt. Die bestaat nog niet en is een beslissing
/// van fase 4, wanneer er een tweede lezer is. Tot dan staat het hier: puur, zonder afhankelijkheid
/// op Cosmos of op een scope, zodat verhuizen een bestandsverplaatsing is en geen herschrijving.
/// Zolang er één lezer is, is een bibliotheek voor het delen ervan een belofte en geen deling.</para>
///
/// <para>Geen enkele methode leest de klok — <c>today</c> komt als parameter binnen. Dezelfde afspraak
/// als in <c>Soratus.Agents.Contracts</c>, en om dezelfde reden: een maandgrens is anders niet te
/// testen zonder tot volgende maand te wachten.</para>
/// </remarks>
public static class HourBalanceCalculator
{
    /// <summary>
    /// De stand van één maand.
    /// </summary>
    /// <param name="month">De maand, als <c>yyyy-MM</c>.</param>
    /// <param name="bundledHours">
    /// De urenbundel uit het contract, of <c>null</c> als er geen bundel is vastgelegd.
    /// </param>
    /// <param name="entries">
    /// Alle urenregels van deze klant. Regels van een andere maand en regels die niet meetellen
    /// worden hier gefilterd, zodat de aanroeper dat niet kan vergeten.
    /// </param>
    /// <returns>De stand.</returns>
    /// <remarks>
    /// <para>Het filteren gebeurt hier en niet bij de aanroeper. Dat is een bewuste keuze: zou deze
    /// methode een al gefilterde lijst eisen, dan is "alleen gefiatteerde regels" een regel die op
    /// elke aanroepplek opnieuw moet worden nageleefd, en de eerste die het vergeet krijgt een
    /// maandtotaal met te fiatteren uren erin — dat is precies de fout die de acceptatie van fase 3
    /// verbiedt, en hij is aan de uitkomst niet te zien.</para>
    ///
    /// <para>Een regel met een onleesbare maand valt buiten élke maand en telt dus nergens mee. Dat is
    /// de veilige kant: liever een uur dat niet in een totaal staat dan een uur in de verkeerde maand,
    /// want het eerste valt op bij het nakijken van de specificatie en het tweede niet.</para>
    /// </remarks>
    public static HourBalance ForMonth(
        string month,
        decimal? bundledHours,
        IEnumerable<HourEntryDocument> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(month);
        ArgumentNullException.ThrowIfNull(entries);

        var counted = entries
            .Where(entry => entry.Counts && string.Equals(entry.Month, month, StringComparison.Ordinal))
            .ToArray();

        var booked = counted.Sum(entry => entry.Hours);

        var corrections = counted
            .Where(entry => string.Equals(entry.Category, HourCategories.Correction, StringComparison.Ordinal))
            .Sum(entry => entry.Hours);

        // Bundel én saldo blijven null als er geen bundel is. Geen "?? 0m" — dat is de regel van
        // besluit 15, en dit is de berekening waarin hij anders stil misgaat.
        var balance = bundledHours is { } bundle ? bundle - booked : (decimal?)null;

        return new HourBalance
        {
            Month = month,
            MonthLabel = HourMonths.Label(month),
            BundledHours = bundledHours,
            Booked = booked,
            Balance = balance,
            OverBundleHours = balance is { } value ? Math.Max(0m, -value) : null,
            Status = StatusOf(booked, bundledHours, balance),
            CorrectionHours = corrections,
            EntryCount = counted.Length,
        };
    }

    /// <summary>
    /// De stand van een heel jaar, met de maanden erin.
    /// </summary>
    /// <param name="year">Het jaartal.</param>
    /// <param name="bundledHours">De urenbundel per maand, of <c>null</c>.</param>
    /// <param name="entries">Alle urenregels van deze klant.</param>
    /// <param name="contractStart">
    /// De ingangsdatum van het contract, of <c>null</c> als die niet is vastgelegd.
    /// </param>
    /// <param name="today">De dag van vandaag, in de tijdzone van de lezer.</param>
    /// <returns>Het jaar met zijn maanden, oudste eerst.</returns>
    public static HourYear ForYear(
        int year,
        decimal? bundledHours,
        IEnumerable<HourEntryDocument> entries,
        DateOnly? contractStart,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var all = entries as IReadOnlyCollection<HourEntryDocument> ?? [.. entries];

        var months = MonthsInScope(year, contractStart, today, all)
            .Select(month => ForMonth(month, BundleFor(month, bundledHours, contractStart), all))
            .ToArray();

        // Er is een jaarbundel zodra er één maand een bundel heeft. Heeft geen enkele maand er een —
        // omdat het contract nog niet was ingegaan, of omdat er geen bundel is afgesproken — dan is
        // het jaarsaldo null en niet nul. Zelfde regel als punt 15.
        var hasBundle = months.Any(month => month.BundledHours is not null);

        return new HourYear
        {
            Year = year,
            Months = months,
            Booked = months.Sum(month => month.Booked),
            BundledHours = hasBundle ? months.Sum(month => month.BundledHours ?? 0m) : null,

            // De som van de overschrijdingen per maand, niet de overschrijding van het jaartotaal.
            // Zie HourYear.OverBundleHours: een maand met uren over betaalt niet voor een maand met
            // uren te veel, want de bundel is een afspraak per maand en rolt niet door.
            OverBundleHours = hasBundle ? months.Sum(month => month.OverBundleHours ?? 0m) : null,
            CorrectionHours = months.Sum(month => month.CorrectionHours),
        };
    }

    /// <summary>
    /// De bundel die voor deze maand geldt: de afgesproken bundel, of <c>null</c> als het contract
    /// deze maand niet dekt.
    /// </summary>
    /// <param name="month">De maand, als <c>yyyy-MM</c>.</param>
    /// <param name="bundledHours">De afgesproken bundel per maand, of <c>null</c>.</param>
    /// <param name="contractStart">De ingangsdatum van het contract, of <c>null</c>.</param>
    /// <returns>De bundel van deze maand, of <c>null</c>.</returns>
    /// <remarks>
    /// <para><strong>Twee regels die niet door elkaar mogen lopen.</strong>
    /// <see cref="MonthsInScope"/> bepaalt welke maanden er in het overzicht <em>staan</em>; deze
    /// bepaalt of zo'n maand een bundel <em>heeft</em>. Ze staan apart omdat een maand op het overzicht
    /// kan staan zonder bundel te hebben: een regel die op een maand vóór de ingangsdatum is geboekt
    /// hoort zichtbaar te zijn, maar er hoort geen tegoed bij dat nooit is afgesproken.</para>
    ///
    /// <para>Zonder deze scheiding zou een klant die op 1 mei begint over januari tot april vier
    /// bundels in zijn jaartotaal krijgen. Dat is niet een cosmetisch verschil: het jaartotaal zou dan
    /// binnen bundel lijken te vallen op een tegoed dat niet bestaat.</para>
    /// </remarks>
    private static decimal? BundleFor(string month, decimal? bundledHours, DateOnly? contractStart) =>
        contractStart is not { } start
        || string.CompareOrdinal(month, HourMonths.Of(start)) >= 0
            ? bundledHours
            : null;

    /// <summary>
    /// Welke maanden van dit jaar op het overzicht horen te staan.
    /// </summary>
    /// <param name="year">Het jaartal.</param>
    /// <param name="contractStart">De ingangsdatum van het contract, of <c>null</c>.</param>
    /// <param name="today">De dag van vandaag.</param>
    /// <param name="entries">
    /// Alle urenregels, zodat een maand met uren erin nooit wegvalt — ook niet als hij buiten de
    /// gerekende periode ligt.
    /// </param>
    /// <returns>De maandsleutels, oudste eerst. Kan leeg zijn.</returns>
    /// <remarks>
    /// <para><strong>Niet altijd twaalf, en dat is een geldkwestie.</strong> Een bundel is een afspraak
    /// per maand; een maand die nog niet is begonnen levert geen tegoed op, en een maand vóór de
    /// ingangsdatum van het contract ook niet. Zou het overzicht twaalf maanden rekenen, dan telt het
    /// jaartotaal een bundel mee die niemand is overeengekomen, en dan lijkt een klant die net begint
    /// ruim binnen zijn bundel te vallen terwijl er niets is afgesproken over de maanden ervoor.</para>
    ///
    /// <para><strong>Maar een maand met uren valt nooit weg.</strong> Staat er een regel geboekt op
    /// een maand buiten die periode — geantidateerd, of vóór de ingangsdatum die iemand later heeft
    /// bijgesteld — dan hoort die maand er gewoon bij te staan. Uren die uit het overzicht verdwijnen
    /// omdat een grens ze uitsluit, verdwijnen ook uit het jaartotaal, en dat is stil. Liever een
    /// maand op het scherm die vragen oproept.</para>
    ///
    /// <para>De maand van vandaag hoort er wél bij, ook als hij nog niet om is. §3.6 laat het scherm
    /// standaard juist die maand tonen.</para>
    /// </remarks>
    public static IReadOnlyList<string> MonthsInScope(
        int year,
        DateOnly? contractStart,
        DateOnly today,
        IEnumerable<HourEntryDocument> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var booked = entries
            .Where(entry => HourMonths.YearOf(entry.Month) == year)
            .Select(entry => entry.Month)
            .ToHashSet(StringComparer.Ordinal);

        var first = contractStart is { Year: var startYear } start && startYear == year ? start.Month : 1;
        var last = today.Year == year ? today.Month : 12;

        // Een jaar volledig in de toekomst, of volledig voor de ingangsdatum, levert geen enkele
        // gerekende maand op. De maanden waarop toch iets is geboekt komen er hieronder alsnog bij.
        IEnumerable<string> inScope = today.Year < year || (contractStart is { } begin && begin.Year > year)
            ? []
            : HourMonths.InYear(year).Skip(first - 1).Take(Math.Max(0, last - first + 1));

        return
        [
            .. inScope
                .Concat(booked)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(month => month, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// De stand van een maand uit de drie getallen.
    /// </summary>
    /// <remarks>
    /// De volgorde is de betekenis. Niets geboekt gaat voorop, want dan doet de bundel niet mee. Daarna
    /// komt "geen bundel vastgelegd", want zonder bundel is er niets om binnen of buiten te vallen.
    /// Pas als beide vaststaan is de vergelijking zinvol, en dan is exact nul binnen de afspraak.
    /// </remarks>
    private static HourMonthStatus StatusOf(decimal booked, decimal? bundledHours, decimal? balance) =>
        booked == 0m ? HourMonthStatus.NothingBooked
        : bundledHours is null || balance is null ? HourMonthStatus.NoBundleAgreed
        : balance < 0m ? HourMonthStatus.OverBundle
        : HourMonthStatus.WithinBundle;
}
