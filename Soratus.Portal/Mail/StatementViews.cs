using Microsoft.Extensions.Options;
using Soratus.Portal.Data;
using Soratus.Portal.Security;

namespace Soratus.Portal.Mail;

/// <summary>
/// Bouwt de weergave van het maandoverzicht en de verzendbevestigingen op.
/// </summary>
/// <remarks>
/// <para><strong>Er is precies één overload en die neemt een <see cref="CustomerWriteScope"/>.</strong>
/// Bij het contractscherm en het urenscherm zijn er twee — een klantscope levert het klanttype, een
/// schrijfrecht het operatortype — en dat is daar de vorm waarin de rolgrens een typeverschil is.
/// Hier is de rolgrens strenger: er is <em>geen</em> klantvorm. Een verzendbevestiging draagt de
/// e-mailadressen waar wij de klant op hebben gemaild, de onderwerpregel, het aantal pogingen en de
/// vaststelling van een operator over een mislukte verzending. Dat is allemaal Soratus-werk. Zou er
/// een klantoverload bestaan, dan zou de vraag "wat mag de klant hiervan zien" per veld beantwoord
/// moeten worden — en dat is precies het soort vraag dat dit portaal met een type oplost in plaats
/// van met een <c>@if</c>.</para>
///
/// <para>Wat de klant wél hoort te zien over zijn facturatie staat in §3.7 en is het bedrag en de
/// status van de factuur. Dat is de kostenkant en niet deze.</para>
/// </remarks>
public interface IStatementViews
{
    /// <summary>
    /// De weergave voor de operator: wat er per maand is verstuurd en wat er nog kan.
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="year">Het jaar dat in beeld is.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De weergave.</returns>
    Task<OperatorStatementView> BuildStatementsAsync(
        CustomerWriteScope scope,
        int year,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// De enige implementatie van <see cref="IStatementViews"/>.
/// </summary>
internal sealed class StatementViews(
    IStatementStore statements,
    IOptions<PortalMailOptions> options,
    TimeProvider timeProvider) : IStatementViews
{
    /// <summary>
    /// Hoeveel afgesloten maanden er in de keuzelijst staan.
    /// </summary>
    /// <remarks>
    /// Twaalf, zodat een maand die in december is vergeten in januari nog te versturen is. Meer zou
    /// suggereren dat een overzicht van twee jaar terug nog iets betekent; dat is dan een
    /// factuurdiscussie en geen mail.
    /// </remarks>
    private const int SendableMonths = 12;

    /// <inheritdoc />
    public async Task<OperatorStatementView> BuildStatementsAsync(
        CustomerWriteScope scope,
        int year,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var now = timeProvider.GetUtcNow();
        var documents = await statements.ListAsync(scope, year, cancellationToken).ConfigureAwait(false);
        var byMonth = documents.ToDictionary(document => document.Month, StringComparer.Ordinal);

        return new OperatorStatementView
        {
            CustomerId = scope.CustomerId,
            DisplayName = scope.DisplayName,
            GeneratedAt = now,
            Year = year,
            IsMailConfigured = options.Value.Sender() is not null,
            IsDryRun = options.Value.DryRun,
            DefaultMonth = StatementText.PreviousMonthOf(now),
            Months = [.. ClosedMonths(now)],
            Rows = [.. documents.Select(Row)],
            HasRow = month => byMonth.ContainsKey(month),
        };
    }

    /// <summary>De afgesloten maanden, nieuwste eerst.</summary>
    /// <remarks>
    /// Uitgerekend en niet uit de opslag gehaald: een maand waarover nog nooit is gemaild heeft geen
    /// document, en juist die maand moet in de lijst staan. Punt 2 van de fase-0-afwijkingen, van de
    /// andere kant bekeken — de afwezigheid van een document is hier de normale toestand.
    /// </remarks>
    private static IEnumerable<string> ClosedMonths(DateTimeOffset now)
    {
        for (var back = 1; back <= SendableMonths; back++)
        {
            yield return StatementText.PreviousMonthOf(now.AddMonths(1 - back));
        }
    }

    private static StatementRow Row(StatementDocument document) => new()
    {
        Month = document.Month,
        MonthLabel = HourMonths.Label(document.Month),
        State = document.State,
        StateLabel = StatementText.StateLabel(document.State),
        StateNotice = StatementText.StateNotice(document.State),
        AttemptedAt = document.AttemptedAt,
        AttemptedBy = document.AttemptedBy,
        SentAt = document.SentAt,
        OperationId = document.OperationId,
        Recipients = document.Recipients,
        Subject = document.Subject,
        MeasuredAt = document.MeasuredAt,
        Total = document.Total,
        Attempts = document.Attempts,
        ReleasedAt = document.ReleasedAt,
        ReleasedBy = document.ReleasedBy,
        ReleaseNote = document.ReleaseNote,
        ETag = document.ETag,
        WhyNotSend = StatementTransitions.WhyNotSend(document),
        WhyNotRelease = StatementTransitions.WhyNotRelease(document),
    };
}

/// <summary>
/// De verzendbevestigingen van één klant, zoals de operator ze ziet.
/// </summary>
/// <remarks>
/// Er is geen klantvariant van dit type, en dat is geen omissie — zie <see cref="IStatementViews"/>.
/// </remarks>
public sealed record OperatorStatementView
{
    /// <summary>De klantslug.</summary>
    public required string CustomerId { get; init; }

    /// <summary>De klantnaam.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Wanneer deze weergave is opgebouwd.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>Het jaar dat in beeld is.</summary>
    public required int Year { get; init; }

    /// <summary>
    /// Of er een Communication Services-endpoint en een afzender zijn geconfigureerd.
    /// </summary>
    /// <remarks>
    /// Staat op het scherm en niet alleen in een melding bij het mislukken. Een knop die pas bij het
    /// indrukken zegt dat de dienst niet is ingericht, belooft dat het wél kan — en dat is de
    /// ontwerpregel uit §1 van de spec: laat de beperking zien in plaats van hem weg te poetsen.
    /// </remarks>
    public required bool IsMailConfigured { get; init; }

    /// <summary>
    /// Of het portaal in proefdraaimodus staat.
    /// </summary>
    /// <remarks>
    /// Zichtbaar, en met zoveel woorden. Een operator die denkt dat hij heeft gemaild terwijl er
    /// niets is verstuurd, is de gevaarlijkste van de twee vergissingen: de klant wacht dan op iets
    /// wat niet komt en niemand weet het.
    /// </remarks>
    public required bool IsDryRun { get; init; }

    /// <summary>De maand die is voorgeselecteerd: de vorige maand.</summary>
    public required string DefaultMonth { get; init; }

    /// <summary>De afgesloten maanden waarover verstuurd kan worden, nieuwste eerst.</summary>
    /// <remarks>
    /// Een keuzelijst en geen vrij veld, om dezelfde reden als bij het boekformulier van de uren: het
    /// formulier kan dan geen maand aanbieden die de schrijfkant weigert.
    /// </remarks>
    public required IReadOnlyList<string> Months { get; init; }

    /// <summary>De bevestigingen van dit jaar, nieuwste maand eerst.</summary>
    public required IReadOnlyList<StatementRow> Rows { get; init; }

    /// <summary>Of er over deze maand al een bevestiging is.</summary>
    /// <remarks>
    /// Een functie en geen tweede lijst, zodat de maandkeuzelijst en de bevestigingen niet uiteen
    /// kunnen lopen.
    /// </remarks>
    public required Func<string, bool> HasRow { get; init; }
}

/// <summary>
/// Eén verzendbevestiging, zoals de operator hem ziet.
/// </summary>
public sealed record StatementRow
{
    /// <summary>De maand als <c>jjjj-MM</c>.</summary>
    public required string Month { get; init; }

    /// <summary>De maand in woorden, bijvoorbeeld <c>augustus 2026</c>.</summary>
    public required string MonthLabel { get; init; }

    /// <summary>De verzendtoestand.</summary>
    public required StatementSendState State { get; init; }

    /// <summary>Het woordlabel van de toestand.</summary>
    /// <remarks>
    /// Draagt de tekst mee in plaats van dat het scherm hem verzint. Dezelfde afspraak als bij
    /// <c>ContractNotice</c> en <c>AgentConfigurationNotice</c>: dit is een bewering over wat het
    /// portaal weet, en die hoort te veranderen als het portaal verandert en niet als iemand een
    /// Razor-bestand herschrijft.
    /// </remarks>
    public required string StateLabel { get; init; }

    /// <summary>De uitleg bij de toestand.</summary>
    public required string StateNotice { get; init; }

    /// <summary>Wanneer de laatste poging is begonnen.</summary>
    public required DateTimeOffset AttemptedAt { get; init; }

    /// <summary>Welke operator hem heeft gestart.</summary>
    public string? AttemptedBy { get; init; }

    /// <summary>Wanneer het bericht is aangenomen, of <c>null</c>.</summary>
    public DateTimeOffset? SentAt { get; init; }

    /// <summary>De operatie-id van Communication Services, of <c>null</c>.</summary>
    public string? OperationId { get; init; }

    /// <summary>De ontvangers zoals ze zijn geadresseerd.</summary>
    public IReadOnlyList<string> Recipients { get; init; } = [];

    /// <summary>De onderwerpregel zoals hij is verstuurd.</summary>
    public string? Subject { get; init; }

    /// <summary>Wanneer de kostenmeting achter deze bedragen is gedaan.</summary>
    public DateTimeOffset? MeasuredAt { get; init; }

    /// <summary>
    /// Het totaal dat in de mail stond, of <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Uit het document en niet opnieuw uitgerekend. Dat is het hele punt van het vastleggen: over
    /// een maand levert een herberekening een ander getal op, en de vraag is wat er in de mail stond.
    /// </remarks>
    public decimal? Total { get; init; }

    /// <summary>Hoeveel keer er over deze maand een verzending is gestart.</summary>
    public required int Attempts { get; init; }

    /// <summary>Wanneer een operator de onbekende uitkomst heeft vastgesteld.</summary>
    public DateTimeOffset? ReleasedAt { get; init; }

    /// <summary>Welke operator dat heeft gedaan.</summary>
    public string? ReleasedBy { get; init; }

    /// <summary>Wat hij heeft vastgesteld. Komt in geen enkele mail.</summary>
    public string? ReleaseNote { get; init; }

    /// <summary>De etag, voor het vaststellingsformulier.</summary>
    public string? ETag { get; init; }

    /// <summary>Waarom er niet (opnieuw) verstuurd kan worden, of <c>null</c> als het kan.</summary>
    /// <remarks>
    /// Uit <see cref="StatementTransitions.WhyNotSend"/>, dezelfde functie die de schrijfkant
    /// gebruikt. Zonder dat zou er een knop kunnen staan die een melding oplevert.
    /// </remarks>
    public string? WhyNotSend { get; init; }

    /// <summary>Waarom er niets is vast te stellen, of <c>null</c> als dat kan.</summary>
    public string? WhyNotRelease { get; init; }
}
