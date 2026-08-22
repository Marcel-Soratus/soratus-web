using System.Globalization;
using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Soratus.Portal.Data;
using Soratus.Portal.Security;

namespace Soratus.Portal.Support;

/// <summary>
/// De enige implementatie van <see cref="ISupportStore"/>: berichten in de container
/// <c>customers</c>, op de partitiesleutel van de klant.
/// </summary>
/// <remarks>
/// <para><strong>Elke query loopt binnen één partitiesleutel.</strong> Er is geen cross-partition query
/// in deze klasse, en dat is niet toevallig: de partitiesleutel komt uit de scope, dus er is geen
/// aanroep waarmee je met de scope van klant A de draad van klant B leest. Het filter op <c>c.cid</c>
/// staat er niet bij omdat de partitiesleutel diezelfde waarde is — een tweede filter op dezelfde waarde
/// suggereert dat de eerste onvoldoende is.</para>
///
/// <para><strong>Hier wordt wél met <c>ORDER BY</c> gesorteerd, en dat wijkt af van
/// <see cref="CosmosPortalHoursStore"/>.</strong> Daar staat de sortering in het geheugen met een
/// uitgebreide waarschuwing erbij, en de reden is de tie-break: bij een gelijk moment moet de sleutel de
/// volgorde bepalen, en een <c>ORDER BY</c> op één veld laat die gevallen willekeurig staan. Hier is dat
/// probleem er niet, omdat er op de <em>sleutel zelf</em> wordt gesorteerd — en die is uniek, dus er is
/// geen tie-break nodig. Dat werkt alleen omdat <see cref="SupportDocumentKeys.Id"/> chronologisch
/// sorteert; er staat een test op precies die eigenschap, want als hij wegvalt verandert deze query
/// stil van "de vorige vijftig" in "vijftig willekeurige".</para>
///
/// <para>De indexeringspolitiek van deze container staat in <c>infra/portal/portal-rg.bicep</c> op
/// <c>/*</c> — alles geïndexeerd — dus deze sortering vraagt geen uitrol. Dat is nagekeken en niet
/// aangenomen; het is precies het bezwaar dat de urenopslag tegen een <c>ORDER BY</c> maakt.</para>
///
/// <para><strong>Er wordt nooit een bestaand bericht gewijzigd.</strong> Er is geen
/// <c>ReplaceItemAsync</c> in deze klasse en geen <c>Upsert</c>. Alle drie de schrijfpaden doen
/// <c>CreateItemAsync</c>, en een botsing op de afgeleide sleutel is precies het antwoord dat we willen:
/// één bericht in plaats van twee.</para>
/// </remarks>
internal sealed class CosmosSupportStore(
    CosmosContainerProvider containers,
    IOptions<PortalDataOptions> options,
    TimeProvider timeProvider,
    ILogger<CosmosSupportStore> logger) : ISupportStore
{
    private readonly PortalDataOptions _options = options.Value;

    /// <inheritdoc />
    public Task<SupportMessagePage> ReadThreadAsync(
        CustomerScope scope,
        SupportThreadQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        return ReadAsync(scope.CustomerId, query, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SupportMessagePage> ReadThreadAsync(
        CustomerWriteScope scope,
        SupportThreadQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        return ReadAsync(scope.CustomerId, query, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PortalWriteResult<SupportMessageDocument>> PostQuestionAsync(
        CustomerScope scope,
        SupportQuestion question,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(question);

        if (question.Validate() is { } error)
        {
            return Task.FromResult(PortalWriteResult<SupportMessageDocument>.Invalid(error));
        }

        // De afzender staat hier vast en is geen parameter. Dat is wat een klantbericht een
        // klantbericht maakt: er is geen aanroep waarmee een klant iets namens Soratus of namens de
        // eerstelijn in zijn eigen draad zet.
        return CreateAsync(
            scope.CustomerId,
            SupportAuthor.Customer,
            SupportBody.Clean(question.Text),
            question.Author.Trim(),
            ground: null,
            escalation: null,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<PortalWriteResult<SupportMessageDocument>> PostReplyAsync(
        CustomerWriteScope scope,
        SupportReply reply,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(reply);

        if (reply.Validate() is { } error)
        {
            return Task.FromResult(PortalWriteResult<SupportMessageDocument>.Invalid(error));
        }

        // De naam komt uit de scope en niet uit het formulier. Zelfde vorm als approvedBy op een
        // urenregel.
        return CreateAsync(
            scope.CustomerId,
            SupportAuthor.Soratus,
            SupportBody.Clean(reply.Text),
            scope.Actor,
            ground: null,
            escalation: null,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<PortalWriteResult<SupportMessageDocument>> RecordFirstLineAsync(
        CustomerScope scope,
        SupportEnquiry enquiry,
        SupportAnswer? answer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(enquiry);

        // ── De hele acceptatie-eis van fase 5, in twaalf regels ──────────────────────────────────
        //
        // Een antwoord wordt alleen aangenomen als élke grondslag erin ook is aangeboden. De
        // constructor van SupportGround is internal, dus een implementatie buiten deze assembly kan
        // er geen máken; deze controle vangt het geval dat zij er een van een ánder verzoek
        // teruggeeft — een gecachete grondslag van een andere klant, bijvoorbeeld.
        //
        // Wat er niet doorheen komt: null, een escalatie, en een grondslag die niet is aangeboden.
        // Alle drie leveren hetzelfde op: een escalatie. Er is dus geen tussenstand waarin een
        // bericht van de eerstelijn iets beweert zonder aanwijsbare bron.
        var accepted = Accept(enquiry, answer);

        if (accepted is null)
        {
            var reason = answer?.Escalation ?? SupportEscalation.AnswerNotUsable;

            logger.LogInformation(
                "Eerstelijn escaleert bij klant {CustomerId}: {Reason}. {Offered} grondslag(en) "
                + "aangeboden, {Returned} teruggegeven.",
                scope.CustomerId,
                reason,
                enquiry.Grounds.Count,
                answer?.Ground is null ? 0 : 1);

            return CreateAsync(
                scope.CustomerId,
                SupportAuthor.FirstLine,
                SupportText.Handoff(),
                who: null,
                ground: null,
                escalation: reason,
                cancellationToken);
        }

        // De tekst wordt hier samengesteld en komt niet van de eerstelijn: SupportAnswer heeft geen
        // tekstveld. Dit is de enige plek waar de tekst van een AI-bubbel ontstaat.
        return CreateAsync(
            scope.CustomerId,
            SupportAuthor.FirstLine,
            SupportText.Answer(accepted),
            who: null,
            ground: accepted,
            escalation: null,
            cancellationToken);
    }

    // ── Binnenwerk ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bepaalt of dit antwoord aangenomen mag worden, en levert dan de grondslag op.
    /// </summary>
    /// <param name="enquiry">Het verzoek, met de grondslagen die zijn aangeboden.</param>
    /// <param name="answer">Het antwoord van de eerstelijn, of <c>null</c>.</param>
    /// <returns>De grondslag, of <c>null</c> als er een escalatie van moet worden gemaakt.</returns>
    /// <remarks>
    /// <para><strong>Puur, en met opzet een eigen methode.</strong> Deze beslissing is de acceptatie-eis
    /// van fase 5; zij hoort te toetsen zonder Cosmos. Er staan tests op die haar rechtstreeks
    /// aanroepen.</para>
    ///
    /// <para>De vergelijking is waardegelijkheid, want <see cref="SupportGround"/> is een <c>record</c>.
    /// Dat is hier het goede: een implementatie mag een grondslag doorgeven zoals zij hem kreeg, en hoeft
    /// niet dezelfde <em>instantie</em> terug te geven -- bij een serialisatie over een procesgrens zou
    /// dat laatste nooit lukken.</para>
    ///
    /// <para><strong>Twee controles die er niet staan, en waarom niet.</strong> Er wordt niet gekeken of
    /// de grondslag bij de vraag past -- dat is een inhoudelijke beoordeling, en die is precies wat de
    /// eerstelijn doet. En er wordt niet gekeken of de tekst van de grondslag klopt: die is door het
    /// portaal zelf opgemaakt, en een controle op eigen werk voegt niets toe behalve een tweede plek
    /// waar de opmaak vastligt.</para>
    /// </remarks>
    internal static SupportGround? Accept(SupportEnquiry enquiry, SupportAnswer? answer)
    {
        ArgumentNullException.ThrowIfNull(enquiry);

        if (answer?.Ground is not { } ground)
        {
            return null;
        }

        return enquiry.Grounds.Contains(ground) ? ground : null;
    }

    private async Task<Container> ContainerAsync(CancellationToken cancellationToken)
    {
        var location = _options.Location()
            ?? throw new PortalDataNotProvisionedException(
                "De portaalopslag is niet ingericht: PortalData:AccountEndpoint is leeg. De "
                + "supportdraad is daarmee niet te lezen of te schrijven. Het scherm hoort dat te "
                + "melden in plaats van een lege draad te tonen — geen berichten en een onbereikbare "
                + "opslag zien er hetzelfde uit, en dat is precies het verschil tussen 'niemand heeft "
                + "iets gevraagd' en 'wij zien de vraag niet'.");

        return await containers.CustomersAsync(location, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Leest één deel van de draad van één klant.
    /// </summary>
    /// <param name="customerId">
    /// De klantslug. Dit is de partitiesleutel, en daarmee de isolatiegrens: deze waarde komt uit de
    /// scope, dus er is geen aanroep waarmee je hier de draad van een andere klant leest.
    /// </param>
    /// <param name="query">Welk deel.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De berichten, oudste eerst, met de grens naar het oudere deel.</returns>
    private async Task<SupportMessagePage> ReadAsync(
        string customerId,
        SupportThreadQuery query,
        CancellationToken cancellationToken)
    {
        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        var text = query.OlderThan is null
            ? "SELECT * FROM c WHERE c.kind = @kind ORDER BY c.id DESC"
            : "SELECT * FROM c WHERE c.kind = @kind AND c.id < @before ORDER BY c.id DESC";

        var definition = new QueryDefinition(text)
            .WithParameter("@kind", SupportDocumentKeys.Kind);

        if (query.OlderThan is { } before)
        {
            definition = definition.WithParameter("@before", before);
        }

        // Eén meer dan we teruggeven. Dat ene is het antwoord op "is er meer", en het is goedkoper dan
        // een tweede query of een COUNT over de partitie.
        var wanted = SupportThreadQuery.PageSize;
        var newestFirst = new List<SupportMessageDocument>(wanted + 1);
        var charge = 0d;

        using var iterator = container.GetItemQueryIterator<SupportMessageDocument>(
            definition,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(customerId),
                MaxItemCount = wanted + 1,
            });

        while (iterator.HasMoreResults && newestFirst.Count <= wanted)
        {
            var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            newestFirst.AddRange(response);
            charge += response.RequestCharge;
        }

        logger.LogDebug(
            "Supportdraad van {CustomerId}{Before}: {Count} bericht(en), {Charge} RU.",
            customerId,
            query.OlderThan is null ? string.Empty : $" vóór {query.OlderThan}",
            newestFirst.Count,
            charge);

        var hasMore = newestFirst.Count > wanted;
        var page = hasMore ? newestFirst.Take(wanted).ToList() : newestFirst;

        // Terug naar de leesrichting van een gesprek: oudste eerst. De query leest nieuwste eerst,
        // want het recentste deel is wat je wilt hebben als je niet zegt welk deel.
        page.Reverse();

        return new SupportMessagePage(page, hasMore && page.Count > 0 ? page[0].Id : null);
    }

    /// <summary>
    /// Schrijft één bericht weg.
    /// </summary>
    /// <remarks>
    /// <para><c>CreateItemAsync</c> en geen <c>Upsert</c>. De sleutel is afgeleid van het moment en de
    /// inhoud, dus een tweede verzending van hetzelfde formulier botst hier — en een botsing is precies
    /// het antwoord dat we willen. Met een upsert zou de tweede verzending de eerste overschrijven en
    /// stil slagen.</para>
    ///
    /// <para>De vingerafdruk bevat de afzender. Zonder dat zou een antwoord van de eerstelijn dat
    /// letterlijk gelijk is aan een eerder antwoord binnen dezelfde milliseconde botsen met dat eerdere
    /// bericht, en dat is bij een escalatietekst — die altijd hetzelfde is — geen theoretisch geval.</para>
    /// </remarks>
    private async Task<PortalWriteResult<SupportMessageDocument>> CreateAsync(
        string customerId,
        SupportAuthor author,
        string text,
        string? who,
        SupportGround? ground,
        SupportEscalation? escalation,
        CancellationToken cancellationToken)
    {
        if (text.Length == 0)
        {
            return PortalWriteResult<SupportMessageDocument>.Invalid(
                "Er is geen bericht om vast te leggen: na het schonen bleef er geen tekst over.");
        }

        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();

        var key = SupportDocumentKeys.Id(
            now,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{SupportJsonValues.Of(author)}|{who}|{ground?.Kind}|{ground?.Key}|{escalation}|{text}"));

        var document = new SupportMessageDocument
        {
            Id = key,
            PartitionKey = customerId,
            CustomerId = customerId,
            Author = author,
            Who = who,
            Text = text,
            GroundKind = ground?.Kind,
            GroundKey = ground?.Key,
            Escalation = escalation,
            CreatedAt = now,
        };

        try
        {
            var response = await container
                .CreateItemAsync(
                    document,
                    new PartitionKey(customerId),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Supportbericht {MessageId} van {Author} op klant {CustomerId} vastgelegd; "
                + "{Length} tekens, grondslag {Ground}, escalatie {Escalation}. {Charge} RU.",
                document.Id,
                author,
                customerId,
                text.Length,
                ground?.Label ?? "geen",
                escalation?.ToString() ?? "geen",
                response.RequestCharge);

            return PortalWriteResult<SupportMessageDocument>.Saved(response.Resource);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            return PortalWriteResult<SupportMessageDocument>.Conflict(
                "Dit bericht staat er al. Waarschijnlijk is het formulier twee keer verstuurd; er is "
                + "één bericht vastgelegd en geen twee.",
                current: null);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new PortalDataNotProvisionedException(
                "Het portaal mag niet schrijven in de portaalopslag. De managed identity heeft "
                + "'Cosmos DB Built-in Data Contributor' nodig op de database platform, als "
                + "sqlRoleAssignment op het Cosmos-dataplane. Een Reader-rol laat de supportdraad "
                + "gewoon lezen en pas het versturen falen, en dat is precies wat er nu gebeurt.",
                exception);
        }
    }
}
