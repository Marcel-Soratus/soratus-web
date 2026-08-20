using System.Security.Claims;

namespace Soratus.Portal.Security;

/// <summary>
/// De enige plek waar een scope kan ontstaan.
/// </summary>
/// <remarks>
/// Elke methode geeft <c>null</c> terug als de gebruiker er geen recht op heeft. Dat is de enige
/// manier waarop een weigering hier naar buiten komt: er wordt niet geworpen, en er is geen
/// aparte "mag ik"-vraag die je kunt vergeten te stellen. Je vraagt een scope, en je krijgt hem
/// wel of niet.
///
/// <para><strong>Weigeren is 404, niet 403.</strong> Een pagina die <c>null</c> terugkrijgt hoort
/// <c>NavigationManager.NotFound()</c> aan te roepen en niets anders. 403 zou bevestigen dat de
/// klant achter die URL bestaat, en dat is precies wat we iemand die er niet bij hoort niet willen
/// vertellen. Om dezelfde reden geven deze methoden ook <c>null</c> voor een klant die helemaal
/// niet bestaat: de twee gevallen zijn van buitenaf niet te onderscheiden, en dat is de
/// bedoeling.</para>
/// </remarks>
public interface ICustomerScopeResolver
{
    /// <summary>
    /// Vraagt leesrecht op één klant.
    /// </summary>
    /// <param name="user">De aangemelde gebruiker.</param>
    /// <param name="customerId">De slug uit de URL.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// De scope, of <c>null</c> als de gebruiker geen recht heeft op deze klant of als de klant
    /// niet bestaat.
    /// </returns>
    /// <remarks>
    /// Een operator krijgt een scope voor elke bestaande klant. Een klantgebruiker alleen voor de
    /// klanten waar zijn e-mailadres in de toegangslijst staat.
    /// </remarks>
    Task<CustomerScope?> ResolveAsync(
        ClaimsPrincipal? user,
        string? customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Vraagt het recht om over alle klanten heen te kijken.
    /// </summary>
    /// <param name="user">De aangemelde gebruiker.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De operatorscope, of <c>null</c> als de gebruiker geen operator is.</returns>
    Task<OperatorScope?> ResolveOperatorAsync(
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Vraagt operatorrecht op één klant, voor de doorklikweergave met beheerfuncties.
    /// </summary>
    /// <param name="user">De aangemelde gebruiker.</param>
    /// <param name="customerId">De slug uit de URL.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// De scope, of <c>null</c> als de gebruiker geen operator is of de klant niet bestaat.
    /// </returns>
    Task<OperatorCustomerScope?> ResolveOperatorAsync(
        ClaimsPrincipal? user,
        string? customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Vraagt het recht om portaaleigen gegevens te wijzigen.
    /// </summary>
    /// <param name="user">De aangemelde gebruiker.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Het schrijfrecht, of <c>null</c> als de gebruiker geen operator is.</returns>
    /// <remarks>
    /// Voor het aanmaken van een klant (§3.9): dat gaat over een klant die nog niet bestaat, dus er
    /// is niets om een klantscope op te baseren. Alleen een operator krijgt dit — de rolmatrix (§2)
    /// geeft de klant op contract en toegang lezen en niets meer.
    /// </remarks>
    Task<PortalWriteScope?> ResolveWriteAsync(
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Vraagt het recht om de portaalgegevens van één klant te wijzigen.
    /// </summary>
    /// <param name="user">De aangemelde gebruiker.</param>
    /// <param name="customerId">De slug uit de URL.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// Het schrijfrecht, of <c>null</c> als de gebruiker geen operator is of de klant niet bestaat.
    /// </returns>
    /// <remarks>
    /// Anders dan
    /// <see cref="ResolveOperatorAsync(ClaimsPrincipal?,string?,CancellationToken)"/> hangt dit
    /// <em>niet</em> aan een ingerichte telemetrie-opslag. Zie <see cref="CustomerWriteScope"/>: de
    /// klant zonder opslag is juist de klant wiens contract je aan het invullen bent.
    /// </remarks>
    Task<CustomerWriteScope?> ResolveWriteAsync(
        ClaimsPrincipal? user,
        string? customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// De klanten waar deze gebruiker als klantgebruiker recht op heeft.
    /// </summary>
    /// <param name="user">De aangemelde gebruiker.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De scopes, leeg als er geen zijn.</returns>
    /// <remarks>
    /// Bestaat voor de landingsroute: een klantgebruiker met precies één klant hoort daar
    /// rechtstreeks heen te gaan in plaats van een keuzescherm te zien. Geeft voor een operator
    /// bewust <em>niet</em> alle klanten terug — die heeft het overzicht.
    /// </remarks>
    Task<IReadOnlyList<CustomerScope>> ResolveOwnAsync(
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);
}
