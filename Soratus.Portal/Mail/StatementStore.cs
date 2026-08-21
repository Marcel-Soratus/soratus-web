using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Soratus.Portal.Data;
using Soratus.Portal.Security;

namespace Soratus.Portal.Mail;

/// <summary>
/// Welke overgangen een verzendbevestiging mag maken.
/// </summary>
/// <remarks>
/// <para>Eén plek, en zowel het scherm als de schrijfkant gebruikt hem. Dezelfde constructie en
/// dezelfde reden als <c>HourEntryTransitions</c>: zou het scherm zelf bepalen of er een knop hoort
/// te staan, dan staat er een knop die een melding oplevert — of ontbreekt er een bij iets dat wel
/// mag.</para>
/// </remarks>
public static class StatementTransitions
{
    /// <summary>
    /// Waarom er voor deze maand niet (opnieuw) verstuurd mag worden, of <c>null</c> als het mag.
    /// </summary>
    /// <param name="current">De bestaande bevestiging, of <c>null</c> als er nog nooit is verstuurd.</param>
    /// <returns>De reden in het Nederlands, of <c>null</c>.</returns>
    /// <remarks>
    /// <para>Drie gevallen, en het middelste is waar dit ontwerp over gaat.</para>
    /// <list type="bullet">
    ///   <item><description>
    ///     Geen document: er is nooit iets verstuurd. Mag.
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="StatementSendState.Unknown"/>: <strong>mag niet.</strong> Dit is de vaste
    ///     stelregel van dit project — "onbekend of het gelukt is" is een eigen toestand en geen
    ///     reden om het opnieuw te proberen. Een tweede maandoverzicht naar dezelfde klant is erger
    ///     dan een dag later mailen. Eerst vaststellen wat er is gebeurd
    ///     (<see cref="IStatementStore.ReleaseAsync"/>), daarna mag het.
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="StatementSendState.Sent"/>: mag niet. Er is al een overzicht verstuurd.
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="StatementSendState.NotSent"/>: mag. Er is zeker niets verstuurd.
    ///   </description></item>
    /// </list>
    /// </remarks>
    public static string? WhyNotSend(StatementDocument? current) => current?.State switch
    {
        null or StatementSendState.NotSent => null,
        StatementSendState.Sent =>
            "Het maandoverzicht van deze maand is al verstuurd. Er gaat er geen tweede uit; dat zou "
            + "de klant twee overzichten over dezelfde maand geven en die zijn niet terug te halen.",
        StatementSendState.Unknown =>
            "Van deze maand is niet bekend of het overzicht is aangekomen. Het portaal probeert dat "
            + "niet opnieuw: bij een tijdslimiet of een storing kan het bericht wél zijn aangenomen "
            + "en alleen het antwoord zijn weggevallen. Stel eerst vast wat er is gebeurd en leg dat "
            + "vast; daarna kan er opnieuw worden verstuurd.",
        _ => null,
    };

    /// <summary>
    /// Waarom deze bevestiging niet is vrij te geven, of <c>null</c> als het mag.
    /// </summary>
    /// <param name="current">De bestaande bevestiging, of <c>null</c>.</param>
    /// <returns>De reden in het Nederlands, of <c>null</c>.</returns>
    /// <remarks>
    /// Vrijgeven kan alleen vanuit <see cref="StatementSendState.Unknown"/>. Vanuit
    /// <see cref="StatementSendState.Sent"/> zou het een verzonden mail onverzonden verklaren, en dat
    /// is geen vaststelling maar een wens.
    /// </remarks>
    public static string? WhyNotRelease(StatementDocument? current) => current?.State switch
    {
        StatementSendState.Unknown => null,
        null =>
            "Er is over deze maand nooit een verzending gestart, dus er is niets vast te stellen.",
        StatementSendState.Sent =>
            "Dit overzicht is verstuurd. Een verzonden mail is niet achteraf onverzonden te "
            + "verklaren; wat er daarna in het postsysteem van de klant is gebeurd, zien wij niet.",
        StatementSendState.NotSent =>
            "Van deze maand staat al vast dat er niets is verstuurd. Er is niets meer op te lossen.",
        _ => null,
    };
}

