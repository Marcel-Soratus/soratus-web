using System.Globalization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Soratus.Portal.Security;

namespace Soratus.Portal.Data;

/// <summary>
/// De enige implementatie van <see cref="IPortalCostsStore"/>: verbruiksdocumenten in de container
/// <c>customers</c>, op de partitiesleutel van de klant.
/// </summary>
/// <remarks>
/// <para><strong>Eén query per klant per jaar, binnen één partitiesleutel.</strong> Er is geen
/// cross-partition query in deze klasse, en dat is niet toevallig: de partitiesleutel komt uit de
/// scope, dus er is geen aanroep waarmee je met de scope van klant A de kosten van klant B leest. Het
/// filter op <c>c.cid</c> staat er niet bij, omdat de partitiesleutel diezelfde waarde is — dezelfde
/// keuze en dezelfde reden als in <see cref="CosmosPortalHoursStore"/>.</para>
///
/// <para><strong>De maandgrens is een tekstvergelijking, en dat mag omdat de opslagvorm vast is.</strong>
/// <c>c.month &gt;= '2026-01' AND c.month &lt;= '2026-12'</c> werkt op <c>yyyy-MM</c> en op geen enkele
/// andere vorm. Punt 7 van de fase-0-afwijkingen, hier voor de derde keer.</para>
///
/// <para><strong>Een onbereikbare opslag werpt en levert geen lege lijst op.</strong> Dat is hier
/// zwaarder dan elders: een lege lijst wordt door <see cref="AzureCostReading.From"/> tot
/// <see cref="AzureCostState.Unknown"/> gemaakt, en dat is de juiste uitkomst voor "deze maand is niet
/// gemeten" — maar niet voor "de opslag is niet ingericht". Die tweede is een inrichtingsfout en hoort
/// luidruchtig te zijn, want anders staat er maanden lang "onbekend" op een scherm terwijl de
/// collector netjes zijn werk doet. Zie <see cref="PortalDataNotProvisionedException"/>.</para>
///
/// <para><strong>Er wordt niets geschreven.</strong> Deze klasse heeft geen enkele methode die dat
/// doet; zie <see cref="IPortalCostsStore"/> voor waarom dat zo blijft.</para>
/// </remarks>
internal sealed class CosmosPortalCostsStore(
    CosmosContainerProvider containers,
    IOptions<PortalDataOptions> options,
    ILogger<CosmosPortalCostsStore> logger) : IPortalCostsStore
{
    private readonly PortalDataOptions _options = options.Value;

    /// <inheritdoc />
    public Task<IReadOnlyList<AzureCostDocument>> GetAzureCostsAsync(
        CustomerScope scope,
        int year,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return ReadAsync(scope.CustomerId, year, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AzureCostDocument>> GetAzureCostsAsync(
        CustomerWriteScope scope,
        int year,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return ReadAsync(scope.CustomerId, year, cancellationToken);
    }

    /// <summary>
    /// Leest de verbruiksdocumenten van één klant binnen één jaar.
    /// </summary>
    /// <param name="customerId">
    /// De klantslug. Dit is de partitiesleutel, en daarmee de isolatiegrens: deze waarde komt uit de
    /// scope en nergens anders vandaan.
    /// </param>
    /// <param name="year">Het jaartal.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De documenten, nieuwste maand eerst.</returns>
    /// <remarks>
    /// Sorteren gebeurt in het geheugen en niet met <c>ORDER BY</c>. Dezelfde keuze en dezelfde reden
    /// als bij de uren: het gaat om maximaal twaalf documenten van één klant, en een <c>ORDER BY</c>
    /// vraagt een index die in Bicep staat — dus zou een sortering hier stilzwijgend een uitrol
    /// vereisen die niemand aanvraagt.
    /// </remarks>
    private async Task<IReadOnlyList<AzureCostDocument>> ReadAsync(
        string customerId,
        int year,
        CancellationToken cancellationToken)
    {
        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        var definition = new QueryDefinition(
                "SELECT * FROM c WHERE c.kind = @kind AND c.month >= @from AND c.month <= @to")
            .WithParameter("@kind", AzureCostDocumentKeys.Kind)
            .WithParameter("@from", string.Create(CultureInfo.InvariantCulture, $"{year:D4}-01"))
            .WithParameter("@to", string.Create(CultureInfo.InvariantCulture, $"{year:D4}-12"));

        var results = new List<AzureCostDocument>();
        var charge = 0d;

        using var iterator = container.GetItemQueryIterator<AzureCostDocument>(
            definition,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(customerId) });

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            results.AddRange(response);
            charge += response.RequestCharge;
        }

        logger.LogDebug(
            "Azure-verbruik van {CustomerId} over {Year}: {Count} maand(en), {Charge} RU.",
            customerId,
            year,
            results.Count,
            charge);

        // Nieuwste maand eerst: §3.7 zet de lopende maand bovenaan. Dat is het omgekeerde van de
        // urenspecificatie, die oudste-eerst loopt omdat dat een tijdlijn is; een facturatieoverzicht
        // is dat niet — daar is de laatste regel de regel waar het over gaat.
        return [.. results.OrderByDescending(document => document.Month, StringComparer.Ordinal)];
    }

    /// <inheritdoc cref="CosmosPortalHoursStore" />
    private async Task<Container> ContainerAsync(CancellationToken cancellationToken)
    {
        var location = _options.Location()
            ?? throw new PortalDataNotProvisionedException(
                "De portaalopslag is niet ingericht: PortalData:AccountEndpoint is leeg. Het "
                + "Azure-verbruik is daarmee niet te lezen. Het facturatiescherm hoort dat te melden "
                + "in plaats van een maand zonder kosten te tonen — nul euro verbruik en een "
                + "onbereikbare opslag zien er op een totaalregel hetzelfde uit, en dat is precies "
                + "het verschil dat een factuur raakt.");

        return await containers.CustomersAsync(location, cancellationToken).ConfigureAwait(false);
    }
}
