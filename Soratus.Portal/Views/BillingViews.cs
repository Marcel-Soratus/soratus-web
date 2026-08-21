using System.Globalization;
using Soratus.Portal.Data;
using Soratus.Portal.Security;

namespace Soratus.Portal.Views;

/// <summary>
/// Bouwt de viewmodellen van het facturatiescherm op uit het gemeten verbruik, het contract en de
/// gefiatteerde uren (§3.7).
/// </summary>
/// <remarks>
/// <para><strong>Beide rollen rekenen met dezelfde functies op dezelfde gegevens.</strong> Het
/// maandbedrag komt uit <see cref="MonthlyChargeCalculator.ForMonth"/> en de uren boven bundel uit
/// <see cref="HourBalanceCalculator"/> — dezelfde functie die het urenscherm gebruikt. Er is dus geen
/// pad waarlangs de operator een ander bedrag ziet dan de klant, en geen pad waarlangs de facturatie
/// van een ander aantal uren uitgaat dan de urenspecificatie. Dat tweede is de reden dat deze klasse
/// de urenopslag leest in plaats van een eigen som te maken: twee definities van "uren boven bundel"
/// zouden op de eerste factuur uiteenlopen.</para>
///
/// <para><strong>Alleen gefiatteerde uren tellen mee, en dat is hier geen keuze maar een
/// eigenschap.</strong> <see cref="HourBalanceCalculator.ForMonth"/> filtert zelf op gefiatteerd, ook
/// als deze klasse hem alle regels aanreikt. §5 zegt dat wat een agent inschiet pas na akkoord
/// meetelt "in uren en facturatie", en dat is hier waar door constructie.</para>
///
/// <para>De klok wordt één keer per opbouw uitgelezen, dezelfde afspraak als in
/// <see cref="HourViews"/>. "Vandaag" is de Nederlandse dag; zie <see cref="PortalTimeZone"/>.</para>
/// </remarks>
internal sealed class BillingViews(
    IPortalCostsStore costs,
    IPortalHoursStore hours,
    IPortalDataStore store,
    ICustomerDirectory directory,
    TimeProvider timeProvider,
    ILogger<BillingViews> logger) : IBillingViews
{
    /// <inheritdoc />
    public async Task<CustomerBillingView> BuildBillingAsync(
        CustomerScope scope,
        int year,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var contract = await store.GetContractAsync(scope, cancellationToken).ConfigureAwait(false);
        var entries = await hours.GetApprovedHoursAsync(scope, HoursQuery.ForYear(year), cancellationToken)
            .ConfigureAwait(false);
        var readings = await costs.GetAzureCostsAsync(scope, year, cancellationToken)
            .ConfigureAwait(false);

        var months = Charges(year, contract, entries, readings, scope.IsInternal);

        return new CustomerBillingView
        {
            CustomerId = scope.CustomerId,
            DisplayName = scope.DisplayName,
            GeneratedAt = timeProvider.GetUtcNow(),
            IsInternal = scope.IsInternal,
            Year = year,
            Months = [.. months.Select(charge => CustomerRow(charge, readings))],
            YearTotal = YearTotal(months),
            ReadOnlyNotice = BillingNotice.CustomerReadOnly,
            UnknownNotice = BillingNotice.CustomerAmountUnknown,
        };
    }

    /// <inheritdoc />
    public async Task<OperatorBillingView> BuildBillingAsync(
        CustomerWriteScope scope,
        int year,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var contract = await store.GetContractAsync(scope, cancellationToken).ConfigureAwait(false);
        var entries = await hours.GetHoursAsync(scope, HoursQuery.ForYear(year), cancellationToken)
            .ConfigureAwait(false);
        var readings = await costs.GetAzureCostsAsync(scope, year, cancellationToken)
            .ConfigureAwait(false);

        // Uit de klantenlijst en niet uit de scope: CustomerWriteScope draagt dit gegeven bewust niet.
        // Dezelfde terugval als in HourViews en ContractViews.
        var isInternal = directory.Find(scope.CustomerId)?.IsInternal ?? false;

        var months = Charges(year, contract, entries, readings, isInternal);

        return new OperatorBillingView
        {
            CustomerId = scope.CustomerId,
            DisplayName = scope.DisplayName,
            GeneratedAt = timeProvider.GetUtcNow(),
            IsInternal = isInternal,
            Year = year,
            Months = [.. months.Select(charge => OperatorRow(charge, readings))],
            YearTotal = YearTotal(months),
            SurchargePercentage = contract?.AzureSurchargePercentage,
            HasContract = contract is not null,
            UnknownNotice = BillingNotice.OperatorAmountUnknown,
            SurchargeNotice = BillingNotice.SurchargeIsOnTheContract,
            ServicesNotice = BillingNotice.ServicesComeFromAzure,
            SubtotalNotice = BillingNotice.SubtotalIsExact,
        };
    }

    /// <inheritdoc />
    public async Task<CustomerChargeRow> BuildMonthAsync(
        CustomerScope scope,
        string month,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(month);

        var wanted = month.Trim();
        var year = HourMonths.YearOf(wanted)
            ?? throw new ArgumentException(
                $"'{month}' is geen maand in de vorm jjjj-mm.",
                nameof(month));

        var contract = await store.GetContractAsync(scope, cancellationToken).ConfigureAwait(false);
        var entries = await hours.GetApprovedHoursAsync(scope, HoursQuery.ForYear(year), cancellationToken)
            .ConfigureAwait(false);
        var readings = await costs.GetAzureCostsAsync(scope, year, cancellationToken)
            .ConfigureAwait(false);

        // Niet uit Charges() plukken: die levert alleen de maanden die op het overzicht horen, en het
        // maandoverzicht per mail gaat over een maand die is opgegeven. Zou hier "niet gevonden" uit
        // komen, dan moet de aanroeper besluiten wat dat betekent — en dat is precies waar € 0,00 wordt
        // uitgevonden. Zie IBillingViews.BuildMonthAsync.
        var charge = Charge(wanted, contract, entries, readings, scope.IsInternal);

        return CustomerRow(charge, readings);
    }

    /// <inheritdoc />
    public async Task<CustomerChargeRow> BuildMonthAsync(
        CustomerWriteScope scope,
        string month,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(month);

        var wanted = month.Trim();
        var year = HourMonths.YearOf(wanted)
            ?? throw new ArgumentException(
                $"'{month}' is geen maand in de vorm jjjj-mm.",
                nameof(month));

        var contract = await store.GetContractAsync(scope, cancellationToken).ConfigureAwait(false);
        var entries = await hours.GetHoursAsync(scope, HoursQuery.ForYear(year), cancellationToken)
            .ConfigureAwait(false);
        var readings = await costs.GetAzureCostsAsync(scope, year, cancellationToken)
            .ConfigureAwait(false);

        // GetHoursAsync levert álle regels, ook de te fiatteren en de afgewezen — en dat verandert het
        // getal niet: HourBalanceCalculator.ForMonth filtert zelf op gefiatteerd. Dat is precies waarom
        // dat filter daar staat en niet bij de aanroeper, en het is de reden dat de mail hetzelfde
        // aantal uren noemt als het urenscherm van de klant.
        var charge = Charge(
            wanted,
            contract,
            entries,
            readings,
            directory.Find(scope.CustomerId)?.IsInternal ?? false);

        return CustomerRow(charge, readings);
    }

    // ── Binnenwerk ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// De maandbedragen van één jaar, nieuwste eerst.
    /// </summary>
    /// <remarks>
    /// <para><strong>Welke maanden erop staan.</strong> De maanden die het urenoverzicht rekent
    /// (<see cref="HourBalanceCalculator.MonthsInScope"/>: binnen de contractperiode, al begonnen, plus
    /// elke maand waarop uren zijn geboekt), samen met elke maand waarvoor er een meting is. Die
    /// tweede groep hoort erbij omdat verbruik geld is: een maand met kosten die buiten de
    /// contractperiode valt hoort zichtbaar te zijn en niet stil weg te vallen — dezelfde regel als bij
    /// de uren, waar een maand met geboekte regels nooit uit het overzicht verdwijnt.</para>
    ///
    /// <para>Nieuwste eerst, want §3.7 zet de lopende maand bovenaan. Dat is het omgekeerde van de
    /// urenspecificatie, en met opzet: die is een tijdlijn, dit is een rekening.</para>
    /// </remarks>
    private IReadOnlyList<MonthlyBilling> Charges(
        int year,
        ContractDocument? contract,
        IReadOnlyList<HourEntryDocument> entries,
        IReadOnlyList<AzureCostDocument> readings,
        bool isInternal)
    {
        var months = HourBalanceCalculator
            .MonthsInScope(year, ContractStart(contract), Today(), entries)
            .Concat(readings.Select(reading => reading.Month))
            .Where(month => HourMonths.YearOf(month) == year)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(month => month, StringComparer.Ordinal);

        return [.. months.Select(month => Charge(month, contract, entries, readings, isInternal))];
    }

    /// <summary>
    /// Eén maand: het bedrag en de urenstand waaruit het is opgebouwd.
    /// </summary>
    /// <param name="Charge">Het bedrag.</param>
    /// <param name="Balance">
    /// De urenstand van die maand, of <c>null</c> als de maand geen urenstand heeft.
    /// </param>
    /// <remarks>
    /// <para><strong>Waarom die twee bij elkaar horen en de urenstand niet op
    /// <see cref="MonthlyCharge"/> staat.</strong> Die berekening neemt met opzet vier losse nullable
    /// getallen aan en kent geen enkel documenttype; dat maakt élke combinatie van ontbrekende gegevens
    /// in één regel testbaar, en dat is precies de combinatoriek waar besluit 15 over gaat. Er de
    /// urenstand bij zetten zou dat opgeven voor twee velden die met de berekening niets te maken
    /// hebben.</para>
    ///
    /// <para>Ze zijn hier wél samen nodig: het maandoverzicht per mail vraagt de bestede uren en de
    /// bundel, zodat een klant zijn specificatie kan laten optellen tot het bedrag dat hij betaalt.
    /// Dat zijn getallen uit de urenstand en niet uit de berekening.</para>
    /// </remarks>
    private sealed record MonthlyBilling(MonthlyCharge Charge, HourBalance? Balance);

    /// <summary>
    /// Het bedrag van één maand.
    /// </summary>
    /// <remarks>
    /// <para><strong>De bundel van deze maand komt uit het jaaroverzicht van de uren en niet
    /// rechtstreeks uit het contract.</strong> Dat is het verschil tussen "de afgesproken bundel" en
    /// "de bundel die voor deze maand geldt": een maand vóór de ingangsdatum heeft er geen, en zou
    /// anders een tegoed krijgen dat niemand is overeengekomen. Zie
    /// <see cref="HourBalanceCalculator.ForYear"/> — die regel staat daar en wordt hier niet
    /// nagebouwd.</para>
    ///
    /// <para>Staat de maand niet in dat jaaroverzicht — dat kan alleen bij een maand die er dankzij een
    /// meting bij komt terwijl hij buiten de contractperiode valt — dan is er voor die maand geen
    /// bundel, en dus geen uren boven bundel en geen totaal. Wat er wél staat is het gemeten verbruik,
    /// want dat is een feit.</para>
    /// </remarks>
    private MonthlyBilling Charge(
        string month,
        ContractDocument? contract,
        IReadOnlyList<HourEntryDocument> entries,
        IReadOnlyList<AzureCostDocument> readings,
        bool isInternal)
    {
        var year = HourMonths.YearOf(month);

        var balance = year is { } value
            ? HourBalanceCalculator
                .ForYear(value, contract?.BundledHours, entries, ContractStart(contract), Today())
                .Months
                .FirstOrDefault(candidate => string.Equals(candidate.Month, month, StringComparison.Ordinal))
            : null;

        var reading = Reading(month, readings);

        return new MonthlyBilling(
            MonthlyChargeCalculator.ForMonth(
                month,
                HourMonths.Label(month),
                reading.State,
                reading.Subtotal,
                contract?.AzureSurchargePercentage,
                balance?.OverBundleHours,
                contract?.HourlyRate,
                isInternal),
            balance);
    }

    /// <summary>
    /// De lezing van één maand, of de lezing die zegt dat er niets bekend is.
    /// </summary>
    /// <remarks>
    /// Eén plek waar de afwezigheid van een document tot <see cref="AzureCostState.Unknown"/> wordt.
    /// Zie <see cref="AzureCostReading.From"/>: dat is de enige plek waar dat mag gebeuren, en dit is
    /// de enige aanroeper.
    /// </remarks>
    private static AzureCostReading Reading(string month, IReadOnlyList<AzureCostDocument> readings) =>
        AzureCostReading.From(
            month,
            HourMonths.Label(month),
            readings.FirstOrDefault(document =>
                string.Equals(document.Month, month, StringComparison.Ordinal)));

    /// <summary>De klantvariant van één maand.</summary>
    /// <remarks>
    /// Een expliciete projectie en geen automatische mapping, om dezelfde reden als bij
    /// <see cref="CustomerHourRow.From"/>: komt er morgen een veld bij op <see cref="MonthlyCharge"/>,
    /// dan komt het hier niet stilzwijgend mee. Iemand moet er een regel voor schrijven, en dat is het
    /// moment waarop de vraag "mag de klant dit zien" hoort te vallen.
    /// </remarks>
    private CustomerChargeRow CustomerRow(
        MonthlyBilling billing,
        IReadOnlyList<AzureCostDocument> readings)
    {
        var charge = billing.Charge;

        return new CustomerChargeRow
        {
            Month = charge.Month,
            MonthLabel = charge.MonthLabel,
            AzureCharged = charge.AzureCharged,
            OverBundleHours = charge.OverBundleHours,
            HoursAmount = charge.HoursAmount,
            UsedHours = billing.Balance?.Booked,
            BundledHours = billing.Balance?.BundledHours,
            Total = charge.Total,
            IsRunningMonth = IsRunningMonth(charge.Month),
            IsFinal = charge.IsFinal,
            IsPeriodComplete = charge.IsPeriodComplete,
            IsInternal = charge.IsInternal,
            MeasuredAt = Reading(charge.Month, readings).MeasuredAt,

            // De operatorvlaggen worden hier omgezet en niet doorgegeven. MonthlyChargeGap heeft een
            // waarde die NoSurchargeAgreed heet, en de mededeling "er is nog geen opslag afgesproken"
            // vertelt een klant dat er een opslag ís. Zie CustomerChargeGap.
            Gap = CustomerGap(charge),
            TotalNotice = CustomerTotalNotice(charge),
        };
    }

    /// <summary>De operatorvariant van één maand.</summary>
    private OperatorChargeRow OperatorRow(
        MonthlyBilling billing,
        IReadOnlyList<AzureCostDocument> readings)
    {
        var charge = billing.Charge;
        var reading = Reading(charge.Month, readings);

        return new OperatorChargeRow
        {
            Month = charge.Month,
            MonthLabel = charge.MonthLabel,
            AzureState = charge.AzureState,
            Lines = reading.Lines,
            AzureSubtotal = charge.AzureSubtotal,
            SurchargePercentage = charge.SurchargePercentage,
            SurchargeAmount = charge.SurchargeAmount,
            AzureCharged = charge.AzureCharged,
            OverBundleHours = charge.OverBundleHours,
            HourlyRate = charge.HourlyRate,
            HoursAmount = charge.HoursAmount,
            Total = charge.Total,
            Gap = charge.Gap,
            IsRunningMonth = IsRunningMonth(charge.Month),
            IsFinal = charge.IsFinal,
            IsInternal = charge.IsInternal,
            MeasuredAt = reading.MeasuredAt,
            CoversThrough = reading.CoversThrough,
            Scope = reading.Scope,
            Failure = reading.Failure,
        };
    }

    /// <summary>
    /// Zet de operatorvlaggen om naar de vlaggen die een klant mag zien.
    /// </summary>
    /// <remarks>
    /// <para><strong>De enige plek waar <see cref="MonthlyChargeGap"/> in
    /// <see cref="CustomerChargeGap"/> overgaat, en de omzetting is niet omkeerbaar.</strong> Drie
    /// operatorvlaggen — geen opslag, geen bundel, geen tarief — vallen op één klantvlag. Dat is met
    /// opzet informatieverlies: welke van de drie afspraken ontbreekt, is onze administratie, en de
    /// naam <c>NoSurchargeAgreed</c> noemt onze marge.</para>
    ///
    /// <para>De interne klant komt vóór de rest. Daar is het ontbreken van een bedrag geen gat maar
    /// een antwoord, en de andere vlaggen zeggen er dan niets nuttigs meer bij: een interne omgeving
    /// zonder urenbundel wordt niet doorbelast, en dát is wat er hoort te staan.</para>
    /// </remarks>
    private static CustomerChargeGap CustomerGap(MonthlyCharge charge)
    {
        if (charge.IsInternal)
        {
            return CustomerChargeGap.NotCharged;
        }

        if (charge.HasTotal)
        {
            return CustomerChargeGap.None;
        }

        var gap = CustomerChargeGap.None;

        if (charge.Gap.HasFlag(MonthlyChargeGap.AzureUnknown))
        {
            gap |= CustomerChargeGap.ConsumptionUnknown;
        }

        if (charge.Gap.HasFlag(MonthlyChargeGap.NoSurchargeAgreed)
            || charge.Gap.HasFlag(MonthlyChargeGap.NoBundleAgreed)
            || charge.Gap.HasFlag(MonthlyChargeGap.NoRateAgreed))
        {
            gap |= CustomerChargeGap.ContractIncomplete;
        }

        return gap;
    }

    /// <summary>
    /// Waarom er voor de klant geen totaal is, of <c>null</c> als er wel een is.
    /// </summary>
    /// <remarks>
    /// <para><strong>Nergens het woord "opslag", en dat is een harde eis en geen stijlkwestie.</strong>
    /// "beheeropslag" staat in de lijst met woorden die op geen enkel klantscherm mogen staan, en de
    /// reden daarvoor is dat het onze marge is. Wat de klant nodig heeft is dat het bedrag nog niet
    /// vaststaat en wie het vaststelt; wélke afspraak nog ontbreekt is onze administratie.</para>
    ///
    /// <para>Drie van de vier gaten uit <see cref="MonthlyChargeGap"/> — geen opslag, geen bundel, geen
    /// tarief — leiden daarom naar dezelfde zin. Dat is geen informatieverlies voor de klant: het zijn
    /// alle drie contractafspraken, en de handeling die erop volgt is voor alle drie dezelfde.</para>
    /// </remarks>
    private static string? CustomerTotalNotice(MonthlyCharge charge)
    {
        // Uit de klantvlaggen en niet uit de operatorvlaggen. Zouden ze elk hun eigen omzetting doen,
        // dan bestaat het geval waarin de tekst iets anders zegt dan het gegeven eronder — en dan
        // beslist het maandoverzicht per mail op het ene en leest de klant op het scherm het andere.
        var gap = CustomerGap(charge);

        if (gap == CustomerChargeGap.None)
        {
            return null;
        }

        if (gap.HasFlag(CustomerChargeGap.NotCharged))
        {
            return "Deze omgeving is intern beheer van Soratus en wordt niet doorbelast.";
        }

        var parts = new List<string>(2);

        if (gap.HasFlag(CustomerChargeGap.ConsumptionUnknown))
        {
            parts.Add("Het verbruik van deze maand is nog niet vastgesteld.");
        }

        if (gap.HasFlag(CustomerChargeGap.ContractIncomplete))
        {
            parts.Add("Er staan nog contractafspraken open die dit bedrag bepalen; Soratus stelt het vast.");
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// De som van de maandtotalen, of <c>null</c> zodra er één maand geen totaal heeft.
    /// </summary>
    /// <remarks>
    /// Zie <see cref="CustomerBillingView.YearTotal"/>: geen deelsom. Een jaartotaal waarin twee
    /// maanden ontbreken is niet te onderscheiden van een compleet jaartotaal en het is lager, en van
    /// de twee mogelijke fouten is alleen "geen getal" zichtbaar.
    /// </remarks>
    private static decimal? YearTotal(IReadOnlyList<MonthlyBilling> months) =>
        months.Count > 0 && months.All(month => month.Charge.Total is not null)
            ? months.Sum(month => month.Charge.Total!.Value)
            : null;

    /// <summary>Of dit de maand van vandaag is (§3.7, "de lopende maand staat bovenaan").</summary>
    private bool IsRunningMonth(string month) =>
        string.Equals(month, HourMonths.Of(Today()), StringComparison.Ordinal);

    /// <summary>Vandaag, in de Nederlandse dag. Zie <see cref="PortalTimeZone"/>.</summary>
    private DateOnly Today() =>
        DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), PortalTimeZone.Display).DateTime);

    /// <summary>
    /// De ingangsdatum van het contract, of <c>null</c> als die er niet is of niet te lezen valt.
    /// </summary>
    /// <remarks>
    /// Dezelfde lezing en dezelfde melding als in <see cref="HourViews"/>. Hij staat hier apart en niet
    /// in een gedeelde hulpklasse omdat de twee schermen dan aan één plek hangen voor iets wat drie
    /// regels is; wat wél gedeeld moet zijn — de betekenis van de datum voor de bundel — zit in
    /// <see cref="HourBalanceCalculator"/> en niet in deze functie.
    /// </remarks>
    private DateOnly? ContractStart(ContractDocument? contract)
    {
        if (string.IsNullOrWhiteSpace(contract?.StartsOn))
        {
            return null;
        }

        if (DateOnly.TryParseExact(
                contract.StartsOn.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return date;
        }

        logger.LogWarning(
            "De ingangsdatum '{StartsOn}' van het contract van klant {CustomerId} is niet jjjj-mm-dd. "
            + "Het facturatieoverzicht rekent daardoor met het hele jaar in plaats van vanaf de "
            + "ingangsdatum.",
            contract.StartsOn,
            contract.CustomerId);

        return null;
    }
}