/// <summary>
/// Wat er wordt vastgelegd vóórdat er wordt verstuurd.
/// </summary>
/// <param name="Month">De maand als <c>jjjj-MM</c>.</param>
/// <param name="Subject">De onderwerpregel zoals hij de deur uit gaat.</param>
/// <param name="Recipients">De ontvangers.</param>
/// <param name="MeasuredAt">Wanneer de kostenmeting achter deze bedragen is gedaan.</param>
/// <param name="AzureAmount">Het door te belasten Azure-bedrag dat in de mail staat.</param>
/// <param name="ExtraHoursAmount">Het bedrag voor uren boven bundel dat in de mail staat.</param>
/// <param name="Total">Het totaal dat in de mail staat.</param>
/// <remarks>
/// De drie bedragen zijn hier <em>niet</em> nullable, anders dan op
/// <see cref="MonthlyStatementFigures"/>. Dat is opzet: een claim ontstaat alleen bij een mail die
/// werkelijk wordt verstuurd, en die bestaat alleen als alle drie de bedragen bekend zijn. De
/// weigering staat eerder in de keten, in <see cref="StatementMailComposer"/>. Zou dit type de
/// nullables overnemen, dan zou het pad naar een bevestiging met een gat erin bestaan zonder dat
/// iemand hem hoeft te bewandelen.
/// </remarks>
public sealed record StatementClaim(
    string Month,
    string Subject,
    IReadOnlyList<string> Recipients,
    DateTimeOffset MeasuredAt,
    decimal AzureAmount,
    decimal ExtraHoursAmount,
    decimal Total);

/// <summary>
/// Wat een operator heeft vastgesteld over een onbekende uitkomst.
/// </summary>
/// <param name="Month">De maand als <c>jjjj-MM</c>.</param>
/// <param name="Note">Wat hij heeft vastgesteld, in zijn eigen woorden.</param>
/// <param name="BasedOnETag">De etag waarop de vaststelling rust, of <c>null</c>.</param>
public sealed record StatementRelease(string Month, string Note, string? BasedOnETag)
{
    /// <summary>De minimale lengte van de vaststelling.</summary>
    /// <remarks>
    /// Kort genoeg voor "gebeld met Jan, niets ontvangen" en lang genoeg om "ok" te weigeren. Over
    /// een half jaar is dit veld het antwoord op de vraag waarom er twee keer is gemaild.
    /// </remarks>
    public const int MinimumNoteLength = 10;

    /// <summary>De maximale lengte.</summary>
    public const int MaximumNoteLength = 400;

    /// <summary>
    /// Wat er niet klopt aan deze vaststelling, of <c>null</c> als hij klopt.
    /// </summary>
    /// <returns>De melding in het Nederlands, of <c>null</c>.</returns>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Note) || Note.Trim().Length < MinimumNoteLength)
        {
            return "Schrijf op wat je hebt vastgesteld. Dit is de enige plek waar over een half jaar "
                + "staat waarom er over deze maand twee keer — of geen keer — is gemaild.";
        }

        return Note.Trim().Length > MaximumNoteLength
            ? $"Houd de vaststelling onder {MaximumNoteLength} tekens."
            : null;
    }
}

