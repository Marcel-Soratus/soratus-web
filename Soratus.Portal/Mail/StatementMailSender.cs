using Azure;
using Azure.Communication.Email;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace Soratus.Portal.Mail;

/// <summary>
/// Hoe een verzendpoging is afgelopen.
/// </summary>
/// <remarks>
/// <para><strong>Drie uitkomsten, en de derde is de reden dat dit geen <c>bool</c> is.</strong>
/// Dezelfde afweging als bij <see cref="StatementSendState"/> en bij <c>recorded</c> in de
/// MCP-server: bij een tijdslimiet of een <c>5xx</c> kan het bericht zijn aangenomen en alleen het
/// antwoord zijn weggevallen.</para>
/// </remarks>
public enum MailDelivery
{
    /// <summary>Niet vast te stellen of het bericht is aangenomen.</summary>
    /// <remarks>
    /// De eerste waarde, om dezelfde reden als bij <see cref="StatementSendState.Unknown"/>: de
    /// standaardwaarde van een niet-gezette enum hoort de veilige te zijn.
    /// </remarks>
    Unknown,

    /// <summary>Communication Services heeft het bericht aangenomen.</summary>
    Accepted,

    /// <summary>Communication Services heeft het bericht geweigerd. Er is zeker niets verstuurd.</summary>
    Refused,
}

/// <summary>
/// De uitkomst van één verzendpoging.
/// </summary>
/// <param name="Delivery">Hoe het is afgelopen.</param>
/// <param name="OperationId">
/// De operatie-id van Communication Services bij <see cref="MailDelivery.Accepted"/>, anders
/// <c>null</c>. Dit is het enige bewijs buiten ons systeem.
/// </param>
/// <remarks>
/// <para><strong>Er zit geen foutmelding op dit type, en dat is geen omissie.</strong> Een
/// <c>Exception.Message</c> van een dienstverlener is de vorm van tekst die de punten 13 en 14 van
/// de fase-0-afwijkingen twee keer uit een klantoppervlak hebben moeten halen — en dit type reist
/// naar het scherm en naar de verzendbevestiging. Wat er wél gebeurt: de melding gaat naar de
/// logregel, met de <c>ErrorCode</c> en de status erbij, en het scherm zegt dát het is geweigerd en
/// waar de reden te vinden is. Een operator die de reden nodig heeft, heeft een logregel; een
/// klantnaam boven een stacktrace helpt niemand.</para>
/// </remarks>
public sealed record StatementSendResult(MailDelivery Delivery, string? OperationId);

