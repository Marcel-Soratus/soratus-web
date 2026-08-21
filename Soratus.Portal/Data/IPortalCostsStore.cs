using Soratus.Portal.Security;

namespace Soratus.Portal.Data;

/// <summary>
/// De enige toegang tot het opgeslagen Azure-verbruik per maand (§3.7).
/// </summary>
/// <remarks>
/// <para><strong>Alleen lezen, en dat is geen tijdelijke beperking.</strong> Deze documenten worden
/// geschreven door de beheeragent <c>kosten-collector</c> (§4) en door niets anders. Er is geen
/// scherm en geen formulier dat een kostenbedrag vastlegt, en er hoort er ook geen te komen: een
/// bedrag dat een mens kan intypen is een bedrag dat naast de meting gaat staan, en dan is niet te
/// zeggen welke van de twee op de factuur hoort. Dat is dezelfde afweging als bij een agent die zijn
/// eigen status niet publiceert (punt 2 van de fase-0-afwijkingen).</para>
///
/// <para>Wat een mens wél instelt is het <em>opslagpercentage</em>, en dat staat op het contract
/// (<see cref="ContractDocument.AzureSurchargePercentage"/>) en wordt op het contractscherm bewerkt.
/// Eén plek, want het is een afspraak en geen meting.</para>
///
/// <para><strong>Twee overloads met dezelfde naam, en het verschil is de projectie en niet de
/// verzameling.</strong> Beide rollen lezen dezelfde documenten; wat de klant niet mag zien — de
/// uitsplitsing per dienst, de beheeropslag, de bevraagde scope — verdwijnt in het viewmodel en niet
/// in de query. Daarin wijkt dit af van <see cref="IPortalHoursStore.GetApprovedHoursAsync"/>, waar
/// de klant werkelijk andere <em>documenten</em> krijgt en het filter dus in de <c>WHERE</c> staat.
/// Het verschil tussen die twee gevallen: bij uren is het verboden gegeven een heel document, hier is
/// het een veld op een document waarvan de klant de rest wél mag zien. Een veld valt niet uit een
/// query weg te filteren zonder het document te verminken.</para>
///
/// <para>Er is precies één implementatie: <see cref="CosmosPortalCostsStore"/>. Geen seed-variant en
/// geen in-memory variant, om dezelfde reden als bij de andere drie stores.</para>
/// </remarks>
public interface IPortalCostsStore
{
    /// <summary>
    /// Het Azure-verbruik van deze klant over één jaar, zoals de klant het mag lezen.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="year">Het jaartal.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De maanden waarvoor er een meting is, nieuwste eerst. Kan leeg zijn.</returns>
    /// <remarks>
    /// <para><strong>Een lege uitkomst betekent "er is niet gemeten" en niet "er is niets
    /// verbruikt".</strong> Dat onderscheid moet de aanroeper maken en niet vergeten, en daarom komt
    /// hier een lijst documenten terug en geen woordenboek met nullen erin. Zie
    /// <see cref="AzureCostReading.From"/>: dat is de plek waar de afwezigheid van een maand
    /// <see cref="AzureCostState.Unknown"/> wordt, en de enige plek waar dat mag gebeuren.</para>
    ///
    /// <para>Een jaar en niet één maand, ook al toont §3.7 standaard de lopende maand bovenaan: een
    /// facturatieoverzicht is een lijst maanden en de vergelijking met de vorige maand is de enige
    /// manier om aan een bedrag te zien of het klopt. Één query per klant per jaar is bovendien
    /// goedkoper dan twaalf, en dat telt hier — deze query loopt binnen één partitie en levert
    /// maximaal twaalf kleine documenten.</para>
    /// </remarks>
    Task<IReadOnlyList<AzureCostDocument>> GetAzureCostsAsync(
        CustomerScope scope,
        int year,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Het Azure-verbruik van deze klant over één jaar, voor de operator (§2).
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="year">Het jaartal.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De maanden waarvoor er een meting is, nieuwste eerst. Kan leeg zijn.</returns>
    /// <remarks>
    /// <para>Vraagt een schrijfbewijs om te lezen, net als
    /// <see cref="IPortalDataStore.GetContractAsync(CustomerWriteScope, CancellationToken)"/>. Er
    /// wordt geen recht mee opgerekt en er valt hier niets te schrijven: het is het bewijs dat de
    /// aanroeper een operator is die naar déze klant kijkt, en dat is de voorwaarde om de
    /// uitsplitsing per dienst en de beheeropslag te mogen zien.</para>
    ///
    /// <para><see cref="CustomerWriteScope"/> en niet <see cref="OperatorCustomerScope"/>: dat laatste
    /// vraagt een ingerichte telemetrie-opslag, en de kosten van een klant zijn te bekijken voordat
    /// zijn agents zijn uitgerold. Dezelfde afweging als op het urenscherm.</para>
    /// </remarks>
    Task<IReadOnlyList<AzureCostDocument>> GetAzureCostsAsync(
        CustomerWriteScope scope,
        int year,
        CancellationToken cancellationToken = default);
}
