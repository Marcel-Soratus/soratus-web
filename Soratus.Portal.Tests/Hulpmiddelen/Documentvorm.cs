using Soratus.Portal.Data;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// Maakt de documenten die de portaalopslag wegschrijft, met de omzetting uit de productiecode zelf.
/// </summary>
/// <remarks>
/// <para><see cref="Vasteportaalopslag"/> heeft ze nodig: een schrijfactie levert een document terug,
/// en dat document komt bij een conflict én na een gelukte schrijfactie terug in het formulier. Zou
/// de testopslag die omzetting nabouwen, dan is de vergelijking "wat is er intussen veranderd" een
/// vergelijking tussen twee eigen mappings — en dan meldt het scherm een wijziging die niemand heeft
/// gemaakt, of juist geen wijziging waar er wel een is, zonder dat een test dat merkt.</para>
///
/// <para>Hier stond reflectie op een <c>private static</c>. Dat argument voor het gebruik van de
/// productiemapping wordt niet zwakker door de compiler het te laten controleren maar sterker:
/// <c>CosmosPortalDataStore.ToDocument</c> is nu <c>internal</c>, dus een hernoeming is een bouwfout
/// op deze twee regels in plaats van een uitzondering tijdens een testrun.</para>
///
/// <para>Wat blijft is de naam. Deze klasse voegt geen gedrag toe en dat is de bedoeling: ze zegt
/// waaróm de testopslag de productie-omzetting gebruikt, op één plek, in plaats van dat die reden
/// zes keer als opmerking bij een aanroep staat.</para>
/// </remarks>
internal static class Documentvorm
{
    /// <summary>Het contractdocument zoals de opslag het wegschrijft.</summary>
    /// <param name="edit">De bewerking uit het formulier.</param>
    /// <param name="customerId">De klantslug, die ook de partitiesleutel is.</param>
    /// <param name="actor">Wie de wijziging op zijn naam krijgt.</param>
    /// <param name="moment">Het moment van wijzigen.</param>
    /// <returns>Het document, zonder etag — die komt van de opslag.</returns>
    public static ContractDocument Contract(
        ContractEdit edit,
        string customerId,
        string actor,
        DateTimeOffset moment) =>
        CosmosPortalDataStore.ToDocument(edit, customerId, actor, moment);

    /// <summary>Het toegangsdocument zoals de opslag het wegschrijft.</summary>
    /// <param name="grant">De toegang uit het formulier.</param>
    /// <param name="customerId">De klantslug.</param>
    /// <param name="actor">Welke operator hem uitdeelt.</param>
    /// <param name="moment">Het moment van vastleggen.</param>
    /// <returns>Het document, zonder etag.</returns>
    public static AccessDocument Toegang(
        AccessGrant grant,
        string customerId,
        string actor,
        DateTimeOffset moment) =>
        CosmosPortalDataStore.ToDocument(grant, customerId, actor, moment);
}