/// <summary>
/// Verstuurt één opgemaakt maandoverzicht.
/// </summary>
/// <remarks>
/// Een eigen interface met precies één methode, zodat de tests het verzendpad kunnen uitoefenen
/// zonder een mail te versturen. Dat is hier geen luxe: de drie uitkomsten hierboven zijn de kern
/// van dit ontwerp, en de enige manier om ze alle drie te beproeven is een dubbel die ze kan
/// opleveren. Een echte verzending kan er per definitie maar één van tonen.
/// </remarks>
public interface IStatementMailSender
{
    /// <summary>
    /// Verstuurt het maandoverzicht.
    /// </summary>
    /// <param name="sender">De afzender. Zijn bestaan is het bewijs dat mailen is ingericht.</param>
    /// <param name="mail">De opgemaakte mail.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De uitkomst. Werpt niet.</returns>
    /// <remarks>
    /// <para><strong>Deze methode werpt niet, en dat is een eis en geen gemak.</strong> Een
    /// uitzondering die langs de aanroeper naar boven gaat, laat de claim in de opslag op
    /// <see cref="StatementSendState.Unknown"/> staan zonder dat er iets is vastgelegd over de
    /// oorzaak — en dan is er geen verschil meer tussen "geweigerd, dus zeker niets verstuurd" en
    /// "niet vast te stellen". Dat verschil is het hele punt: het eerste mag opnieuw, het tweede
    /// niet.</para>
    /// </remarks>
    Task<StatementSendResult> SendAsync(
        MailSender sender,
        StatementMail mail,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Verstuurt het maandoverzicht met Azure Communication Services Email, op de managed identity van
/// het portaal.
/// </summary>
/// <remarks>
/// <para><strong>Dit pad volgt <c>Soratus.Web/Services/LeadSink.cs</c> en wijkt op één punt
/// af.</strong> De marketingsite verstuurt de terugbelaanvragen met dezelfde bibliotheek
/// (<c>EmailClient</c>, <c>EmailMessage</c>, <c>WaitUntil.Started</c>) en dat pad loopt vandaag in
/// productie; dat is de reden dat het hier wordt gevolgd en niet opnieuw uitgedacht. Het verschil is
/// de aanmelding: de site gebruikt een connection string uit een platte app-setting, dit portaal een
/// <see cref="TokenCredential"/>. Zie <see cref="PortalMailOptions"/> voor waarom, en waarom de
/// bijbehorende rol een custom role is en geen <c>Contributor</c>.</para>
///
/// <para><strong><c>WaitUntil.Started</c> en niet <c>Completed</c>.</strong> Overgenomen van
/// <c>LeadSink</c>, en hier met een eigen reden: dit gebeurt tijdens een <c>POST</c> van een
/// operator die op een antwoord wacht. Wachten tot Communication Services klaar is, betekent
/// pollen — en een <c>POST</c> die dertig seconden hangt, wordt door de operator opnieuw ingediend.
/// Dat is precies het gedrag dat een dubbele mail oplevert. Wat we ervoor betalen: "aangenomen" is
/// niet "afgeleverd", en dat staat er ook zo op het scherm.</para>
///
/// <para><strong>De <see cref="EmailClient"/> wordt per verzending gemaakt en niet gecachet.</strong>
/// Anders dan bij <c>CosmosClientCache</c>, waar het cachen gemeten winst oplevert. Hier gaat het om
/// één bericht per klant per maand: een client die maandenlang blijft staan voor een handeling die
/// per klant één keer per maand gebeurt, is een verbinding die vaker verloopt dan hij wordt
/// gebruikt.</para>
/// </remarks>
internal sealed class AcsStatementMailSender(
    TokenCredential credential,
    ILogger<AcsStatementMailSender> logger) : IStatementMailSender
{
    /// <inheritdoc />
    public async Task<StatementSendResult> SendAsync(
        MailSender sender,
        StatementMail mail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(mail);

        var message = new EmailMessage(
            senderAddress: sender.FromAddress,
            content: new EmailContent(mail.Subject)
            {
                PlainText = mail.PlainText,
                Html = mail.Html,
            },
            recipients: new EmailRecipients([.. mail.Recipients.Select(address => new EmailAddress(address))]));

        // Antwoorden landen bij een mens en niet bij DoNotReply. Zie PortalMailOptions.ReplyToAddress:
        // er is vandaag precies één geverifieerd afzenderadres, en een maandoverzicht waarop je niet
        // kunt antwoorden stuurt de klant naar de telefoon.
        if (sender.ReplyToAddress is { } replyTo)
        {
            message.ReplyTo.Add(new EmailAddress(replyTo));
        }

        try
        {
            var client = new EmailClient(new Uri(sender.Endpoint), credential);

            EmailSendOperation operation = await client
                .SendAsync(WaitUntil.Started, message, cancellationToken)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Maandoverzicht aangenomen door Communication Services, operatie {OperationId}, " +
                "{Count} ontvanger(s).",
                operation.Id,
                mail.Recipients.Count);

            return new StatementSendResult(MailDelivery.Accepted, operation.Id);
        }
        catch (RequestFailedException exception) when (exception.Status is >= 400 and < 500)
        {
            // Een 4xx is een afwijzing: het bericht is niet aangenomen. Dat geldt ook voor 429 —
            // throttling is hier "niet aangenomen" en niet "misschien wel". Deze tak is dus zeker,
            // en alleen daarom mag de toestand NotSent worden.
            logger.LogError(
                exception,
                "Communication Services heeft het maandoverzicht geweigerd (status {Status}, code " +
                "{Code}). Er is niets verstuurd.",
                exception.Status,
                exception.ErrorCode);

            return new StatementSendResult(MailDelivery.Refused, OperationId: null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Alles wat hier komt is onbekend en niet mislukt: een 5xx, een tijdslimiet, een
            // afgebroken verbinding, een geannuleerd verzoek. Bij elk daarvan kan het bericht zijn
            // aangenomen en alleen het antwoord zijn weggevallen.
            //
            // OperationCanceledException wordt hier bewust óók als onbekend gelezen en niet
            // doorgegooid. Dat is tegen de gewoonte in, en het is hier de juiste keuze: de
            // annulering komt van een afgebroken HTTP-verzoek — een operator die zijn tabblad sluit
            // — en op dat moment is het bericht misschien al de deur uit. Doorgooien zou de claim op
            // Unknown laten staan zonder dat er iets wordt vastgelegd, en dat is dezelfde uitkomst
            // met minder informatie.
            logger.LogError(
                exception,
                "Het is niet vast te stellen of het maandoverzicht is verstuurd. Er wordt niets " +
                "opnieuw geprobeerd; de verzendbevestiging blijft op onbekend staan.");

            return new StatementSendResult(MailDelivery.Unknown, OperationId: null);
        }
    }
}
