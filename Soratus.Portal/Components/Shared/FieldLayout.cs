namespace Soratus.Portal.Components.Shared;

/// <summary>
/// Waar het label van een veld staat.
/// </summary>
/// <remarks>
/// Net als <see cref="FieldMode"/> komt dit uit de <see cref="FieldScope"/> van de omhullende
/// kaart, zodat een kaart één indeling heeft en niet elf velden die het los afspreken.
/// </remarks>
public enum FieldLayout
{
    /// <summary>
    /// Label boven de waarde, over de volle breedte. Voor een smal formulier waarin de velden
    /// elkaar opvolgen (toegang geven, klant aanmaken).
    /// </summary>
    Stacked,

    /// <summary>
    /// Label links, waarde rechts. Voor een kaart die een reeks eigenschappen van één ding toont
    /// — de contractkaart uit §3.5. Dit is ook de indeling waarin de bewerkbare en de read-only
    /// weergave even breed zijn, zodat de kaart niet verspringt als de operator gaat bewerken.
    /// </summary>
    Row,
}
