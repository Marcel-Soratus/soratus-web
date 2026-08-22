using Soratus.Portal.Security;

namespace Soratus.Portal.Views;

/// <summary>
/// Bouwt de sprintweergave van één klant (§3.4).
/// </summary>
/// <remarks>
/// <para><strong>Twee overloads met dezelfde naam, een ander scopetype en een ander retourtype.</strong>
/// Dezelfde vorm als <see cref="IBillingViews"/>, <see cref="IHourViews"/> en
/// <see cref="IContractViews"/>, en om dezelfde reden: <em>de rolgrens is een typeverschil en geen
/// filter.</em> Er bestaat geen aanroep waarmee je met een <see cref="CustomerScope"/> een
/// <see cref="OperatorSprintView"/> krijgt, en er bestaat geen veld op de klantvorm waar een
/// koppelingsdetail in kan belanden.</para>
///
/// <para><strong>Geen jaartal en geen andere parameter.</strong> Het facturatiescherm neemt een jaar,
/// want een facturatieoverzicht is een lijst maanden. Een sprintweergave is er één: §3.4 vraagt <em>de</em>
/// sprint met zijn statistieken en items, en het portaal bewaart er ook maar één per klant. Zou hier ooit
/// een sprinthistorie komen, dan is dat een parameter erbij en een sleutel met de sprint erin — een
/// wijziging met een lezer, en niet een lijst die per kwartier groeit en waarvan niemand de laatste versie
/// kan aanwijzen.</para>
///
/// <para><strong>Deze laag roept Azure DevOps niet aan.</strong> Hij leest uitsluitend wat er in Cosmos
/// staat, precies zoals het facturatiescherm Cost Management niet aanroept bij het renderen. Zie
/// <see cref="Sprints.SprintCollector"/> voor waarom er wordt verzameld, en waarom §3.4's "het portaal
/// haalt bij openen de laatste status op" de zin is die verschuift.</para>
/// </remarks>
public interface ISprintViews
{
    /// <summary>
    /// De sprintweergave zoals de klant hem mag lezen.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De weergave. Nooit <c>null</c>: een klant zonder lezing krijgt een weergave die dat zegt.</returns>
    /// <remarks>
    /// <para><strong>Er komt altijd een weergave uit en nooit <c>null</c>.</strong> Dat is met opzet: de
    /// afwezigheid van een sprintdocument is een <em>toestand</em> (<see cref="Sprints.SprintState.Unknown"/>)
    /// en geen ontbrekende pagina, en dit is de enige plek waar die omzetting mag gebeuren.</para>
    /// </remarks>
    Task<CustomerSprintView> BuildSprintAsync(
        CustomerScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// De sprintweergave zoals de operator hem mag lezen (§2).
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant, als bewijs van de rol.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De weergave.</returns>
    /// <remarks>
    /// Vraagt een schrijfbewijs om te lezen, net als <see cref="IBillingViews"/>. Er wordt geen recht mee
    /// opgerekt en er valt niets te schrijven — §3.4 is uitdrukkelijk: het portaal schrijft nooit terug
    /// naar DevOps. Het is het bewijs dat de aanroeper een operator is die naar déze klant kijkt, en dat
    /// is de voorwaarde om de koppeling en de reden van een mislukking te mogen zien.
    /// </remarks>
    Task<OperatorSprintView> BuildSprintAsync(
        CustomerWriteScope scope,
        CancellationToken cancellationToken = default);
}
