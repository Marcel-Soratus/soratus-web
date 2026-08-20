using System.Security.Claims;

namespace Soratus.Portal.Security;

/// <summary>
/// De lijst van klanten en wie er namens welke klant mag inloggen.
/// </summary>
/// <remarks>
/// Dit is de bron waaruit <see cref="CustomerScopeResolver"/> zijn oordeel haalt. Hij staat apart
/// zodat fase 2 (contract en toegangsbeheer) alleen de implementatie hoeft te vervangen — van
/// configuratie naar een beheerd model — zonder dat er iets aan de scopeconstructie verandert.
///
/// Merk op dat hier geen methode staat die op basis van een e-mailadres een klant <em>zoekt</em>
/// zonder rol te wegen. Dat is opzet: elke vraag gaat via <see cref="ForUser"/>, dus de vraag "van
/// welke klant is deze gebruiker" is niet los te stellen van "wie is deze gebruiker".
/// </remarks>
public interface ICustomerDirectory
{
    /// <summary>Alle ingerichte klanten, in de volgorde waarin ze zijn geconfigureerd.</summary>
    IReadOnlyList<CustomerRecord> All { get; }

    /// <summary>
    /// Zoekt een klant op zijn slug, hoofdletterongevoelig.
    /// </summary>
    /// <param name="customerId">De slug uit de URL.</param>
    /// <returns>De klant, of <c>null</c> als hij niet bestaat.</returns>
    CustomerRecord? Find(string? customerId);

    /// <summary>
    /// De klanten waar deze gebruiker als klantgebruiker toegang toe heeft.
    /// </summary>
    /// <param name="user">De aangemelde gebruiker.</param>
    /// <returns>
    /// De klanten die dit e-mailadres in hun toegangslijst hebben staan. Leeg als de gebruiker
    /// niet is aangemeld of nergens is toegevoegd.
    /// </returns>
    /// <remarks>
    /// Weegt de app-rol <em>niet</em> mee. Dat doet de resolver, zodat de rolregel op één plek
    /// staat. Een operator die toevallig ook in een toegangslijst staat komt hier dus gewoon uit,
    /// en dat is goed: hij ziet die klant dan als klant én als operator.
    /// </remarks>
    IReadOnlyList<CustomerRecord> ForUser(ClaimsPrincipal? user);
}