/// <summary>
/// De verzendbevestigingen in de opslag: lezen, claimen, bevestigen en vrijgeven.
/// </summary>
/// <remarks>
/// <para><strong>Elke methode neemt een <see cref="CustomerWriteScope"/>, ook de leesmethoden.</strong>
/// Dat wijkt af van <see cref="IPortalDataStore"/>, waar lezen een <see cref="CustomerScope"/> neemt,
/// en het is hier de juiste vorm: er is geen klantvariant van deze gegevens. Een verzendbevestiging
/// draagt de ontvangers, de onderwerpregel en de vaststelling van een operator, en dat is
/// operatorwerk. Zou er een leesmethode op een klantscope staan, dan bestaat er een pad waarlangs een
/// klant kan zien naar welk adres wij hem hebben gemaild en wat een operator daarover heeft
/// opgeschreven. Wat er niet is kan niet lekken.</para>
///
/// <para><strong>Er is geen methode die een bevestiging verwijdert.</strong> Een verstuurde mail is
/// een feit buiten ons systeem, en een feit waarvan het bewijs te wissen is, is geen bewijs. Een
/// onbekende uitkomst wordt opgelost met <see cref="ReleaseAsync"/> — die schrijft wat er is
/// vastgesteld erbij in plaats van de geschiedenis weg te halen.</para>
/// </remarks>
public interface IStatementStore
{
    /// <summary>
    /// De bevestiging van één maand, of <c>null</c> als er nooit iets is verstuurd.
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant. Levert de partitiesleutel.</param>
    /// <param name="month">De maand als <c>jjjj-MM</c>.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De bevestiging, of <c>null</c>.</returns>
    /// <remarks>
    /// <c>null</c> is een gewone uitkomst met een eigen betekenis: er is nooit een poging gedaan.
    /// Punt 2 van de fase-0-afwijkingen — geen document betekent geen status — en de reden dat
    /// <see cref="StatementSendState"/> geen waarde <c>NotAttempted</c> heeft.
    /// </remarks>
    Task<StatementDocument?> GetAsync(
        CustomerWriteScope scope,
        string month,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// De bevestigingen van één jaar, nieuwste maand eerst.
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="year">Het jaar.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De bevestigingen. Leeg als er dat jaar niets is verstuurd.</returns>
    Task<IReadOnlyList<StatementDocument>> ListAsync(
        CustomerWriteScope scope,
        int year,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Legt vast dat er wordt verstuurd, vóórdat er wordt verstuurd.
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="claim">Wat er wordt verstuurd.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// De claim met zijn etag, of een conflict met de bestaande bevestiging erbij, of een melding
    /// waarom deze maand niet (opnieuw) verstuurd mag worden.
    /// </returns>
    /// <remarks>
    /// <para><strong>De claim gaat vóór de mail. Nooit andersom.</strong> Dat is dezelfde volgorde
    /// als §6 van het haalbaarheidsrapport voor de conceptfactuur voorschrijft, en om dezelfde reden:
    /// dit is precies de volgorde waarin een dubbele verzending níet ontstaat. De sleutel is
    /// afgeleid van de maand, dus een tweede poging botst hier — bij Cosmos, met een <c>409</c>,
    /// vóórdat er een netwerkverbinding met Communication Services is opgezet.</para>
    ///
    /// <para>Wat de claim kost: valt het proces om tussen de claim en het bevestigen, dan staat er
    /// een bevestiging op <see cref="StatementSendState.Unknown"/> terwijl er misschien niets is
    /// verstuurd. Dat is de goede kant om fout te zitten. De andere volgorde — eerst versturen, dan
    /// vastleggen — laat bij dezelfde storing een verstuurde mail zonder enig spoor achter, en dan
    /// verstuurt de volgende poging er een tweede.</para>
    /// </remarks>
    Task<PortalWriteResult<StatementDocument>> ClaimAsync(
        CustomerWriteScope scope,
        StatementClaim claim,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Legt de uitkomst van de verzending vast op de claim.
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="month">De maand als <c>jjjj-MM</c>.</param>
    /// <param name="delivery">Hoe het is afgelopen.</param>
    /// <param name="operationId">De operatie-id, of <c>null</c>.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De bijgewerkte bevestiging.</returns>
    /// <remarks>
    /// <see cref="MailDelivery.Unknown"/> hoeft niets te schrijven — de claim staat al op
    /// <see cref="StatementSendState.Unknown"/> — en toch neemt deze methode die waarde aan. Dat is
    /// opzet: de aanroeper hoort niet te hoeven weten dat "niets doen" hier de juiste handeling is.
    /// Zou hij dat wél moeten weten, dan is er een pad waarin hij het vergeet en er iets anders
    /// gebeurt.
    /// </remarks>
    Task<PortalWriteResult<StatementDocument>> ConfirmAsync(
        CustomerWriteScope scope,
        string month,
        MailDelivery delivery,
        string? operationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Legt vast wat een mens heeft vastgesteld over een onbekende uitkomst.
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="release">De vaststelling.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De bijgewerkte bevestiging, of een melding waarom dit niet kan.</returns>
    /// <remarks>
    /// <para><strong>Dit is de enige uitgang uit <see cref="StatementSendState.Unknown"/>, en er
    /// staat met opzet een mens in.</strong> Er is geen manier waarop een programma kan vaststellen
    /// of een mail is aangekomen — Communication Services weet het niet, wij hebben geen
    /// leesrechten op de postbus van de klant, en een tweede mail sturen om het te vragen is precies
    /// wat we wilden vermijden. De enige beschikbare bron is iemand die het navraagt.</para>
    ///
    /// <para>Dezelfde vorm als de toestand <c>abandoned</c> in §6 van het haalbaarheidsrapport: een
    /// mens stelt vast dat er geen order is, en pas daarna mag het opnieuw.</para>
    /// </remarks>
    Task<PortalWriteResult<StatementDocument>> ReleaseAsync(
        CustomerWriteScope scope,
        StatementRelease release,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// De enige implementatie van <see cref="IStatementStore"/>: verzendbevestigingen in de container
/// <c>customers</c>, op de partitiesleutel van de klant.
/// </summary>
/// <remarks>
/// <para>Dezelfde vorm als <c>CosmosPortalHoursStore</c>, en met dezelfde twee eigenschappen die
/// daar met een meting zijn onderbouwd. Elke query loopt binnen één partitiesleutel, en die sleutel
/// komt uit de scope — er is dus geen aanroep waarmee je met de scope van klant A de bevestigingen
/// van klant B leest. En de maandgrens is een tekstvergelijking op <c>jjjj-MM</c>, wat werkt omdat
/// de opslagvorm vast is (punt 7).</para>
/// </remarks>
internal sealed class CosmosStatementStore(
    CosmosContainerProvider containers,
    IOptions<PortalDataOptions> options,
    TimeProvider timeProvider,
    ILogger<CosmosStatementStore> logger) : IStatementStore
{
    private readonly PortalDataOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<StatementDocument?> GetAsync(
        CustomerWriteScope scope,
        string month,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(month);

        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        return await PointReadAsync(container, scope.CustomerId, month, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StatementDocument>> ListAsync(
        CustomerWriteScope scope,
        int year,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        var definition = new QueryDefinition(
                "SELECT * FROM c WHERE c.kind = @kind AND c.month >= @from AND c.month <= @to")
            .WithParameter("@kind", StatementDocumentKeys.Kind)
            .WithParameter("@from", $"{year:D4}-01")
            .WithParameter("@to", $"{year:D4}-12");

        var results = new List<StatementDocument>();

        using var iterator = container.GetItemQueryIterator<StatementDocument>(
            definition,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(scope.CustomerId),
            });

        while (iterator.HasMoreResults)
        {
            results.AddRange(await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false));
        }

        // Sorteren in het geheugen en niet met ORDER BY, om dezelfde reden als bij de urenregels: het
        // gaat om ten hoogste twaalf documenten van één klant, en een ORDER BY vraagt een index die in
        // Bicep staat — dus zou een sortering hier stilzwijgend een uitrol vereisen die niemand
        // aanvraagt. Op month en niet op een tijdstempel: een maand zonder verzending heeft geen
        // tijdstempel, en de maand is wat de lezer zoekt.
        return [.. results.OrderByDescending(document => document.Month, StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public async Task<PortalWriteResult<StatementDocument>> ClaimAsync(
        CustomerWriteScope scope,
        StatementClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(claim);

        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();

        var document = new StatementDocument
        {
            Id = StatementDocumentKeys.Id(claim.Month),
            PartitionKey = scope.CustomerId,
            CustomerId = scope.CustomerId,
            Month = claim.Month,

            // De claim staat op Unknown en niet op iets als "bezig". Zie StatementSendState.Unknown:
            // het verschil tussen "loopt nog" en "onbekend" is alleen door de tijd te bepalen, en een
            // proces dat halverwege omvalt laat "loopt nog" staan.
            State = StatementSendState.Unknown,
            AttemptedAt = now,
            AttemptedBy = scope.Actor,
            Recipients = claim.Recipients,
            Subject = claim.Subject,
            MeasuredAt = claim.MeasuredAt,
            AzureAmount = claim.AzureAmount,
            ExtraHoursAmount = claim.ExtraHoursAmount,
            Total = claim.Total,
            Attempts = 1,
        };

        try
        {
            // CreateItemAsync en geen Upsert. Dit is het slot: bestaat het document al, dan komt hier
            // een 409 en is er niets verstuurd. Met een upsert zou de tweede poging de eerste
            // overschrijven en stil slagen — en dan staat er één bevestiging bij twee mails.
            var response = await container
                .CreateItemAsync(
                    document,
                    new PartitionKey(scope.CustomerId),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Verzending van het maandoverzicht {Month} van klant {CustomerId} geclaimd door " +
                "{Actor}, {Count} ontvanger(s). {Charge} RU.",
                claim.Month,
                scope.CustomerId,
                scope.Actor,
                claim.Recipients.Count,
                response.RequestCharge);

            return PortalWriteResult<StatementDocument>.Saved(response.Resource);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            return await ReclaimAsync(container, scope, document, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            throw WriteForbidden(exception);
        }
    }

    /// <inheritdoc />
    public async Task<PortalWriteResult<StatementDocument>> ConfirmAsync(
        CustomerWriteScope scope,
        string month,
        MailDelivery delivery,
        string? operationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(month);

        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        var current = await PointReadAsync(container, scope.CustomerId, month, cancellationToken)
            .ConfigureAwait(false);

        if (current is null)
        {
            // De claim is er niet meer. Dat kan alleen als iemand hem buiten het portaal heeft
            // weggehaald, en dan is er een mail verstuurd waarvan het bewijs weg is. Dat is een
            // conflict en geen succes.
            return PortalWriteResult<StatementDocument>.Conflict(
                $"De verzendbevestiging van {month} bestaat niet meer bij {scope.DisplayName}. Er is "
                + "mogelijk wél gemaild; controleer dat vóór er opnieuw wordt verstuurd.",
                current: null);
        }

        var now = timeProvider.GetUtcNow();

        var updated = delivery switch
        {
            MailDelivery.Accepted => current with
            {
                State = StatementSendState.Sent,
                SentAt = now,
                OperationId = operationId,
                Refusal = StatementRefusal.None,
            },
            MailDelivery.Refused => current with
            {
                State = StatementSendState.NotSent,
                SentAt = null,
                OperationId = null,
                Refusal = StatementRefusal.Rejected,
            },

            // Onbekend: de claim staat al goed. Niets schrijven is hier de juiste handeling, en een
            // schrijfactie zou het moment van de claim verplaatsen — het enige moment dat er is.
            _ => current,
        };

        if (ReferenceEquals(updated, current))
        {
            logger.LogWarning(
                "De uitkomst van het maandoverzicht {Month} van klant {CustomerId} is onbekend. De " +
                "bevestiging blijft op onbekend staan en er wordt niets opnieuw geprobeerd.",
                month,
                scope.CustomerId);

            return PortalWriteResult<StatementDocument>.Saved(current);
        }

        return await ReplaceAsync(container, scope, updated, current, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PortalWriteResult<StatementDocument>> ReleaseAsync(
        CustomerWriteScope scope,
        StatementRelease release,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(release);

        if (release.Validate() is { } invalid)
        {
            return PortalWriteResult<StatementDocument>.Invalid(invalid);
        }

        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        var current = await PointReadAsync(container, scope.CustomerId, release.Month, cancellationToken)
            .ConfigureAwait(false);

        // De overgang vóór de etag, dezelfde volgorde en dezelfde reden als bij het fiatteren van een
        // urenregel: "dit overzicht is al verstuurd" is een preciezere mededeling dan "iemand anders
        // was eerder", ook al is de oorzaak dezelfde.
        if (StatementTransitions.WhyNotRelease(current) is { } refused)
        {
            return PortalWriteResult<StatementDocument>.Invalid(refused);
        }

        var now = timeProvider.GetUtcNow();

        var updated = current! with
        {
            State = StatementSendState.NotSent,
            ReleasedAt = now,
            ReleasedBy = scope.Actor,
            ReleaseNote = release.Note.Trim(),
        };

        return await ReplaceAsync(
                container,
                scope,
                updated,
                current,
                cancellationToken,
                release.BasedOnETag)
            .ConfigureAwait(false);
    }

    // ── Binnenwerk ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Een tweede poging op een maand die al een bevestiging heeft.
    /// </summary>
    /// <remarks>
    /// <para>Alleen vanuit <see cref="StatementSendState.NotSent"/>, en dat is de enige plek waar die
    /// beperking hoeft te staan: de <c>409</c> hierboven is het slot, en dit is de sleutel. Staat de
    /// bevestiging op <see cref="StatementSendState.Sent"/> of
    /// <see cref="StatementSendState.Unknown"/>, dan komt hier een melding terug en is er niets
    /// verstuurd.</para>
    ///
    /// <para>De vervanging gaat met <c>If-Match</c> op de etag die we net hebben gelezen. Zonder die
    /// voorwaarde zouden twee operators die tegelijk op "opnieuw versturen" drukken elk hun claim
    /// zetten, en dan gaan er twee mails uit met één bevestiging.</para>
    /// </remarks>
    private async Task<PortalWriteResult<StatementDocument>> ReclaimAsync(
        Container container,
        CustomerWriteScope scope,
        StatementDocument claim,
        CancellationToken cancellationToken)
    {
        var current = await PointReadAsync(container, scope.CustomerId, claim.Month, cancellationToken)
            .ConfigureAwait(false);

        if (StatementTransitions.WhyNotSend(current) is { } refused)
        {
            return PortalWriteResult<StatementDocument>.Conflict(refused, current);
        }

        var next = claim with
        {
            Attempts = (current?.Attempts ?? 0) + 1,

            // De vaststelling van de vorige poging blijft staan. Dat is de audittrail: over een half
            // jaar is dit het antwoord op de vraag waarom er over deze maand twee keer is gemaild.
            ReleasedAt = current?.ReleasedAt,
            ReleasedBy = current?.ReleasedBy,
            ReleaseNote = current?.ReleaseNote,
        };

        return await ReplaceAsync(
                container,
                scope,
                next,
                current!,
                cancellationToken,
                current!.ETag)
            .ConfigureAwait(false);
    }

    private async Task<PortalWriteResult<StatementDocument>> ReplaceAsync(
        Container container,
        CustomerWriteScope scope,
        StatementDocument updated,
        StatementDocument current,
        CancellationToken cancellationToken,
        string? basedOnETag = null)
    {
        try
        {
            var response = await container
                .ReplaceItemAsync(
                    updated,
                    updated.Id,
                    new PartitionKey(scope.CustomerId),
                    basedOnETag is null ? null : new ItemRequestOptions { IfMatchEtag = basedOnETag },
                    cancellationToken)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Verzendbevestiging {Id} van klant {CustomerId} staat nu op {State} (was {Previous}), " +
                "poging {Attempts}. {Charge} RU.",
                updated.Id,
                scope.CustomerId,
                updated.State,
                current.State,
                updated.Attempts,
                response.RequestCharge);

            return PortalWriteResult<StatementDocument>.Saved(response.Resource);
        }
        catch (CosmosException exception) when (
            exception.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
        {
            var fresh = await PointReadAsync(container, scope.CustomerId, updated.Month, cancellationToken)
                .ConfigureAwait(false);

            return PortalWriteResult<StatementDocument>.Conflict(
                "Iemand anders was net eerder bij het maandoverzicht van deze maand. Er is niets "
                + "gewijzigd en er is niets verstuurd; bekijk wat er nu staat.",
                fresh);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            throw WriteForbidden(exception);
        }
    }

    private static async Task<StatementDocument?> PointReadAsync(
        Container container,
        string customerId,
        string month,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await container
                .ReadItemAsync<StatementDocument>(
                    StatementDocumentKeys.Id(month),
                    new PartitionKey(customerId),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return response.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<Container> ContainerAsync(CancellationToken cancellationToken)
    {
        var location = _options.Location()
            ?? throw new PortalDataNotProvisionedException(
                "De portaalopslag is niet ingericht: PortalData:AccountEndpoint is leeg. "
                + "Verzendbevestigingen zijn daarmee niet te lezen of te schrijven, en zonder "
                + "bevestiging hoort er niet gemaild te worden — dan is niet te zien of een klant "
                + "zijn overzicht al heeft gehad, en de tweede poging levert een tweede mail op.");

        return await containers.CustomersAsync(location, cancellationToken).ConfigureAwait(false);
    }

    private static PortalDataNotProvisionedException WriteForbidden(CosmosException exception) =>
        new(
            "Het portaal mag niet schrijven in de container 'customers'. Verzendbevestigingen vragen "
            + "hetzelfde recht als klanten, contracten en urenregels: de rol 'Cosmos DB Built-in Data "
            + "Contributor' op de database, niet 'Data Reader'. Een leesrol geeft een 403 op de eerste "
            + "schrijfpoging en niet eerder.",
            exception);
}
