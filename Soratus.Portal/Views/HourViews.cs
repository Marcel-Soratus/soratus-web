using System.Globalization;
using Soratus.Portal.Data;
using Soratus.Portal.Security;

namespace Soratus.Portal.Views;

/// <summary>
/// Bouwt de viewmodels van het urenscherm op uit de urenregels en de bundel uit het contract.
/// </summary>
/// <remarks>
/// <para><strong>Beide rollen rekenen met dezelfde functie op dezelfde regels.</strong> Het
/// maandtotaal komt uit <see cref="HourBalanceCalculator.ForMonth"/>, en die filtert zelf op
/// gefiatteerd. Er is dus geen pad waarlangs de operator een ander totaal ziet dan de klant, ook niet
/// als de operator regels in handen heeft die de klant niet krijgt. Dat is de acceptatie-eis van fase 3
/// en hij is hier waar door constructie en niet door zorgvuldigheid.</para>
///
/// <para>De klok wordt één keer per opbouw uitgelezen, dezelfde afspraak als in
/// <see cref="PortalViews"/> en <see cref="ContractViews"/>. "Vandaag" is de Nederlandse dag; zie
/// <see cref="PortalTimeZone"/>.</para>
/// </remarks>
internal sealed class HourViews(
    IPortalHoursStore hours,
    IPortalDataStore store,
    ICustomerDirectory directory,
    TimeProvider timeProvider,
    ILogger<HourViews> logger) : IHourViews
{
    /// <inheritdoc />
    public async Task<CustomerHoursView> BuildHoursAsync(
        CustomerScope scope,
        HoursQuery query,
        string? selectedMonth = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        var contract = await store.GetContractAsync(scope, cancellationToken).ConfigureAwait(false);
        var entries = await hours.GetApprovedHoursAsync(scope, query, cancellationToken)
            .ConfigureAwait(false);

        var period = Period(query, contract, entries);
        var shown = Filter(entries, selectedMonth);

        return new CustomerHoursView
        {
            CustomerId = scope.CustomerId,
            DisplayName = scope.DisplayName,
            GeneratedAt = timeProvider.GetUtcNow(),
            IsInternal = scope.IsInternal,
            BundledHours = contract?.BundledHours,
            HourlyRate = contract?.HourlyRate,
            Months = period.Months,
            Year = period.Year,
            SelectedMonth = selectedMonth,
            Entries = [.. shown.Select(CustomerHourRow.From)],

            // De som van precies de regels die eronder staan, en niet een tweede telling. Alles in
            // deze lijst is gefiatteerd — de query gaf niets anders terug — dus dit is per definitie
            // hetzelfde getal als het maandtotaal van de gefilterde maand.
            EntryHours = shown.Sum(entry => entry.Hours),
            ReadOnlyNotice = HoursNotice.CustomerReadOnly,
            TotalNotice = HoursNotice.TotalIsTheSum,
            NoBundleNotice = contract?.BundledHours is null ? HoursNotice.NoBundle : null,
        };
    }

    /// <inheritdoc />
    public async Task<OperatorHoursView> BuildHoursAsync(
        CustomerWriteScope scope,
        HoursQuery query,
        string? selectedMonth = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        var contract = await store.GetContractAsync(scope, cancellationToken).ConfigureAwait(false);
        var entries = await hours.GetHoursAsync(scope, query, cancellationToken).ConfigureAwait(false);

        var period = Period(query, contract, entries);
        var shown = Filter(entries, selectedMonth);

        // Afgewezen regels vallen uit de specificatie en krijgen hun eigen lijst. Zie
        // OperatorHoursView.Rejected: het bezwaar tegen bewaren is dat de lijst volloopt, en dat is
        // hier opgelost en niet in de opslag.
        var rejected = shown.Where(entry => entry.Status == HourEntryStatus.Rejected).ToArray();
        var specification = shown.Where(entry => entry.Status != HourEntryStatus.Rejected).ToArray();
        var pending = specification.Where(entry => entry.Status == HourEntryStatus.Pending).ToArray();

        var months = period.Months
            .Select(balance => new OperatorMonthRow
            {
                Balance = balance,
                PendingHours = PendingIn(entries, balance.Month).Sum(entry => entry.Hours),
                PendingCount = PendingIn(entries, balance.Month).Count(),
            })
            .ToArray();

        var today = Today();

        return new OperatorHoursView
        {
            CustomerId = scope.CustomerId,
            DisplayName = scope.DisplayName,
            GeneratedAt = timeProvider.GetUtcNow(),
            // Uit de klantenlijst en niet uit de scope: CustomerWriteScope draagt dit gegeven bewust
            // niet, want hij leunt niet op een CustomerScope (zie dat type). Dezelfde terugval als in
            // ContractViews.
            IsInternal = directory.Find(scope.CustomerId)?.IsInternal ?? false,
            BundledHours = contract?.BundledHours,
            HourlyRate = contract?.HourlyRate,
            HasContract = contract is not null,
            Months = months,
            Year = period.Year,
            SelectedMonth = selectedMonth,
            Entries = [.. specification.Select(OperatorHourRow.From)],
            Rejected = [.. rejected.Select(OperatorHourRow.From)],
            EntryHours = specification.Where(entry => entry.Counts).Sum(entry => entry.Hours),
            PendingHours = pending.Sum(entry => entry.Hours),
            PendingCount = pending.Length,

            // Nieuwste maand bovenaan in het keuzeveld: een operator boekt bijna altijd op de maand
            // waarin hij zit. De tabel eronder loopt juist oudste-eerst, want dat is een tijdlijn.
            BookableMonths = [.. period.Months.Select(month => month.Month).Reverse()],
            DefaultMonth = selectedMonth ?? DefaultMonth(period, today),
            PendingNotice = HoursNotice.PendingRule,
            TotalNotice = HoursNotice.TotalIsTheSum,
            CorrectionNotice = HoursNotice.CorrectionIsAnEntry,
            ApprovalNotice = HoursNotice.ApprovalIsFinal,
            RejectionNotice = HoursNotice.RejectionIsKept,
            NoBundleNotice = contract?.BundledHours is null ? HoursNotice.NoBundle : null,
        };
    }

    // ── Binnenwerk ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// De maandstanden en, bij een jaarweergave, het jaartotaal.
    /// </summary>
    /// <remarks>
    /// Één plek voor beide rollen, zodat de maandtabel van de operator letterlijk dezelfde
    /// <see cref="HourBalance"/>-objecten bevat als die van de klant. De regels die erin gaan
    /// verschillen wel — de operator heeft er ook te fiatteren regels bij — maar
    /// <see cref="HourBalanceCalculator.ForMonth"/> filtert die eruit, en dat is precies waarom dat
    /// filter daar staat en niet bij de aanroeper.
    /// </remarks>
    private (IReadOnlyList<HourBalance> Months, HourYear? Year) Period(
        HoursQuery query,
        ContractDocument? contract,
        IReadOnlyList<HourEntryDocument> entries)
    {
        var start = ContractStart(contract);

        if (query.IsSingleMonth)
        {
            // Eén maand, en geen jaartotaal. Zie CustomerHoursView.Year: een jaartotaal over één maand
            // is geen jaartotaal maar een tweede plek waar hetzelfde getal staat.
            return
            (
                [HourBalanceCalculator.ForMonth(query.Month!, contract?.BundledHours, entries)],
                null
            );
        }

        var year = HourBalanceCalculator.ForYear(
            query.Year,
            contract?.BundledHours,
            entries,
            start,
            Today());

        return (year.Months, year);
    }

    /// <summary>
    /// De regels die in de specificatie horen: alle, of die van de gekozen maand (§3.6).
    /// </summary>
    private static IReadOnlyList<HourEntryDocument> Filter(
        IReadOnlyList<HourEntryDocument> entries,
        string? selectedMonth) =>
        selectedMonth is null
            ? entries
            : [.. entries.Where(entry => string.Equals(entry.Month, selectedMonth, StringComparison.Ordinal))];

    private static IEnumerable<HourEntryDocument> PendingIn(
        IEnumerable<HourEntryDocument> entries,
        string month) =>
        entries.Where(entry =>
            entry.Status == HourEntryStatus.Pending
            && string.Equals(entry.Month, month, StringComparison.Ordinal));

    /// <summary>
    /// De maand die het boekformulier voorselecteert (§3.6, "default huidige").
    /// </summary>
    /// <remarks>
    /// De huidige maand als die in de weergave zit, en anders de laatste maand die er wél in zit. Dat
    /// tweede geval is een operator die naar een vorig jaar kijkt; dan is de huidige maand geen geldige
    /// keuze en zou een formulier met een maand buiten de lijst bij het opslaan afketsen.
    /// </remarks>
    private static string DefaultMonth((IReadOnlyList<HourBalance> Months, HourYear? Year) period, DateOnly today)
    {
        var current = HourMonths.Of(today);

        return period.Months.Any(month => string.Equals(month.Month, current, StringComparison.Ordinal))
            ? current
            : period.Months.Count > 0 ? period.Months[^1].Month : current;
    }

    /// <summary>Vandaag, in de Nederlandse dag. Zie <see cref="PortalTimeZone"/>.</summary>
    private DateOnly Today() =>
        DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), PortalTimeZone.Display).DateTime);

    /// <summary>
    /// De ingangsdatum van het contract, of <c>null</c> als die er niet is of niet te lezen valt.
    /// </summary>
    /// <remarks>
    /// Bepaalt welke maanden er in het jaaroverzicht meetellen; zie
    /// <see cref="HourBalanceCalculator.MonthsInScope"/>. Een onleesbare datum wordt gelogd en niet
    /// stil als "geen contractbegin" behandeld: dat laatste zou het jaaroverzicht twaalf maanden bundel
    /// laten rekenen waar er misschien drie zijn afgesproken, en dat scheelt geld.
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
            "De ingangsdatum '{StartsOn}' van het contract van klant {CustomerId} is niet jjjj-mm-dd. " +
            "Het urenoverzicht rekent daardoor met het hele jaar in plaats van vanaf de ingangsdatum.",
            contract.StartsOn,
            contract.CustomerId);

        return null;
    }

}
