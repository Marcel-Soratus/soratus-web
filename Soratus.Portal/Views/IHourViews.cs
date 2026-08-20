using Soratus.Portal.Data;
using Soratus.Portal.Security;

namespace Soratus.Portal.Views;

/// <summary>
/// Bouwt de viewmodels van het urenscherm op (§3.6).
/// </summary>
/// <remarks>
/// <para>Staat naast <see cref="IContractViews"/> en niet erin, om dezelfde reden als waarom dat naast
/// <see cref="IPortalViews"/> staat: een andere bron. Het urenscherm leest de urenregels én het
/// contract, en dat laatste alleen voor één getal — de bundel. Dat is geen vermenging maar een
/// afhankelijkheid met een reden: het saldo bestaat niet zonder de bundel, en de bundel staat in het
/// contract. Zou het urenscherm de bundel apart bijhouden, dan bestaan er twee bundels.</para>
///
/// <para><strong>De scope die je meegeeft bepaalt de vorm die je terugkrijgt.</strong> Een
/// <see cref="CustomerScope"/> levert <see cref="CustomerHoursView"/>, een
/// <see cref="CustomerWriteScope"/> levert <see cref="OperatorHoursView"/>. Geen conventie maar
/// overloadresolutie: er is geen manier om met een klantscope het operatorviewmodel te krijgen, want
/// die overload bestaat niet. Dat is hier zwaarder dan bij het contract, want het verschil tussen de
/// twee vormen is precies de acceptatie-eis van fase 3.</para>
///
/// <para><strong>Schrijven loopt hier niet langs.</strong> Een pagina die boekt, fiatteert, afwijst of
/// corrigeert roept <see cref="IPortalHoursStore"/> aan en bouwt daarna de weergave opnieuw op.
/// Dezelfde afspraak als bij het contract: aan een schrijfactie valt niets te rekenen, en een
/// doorgeefluik erlangs zou alleen betekenen dat de meldingen op twee plekken kunnen ontstaan.</para>
///
/// <para><strong>De drie schermtoestanden van §3.6, en hoe ze op deze parameters vallen.</strong></para>
/// <list type="number">
///   <item><description>
///     Standaard: alleen de huidige maand. <c>HoursQuery.ForMonth(nu)</c>, <c>selectedMonth</c> op
///     <c>null</c>.
///   </description></item>
///   <item><description>
///     "Alle maanden": <c>HoursQuery.ForYear(jaar)</c>, <c>selectedMonth</c> op <c>null</c>. Dan komt
///     ook het jaartotaal mee.
///   </description></item>
///   <item><description>
///     Klik op een maand: <c>HoursQuery.ForYear(jaar)</c> met <c>selectedMonth</c> op die maand. De
///     maandtabel blijft compleet, de specificatie filtert.
///   </description></item>
/// </list>
/// </remarks>
public interface IHourViews
{
    /// <summary>
    /// Bouwt het urenscherm zoals de klant het ziet: alleen gefiatteerde regels, en niets dat verraadt
    /// dat er meer bestaat.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="query">Één maand of één jaar.</param>
    /// <param name="selectedMonth">
    /// De maand waarop de specificatie is gefilterd, of <c>null</c> voor de hele weergave.
    /// </param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De weergave. Ook als er niets is geboekt — dan met een maand op "Niets geboekt".</returns>
    Task<CustomerHoursView> BuildHoursAsync(
        CustomerScope scope,
        HoursQuery query,
        string? selectedMonth = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bouwt het urenscherm zoals de operator het ziet: alle standen, met de etags voor de acties.
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="query">Één maand of één jaar.</param>
    /// <param name="selectedMonth">
    /// De maand waarop de specificatie is gefilterd, of <c>null</c>.
    /// </param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De weergave, met de te fiatteren regels en de keuzelijsten van de formulieren.</returns>
    Task<OperatorHoursView> BuildHoursAsync(
        CustomerWriteScope scope,
        HoursQuery query,
        string? selectedMonth = null,
        CancellationToken cancellationToken = default);
}
