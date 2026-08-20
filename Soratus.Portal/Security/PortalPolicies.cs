namespace Soratus.Portal.Security;

/// <summary>
/// De namen van de autorisatiebeleiden, als constante.
/// </summary>
/// <remarks>
/// Een beleidsnaam is een string die op twee plekken moet kloppen: bij het registreren in
/// <c>Program.cs</c> en bij elk <c>[Authorize(Policy = ...)]</c>. Een typefout op de tweede plek
/// levert geen compileerfout maar een runtime-uitzondering, en dat wil je niet ontdekken in
/// productie. Daarom staan ze hier.
///
/// Let op de rangorde van deze beleiden ten opzichte van <see cref="CustomerScope"/>: een beleid
/// zegt alleen <em>welke rol</em> iemand heeft, niet <em>welke klant</em> hij mag zien. Dat
/// tweede kan een attribuut niet weten, en dus loopt het via
/// <see cref="ICustomerScopeResolver"/>. Gebruik een beleid om een hele pagina dicht te zetten;
/// gebruik een scope om gegevens op te halen.
/// </remarks>
public static class PortalPolicies
{
    /// <summary>Vereist de app-rol <see cref="PortalRoles.Operator"/>.</summary>
    public const string Operator = "portal.operator";

    /// <summary>Vereist de app-rol <see cref="PortalRoles.Customer"/>.</summary>
    public const string Customer = "portal.klant";
}
