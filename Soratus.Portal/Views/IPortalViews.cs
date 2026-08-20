using Soratus.Portal.Security;

namespace Soratus.Portal.Views;

/// <summary>
/// Bouwt de viewmodels op uit de telemetrie. De enige laag tussen de store en een Razor-pagina.
/// </summary>
/// <remarks>
/// <para><strong>De scope die je meegeeft bepaalt de vorm die je terugkrijgt.</strong> Geef een
/// <see cref="CustomerScope"/> en je krijgt het klanttype; geef een
/// <see cref="OperatorCustomerScope"/> en je krijgt het operatortype. Dat is geen conventie maar
/// overloadresolutie: er is geen manier om met een klantscope het operatorviewmodel te krijgen,
/// want die overload bestaat niet.</para>
///
/// <para>Alles wat te rekenen valt is hier al gerekend: statussen, tellingen, sortering,
/// stiltes. Een pagina drukt af en maakt op — een relatieve tijd, een label, een glyph — en rekent
/// niet. Zou een pagina wél rekenen, dan is er een tweede plek waar een getal ontstaat, en dat is
/// precies hoe twee schermen elkaar gaan tegenspreken.</para>
/// </remarks>
public interface IPortalViews
{
    /// <summary>
    /// Bouwt het Soratus-overzicht over alle klanten (§3.1).
    /// </summary>
    /// <param name="scope">Het operatorrecht.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Het overzicht, met de klanten al gesorteerd.</returns>
    Task<OperatorOverviewView> BuildOverviewAsync(
        OperatorScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bouwt de klantweergave van de agentlijst: read-only, alleen productie (§3.2).
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De weergave.</returns>
    Task<CustomerAgentsView> BuildAgentsAsync(
        CustomerScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bouwt de operatorweergave van de agentlijst van één klant: alle omgevingen.
    /// </summary>
    /// <param name="scope">Het operatorrecht op deze klant.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De weergave.</returns>
    Task<OperatorCustomerAgentsView> BuildAgentsAsync(
        OperatorCustomerScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bouwt het agentdetail zoals de klant het ziet (§3.3).
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="agentName">De technische naam uit de URL.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// De weergave, of <c>null</c> als de agent niet bestaat, van een andere klant is, óf niet in
    /// de productieomgeving draait. Alle drie leveren 404 op; het scherm hoort ze niet uit elkaar
    /// te houden.
    /// </returns>
    Task<CustomerAgentDetailView?> BuildAgentDetailAsync(
        CustomerScope scope,
        string agentName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bouwt het agentdetail zoals de operator het ziet.
    /// </summary>
    /// <param name="scope">Het operatorrecht op deze klant.</param>
    /// <param name="agentName">De technische naam uit de URL.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De weergave, of <c>null</c> als de agent niet bestaat of van een andere klant is.</returns>
    Task<OperatorAgentDetailView?> BuildAgentDetailAsync(
        OperatorCustomerScope scope,
        string agentName,
        CancellationToken cancellationToken = default);
}
