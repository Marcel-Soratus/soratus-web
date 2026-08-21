using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Soratus.Agents.Contracts;
using Soratus.Portal.Data;
using Soratus.Portal.Mail;

namespace Soratus.Portal.Alerts;

/// <summary>
/// Wat er over één agent wordt geclaimd voordat de melding de deur uit gaat.
/// </summary>
/// <param name="CustomerId">De klantslug.</param>
/// <param name="AgentName">De technische naam van de agent.</param>
/// <param name="Status">De status waarover wordt gemeld.</param>
/// <param name="Now">Het moment van de claim, in UTC.</param>
/// <param name="Existing">
/// De markering die er al stond, of <c>null</c>. Bepaalt of er wordt aangemaakt of vervangen — en
/// levert de etag waarop de vervanging loopt.
/// </param>
internal sealed record AgentAlertClaim(
    string CustomerId,
    string AgentName,
    AgentStatus Status,
    DateTimeOffset Now,
    AgentAlertDocument? Existing);

/// <summary>
/// De boekhouding van de storingsmelder: de markeringen lezen, claimen, bevestigen en afsluiten.
/// </summary>
/// <remarks>
/// <para><strong>Een eigen naad en geen methoden op <see cref="IPortalDataStore"/>, en dat is de
/// rolgrens.</strong> Elke methode daar neemt een <see cref="Security.CustomerScope"/> of een
/// <see cref="Security.CustomerWriteScope"/>: het bewijs dat er een mens naar een klant kijkt. De
/// melder heeft geen mens. Dezelfde afweging en dezelfde uitkomst als bij
/// <see cref="IAzureCostCollectorStore"/> (punt 39).</para>
///
/// <para><strong>Wat dat hier goedkoper maakt dan daar.</strong> De kostencollector schrijft in de
/// partitie van een klant en verliest daarmee de isolatie-eigenschap van de leeskant. Deze markeringen
/// staan alle in de gereserveerde partitie <c>$portal</c> — het is Soratus-eigen boekhouding en niet
/// klantgegevens — dus er is geen enkele aanroep in deze interface die een klantpartitie raakt. Het
/// ergste dat een fout hier kan doen is een markering met een verkeerde naam neerzetten, en dan gaat
/// er een mail te veel of te weinig uit; er belandt niets bij een klant.</para>
///
/// <para>Er is precies één implementatie en die is <c>internal</c>. De naad is er voor de test en voor
/// de rolgrens, niet voor een tweede opslag.</para>
/// </remarks>
internal interface IAgentAlertStore
{
    /// <summary>
    /// Alle markeringen die er staan.
    /// </summary>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De markeringen, ook de afgesloten.</returns>
    /// <remarks>
    /// <para><strong>Eén query per ronde en niet één per klant.</strong> Alle markeringen staan in
    /// dezelfde partitie, dus dit is een query binnen één partitie over hoogstens enkele tientallen
    /// documenten. Dat is de reden dat ze niet bij de klant staan.</para>
    ///
    /// <para><strong>Ook de afgesloten komen mee, en dat is nodig.</strong> Een afgesloten markering
    /// geldt als geen markering voor de vraag of er gemeld moet worden — maar hij moet wel gevonden
    /// worden om hem bij een terugkeer te kunnen vervangen in plaats van te moeten aanmaken. Een
    /// <c>CreateItemAsync</c> op een sleutel die er al is levert een <c>409</c>, en dat zou hier een
    /// storing verzwijgen.</para>
    ///
    /// <para><strong>En ze komen ongefilterd terug.</strong> De melder hoort te kunnen zien welke
    /// markeringen bij een agent horen die er niet meer is; zat dat filter in de query, dan is een
    /// opgeruimde agent niet te onderscheiden van een agent die niet te lezen was.</para>
    /// </remarks>
    Task<IReadOnlyList<AgentAlertDocument>> MarkersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Claimt de melding over één agent.
    /// </summary>
    /// <param name="claim">Wat er wordt geclaimd.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// De geschreven markering, of <c>null</c> als een andere instantie deze melding al doet.
    /// </returns>
    /// <remarks>
    /// <para><strong>De claim gaat vóór de mail. Nooit andersom.</strong> Dezelfde volgorde en dezelfde
    /// reden als bij het maandoverzicht: valt het proces om tussen de claim en de verzending, dan staat
    /// er een markering bij een mail die misschien nooit is verstuurd. Dat is de goede kant om fout te
    /// zitten. De andere volgorde — eerst versturen, dan vastleggen — laat bij dezelfde storing een
    /// verstuurde mail zonder spoor achter, en dan verstuurt de volgende ronde er een tweede. En de
    /// volgende ronde is hier een minuut later en niet een maand.</para>
    ///
    /// <para><c>null</c> is een gewone uitkomst en geen fout. Op een portaal met twee instanties is dat
    /// bij elke storing het normale gedrag van de ene van de twee, en het hoort dus als
    /// <c>information</c> in het log en niet als waarschuwing.</para>
    /// </remarks>
    Task<AgentAlertDocument?> ClaimAsync(
        AgentAlertClaim claim,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Legt vast hoe de verzending is afgelopen.
    /// </summary>
    /// <param name="claimed">De markering zoals hij bij het claimen is weggeschreven.</param>
    /// <param name="delivery">De uitkomst.</param>
    /// <param name="operationId">De operatie-id, of <c>null</c>.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Een taak.</returns>
    /// <remarks>
    /// Werpt niet. Deze schrijfactie voegt informatie toe en verandert niets aan de ontdubbeling: die
    /// hangt aan <see cref="AgentAlertDocument.NotifiedAt"/>, en die staat er na het claimen al. Een
    /// mislukte bevestiging mag de ronde dus niet meenemen — dan zou een hapering in de opslag ná de
    /// verzending de melder stilzetten, en dat is precies op het moment dat hij nodig is.
    /// </remarks>
    Task ConfirmAsync(
        AgentAlertDocument claimed,
        MailDelivery delivery,
        string? operationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sluit de markering van een agent die weer in orde is.
    /// </summary>
    /// <param name="marker">De markering die er staat.</param>
    /// <param name="clearedAt">Het moment waarop is vastgesteld dat de storing weg is.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Een taak.</returns>
    /// <remarks>
    /// Werpt niet, en een botsing is geen fout: twee instanties die tegelijk vaststellen dat een agent
    /// hersteld is, doen hetzelfde. Er wordt afgesloten en niet verwijderd; zie
    /// <see cref="AgentAlertDocument.ClearedAt"/>.
    /// </remarks>
    Task ClearAsync(
        AgentAlertDocument marker,
        DateTimeOffset clearedAt,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// De enige implementatie van <see cref="IAgentAlertStore"/>.
/// </summary>
/// <remarks>
/// <para>Singleton, want <see cref="AgentFaultAlerter"/> is een achtergronddienst en die kan geen
/// scoped afhankelijkheid krijgen. Dezelfde reden als bij <c>CosmosAzureCostCollectorStore</c>. Deze
/// klasse houdt geen staat vast.</para>
///
/// <para><strong>Een onbereikbare opslag werpt.</strong> Zonder markeringen is er geen ontdubbeling, en
/// een melder die zonder ontdubbeling doorgaat mailt elke minuut over dezelfde storing. Dat is erger
/// dan een ronde die niets doet — en de melder vangt de uitzondering af en logt hem, dus het portaal
/// gaat er niet van om.</para>
/// </remarks>
internal sealed class CosmosAgentAlertStore(
    CosmosContainerProvider containers,
    IOptions<PortalDataOptions> options,
    ILogger<CosmosAgentAlertStore> logger) : IAgentAlertStore
{
    private readonly PortalDataOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentAlertDocument>> MarkersAsync(
        CancellationToken cancellationToken = default)
    {
        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        var definition = new QueryDefinition("SELECT * FROM c WHERE c.kind = @kind")
            .WithParameter("@kind", AgentAlertDocumentKeys.Kind);

        var results = new List<AgentAlertDocument>();
        var charge = 0d;

        // De partitiesleutel staat expliciet op de queryopties: dan is dit een query binnen één
        // partitie en niet een cross-partition query. Zonder die regel kost hij bij elke ronde een
        // fan-out over elke klantpartitie, en dit draait elke minuut.
        using var iterator = container.GetItemQueryIterator<AgentAlertDocument>(
            definition,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(PortalDocumentIds.ReservedPartitionKey),
            });

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            charge += response.RequestCharge;
            results.AddRange(response);
        }

        logger.LogDebug(
            "De storingsmelder kent {Count} markering(en). {Charge} RU.",
            results.Count,
            charge);

        return results;
    }

    /// <inheritdoc />
    public async Task<AgentAlertDocument?> ClaimAsync(
        AgentAlertClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);

        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        var document = new AgentAlertDocument
        {
            Id = AgentAlertDocumentKeys.Id(claim.CustomerId, claim.AgentName),
            PartitionKey = PortalDocumentIds.ReservedPartitionKey,
            CustomerId = claim.CustomerId,
            AgentName = claim.AgentName,
            Status = claim.Status,
            NotifiedAt = claim.Now,

            // Bij een nieuwe storingsperiode begint de teller opnieuw, en dat is precies wat een
            // afgesloten markering betekent. Zou FirstNotifiedAt uit een afgesloten markering worden
            // overgenomen, dan zou er "sinds gisteren" staan bij een storing van vijf minuten oud.
            FirstNotifiedAt = claim.Existing is { ClearedAt: null } running
                ? running.FirstNotifiedAt
                : claim.Now,
            Notifications = claim.Existing is { ClearedAt: null } counted
                ? counted.Notifications + 1
                : 1,
            NotifiedBy = Instance,
            Delivery = MailDelivery.Unknown,
            ClearedAt = null,
        };

        var partition = new PartitionKey(PortalDocumentIds.ReservedPartitionKey);

        try
        {
            if (claim.Existing is null)
            {
                // CreateItemAsync en geen upsert. Dit is het slot: bestaat het document al, dan komt
                // hier een 409 en heeft een andere instantie deze melding al gedaan. Met een upsert
                // zouden beide instanties denken dat ze mogen, en dan gaan er twee mails uit.
                var created = await container
                    .CreateItemAsync(document, partition, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                return created.Resource;
            }

            // Een herhaling of een terugkeer: vervangen op de etag die we hebben gelezen. Twee
            // instanties lezen dezelfde etag, één vervanging slaagt en de andere krijgt een 412.
            // Zonder die controle zou de tweede de eerste overschrijven en toch mailen.
            var replaced = await container
                .ReplaceItemAsync(
                    document,
                    document.Id,
                    partition,
                    new ItemRequestOptions { IfMatchEtag = claim.Existing.ETag },
                    cancellationToken)
                .ConfigureAwait(false);

            return replaced.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode is HttpStatusCode.Conflict
            or HttpStatusCode.PreconditionFailed)
        {
            // Information en geen warning: op een portaal met twee instanties is dit bij elke storing
            // het normale gedrag van de ene van de twee.
            logger.LogInformation(
                "De melding over {AgentName} van {CustomerId} is al door een andere instantie "
                + "geclaimd ({Status}); deze instantie ({Instance}) verstuurt niets.",
                claim.AgentName,
                claim.CustomerId,
                exception.StatusCode,
                Instance);

            return null;
        }
    }

    /// <inheritdoc />
    public async Task ConfirmAsync(
        AgentAlertDocument claimed,
        MailDelivery delivery,
        string? operationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claimed);

        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await container
                .ReplaceItemAsync(
                    claimed with { Delivery = delivery, OperationId = operationId },
                    claimed.Id,
                    new PartitionKey(PortalDocumentIds.ReservedPartitionKey),
                    new ItemRequestOptions { IfMatchEtag = claimed.ETag },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CosmosException exception)
        {
            // Werpt niet. De ontdubbeling hangt aan NotifiedAt en die staat er al; dit veld is er om
            // na te zoeken. Een hapering hier hoort de ronde niet mee te nemen.
            logger.LogWarning(
                "De uitkomst van de melding over {AgentName} van {CustomerId} is niet vastgelegd "
                + "({Status}). De markering staat er wel, dus er wordt niet opnieuw gemeld.",
                claimed.AgentName,
                claimed.CustomerId,
                exception.StatusCode);
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync(
        AgentAlertDocument marker,
        DateTimeOffset clearedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(marker);

        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await container
                .ReplaceItemAsync(
                    marker with { ClearedAt = clearedAt },
                    marker.Id,
                    new PartitionKey(PortalDocumentIds.ReservedPartitionKey),
                    new ItemRequestOptions { IfMatchEtag = marker.ETag },
                    cancellationToken)
                .ConfigureAwait(false);

            logger.LogInformation(
                "{AgentName} van {CustomerId} is weer in orde; de storingsmarkering is afgesloten.",
                marker.AgentName,
                marker.CustomerId);
        }
        catch (CosmosException exception)
        {
            // Twee instanties die tegelijk vaststellen dat een agent hersteld is doen hetzelfde, dus
            // een 412 is geen fout. Debug en niet warning.
            logger.LogDebug(
                "De storingsmarkering van {AgentName} van {CustomerId} is niet afgesloten "
                + "({Status}). De volgende ronde probeert het opnieuw.",
                marker.AgentName,
                marker.CustomerId,
                exception.StatusCode);
        }
    }

    /// <summary>
    /// De naam van deze instantie, voor de markering en het log.
    /// </summary>
    /// <remarks>
    /// <c>WEBSITE_INSTANCE_ID</c> is wat App Service zet en het is het enige gegeven waarmee twee
    /// instanties van elkaar te onderscheiden zijn. Lokaal is die er niet en dan is de machinenaam het
    /// beste dat er is. Dezelfde vorm als bij <c>CosmosAzureCostCollectorStore</c>.
    /// </remarks>
    private static string Instance { get; } =
        Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID") is { Length: > 0 } id
            ? id[..Math.Min(12, id.Length)]
            : Environment.MachineName;

    /// <summary>De container met de portaalgegevens.</summary>
    private async Task<Container> ContainerAsync(CancellationToken cancellationToken)
    {
        var location = _options.Location()
            ?? throw new PortalDataNotProvisionedException(
                "De portaalopslag is niet ingericht: PortalData:AccountEndpoint is leeg. De "
                + "storingsmelder kan daarom niet ontdubbelen, en zonder ontdubbeling zou hij elke "
                + "minuut over dezelfde storing mailen. Er wordt dus niets verstuurd.");

        return await containers.CustomersAsync(location, cancellationToken).ConfigureAwait(false);
    }
}
