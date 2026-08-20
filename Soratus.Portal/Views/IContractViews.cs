using Soratus.Portal.Security;

namespace Soratus.Portal.Views;

/// <summary>
/// Bouwt de viewmodels van het contractscherm op uit de portaaleigen opslag (§3.5).
/// </summary>
/// <remarks>
/// <para>Staat naast <see cref="IPortalViews"/> en niet erin, om twee redenen. De eerste is de bron:
/// dit scherm leest geen telemetrie maar de eigen administratie, en dat is een andere opslag met een
/// ander rechtenmodel. De tweede is dezelfde als altijd — <see cref="PortalViews"/> heeft de
/// telemetriestore in zijn constructor, en een klasse die twee opslagen bedient wordt de plek waar
/// per ongeluk het ene met het andere wordt gemengd.</para>
///
/// <para><strong>De scope die je meegeeft bepaalt de vorm die je terugkrijgt.</strong> Een
/// <see cref="CustomerScope"/> levert het klanttype, een <see cref="CustomerWriteScope"/> het
/// operatortype. Dat is geen conventie maar overloadresolutie: er is geen manier om met een
/// klantscope het operatorviewmodel te krijgen, want die overload bestaat niet.</para>
///
/// <para><strong>Schrijven loopt hier niet langs.</strong> Een pagina die iets wijzigt roept
/// <see cref="Data.IPortalDataStore"/> aan en bouwt daarna de weergave opnieuw op. Dat is bewust:
/// aan een schrijfactie valt niets te rekenen, en een doorgeefluik erlangs zou alleen betekenen dat
/// de meldingen op twee plekken kunnen ontstaan. De store geeft ze in het Nederlands terug, met het
/// huidige document erbij als iemand anders eerder was.</para>
/// </remarks>
public interface IContractViews
{
    /// <summary>
    /// Bouwt het contractscherm zoals de klant het ziet: lezen, zonder marge en zonder etags.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De weergave. Ook als er nog geen contract is — dan met <c>HasContract</c> op <c>false</c>.</returns>
    Task<CustomerContractView> BuildContractAsync(
        CustomerScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bouwt het contractscherm zoals de operator het ziet en bewerkt.
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De weergave, met de etags die het formulier moet terugsturen.</returns>
    Task<OperatorContractView> BuildContractAsync(
        CustomerWriteScope scope,
        CancellationToken cancellationToken = default);
}
