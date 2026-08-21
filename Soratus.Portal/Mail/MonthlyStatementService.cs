using Microsoft.Extensions.Options;
using Soratus.Portal.Data;
using Soratus.Portal.Security;
using Soratus.Portal.Views;

namespace Soratus.Portal.Mail;

/// <summary>
/// Hoe een verzendpoging is afgelopen, voor het scherm.
/// </summary>
/// <remarks>
/// Zes uitkomsten en niet twee. Dezelfde ordening als de vijf uitkomsten van de MCP-server: het
/// verschil tussen "niet verstuurd" en "onbekend of er verstuurd is" is de duurste van dit hele
/// ontwerp, en een aanroeper die alleen naar "gelukt ja/nee" kijkt, kan hem niet zien.
/// </remarks>
public enum StatementOutcomeKind
{
    /// <summary>Proefdraai: er is opgemaakt en niets verstuurd.</summary>
    DryRun,

    /// <summary>Verstuurd. Communication Services heeft het bericht aangenomen.</summary>
    Sent,

    /// <summary>Niet verstuurd, en dat staat vast.</summary>
    NotSent,

    /// <summary>Onbekend of het is aangekomen. Er wordt niets opnieuw geprobeerd.</summary>
    Unknown,

    /// <summary>Geweigerd vóór er iets is gebeurd. Er is niets vastgelegd en niets verstuurd.</summary>
    Refused,

    /// <summary>Tegengehouden door wat er al over deze maand is vastgelegd.</summary>
    Blocked,
}

/// <summary>
/// De uitkomst van een verzendpoging, zoals het scherm hem toont.
/// </summary>
/// <remarks>
/// <para><strong>De opgemaakte mail zit erin bij een proefdraai en bij niets anders.</strong> Dat is
/// geen zuinigheid: bij een echte verzending is de mail al de deur uit en zou hem hier meesturen
/// suggereren dat er nog iets te bekijken valt. Bij een proefdraai is de mail juist het enige dat er
/// is, en de operator hoort hem letterlijk te zien — dezelfde tekst die anders zou zijn verstuurd,
/// zonder markering en zonder aanpassing, want een proefdraai die iets anders toont dan hij zou
/// versturen bewijst niets.</para>
/// </remarks>
public sealed record StatementResult
{
    /// <summary>Hoe het is afgelopen.</summary>
    public required StatementOutcomeKind Kind { get; init; }

    /// <summary>De melding voor het scherm, in het Nederlands. Altijd gevuld.</summary>
    public required string Message { get; init; }

    /// <summary>De reden van een weigering, of <see cref="StatementRefusal.None"/>.</summary>
    public StatementRefusal Refusal { get; init; } = StatementRefusal.None;

    /// <summary>De vastgelegde bevestiging, of <c>null</c> bij een weigering of een proefdraai.</summary>
    public StatementDocument? Confirmation { get; init; }

    /// <summary>De opgemaakte mail. Alleen bij <see cref="StatementOutcomeKind.DryRun"/>.</summary>
    public StatementMail? Preview { get; init; }
}

