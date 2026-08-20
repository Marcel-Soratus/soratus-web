namespace Soratus.Portal.Security;

/// <summary>
/// De app-rollen zoals ze in de Entra-registratie <c>soratus-portal</c> staan.
/// </summary>
/// <remarks>
/// Deze waarden komen letterlijk uit <c>infra/entra/app-roles.json</c> en verschijnen in het
/// <c>roles</c>-claim van het id-token. Ze staan hier als constante zodat er nergens in de
/// codebasis een losse string <c>"Operator"</c> voorkomt die stil verkeerd gespeld kan raken.
///
/// De registratie heeft <c>appRoleAssignmentRequired</c> aan staan. Wie geen rol toegewezen
/// krijgt komt niet door de aanmelding heen; het portaal hoeft dus geen "gebruiker zonder rol"
/// af te handelen, maar rekent er ook nergens op dat er precies één rol is.
/// </remarks>
public static class PortalRoles
{
    /// <summary>Soratus-medewerker. Ziet alle klanten.</summary>
    public const string Operator = "Operator";

    /// <summary>Klantgebruiker. Ziet uitsluitend de eigen omgeving, altijd read-only.</summary>
    public const string Customer = "Klant";
}
