using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Soratus.Portal.Security;

namespace Soratus.Portal.Data;

/// <summary>
/// De enige implementatie van <see cref="IPortalDataStore"/>: de container <c>customers</c> in de
/// database <c>platform</c>.
/// </summary>
/// <remarks>
/// <para><strong>Gelijktijdigheid loopt over <c>_etag</c>.</strong> Elke wijziging draagt de etag
/// mee waarop hij is gebaseerd, en die gaat als <c>If-Match</c> naar Cosmos. Wie op een verouderde
/// versie werkt krijgt een 412 en daarmee een <see cref="PortalWriteStatus.Conflict"/> — nooit een
/// stille overschrijving. Er is bewust geen automatische herhaling: bij twee operators die dezelfde
/// contractkaart bewerken is "opnieuw proberen" hetzelfde als de laatste laten winnen, en dan is de
/// etag alleen vertraging.</para>
///
/// <para><strong>De etag komt van de aanroeper en niet van een verse lezing.</strong> Dat is het
/// verschil tussen een controle en een schijncontrole. Zou de store zelf lezen en de gevonden etag
/// gebruiken, dan slaagt elke schrijfactie altijd — hij vergelijkt dan de opslag met zichzelf. De
/// etag die telt is die van het formulier dat de operator open had staan.</para>
///
/// <para><strong>Klant aanmaken is één transactionele batch.</strong> Klant, contract en toegangen
/// delen de partitiesleutel, dus Cosmos schrijft ze samen of niet. Een halve klant kan hier dus niet
/// ontstaan. Wat wél half kan blijven staan is alles buiten deze container — de Azure-omgeving en de
/// Entra-rol — en dat is zichtbaar gemaakt in plaats van transactioneel: zie
/// <see cref="IPortalDataStore.CreateCustomerAsync"/>.</para>
///
/// <para><strong>Na een schrijfactie die de autorisatie raakt, wordt de klantenlijst herladen.</strong>
/// Een operator die net toegang heeft gegeven hoort niet op een verversingsinterval te wachten, en
/// een klant die net is aangemaakt hoort meteen te bestaan. Dat herladen gebeurt hier en niet bij de
/// aanroeper, want een verversing die je kunt vergeten wordt vergeten.</para>
/// </remarks>
internal sealed class CosmosPortalDataStore(
    CosmosContainerProvider containers,
    CustomerDirectory directory,
    IOptions<PortalDataOptions> options,
    TimeProvider timeProvider,
    ILogger<CosmosPortalDataStore> logger) : IPortalDataStore
{
    private readonly PortalDataOptions _options = options.Value;

    /// <summary>
    /// Of de portaalopslag is ingericht. Zo niet, dan werpt elke aanroep met uitleg.
    /// </summary>
    internal bool IsConfigured => _options.Location() is not null;

    /// <inheritdoc />
    public Task<ContractDocument?> GetContractAsync(
        CustomerScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return ReadContractAsync(scope.CustomerId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ContractDocument?> GetContractAsync(
        CustomerWriteScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return ReadContractAsync(scope.CustomerId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AccessDocument>> GetAccessAsync(
        CustomerScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return ReadAccessAsync(scope.CustomerId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AccessDocument>> GetAccessAsync(
        CustomerWriteScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return ReadAccessAsync(scope.CustomerId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CustomerDocument?> GetCustomerAsync(
        CustomerWriteScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        return await PointReadAsync<CustomerDocument>(
            container,
            PortalDocumentIds.Customer,
            scope.CustomerId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PortalWriteResult<CustomerDocument>> CreateCustomerAsync(
        PortalWriteScope scope,
        NewCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Validate() is { } error)
        {
            return PortalWriteResult<CustomerDocument>.Invalid(error);
        }

        var slug = request.CustomerId.Trim();
        var now = timeProvider.GetUtcNow();
        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        var customer = new CustomerDocument
        {
            Id = PortalDocumentIds.Customer,
            PartitionKey = slug,
            CustomerId = slug,
            Name = request.Name.Trim(),
            IsInternal = request.IsInternal,
            Environment = Clean(request.Environment),
            EnvironmentDetail = Clean(request.EnvironmentDetail),
            TelemetryEndpoint = Clean(request.TelemetryEndpoint),
            TelemetryDatabase = Clean(request.TelemetryDatabase),
            CreatedAt = now,
            CreatedBy = scope.Actor,
        };

        var batch = container.CreateTransactionalBatch(new PartitionKey(slug));
        batch.CreateItem(customer);

        if (request.Contract is { } contract)
        {
            batch.CreateItem(ToDocument(contract, slug, scope.Actor, now));
        }

        foreach (var grant in request.Access)
        {
            batch.CreateItem(ToDocument(grant, slug, scope.Actor, now));
        }

        using var response = await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // Eén mislukte bewerking laat de hele batch mislukken; de rest krijgt dan 424
            // (Failed Dependency). De eerste die iets anders dan 424 zegt is de echte oorzaak.
            var cause = FirstRealFailure(response);

            if (cause == HttpStatusCode.Conflict)
            {
                var existing = await PointReadAsync<CustomerDocument>(
                    container,
                    PortalDocumentIds.Customer,
                    slug,
                    cancellationToken).ConfigureAwait(false);

                return PortalWriteResult<CustomerDocument>.Conflict(
                    $"Er bestaat al een klant met het id '{slug}'" +
                    (existing is null ? "." : $": {existing.Name}.") +
                    " Kies een ander id, of open de bestaande klant.",
                    existing);
            }

            logger.LogError(
                "Klant {CustomerId} aanmaken is mislukt: batchstatus {Status}, oorzaak {Cause}.",
                slug,
                response.StatusCode,
                cause);

            throw new PortalDataNotProvisionedException(
                $"De klant '{slug}' is niet aangemaakt. Cosmos gaf {(int)response.StatusCode} " +
                $"{response.StatusCode} (eerste echte oorzaak: {(int)cause} {cause}). Er is niets " +
                "half weggeschreven — een transactionele batch schrijft alles of niets.");
        }

        await ReloadDirectoryAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Klant {CustomerId} aangemaakt door {Actor}, met {Access} toegang(en) en {Contract}.",
            slug,
            scope.Actor,
            request.Access.Count,
            request.Contract is null ? "zonder contract" : "een contract");

        return PortalWriteResult<CustomerDocument>.Saved(
            response.GetOperationResultAtIndex<CustomerDocument>(0).Resource);
    }

    /// <inheritdoc />
    public async Task<PortalWriteResult<CustomerDocument>> SaveCustomerAsync(
        CustomerWriteScope scope,
        CustomerEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(edit);

        if (edit.Validate() is { } error)
        {
            return PortalWriteResult<CustomerDocument>.Invalid(error);
        }

        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();

        var current = await PointReadAsync<CustomerDocument>(
            container,
            PortalDocumentIds.Customer,
            scope.CustomerId,
            cancellationToken).ConfigureAwait(false);

        if (StaleCheck(current?.ETag, edit.BasedOnETag) is { } stale)
        {
            return PortalWriteResult<CustomerDocument>.Conflict(stale, current);
        }

        var document = new CustomerDocument
        {
            Id = PortalDocumentIds.Customer,
            PartitionKey = scope.CustomerId,
            CustomerId = scope.CustomerId,
            Name = edit.Name.Trim(),
            IsInternal = current?.IsInternal ?? false,
            Environment = Clean(edit.Environment),
            EnvironmentDetail = Clean(edit.EnvironmentDetail),
            TelemetryEndpoint = Clean(edit.TelemetryEndpoint),
            TelemetryDatabase = Clean(edit.TelemetryDatabase),
            CreatedAt = current?.CreatedAt ?? now,
            CreatedBy = current?.CreatedBy ?? scope.Actor,
            ChangedAt = now,
            ChangedBy = scope.Actor,
        };

        var result = await UpsertAsync(
            container,
            document,
            scope.CustomerId,
            edit.BasedOnETag,
            () => PointReadAsync<CustomerDocument>(
                container,
                PortalDocumentIds.Customer,
                scope.CustomerId,
                cancellationToken),
            "klant",
            cancellationToken).ConfigureAwait(false);

        if (result.IsSaved)
        {
            await ReloadDirectoryAsync(cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<PortalWriteResult<ContractDocument>> SaveContractAsync(
        CustomerWriteScope scope,
        ContractEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(edit);

        if (edit.Validate() is { } error)
        {
            return PortalWriteResult<ContractDocument>.Invalid(error);
        }

        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);
        var document = ToDocument(edit, scope.CustomerId, scope.Actor, timeProvider.GetUtcNow());

        // Het contract zit niet in de klantenlijst, dus hier wordt niets herladen. Zou dat wel
        // gebeuren, dan kostte elke contractwijziging een volledige lezing van alle klanten voor
        // gegevens die de autorisatie niet raken.
        return await UpsertAsync(
            container,
            document,
            scope.CustomerId,
            edit.BasedOnETag,
            () => ReadContractAsync(scope.CustomerId, cancellationToken),
            "contract",
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PortalWriteResult<AccessDocument>> GrantAccessAsync(
        CustomerWriteScope scope,
        AccessGrant grant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(grant);

        if (grant.Validate() is { } error)
        {
            return PortalWriteResult<AccessDocument>.Invalid(error);
        }

        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);
        var document = ToDocument(grant, scope.CustomerId, scope.Actor, timeProvider.GetUtcNow());

        try
        {
            var response = await container
                .CreateItemAsync(
                    document,
                    new PartitionKey(scope.CustomerId),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await ReloadDirectoryAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Toegang voor {Email} op klant {CustomerId} vastgelegd door {Actor}.",
                document.Email,
                scope.CustomerId,
                scope.Actor);

            return PortalWriteResult<AccessDocument>.Saved(response.Resource);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            var existing = await PointReadAsync<AccessDocument>(
                container,
                document.Id,
                scope.CustomerId,
                cancellationToken).ConfigureAwait(false);

            return PortalWriteResult<AccessDocument>.Conflict(
                $"{document.Email} heeft al toegang tot {scope.DisplayName}" +
                (existing is null ? "." : $", als {existing.Role}.") +
                " Wijzig de bestaande regel of trek hem in.",
                existing);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            throw WriteForbidden(exception);
        }
    }

    /// <inheritdoc />
    public async Task<PortalWriteResult<AccessDocument>> RevokeAccessAsync(
        CustomerWriteScope scope,
        string email,
        string? basedOnETag,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var normalized = PortalEmail.Normalize(email);

        if (PortalEmail.Validate(normalized) is { } error)
        {
            return PortalWriteResult<AccessDocument>.Invalid(error);
        }

        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);
        var id = PortalDocumentIds.Access(normalized);

        var current = await PointReadAsync<AccessDocument>(container, id, scope.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        if (current is null)
        {
            return PortalWriteResult<AccessDocument>.Conflict(
                $"{normalized} heeft geen toegang (meer) tot {scope.DisplayName}. Iemand anders " +
                "heeft die mogelijk net ingetrokken.",
                current: null);
        }

        if (StaleCheck(current.ETag, basedOnETag) is { } stale)
        {
            return PortalWriteResult<AccessDocument>.Conflict(stale, current);
        }

        try
        {
            await container
                .DeleteItemAsync<AccessDocument>(
                    id,
                    new PartitionKey(scope.CustomerId),
                    basedOnETag is null ? null : new ItemRequestOptions { IfMatchEtag = basedOnETag },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            var fresh = await PointReadAsync<AccessDocument>(container, id, scope.CustomerId, cancellationToken)
                .ConfigureAwait(false);

            return PortalWriteResult<AccessDocument>.Conflict(
                $"De toegang van {normalized} is intussen gewijzigd. Bekijk de regel opnieuw " +
                "voordat je hem intrekt.",
                fresh);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return PortalWriteResult<AccessDocument>.Conflict(
                $"{normalized} heeft geen toegang (meer) tot {scope.DisplayName}. Iemand anders " +
                "was net eerder.",
                current: null);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            throw WriteForbidden(exception);
        }

        await ReloadDirectoryAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Toegang voor {Email} op klant {CustomerId} ingetrokken door {Actor}.",
            normalized,
            scope.CustomerId,
            scope.Actor);

        return PortalWriteResult<AccessDocument>.Saved(current);
    }

    // ── Systeempaden: geen scope, en dat is verantwoord ──────────────────────────────────────────

    /// <summary>
    /// Leest alle klanten en alle toegangen, voor de klantenlijst in het geheugen.
    /// </summary>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De klantdocumenten en de toegangsdocumenten.</returns>
    /// <remarks>
    /// <para><strong>Deze methode heeft geen scope, en die kan hij niet hebben.</strong> Een scope is
    /// het bewijs dat een gebruiker iets mag, en dat bewijs wordt uit déze gegevens afgeleid. Een
    /// scope eisen zou betekenen dat je de autorisatiebron alleen mag lezen als je hem al hebt
    /// gelezen. Daarom staat dit niet op <see cref="IPortalDataStore"/>: die interface is de
    /// gebruikerskant, en daar begint elke methode met een scope. Dit is systeemcode met precies één
    /// aanroeper, <see cref="PortalDirectoryRefresh"/>, en het resultaat gaat naar de klantenlijst en
    /// nooit naar een scherm.</para>
    ///
    /// <para>Twee cross-partition query's over een container met tientallen documenten. Dat is
    /// goedkoop en het gebeurt niet per verzoek maar bij het opstarten, na een schrijfactie en op een
    /// interval.</para>
    /// </remarks>
    internal async Task<(IReadOnlyList<CustomerDocument> Customers, IReadOnlyList<AccessDocument> Access)>
        LoadDirectoryAsync(CancellationToken cancellationToken)
    {
        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        var customers = await ReadAllAsync<CustomerDocument>(
            container,
            PortalDocumentKinds.Customer,
            cancellationToken).ConfigureAwait(false);

        var access = await ReadAllAsync<AccessDocument>(
            container,
            PortalDocumentKinds.Access,
            cancellationToken).ConfigureAwait(false);

        return (customers, access);
    }

    /// <summary>
    /// Schrijft de klanten uit de configuratie één keer naar de opslag.
    /// </summary>
    /// <param name="records">De klanten uit <c>Portal:Customers</c>.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Hoeveel klanten zijn weggeschreven, of <c>null</c> als de migratie al had gelopen.</returns>
    /// <remarks>
    /// <para>Dit is de omschakeling van fase 0 naar fase 2 en loopt precies één keer per opslag. De
    /// markering (<see cref="BootstrapDocument"/>) wordt als láátste geschreven: stopt de migratie
    /// halverwege, dan maakt de volgende start hem af — elke klant wordt met
    /// <c>CreateItem</c> geschreven, dus wat er al staat levert een 409 op en wordt overgeslagen.
    /// Zolang de markering ontbreekt is de migratie dus herhaalbaar, en zodra hij er staat gebeurt
    /// het nooit meer.</para>
    ///
    /// <para>Er wordt bewust geen contract geschreven. De configuratie heeft er geen, en een leeg
    /// contractdocument neerzetten zou het verschil wegnemen tussen "nog niet vastgelegd" en
    /// "vastgelegd met lege velden". De demo-contracten uit de mockup horen in het seed-project en
    /// niet hier: het portaal hoort niet te weten wat seed-data is.</para>
    /// </remarks>
    internal async Task<int?> BootstrapAsync(
        IReadOnlyList<CustomerRecord> records,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);

        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        var marker = await PointReadAsync<BootstrapDocument>(
            container,
            PortalDocumentIds.Bootstrap,
            PortalDocumentIds.ReservedPartitionKey,
            cancellationToken).ConfigureAwait(false);

        if (marker is not null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var written = new List<string>();

        foreach (var record in records)
        {
            if (PortalSlug.Validate(record.Id) is { } invalid)
            {
                logger.LogWarning(
                    "Klant '{CustomerId}' uit de configuratie is niet gemigreerd: {Reason}",
                    record.Id,
                    invalid);
                continue;
            }

            var batch = container.CreateTransactionalBatch(new PartitionKey(record.Id));

            batch.CreateItem(new CustomerDocument
            {
                Id = PortalDocumentIds.Customer,
                PartitionKey = record.Id,
                CustomerId = record.Id,
                Name = record.Name,
                IsInternal = record.IsInternal,
                Environment = Clean(record.Environment),
                EnvironmentDetail = Clean(record.EnvironmentDetail),
                TelemetryEndpoint = Clean(record.TelemetryEndpoint),
                TelemetryDatabase = Clean(record.TelemetryDatabase),
                CreatedAt = now,
                CreatedBy = "migratie uit appsettings.json",
            });

            foreach (var access in record.Access)
            {
                var email = PortalEmail.Normalize(access.Email);

                if (PortalEmail.Validate(email) is not null)
                {
                    continue;
                }

                batch.CreateItem(new AccessDocument
                {
                    Id = PortalDocumentIds.Access(email),
                    PartitionKey = record.Id,
                    CustomerId = record.Id,
                    Email = email,
                    Name = access.Name,
                    Role = PortalAccessRoles.IsKnown(access.Role) ? access.Role! : PortalAccessRoles.Reader,
                    GrantedAt = now,
                    GrantedBy = "migratie uit appsettings.json",
                });
            }

            using var response = await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                written.Add(record.Id);
                continue;
            }

            if (FirstRealFailure(response) == HttpStatusCode.Conflict)
            {
                // Stond er al. Dat is geen fout: de migratie is herhaalbaar tot de markering staat.
                continue;
            }

            logger.LogError(
                "Klant {CustomerId} migreren is mislukt met {Status}. De markering wordt niet " +
                "gezet, dus de volgende start probeert het opnieuw.",
                record.Id,
                response.StatusCode);

            return written.Count;
        }

        await container.CreateItemAsync(
            new BootstrapDocument
            {
                Id = PortalDocumentIds.Bootstrap,
                PartitionKey = PortalDocumentIds.ReservedPartitionKey,
                RanAt = now,
                Customers = written.Count,
                Slugs = written,
            },
            new PartitionKey(PortalDocumentIds.ReservedPartitionKey),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return written.Count;
    }

    /// <summary>
    /// Leest de klantenlijst opnieuw en vervangt de momentopname in het geheugen.
    /// </summary>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    internal async Task ReloadDirectoryAsync(CancellationToken cancellationToken)
    {
        var (customers, access) = await LoadDirectoryAsync(cancellationToken).ConfigureAwait(false);
        directory.Replace(customers, access);
    }

    // ── Binnenwerk ──────────────────────────────────────────────────────────────────────────────

    private async Task<Container> ContainerAsync(CancellationToken cancellationToken)
    {
        var location = _options.Location()
            ?? throw new PortalDataNotProvisionedException(
                "De portaalopslag is niet ingericht: PortalData:AccountEndpoint is leeg. Klanten, " +
                "contracten en toegang zijn daarmee niet te lezen of te schrijven; het portaal " +
                "werkt op de klantenlijst uit de configuratie.");

        return await containers.CustomersAsync(location, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ContractDocument?> ReadContractAsync(
        string customerId,
        CancellationToken cancellationToken)
    {
        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        return await PointReadAsync<ContractDocument>(
            container,
            PortalDocumentIds.Contract,
            customerId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <remarks>
    /// Eén partitie, dus geen fan-out. De sortering gebeurt hier en niet in de query: het gaat om
    /// een handvol rijen, en een <c>ORDER BY</c> zou een index-afhankelijkheid toevoegen aan een
    /// container waarvan de indexeringspolitiek in Bicep staat.
    /// </remarks>
    private async Task<IReadOnlyList<AccessDocument>> ReadAccessAsync(
        string customerId,
        CancellationToken cancellationToken)
    {
        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        var query = new QueryDefinition("SELECT * FROM c WHERE c.kind = @kind")
            .WithParameter("@kind", PortalDocumentKinds.Access);

        var results = new List<AccessDocument>();

        using var iterator = container.GetItemQueryIterator<AccessDocument>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(customerId) });

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            results.AddRange(response);
        }

        return [.. results.OrderBy(entry => entry.Email, StringComparer.Ordinal)];
    }

    private static async Task<IReadOnlyList<T>> ReadAllAsync<T>(
        Container container,
        string kind,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.kind = @kind")
            .WithParameter("@kind", kind);

        var results = new List<T>();

        using var iterator = container.GetItemQueryIterator<T>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            results.AddRange(response);
        }

        return results;
    }

    private static async Task<T?> PointReadAsync<T>(
        Container container,
        string id,
        string partitionKey,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var response = await container
                .ReadItemAsync<T>(id, new PartitionKey(partitionKey), cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return response.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Schrijft een document weg met de etag van de aanroeper als voorwaarde.
    /// </summary>
    /// <remarks>
    /// Bij <c>basedOnETag == null</c> is het een <c>CreateItem</c>: dan hoort er nog niets te staan,
    /// en staat er toch iets, dan is dat ook een conflict. Bij een etag is het een
    /// <c>ReplaceItem</c> met <c>If-Match</c>. Er is dus geen pad waarlangs een schrijfactie een
    /// andere wijziging overschrijft zonder dat de aanroeper het hoort.
    /// </remarks>
    private async Task<PortalWriteResult<T>> UpsertAsync<T>(
        Container container,
        T document,
        string partitionKey,
        string? basedOnETag,
        Func<Task<T?>> readCurrent,
        string what,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            if (basedOnETag is null)
            {
                var created = await container
                    .CreateItemAsync(
                        document,
                        new PartitionKey(partitionKey),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                return PortalWriteResult<T>.Saved(created.Resource);
            }

            var replaced = await container
                .ReplaceItemAsync(
                    document,
                    IdOf(document),
                    new PartitionKey(partitionKey),
                    new ItemRequestOptions { IfMatchEtag = basedOnETag },
                    cancellationToken)
                .ConfigureAwait(false);

            return PortalWriteResult<T>.Saved(replaced.Resource);
        }
        catch (CosmosException exception) when (
            exception.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
        {
            return PortalWriteResult<T>.Conflict(
                $"Dit {what} is intussen door iemand anders gewijzigd. Je wijziging is niet " +
                "opgeslagen. Vergelijk hem met de huidige versie en probeer het opnieuw.",
                await readCurrent().ConfigureAwait(false));
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // Er was een etag, dus er stond een document. Nu is het weg: iemand heeft het
            // verwijderd terwijl dit formulier open stond.
            return PortalWriteResult<T>.Conflict(
                $"Dit {what} bestaat niet meer. Iemand heeft het verwijderd terwijl je het aan het " +
                "bewerken was.",
                current: null);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            throw WriteForbidden(exception);
        }
    }

    private static string IdOf<T>(T document) => document switch
    {
        CustomerDocument => PortalDocumentIds.Customer,
        ContractDocument => PortalDocumentIds.Contract,
        AccessDocument access => access.Id,
        _ => throw new InvalidOperationException(
            $"{typeof(T).Name} heeft geen bekende documentsleutel. Voeg hem hier toe; een " +
            "documentsleutel die op twee plekken wordt samengesteld levert twee documenten op."),
    };

    /// <summary>
    /// Of de etag van de aanroeper nog overeenkomt met wat er staat.
    /// </summary>
    /// <returns>De melding bij een verschil, of <c>null</c> als er niets aan de hand is.</returns>
    /// <remarks>
    /// Dit is een vroege uitweg en niet de controle zelf. De echte controle doet Cosmos met
    /// <c>If-Match</c> — die is atomair, deze niet. Hij staat er omdat hij de betere melding kan
    /// geven (met het huidige document erbij) en een schrijfpoging uitspaart die toch zou falen.
    /// </remarks>
    private static string? StaleCheck(string? currentETag, string? basedOnETag)
    {
        if (basedOnETag is null || currentETag is null || string.Equals(currentETag, basedOnETag, StringComparison.Ordinal))
        {
            return null;
        }

        return "Dit is intussen door iemand anders gewijzigd. Je wijziging is niet opgeslagen; " +
               "bekijk de huidige versie en probeer het opnieuw.";
    }

    private static HttpStatusCode FirstRealFailure(TransactionalBatchResponse response)
    {
        for (var index = 0; index < response.Count; index++)
        {
            var status = response[index].StatusCode;

            if (status != HttpStatusCode.FailedDependency && !IsSuccess(status))
            {
                return status;
            }
        }

        return response.StatusCode;
    }

    private static bool IsSuccess(HttpStatusCode status) =>
        (int)status is >= 200 and < 300;

    private static PortalDataNotProvisionedException WriteForbidden(CosmosException exception) =>
        new(
            "Het portaal mag niet schrijven in de portaalopslag. De managed identity heeft " +
            "'Cosmos DB Built-in Data Contributor' nodig op de database, als " +
            "sqlRoleAssignment op het Cosmos-dataplane. Een Reader-rol laat lezen lukken en pas de " +
            "eerste schrijfpoging falen, en dat is precies wat er nu gebeurt.",
            exception);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ContractDocument ToDocument(
        ContractEdit edit,
        string customerId,
        string actor,
        DateTimeOffset now) => new()
    {
        Id = PortalDocumentIds.Contract,
        PartitionKey = customerId,
        CustomerId = customerId,
        Number = Clean(edit.Number),
        Type = Clean(edit.Type),
        StartsOn = Clean(edit.StartsOn),
        Term = Clean(edit.Term),
        NoticePeriod = Clean(edit.NoticePeriod),
        Sla = Clean(edit.Sla),
        BundledHours = edit.BundledHours,
        HourlyRate = edit.HourlyRate,
        Indexation = Clean(edit.Indexation),
        Contact = Clean(edit.Contact),
        ManagedBy = Clean(edit.ManagedBy),
        AzureSurchargePercentage = edit.AzureSurchargePercentage,
        ChangedAt = now,
        ChangedBy = actor,
    };

    private static AccessDocument ToDocument(
        AccessGrant grant,
        string customerId,
        string actor,
        DateTimeOffset now)
    {
        var email = PortalEmail.Normalize(grant.Email);

        return new AccessDocument
        {
            Id = PortalDocumentIds.Access(email),
            PartitionKey = customerId,
            CustomerId = customerId,
            Email = email,
            Name = Clean(grant.Name),
            Role = grant.Role,
            GrantedAt = now,
            GrantedBy = actor,
        };
    }
}