/// <summary>
/// Verstuurt het maandoverzicht van één klant over één maand, en legt vast wat er is gebeurd.
/// </summary>
/// <remarks>
/// <para><strong>De volgorde van deze methode is het ontwerp.</strong> Elke stap die kan weigeren
/// staat vóór de claim, en de claim staat vóór de mail. Daardoor is elke weigering spoorloos — er
/// staat geen halve bevestiging in de opslag — en is elke mail geclaimd. Draai die twee om en je
/// hebt het patroon waarmee een dubbele mail ontstaat: verstuurd, antwoord verloren, niets
/// vastgelegd, volgende poging verstuurt er een tweede.</para>
///
/// <para><strong>Er zit geen herhaling in deze klasse.</strong> Geen <c>retry</c>, geen backoff, geen
/// tweede poging bij een tijdslimiet. Dat is de vaste stelregel van dit project: "onbekend of het
/// gelukt is" is een eigen toestand en geen reden om het opnieuw te proberen. Voor een urenboeking
/// is dezelfde afweging gemaakt (<c>docs/agent-portal/mcp-uren.md</c>) en voor een conceptfactuur ook
/// (§6 van het haalbaarheidsrapport). Een dubbele mail naar een klant is erger dan een dag later
/// mailen.</para>
///
/// <para><strong>Deze klasse rekent niet.</strong> De bedragen komen uit
/// <see cref="IMonthlyStatementFigures"/> en worden doorgegeven zoals ze zijn. Er staat een test op
/// dat er in deze map nergens met een bedrag wordt gerekend.</para>
///
/// <para><strong>Een concrete klasse en geen interface, en dat is opzet — draai het niet terug.</strong>
/// Elke andere laag in dit portaal zit achter een interface, dus deze klasse ziet eruit als een
/// vergeten geval. Ze is het niet. Een interface met één implementatie bestaat om de implementatie te
/// kunnen vervangen, en juist dat mag hier niet gebeuren: wat er in deze klasse te meten valt <em>is</em>
/// de volgorde — weigeren vóór claimen, claimen vóór versturen, versturen vóór vastleggen. Een test
/// die deze klasse vervangt, meet zijn eigen kopie van die volgorde. De tests vervangen daarom de drie
/// afhankelijkheden (<see cref="IMonthlyStatementFigures"/>, <see cref="IStatementStore"/>,
/// <see cref="IMailOutbox"/>) en laten deze klasse staan.</para>
///
/// <para><strong>Deze klasse verstuurt niet zelf.</strong> Het versturen zit in
/// <see cref="IMailOutbox"/>, samen met de indeling van de uitkomsten en de proefdraaimodus — en de
/// storingsmelder van fase 6 gebruikt dezelfde laag. Wat hier staat is wat er om een verzending heen
/// gebeurt en per doel verschilt: weigeren, claimen, vastleggen.</para>
///
/// <para>Wat de keuze kost: een toekomstige tweede verzendstroom — een agent die op de 1e automatisch
/// mailt — moet hier langs en kan er niet naast. Dat is precies de bedoeling. Een tweede pad naar
/// buiten met een eigen volgorde is een tweede pad naar een dubbele mail.</para>
/// </remarks>
public sealed class MonthlyStatementService(
    IOptions<PortalMailOptions> options,
    IMonthlyStatementFigures figures,
    IPortalDataStore data,
    IStatementStore statements,
    IMailOutbox outbox,
    TimeProvider timeProvider,
    ILogger<MonthlyStatementService> logger)
{
    private readonly PortalMailOptions _options = options.Value;

    /// <summary>
    /// Verstuurt het maandoverzicht, of zegt waarom niet.
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="month">De maand als <c>jjjj-MM</c>.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De uitkomst. Werpt niet, behalve bij een inrichtingsfout in de opslag.</returns>
    public async Task<StatementResult> SendAsync(
        CustomerWriteScope scope,
        string month,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        // ── 1. Is dit een afgesloten maand ───────────────────────────────────────────────────────
        // Vóór alles, want het is de goedkoopste controle en de enige die niets hoeft te lezen. De
        // maand komt uit een adresbalk, dus onleesbaar is een gewoon geval en geen fout.
        if (!IsClosedMonth(month))
        {
            return Refused(StatementRefusal.MonthNotClosed);
        }

        // ── 2. Is mailen ingericht ───────────────────────────────────────────────────────────────
        // De stand wordt één keer gelezen en daarna niet opnieuw. Zou hij twee keer worden opgevraagd
        // — hier en bij stap 6 — dan kan een verversing van de configuratie er tussenin vallen en
        // wordt er geclaimd op een stand die niet meer geldt.
        var outboxState = outbox.State;

        if (outboxState == MailOutboxState.NotConfigured)
        {
            return Refused(StatementRefusal.MailNotConfigured);
        }

        // ── 3. Wat staat er al over deze maand ───────────────────────────────────────────────────
        // Dit is een controle en geen slot — het slot is de 409 op de claim in stap 6. Hij staat er
        // omdat "dit overzicht is al verstuurd" een preciezere mededeling is dan een conflict, en
        // omdat een operator dan niet eerst een mail laat opmaken die toch niet weggaat.
        var existing = await statements.GetAsync(scope, month, cancellationToken).ConfigureAwait(false);

        if (StatementTransitions.WhyNotSend(existing) is { } blocked)
        {
            return new StatementResult
            {
                Kind = StatementOutcomeKind.Blocked,
                Message = blocked,
                Confirmation = existing,
            };
        }

        // ── 4. De bedragen en de ontvangers ──────────────────────────────────────────────────────
        var amounts = await figures.BuildStatementAsync(scope, month, cancellationToken)
            .ConfigureAwait(false);

        var access = await data.GetAccessAsync(scope, cancellationToken).ConfigureAwait(false);
        var (addressing, refusal) = StatementRecipients.Resolve(access);

        if (addressing is null)
        {
            return Refused(refusal);
        }

        // ── 5. Opmaken, of weigeren ──────────────────────────────────────────────────────────────
        var composition = StatementMailComposer.Compose(
            scope.DisplayName,
            amounts,
            addressing,
            _options.PortalBaseUri);

        if (composition.Mail is not { } mail)
        {
            logger.LogWarning(
                "Het maandoverzicht {Month} van klant {CustomerId} is niet opgemaakt: {Refusal}. Er " +
                "is niets verstuurd en niets vastgelegd.",
                month,
                scope.CustomerId,
                composition.Refusal);

            return Refused(composition.Refusal);
        }

        // ── 6. Proefdraai: opmaken, tonen, niets doen ────────────────────────────────────────────
        // Vóór de claim en niet erna. Een proefdraai die een document achterlaat is geen proefdraai:
        // dan staat er een bevestiging bij een mail die nooit is verstuurd.
        if (outboxState == MailOutboxState.DryRun)
        {
            logger.LogInformation(
                "Proefdraai: het maandoverzicht {Month} van klant {CustomerId} is opgemaakt voor " +
                "{Count} ontvanger(s). Er is niets verstuurd en niets vastgelegd.",
                month,
                scope.CustomerId,
                mail.Recipients.Count);

            return new StatementResult
            {
                Kind = StatementOutcomeKind.DryRun,
                Message =
                    "PROEFDRAAI — er is NIETS verstuurd en niets vastgelegd. Hieronder staat "
                    + "letterlijk de mail die zou zijn verstuurd, en naar wie.",
                Preview = mail,
            };
        }

        // ── 7. Claimen. Vóór de mail, nooit erna ─────────────────────────────────────────────────
        var claim = await statements.ClaimAsync(
                scope,
                new StatementClaim(
                    month,
                    mail.Subject,
                    mail.Recipients,
                    amounts!.MeasuredAt,
                    amounts.AzureAmount!.Value,
                    amounts.ExtraHoursAmount!.Value,
                    amounts.Total!.Value),
                cancellationToken)
            .ConfigureAwait(false);

        if (!claim.IsSaved)
        {
            // Niet geclaimd betekent niet verstuurd. Dit is het enige pad waarop een tweede
            // gelijktijdige poging uitkomt, en hij eindigt hier — niet bij Communication Services.
            return new StatementResult
            {
                Kind = StatementOutcomeKind.Blocked,
                Message = claim.Message
                    ?? "Er is over deze maand al een verzending vastgelegd. Er is niets verstuurd.",
                Confirmation = claim.Current,
            };
        }

        // ── 8. Versturen ─────────────────────────────────────────────────────────────────────────
        var send = await outbox.SendAsync(mail, cancellationToken).ConfigureAwait(false);

        // ── 9. De uitkomst vastleggen ────────────────────────────────────────────────────────────
        var confirmed = await statements
            .ConfirmAsync(scope, month, send.Delivery, send.OperationId, cancellationToken)
            .ConfigureAwait(false);

        return send.Delivery switch
        {
            MailDelivery.Accepted => new StatementResult
            {
                Kind = StatementOutcomeKind.Sent,
                Message =
                    $"Het maandoverzicht van {HourMonths.Label(month)} is verstuurd naar "
                    + $"{mail.Recipients.Count} ontvanger(s). "
                    + StatementText.StateNotice(StatementSendState.Sent),
                Confirmation = confirmed.Value ?? claim.Value,
            },

            MailDelivery.Refused => new StatementResult
            {
                Kind = StatementOutcomeKind.NotSent,
                Message = StatementText.Refusal(StatementRefusal.Rejected),
                Refusal = StatementRefusal.Rejected,
                Confirmation = confirmed.Value ?? claim.Value,
            },

            // Onbekend. De bevestiging blijft op onbekend staan en er wordt niets opnieuw geprobeerd.
            _ => new StatementResult
            {
                Kind = StatementOutcomeKind.Unknown,
                Message =
                    $"ONBEKEND of het maandoverzicht van {HourMonths.Label(month)} is verstuurd. "
                    + StatementText.StateNotice(StatementSendState.Unknown),
                Confirmation = confirmed.Value ?? claim.Value,
            },
        };
    }

    /// <summary>
    /// Of dit een maand is die voorbij is.
    /// </summary>
    /// <param name="month">De tekst uit het adres.</param>
    /// <returns><c>true</c> als het een maand is en die maand achter ons ligt.</returns>
    /// <remarks>
    /// <para>Twee dingen in één controle, en dat is hier juist: een onleesbare maand en een lopende
    /// maand leveren dezelfde weigering op, want in beide gevallen is er geen afgesloten maand om
    /// een overzicht van te maken. Een aparte melding voor "dit is geen maand" zou een operator iets
    /// vertellen over de adresbalk in plaats van over zijn klant.</para>
    ///
    /// <para>De vergelijking loopt over de Nederlandse maand en niet over UTC. Dezelfde zone als het
    /// urenscherm gebruikt (<see cref="PortalTimeZone.Display"/>): zouden die twee verschillen, dan
    /// is op 1 augustus tussen middernacht en twee uur 's nachts juli op het ene scherm afgesloten
    /// en op het andere niet.</para>
    /// </remarks>
    private bool IsClosedMonth(string month)
    {
        if (HourMonths.Parse(month) is null)
        {
            return false;
        }

        return string.CompareOrdinal(month, StatementText.MonthOf(NowInDisplayZone())) < 0;
    }

    /// <summary>Nu, omgezet naar de Nederlandse zone.</summary>
    /// <remarks>
    /// <see cref="StatementText.MonthOf"/> neemt een <see cref="DateTimeOffset"/> en
    /// <see cref="HourMonths.Of(DateTimeOffset)"/> leest daar de kalendermaand van af; de omzetting
    /// hoort dus híer te gebeuren en niet daar, anders is de zone een aanname van de opmaakfunctie.
    /// </remarks>
    private DateTimeOffset NowInDisplayZone() =>
        TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), PortalTimeZone.Display);

    private static StatementResult Refused(StatementRefusal refusal) => new()
    {
        Kind = StatementOutcomeKind.Refused,
        Message = StatementText.Refusal(refusal),
        Refusal = refusal,
    };
}
