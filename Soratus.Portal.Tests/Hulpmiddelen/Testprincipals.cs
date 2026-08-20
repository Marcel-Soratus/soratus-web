using System.Security.Claims;
using Microsoft.Identity.Web;
using Soratus.Portal.Security;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// Aangemelde gebruikers voor de tests: een klantgebruiker, een operator, en een gebruiker zonder
/// rol.
/// </summary>
/// <remarks>
/// Het rolclaim is <see cref="ClaimConstants.Roles"/> en het is als <c>roleType</c> aan de
/// identiteit meegegeven. Zonder dat levert <see cref="ClaimsPrincipal.IsInRole"/> altijd
/// <c>false</c> en zou elke autorisatietest om de verkeerde reden slagen — precies de val die
/// <c>Program.cs</c> met een expliciete <c>RoleClaimType</c> dichtzet.
/// </remarks>
internal static class Testprincipals
{
    /// <summary>Het e-mailadres van de klantgebruiker in de testconfiguratie.</summary>
    public const string KlantEmail = "inkoop@acme-logistiek.nl";

    /// <summary>Het e-mailadres van de operator.</summary>
    public const string OperatorEmail = "marcel@soratus.com";

    /// <summary>Een aangemelde klantgebruiker met de app-rol Klant.</summary>
    public static ClaimsPrincipal Klant(string email = KlantEmail) =>
        Maak("Inkoop Acme", email, PortalRoles.Customer);

    /// <summary>Een aangemelde Soratus-operator.</summary>
    public static ClaimsPrincipal Operator(string email = OperatorEmail) =>
        Maak("Marcel de Graaf", email, PortalRoles.Operator);

    /// <summary>
    /// Een aangemelde gebruiker zonder app-rol. Kan door <c>appRoleAssignmentRequired</c> niet
    /// voorkomen, maar een autorisatiepad dat op "kan niet voorkomen" leunt is er geen.
    /// </summary>
    public static ClaimsPrincipal ZonderRol(string email = "niemand@example.com") =>
        Maak("Niemand", email, rol: null);

    /// <summary>Een bezoeker die niet is aangemeld.</summary>
    public static ClaimsPrincipal Anoniem() => new(new ClaimsIdentity());

    private static ClaimsPrincipal Maak(string naam, string email, string? rol)
    {
        var claims = new List<Claim>
        {
            new("name", naam),
            new("preferred_username", email),
            new(ClaimConstants.ObjectId, Guid.NewGuid().ToString()),
        };

        if (rol is not null)
        {
            claims.Add(new Claim(ClaimConstants.Roles, rol));
        }

        var identity = new ClaimsIdentity(
            claims,
            authenticationType: "Test",
            nameType: "name",
            roleType: ClaimConstants.Roles);

        return new ClaimsPrincipal(identity);
    }
}
