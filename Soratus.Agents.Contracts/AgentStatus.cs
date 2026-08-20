namespace Soratus.Agents.Contracts;

/// <summary>
/// De toestand van een agent, zoals die op het scherm verschijnt.
/// </summary>
/// <remarks>
/// De numerieke waarde <em>is</em> de ernstrang uit §3.1 van de spec. Sorteren op ernst is
/// daarmee <c>OrderByDescending(x =&gt; x.Status)</c> en niets meer. Verander deze getallen
/// niet zonder de sorteervolgorde van het overzicht opnieuw te beoordelen.
///
/// Een agent publiceert zijn status nooit zelf. Een agent die om is kan niet melden dat hij
/// om is. Alles hier wordt afgeleid uit gepubliceerde feiten door
/// <see cref="AgentStatusCalculator"/>.
/// </remarks>
public enum AgentStatus
{
    /// <summary>
    /// Er is geen telemetrie. De agent is wel verwacht, maar publiceert niets — of hij is
    /// nog niet uitgerold, of de telemetriebibliotheek ontbreekt.
    /// </summary>
    /// <remarks>
    /// Dit is bewust geen storing en geen "live". Het is de eerlijke mededeling dat wij
    /// niets weten. Rang 0 zorgt ervoor dat zo'n agent nooit een echte storing van de
    /// bovenkant van het overzicht verdringt.
    /// </remarks>
    Unknown = 0,

    /// <summary>De agent draait en heeft niets te doen. Normaal, geen storing.</summary>
    Idle = 1,

    /// <summary>De agent draait, meldt zich op tijd en de laatste run is geslaagd.</summary>
    Live = 2,

    /// <summary>De agent heeft zich langer dan de drempel niet gemeld.</summary>
    Degraded = 3,

    /// <summary>De laatste afgeronde run is mislukt.</summary>
    Failed = 4,
}
