using System.Globalization;
using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Soratus.Portal.Security;

namespace Soratus.Portal.Data;

/// <summary>
/// De enige implementatie van <see cref="IPortalHoursStore"/>: urenregels in de container
/// <c>customers</c>, op de partitiesleutel van de klant.
/// </summary>
/// <remarks>
/// <para><strong>Elke query loopt binnen één partitiesleutel.</strong> Er is geen enkele
/// cross-partition query in deze klasse, en dat is niet toevallig: de partitiesleutel komt uit de
/// scope, dus er is geen aanroep waarmee je met de scope van klant A de uren van klant B leest. Het
/// filter op <c>c.cid</c> staat er niet bij, omdat de partitiesleutel diezelfde waarde is — een tweede
/// filter op dezelfde waarde suggereert dat de eerste onvoldoende is.</para>
///
/// <para><strong>De maandgrens is een tekstvergelijking, en dat mag omdat de opslagvorm vast is.</strong>
/// <c>c.month &gt;= '2026-01' AND c.month &lt;= '2026-12'</c> werkt op <c>yyyy-MM</c> en op geen enkele
/// andere vorm. Dat is punt 7 van de fase-0-afwijkingen, hier voor de tweede keer: Cosmos vergelijkt
/// tijdvelden lexicografisch, en op <c>MM-yyyy</c> — de vorm die de mockup toont — zou deze query stil
/// de verkeerde regels teruggeven in plaats van te falen.</para>
///
/// <para><strong>Sorteren gebeurt in het geheugen en niet met <c>ORDER BY</c>.</strong> Dezelfde keuze
/// als bij <see cref="CosmosPortalDataStore"/>: het gaat om de regels van één maand of één jaar van één
/// klant, en de sortering is op twee velden (datum, dan sleutel). Een <c>ORDER BY</c> op twee velden
/// vraagt een composite index, en de indexeringspolitiek van deze container staat in Bicep — dus zou
/// een sortering hier stilzwijgend een uitrol vereisen die niemand aanvraagt.</para>
///
/// <para><strong>Er wordt nooit een gefiatteerde regel gewijzigd.</strong> Boeken en corrigeren maken
/// een document; fiatteren en afwijzen wijzigen alleen een regel die nog niet gefiatteerd is (zie
/// <see cref="HourEntryTransitions"/>). Daarmee is het maandtotaal van een afgesloten maand een som die
/// niet meer kan verschuiven — de eigenschap waarop de conceptfactuur van fase 4 rust.</para>
/// </remarks>
internal sealed class CosmosPortalHoursStore(
    CosmosContainerProvider containers,
    IOptions<PortalDataOptions> options,
    TimeProvider timeProvider,
    ILogger<CosmosPortalHoursStore> logger) : IPortalHoursStore
{
    private readonly PortalDataOptions _options = options.Value;

    /// <inheritdoc />
    public Task<IReadOnlyList<HourEntryDocument>> GetApprovedHoursAsync(
        CustomerScope scope,
        HoursQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        return ReadAsync(scope.CustomerId, query, HourEntryStatus.Approved, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<HourEntryDocument>> GetHoursAsync(
        CustomerWriteScope scope,
        HoursQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        return ReadAsync(scope.CustomerId, query, status: null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PortalWriteResult<HourEntryDocument>> BookHoursAsync(
        CustomerWriteScope scope,
        HourBooking booking,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(booking);

        if (booking.Validate() is { } error)
        {
            return Task.FromResult(PortalWriteResult<HourEntryDocument>.Invalid(error));
        }

        return CreateAsync(
            scope,
            booking.Month.Trim(),
            booking.Hours,
            booking.Category,
            booking.By.Trim(),
            booking.Note.Trim(),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<PortalWriteResult<HourEntryDocument>> CorrectHoursAsync(
        CustomerWriteScope scope,
        HourCorrection correction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(correction);

        if (correction.Validate() is { } error)
        {
            return Task.FromResult(PortalWriteResult<HourEntryDocument>.Invalid(error));
        }

        // Categorie en bron staan hier vast en zijn geen parameter. Dat is wat een correctie een
        // correctie maakt: hij is terug te vinden als rij én als getal in de tooltip, en niemand kan
        // hem per ongeluk als gewone boeking wegschrijven.
        return CreateAsync(
            scope,
            correction.Month.Trim(),
            correction.Hours,
            HourCategories.Correction,
            correction.By.Trim(),
            correction.Note.Trim(),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PortalWriteResult<HourEntryDocument>> ApproveHoursAsync(
        CustomerWriteScope scope,
        string entryId,
        string? basedOnETag,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (string.IsNullOrWhiteSpace(entryId))
        {
            return PortalWriteResult<HourEntryDocument>.Invalid(
                "Er is geen urenregel meegegeven om te fiatteren.");
        }

        var now = timeProvider.GetUtcNow();

        return await DecideAsync(
            scope,
            entryId.Trim(),
            basedOnETag,
            HourEntryTransitions.WhyNotApprove,
            current => current with
            {
                Status = HourEntryStatus.Approved,
                ApprovedAt = now,
                ApprovedBy = scope.Actor,

                // De afwijzing wordt gewist en niet naast de fiattering bewaard. Een document met
                // zowel een fiattering als een afwijzing erop is niet te lezen: het scherm moet dan
                // kiezen welke van de twee het toont, en dat is dezelfde soort tegenspraak als twee
                // velden over hetzelfde bedrag op het contract. Wat er gebeurd is blijft leesbaar in
                // de logregel hieronder.
                RejectedAt = null,
                RejectedBy = null,
                RejectionReason = null,
            },
            "gefiatteerd",
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PortalWriteResult<HourEntryDocument>> RejectHoursAsync(
        CustomerWriteScope scope,
        HourRejection rejection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(rejection);

        if (rejection.Validate() is { } error)
        {
            return PortalWriteResult<HourEntryDocument>.Invalid(error);
        }

        var now = timeProvider.GetUtcNow();

        return await DecideAsync(
            scope,
            rejection.EntryId.Trim(),
            rejection.BasedOnETag,
            HourEntryTransitions.WhyNotReject,
            current => current with
            {
                Status = HourEntryStatus.Rejected,
                RejectedAt = now,
                RejectedBy = scope.Actor,
                RejectionReason = rejection.Reason.Trim(),
                ApprovedAt = null,
                ApprovedBy = null,
            },
            "afgewezen",
            cancellationToken).ConfigureAwait(false);
    }

    // ── Binnenwerk ──────────────────────────────────────────────────────────────────────────────

    private async Task<Container> ContainerAsync(CancellationToken cancellationToken)
    {
        var location = _options.Location()
            ?? throw new PortalDataNotProvisionedException(
                "De portaalopslag is niet ingericht: PortalData:AccountEndpoint is leeg. Urenregels " +
                "zijn daarmee niet te lezen of te schrijven. Het urenscherm hoort dat te melden in " +
                "plaats van een maand zonder uren te tonen — nul geboekte uren en een onbereikbare " +
                "opslag zien er op een totaalregel hetzelfde uit, en dat is precies het verschil dat " +
                "een factuur raakt.");

        return await containers.CustomersAsync(location, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Leest de urenregels van één klant binnen één maand of één jaar.
    /// </summary>
    /// <param name="customerId">
    /// De klantslug. Dit is de partitiesleutel, en daarmee de isolatiegrens: er is geen aanroep
    /// waarmee je hier de uren van een andere klant leest, want deze waarde komt uit de scope.
    /// </param>
    /// <param name="query">Één maand of één jaar. Zie <see cref="HoursQuery"/>.</param>
    /// <param name="status">
    /// De stand waarop wordt gefilterd, of <c>null</c> voor alle standen. Dit is de klantgrens: bij
    /// <see cref="HourEntryStatus.Approved"/> komt een te fiatteren regel niet uit de opslag, in plaats
    /// van eruit te worden weggelaten.
    /// </param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De regels, nieuwste eerst op het moment van vastleggen.</returns>
    private async Task<IReadOnlyList<HourEntryDocument>> ReadAsync(
        string customerId,
        HoursQuery query,
        HourEntryStatus? status,
        CancellationToken cancellationToken)
    {
        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        var text = query.IsSingleMonth
            ? "SELECT * FROM c WHERE c.kind = @kind AND c.month = @month"
            : "SELECT * FROM c WHERE c.kind = @kind AND c.month >= @from AND c.month <= @to";

        var definition = new QueryDefinition(
                status is null ? text : text + " AND c.status = @status")
            .WithParameter("@kind", PortalDocumentKinds.HourEntry);

        if (query.IsSingleMonth)
        {
            definition = definition.WithParameter("@month", query.Month);
        }
        else
        {
            definition = definition
                .WithParameter("@from", query.FirstMonth())
                .WithParameter("@to", query.LastMonth());
        }

        if (status is { } wanted)
        {
            // De tekst komt uit de serializer en niet uit een switch hiernaast. Zie HourJsonValues:
            // dit is de plek waar een verkeerde schrijfwijze nul regels oplevert in plaats van een
            // fout, en dan zegt het scherm dat er niets is geboekt.
            definition = definition.WithParameter("@status", HourJsonValues.Of(wanted));
        }

        var results = new List<HourEntryDocument>();
        var charge = 0d;

        using var iterator = container.GetItemQueryIterator<HourEntryDocument>(
            definition,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(customerId) });

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            results.AddRange(response);
            charge += response.RequestCharge;
        }

        logger.LogDebug(
            "Urenregels van {CustomerId} voor {Period}: {Count} regel(s), {Charge} RU.",
            customerId,
            query.Month ?? query.Year.ToString(CultureInfo.InvariantCulture),
            results.Count,
            charge);

        // Nieuwste eerst, op het moment van vastleggen. Bij een gelijk moment op sleutel — anders
        // wisselt de volgorde van twee regels per lezing en lijkt het scherm te bewegen zonder dat er
        // iets is veranderd. Dat gelijke moment is geen theorie: twee regels die als seed of als import
        // in één keer worden weggeschreven kunnen dezelfde tijdstempel hebben.
        //
        // ZET HIER GEEN "ORDER BY c.createdAt" IN DE QUERY. Dat is de optimalisatie die zich aanbiedt
        // — hij spaart deze sortering uit en maakt paginering mogelijk — en er is één eis waar hij
        // niet aan kan voldoen: de tie-break. Bij een gelijk moment moet de sleutel de volgorde
        // bepalen, en dat gelijke moment is geen theorie (zie hierboven). Een ORDER BY op één veld
        // laat die gevallen in willekeurige volgorde staan.
        //
        // Hier stond een tweede, zwaardere reden: de schrijfkant van dit portaal schreef tijden niet
        // canoniek weg — gemeten "2026-08-20T15:04:05.678+00:00", met een offset in plaats van een Z
        // en een variabel aantal decimalen — en Cosmos vergelijkt tijdvelden als tekst. Die reden is
        // weg: de normalisatie zit sinds punt 25 van de fase-0-afwijkingen op de opties die dit
        // portaal aan de Cosmos-SDK geeft (CosmosClientCache.SerializerOptions), met een assertie
        // erop en met tests in PortaaltijdvormTests die de echte opties uitoefenen. Dat staat hier
        // niet meer als waarschuwing, want een comment dat iets beweert wat gemeten onwaar is maakt
        // de rest van het comment ook onbetrouwbaar.
        //
        // Wil je dit ooit tóch naar de query verplaatsen, dan zijn dit de voorwaarden: een composite
        // index op (createdAt DESC, id ASC) zodat de tie-break in de query zelf staat, én de
        // zekerheid dat élk document in de container de canonieke vorm heeft. Dat tweede is voor
        // urenregels vandaag waar — de container bestaat nog niet, dus alles erin wordt door de
        // gerepareerde schrijfkant geschreven — maar het geldt niet containerbreed voor het portaal:
        // de klantdocumenten in platform/customers dateren van vóór de reparatie en staan er nog in
        // de oude vorm. Zie punt 25.
        //
        // In C# vergelijkt DateTimeOffset altijd correct, dus deze sortering in het geheugen is juist.
        return
        [
            .. results
                .OrderByDescending(entry => entry.CreatedAt)
                .ThenBy(entry => entry.Id, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Schrijft een nieuwe gefiatteerde regel weg: een boeking of een correctie.
    /// </summary>
    /// <remarks>
    /// <c>CreateItem</c> en geen <c>Upsert</c>. De sleutel is afgeleid van het moment en de inhoud, dus
    /// een tweede verzending van hetzelfde formulier botst hier — en een botsing is precies het
    /// antwoord dat we willen. Met een upsert zou de tweede verzending de eerste overschrijven en
    /// stil slagen.
    /// </remarks>
    private async Task<PortalWriteResult<HourEntryDocument>> CreateAsync(
        CustomerWriteScope scope,
        string month,
        decimal hours,
        string category,
        string by,
        string note,
        CancellationToken cancellationToken)
    {
        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();

        var key = HourEntryKeys.ForPortal(
            now,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{HourEntryKeys.Serialize(HourEntrySource.Portal)}|{month}|{category}|{hours}|{by}|{note}"));

        var document = new HourEntryDocument
        {
            Id = PortalDocumentIds.HourEntry(key),
            PartitionKey = scope.CustomerId,
            CustomerId = scope.CustomerId,

            Month = month,
            Category = category,
            Note = note,
            Hours = hours,
            Source = HourEntrySource.Portal,
            By = by,

            // Boeken in het portaal ís het akkoord van Soratus. §5 gaat over wat een agent of
            // koppeling inschiet, en die schrijven niet langs deze methode.
            Status = HourEntryStatus.Approved,
            CreatedAt = now,
            CreatedBy = scope.Actor,
            ApprovedAt = now,
            ApprovedBy = scope.Actor,
        };

        try
        {
            var response = await container
                .CreateItemAsync(
                    document,
                    new PartitionKey(scope.CustomerId),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Urenregel {EntryId} ({Hours} u, {Category}) op klant {CustomerId} vastgelegd door " +
                "{Actor} voor maand {Month}. {Charge} RU.",
                document.Id,
                hours,
                category,
                scope.CustomerId,
                scope.Actor,
                month,
                response.RequestCharge);

            return PortalWriteResult<HourEntryDocument>.Saved(response.Resource);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            var existing = await PointReadAsync(container, document.Id, scope.CustomerId, cancellationToken)
                .ConfigureAwait(false);

            return PortalWriteResult<HourEntryDocument>.Conflict(
                "Deze urenregel staat er al. Waarschijnlijk is het formulier twee keer verstuurd; er " +
                "is één regel vastgelegd en geen twee. Moet dit echt een tweede regel zijn, wijzig " +
                "dan de omschrijving.",
                existing);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            throw WriteForbidden(exception);
        }
    }

    /// <summary>
    /// Fiatteert of wijst één regel af: lezen, de overgang toetsen, en schrijven met <c>If-Match</c>.
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant. Levert de partitiesleutel en de actor.</param>
    /// <param name="entryId">De documentsleutel van de regel, zoals hij op het scherm stond.</param>
    /// <param name="basedOnETag">
    /// De etag waarop de beoordeling rust, of <c>null</c> om te beoordelen zoals de regel nu is.
    /// </param>
    /// <param name="whyNot">
    /// De toets uit <see cref="HourEntryTransitions"/>. Meegegeven en niet hier ingebouwd, zodat de
    /// weergave dezelfde functie gebruikt om te bepalen of er een knop hoort te staan — anders staat er
    /// een knop die een melding oplevert, of ontbreekt er een bij iets wat wel mag.
    /// </param>
    /// <param name="decide">
    /// Wat er met de regel gebeurt. Krijgt het gelezen document en geeft de nieuwe versie terug; deze
    /// methode bepaalt niet zelf welke velden er veranderen, want fiatteren en afwijzen zetten
    /// verschillende sporen en wissen elkaars sporen.
    /// </param>
    /// <param name="what">
    /// Wat er is gebeurd, voor de logregel: "gefiatteerd" of "afgewezen". Alleen voor de log, nooit
    /// voor een melding op het scherm — die staan in <see cref="HourEntryTransitions"/> en in
    /// <see cref="PortalWriteResult{T}"/>.
    /// </param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De gewijzigde regel, of een conflict, of een melding als de overgang niet mag.</returns>
    private async Task<PortalWriteResult<HourEntryDocument>> DecideAsync(
        CustomerWriteScope scope,
        string entryId,
        string? basedOnETag,
        Func<HourEntryStatus, string?> whyNot,
        Func<HourEntryDocument, HourEntryDocument> decide,
        string what,
        CancellationToken cancellationToken)
    {
        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        var current = await PointReadAsync(container, entryId, scope.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        if (current is null)
        {
            return PortalWriteResult<HourEntryDocument>.Conflict(
                $"Deze urenregel bestaat niet (meer) bij {scope.DisplayName}. Vernieuw het scherm.",
                current: null);
        }

        // De overgang wordt vóór de etag getoetst, en dat is de juiste volgorde: "deze regel is al
        // gefiatteerd" is een preciezere mededeling dan "iemand anders was eerder", ook al is de
        // tweede oorzaak dezelfde. Wie deze twee omdraait, krijgt bij de gewone dubbele klik de
        // vage melding.
        if (whyNot(current.Status) is { } refused)
        {
            return PortalWriteResult<HourEntryDocument>.Invalid(refused);
        }

        if (StaleCheck(current.ETag, basedOnETag) is { } stale)
        {
            return PortalWriteResult<HourEntryDocument>.Conflict(stale, current);
        }

        var previous = current.Status;

        try
        {
            var response = await container
                .ReplaceItemAsync(
                    decide(current),
                    entryId,
                    new PartitionKey(scope.CustomerId),
                    basedOnETag is null ? null : new ItemRequestOptions { IfMatchEtag = basedOnETag },
                    cancellationToken)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Urenregel {EntryId} van klant {CustomerId} is {What} door {Actor}; stand was " +
                "{Previous}. {Hours} u op maand {Month}, bron {Source}. {Charge} RU.",
                entryId,
                scope.CustomerId,
                what,
                scope.Actor,
                previous,
                current.Hours,
                current.Month,
                current.Source,
                response.RequestCharge);

            return PortalWriteResult<HourEntryDocument>.Saved(response.Resource);
        }
        catch (CosmosException exception) when (
            exception.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
        {
            var fresh = await PointReadAsync(container, entryId, scope.CustomerId, cancellationToken)
                .ConfigureAwait(false);

            return PortalWriteResult<HourEntryDocument>.Conflict(
                "Deze urenregel is intussen door iemand anders beoordeeld. Je beslissing is niet " +
                "opgeslagen; bekijk de regel opnieuw.",
                fresh);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            throw WriteForbidden(exception);
        }
    }

    private static async Task<HourEntryDocument?> PointReadAsync(
        Container container,
        string id,
        string partitionKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await container
                .ReadItemAsync<HourEntryDocument>(
                    id,
                    new PartitionKey(partitionKey),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // Een id uit een formulier kan van alles zijn. Een document van een ánder soort in dezelfde
            // partitie zou hier als urenregel gelezen worden — de container bevat ook klant, contract
            // en toegang, en die delen deze partitiesleutel. Dat is de prijs van één container, en dit
            // is de plek waar hij betaald wordt.
            return response.Resource is { Kind: PortalDocumentKinds.HourEntry } entry ? entry : null;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc cref="CosmosPortalDataStore" />
    /// <remarks>
    /// Dezelfde vroege uitweg als in <see cref="CosmosPortalDataStore"/>: de echte controle doet Cosmos
    /// met <c>If-Match</c>, deze staat er voor de betere melding.
    /// </remarks>
    private static string? StaleCheck(string? currentETag, string? basedOnETag) =>
        basedOnETag is null
        || currentETag is null
        || string.Equals(currentETag, basedOnETag, StringComparison.Ordinal)
            ? null
            : "Deze urenregel is intussen gewijzigd. Je beslissing is niet opgeslagen; bekijk de " +
              "regel opnieuw voordat je hem beoordeelt.";

    private static PortalDataNotProvisionedException WriteForbidden(CosmosException exception) =>
        new(
            "Het portaal mag niet schrijven in de portaalopslag. De managed identity heeft " +
            "'Cosmos DB Built-in Data Contributor' nodig op de database platform, als " +
            "sqlRoleAssignment op het Cosmos-dataplane. Een Reader-rol laat het urenscherm gewoon " +
            "vullen en pas het boeken falen, en dat is precies wat er nu gebeurt.",
            exception);
}
