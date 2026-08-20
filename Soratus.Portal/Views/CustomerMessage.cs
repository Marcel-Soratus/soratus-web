using Soratus.Agents.Contracts;

namespace Soratus.Portal.Views;

/// <summary>
/// Houdt van <c>msg</c> de eerste regel over, op de laatste plek voordat de tekst naar een klant
/// gaat.
/// </summary>
/// <remarks>
/// <para>De regel zelf staat niet hier maar in <see cref="MessageTruncation"/>, in
/// <c>Soratus.Agents.Contracts</c>. Dat is met opzet en het is een correctie: deze klasse had eerst
/// zijn eigen kopie met eigen constanten, en die liep binnen een dag uit de pas met de
/// schrijfkant — dezelfde knip, maar de kop werd op 8000 tekens geknipt en de markering daarná
/// aangeplakt, dus tot 8013 waar het contract 8000 belooft. Geen lek en één zin verschil, maar wel
/// het bewijs dat drie kopieën van dezelfde regel gaan schuiven. Nu is er één.</para>
///
/// <para><strong>Waarom er hier tóch geknipt wordt en niet alleen bij het wegschrijven.</strong>
/// De schrijfkant is de plek waar de contractregel hoort, want daar ontstaat de tekst. Maar deze
/// projectie is de laatste stap voordat het bericht de HTML in gaat, en die dekt drie gevallen die
/// de schrijfkant niet dekt:</para>
/// <list type="number">
///   <item><description>
///     de documenten die er nu al staan. Logregels blijven dertig dagen; alles wat vóór de knip is
///     weggeschreven houdt zijn lange <c>msg</c> tot de TTL hem opruimt.
///   </description></item>
///   <item><description>een agent die op een oudere versie van de bibliotheek blijft staan.</description></item>
///   <item><description>
///     een agent die de bibliotheek niet gebruikt. Het contract is een documentvorm, geen
///     bibliotheek; niets houdt tegen dat iemand rechtstreeks naar Cosmos schrijft.
///   </description></item>
/// </list>
///
/// <para><strong>Afkappen in het scherm is geen alternatief.</strong> De berichtcel is 766px breed
/// en de inhoud van de gemeten regel 22214px. Die ellipsis is beeld: de volledige tekst staat in de
/// paginabron, ongeacht wat er te zien is. Wat deze projectie doorlaat, staat bij de klant op de
/// schijf. <c>LogText</c> in <c>Components/Shared</c> kapt op 400 tekens af voor de leesbaarheid van
/// een tooltip; dat lijkt hierop maar is een weergavekeuze en geen grens.</para>
///
/// <para><strong>De overloop gaat hier verloren, en dat is de bedoeling.</strong> De schrijfkant zet
/// wat eraf valt in <c>extra</c> onder <see cref="MessageTruncation.OverflowKey"/>, zodat de
/// operator niets kwijtraakt. <see cref="CustomerLogLine"/> heeft geen veld voor vrije JSON en dat
/// blijft zo, dus van de twee helften die <see cref="MessageTruncation.Cut"/> teruggeeft gebruikt
/// deze klasse alleen de eerste.</para>
/// </remarks>
internal static class CustomerMessage
{
    /// <summary>
    /// Geeft van dit bericht de eerste regel terug, met de markering als er iets af is.
    /// </summary>
    /// <param name="message">Het bericht zoals het in het document staat.</param>
    /// <returns>Het bericht zoals de klant het te zien krijgt.</returns>
    /// <remarks>
    /// <para>Een leeg bericht komt onveranderd terug in plaats van als <c>"(geen bericht)"</c>, en
    /// daar wijkt de leeskant bewust af van <see cref="MessageTruncation.Cut"/>. Bij het
    /// wegschrijven is die tekst een correctie op de bron: de agentbouwer vergat een bericht en dat
    /// is op te lossen. Bij het lezen zou hij een document dat niet aan het contract voldoet
    /// verkleden als een document dat dat wel doet. Het scherm hoort een leeg bericht als leeg te
    /// tonen.</para>
    ///
    /// <para>Een bericht dat de schrijfkant al heeft geknipt komt hier onveranderd door: het is dan
    /// één regel en korter dan de grens, dus <see cref="MessageTruncation.Cut"/> laat het staan en
    /// er komt geen tweede markering achter. Nagemeten op de echte opslag.</para>
    /// </remarks>
    internal static string FirstLine(string message) =>
        string.IsNullOrEmpty(message) ? message : MessageTruncation.Cut(message).Message;
}
