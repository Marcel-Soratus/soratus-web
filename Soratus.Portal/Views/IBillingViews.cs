using Soratus.Portal.Security;

namespace Soratus.Portal.Views;

/// <summary>
/// Bouwt de weergaven van het facturatiescherm op (§3.7).
/// </summary>
/// <remarks>
/// <para><strong>Twee overloads met dezelfde naam en een andere scope, en dat is de rolgrens.</strong>
/// Er bestaat geen aanroep waarmee je met een <see cref="CustomerScope"/> een
/// <see cref="OperatorBillingView"/> krijgt. Dezelfde vorm als <see cref="IHourViews"/>, en om dezelfde
/// reden: de acceptatie-eis is een typeverschil en geen filter.</para>
///
/// <para><strong>Een eigen interface naast <see cref="IHourViews"/>, ook al leest hij de uren.</strong>
/// Hij leest ze voor precies één getal — de uren boven bundel — en hij leest ze door
/// <see cref="Data.HourBalanceCalculator"/> heen, dus het is hetzelfde getal dat op het urenscherm
/// staat. Eén klasse voor beide schermen zou betekenen dat het urenscherm een kostenopslag injecteert
/// die het niet gebruikt, en dat is precies de vermenging die <see cref="IContractViews"/> naast
/// <see cref="IPortalViews"/> destijds heeft voorkomen.</para>
///
/// <para>Er is precies één implementatie: <see cref="BillingViews"/>.</para>
/// </remarks>
public interface IBillingViews
{
    /// <summary>
    /// Het facturatieoverzicht van één jaar, zoals de klant het mag lezen (§3.7).
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="year">Het jaartal.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De weergave, met de maanden nieuwste eerst.</returns>
    /// <remarks>
    /// De maanden zijn die waarover er iets te zeggen valt: een meting, geboekte uren boven bundel, of
    /// de lopende maand. Een maand waarin de klant nog niet bestond staat er niet, want daarvan is
    /// "onbekend" geen mededeling maar ruis. Zie <see cref="BuildMonthAsync(Security.CustomerScope, string, CancellationToken)"/> voor het geval dat
    /// iemand tóch om zo'n maand vraagt.
    /// </remarks>
    Task<CustomerBillingView> BuildBillingAsync(
        CustomerScope scope,
        int year,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Het facturatieoverzicht van één jaar, voor de operator (§2).
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="year">Het jaartal.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De weergave, met de uitsplitsing per dienst en de beheeropslag erbij.</returns>
    Task<OperatorBillingView> BuildBillingAsync(
        CustomerWriteScope scope,
        int year,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Het bedrag van één maand, in de vorm die de klant mag lezen.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="month">De maand als <c>yyyy-MM</c>.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De maand. Nooit <c>null</c>: een maand zonder meting is een maand zonder bedrag.</returns>
    /// <remarks>
    /// <para><strong>Dit is de ingang voor het maandoverzicht per mail (§3.7, "Maandoverzicht mailen
    /// naar de contactpersoon").</strong> Hij levert met opzet het <em>klant</em>type
    /// (<see cref="CustomerChargeRow"/>) en niet het operatortype: de mail gaat naar de contactpersoon
    /// van de klant, en een mail die uit het operatortype wordt opgemaakt heeft onze marge één
    /// veldverwijzing ver weg. Dat een mailtekst geen <c>@if</c> nodig heeft om die grens te houden, is
    /// hetzelfde argument als bij de twee razorcomponenten.</para>
    ///
    /// <para><strong>Geen <c>null</c> bij een onbekende maand.</strong> Een aanroeper die <c>null</c>
    /// terugkrijgt moet zelf besluiten wat dat betekent, en de kans is groot dat hij er nul van maakt —
    /// precies de fout die dit hele onderdeel probeert uit te sluiten. Wat er terugkomt is een rij met
    /// <c>null</c>-bedragen en een <see cref="CustomerChargeRow.TotalNotice"/> die zegt waarom. Een
    /// mail over een maand zonder bedrag hoort te zeggen dat het bedrag nog niet vaststaat, en niet
    /// € 0,00 te melden.</para>
    /// </remarks>
    Task<CustomerChargeRow> BuildMonthAsync(
        CustomerScope scope,
        string month,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Het bedrag van één maand in de klantvorm, gelezen met een schrijfbewijs.
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant. Levert de klantslug.</param>
    /// <param name="month">De maand als <c>yyyy-MM</c>.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De maand, in de vorm die de klant mag zien.</returns>
    /// <remarks>
    /// <para><strong>Een schrijfbewijs dat een klantvorm oplevert, en dat is met opzet en geen
    /// verschrijving.</strong> Elders in dit portaal loopt de scope mee met de rijkdom van het antwoord:
    /// een <see cref="CustomerWriteScope"/> geeft de operatorweergave en een <see cref="CustomerScope"/>
    /// de klantweergave. Hier niet. De scope is hier het <em>bewijs van toegang</em> en het
    /// retourtype is de <em>projectie</em>, en dat zijn twee verschillende vragen.</para>
    ///
    /// <para>De reden dat het zo moet: het maandoverzicht per mail draait namens Soratus en niet namens
    /// een ingelogde klant, en een <see cref="CustomerScope"/> bestaat alleen voor een klant met een
    /// ingerichte telemetrie-opslag. Juist de klant zonder uitgerolde agents heeft wél een contract en
    /// wél Azure-kosten; die zou anders geen maandoverzicht kunnen krijgen omdat zijn agents nog niet
    /// draaien, en dat is een koppeling tussen twee dingen die niets met elkaar te maken hebben. Dat
    /// argument staat bij <c>IMonthlyStatementFigures</c> en het is juist.</para>
    ///
    /// <para>Wat er dan overblijft is de vraag of dit een gat is: kan een aanroeper hiermee de
    /// operatorgegevens bereiken? Nee — het retourtype is <see cref="CustomerChargeRow"/> en dat draagt
    /// de uitsplitsing, de marge en de scope niet. Wie de operatorvorm wil moet
    /// <see cref="BuildBillingAsync(CustomerWriteScope, int, CancellationToken)"/> aanroepen, en dat is
    /// een andere methode met een andere naam.</para>
    /// </remarks>
    Task<CustomerChargeRow> BuildMonthAsync(
        CustomerWriteScope scope,
        string month,
        CancellationToken cancellationToken = default);
}
