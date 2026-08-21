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
public sealed record MailSendResult(MailDelivery Delivery, string? OperationId);

/// <summary>
/// Een opgemaakt bericht zoals de verzendlaag het aanneemt: onderwerp, ontvangers, en beide lichamen.
/// </summary>
/// <remarks>
/// <para><strong>Abstract, en de afgeleide typen zijn de doelen.</strong> Dezelfde vorm als
/// <c>AgentRunRow</c> in punt 14 en om dezelfde reden: de compiler dwingt af welk soort bericht op
/// welk pad terechtkomt. <see cref="StatementMail"/> gaat naar een klant en heeft daarom een
/// broncodetest op zijn opmaak; <c>AgentAlertMail</c> gaat naar Soratus en mag een stacktrace dragen.
/// Zou er één type zijn, dan is dat onderscheid alleen nog een afspraak.</para>
///
/// <para><strong>De ontvangers zitten op het bericht en niet op de verzendaanroep.</strong> Dat is de
/// grens die verhindert dat operatortekst bij een klant belandt: wie een bericht opmaakt, bepaalt in
/// dezelfde beweging aan wie het gaat, en er is geen aanroep waarmee je een bestaand bericht naar een
/// ander adres stuurt.</para>
///
/// <para><strong>Beide lichamen komen uit dezelfde gegevens en niet uit twee opmaakfuncties.</strong>
/// Een HTML-versie en een platte versie die uit elkaar lopen betekent dat de lezer met afbeeldingen
/// iets anders leest dan de lezer zonder.</para>
/// </remarks>
public abstract record OutgoingMail
{
    /// <summary>Alleen een opmaakfunctie van een doel maakt een bericht.</summary>
    /// <param name="subject">De onderwerpregel. Altijd één regel.</param>
    /// <param name="recipients">De ontvangers. Nooit leeg.</param>
    /// <param name="plainText">Het platte lichaam.</param>
    /// <param name="html">Het HTML-lichaam.</param>
    protected OutgoingMail(
        string subject,
        IReadOnlyList<string> recipients,
        string plainText,
        string html)
    {
        Subject = subject;
        Recipients = recipients;
        PlainText = plainText;
        Html = html;
    }

    /// <summary>De onderwerpregel. Altijd één regel; zie <see cref="MailText.OneLine"/>.</summary>
    public string Subject { get; }

    /// <summary>De ontvangers.</summary>
    public IReadOnlyList<string> Recipients { get; }

    /// <summary>Het platte lichaam.</summary>
    public string PlainText { get; }

    /// <summary>Het HTML-lichaam. Elke ingevoegde waarde is HTML-gecodeerd.</summary>
    public string Html { get; }
}

/// <summary>
/// Wat er met een bericht gebeurt als je het nú aanbiedt.
/// </summary>
/// <remarks>
/// <para><strong>Dit is een vraag en geen uitkomst, en dat verschil is het hele punt.</strong> De
/// proefdraaimodus en het ontbreken van een inrichting moeten <em>vóór</em> de onomkeerbare
/// boekhouding van een aanroeper bekend zijn — bij het maandoverzicht vóór de claim (§29.8: een
/// proefdraai die een document achterlaat is geen proefdraai), en bij de storingsmelder vóór het
/// zetten van een ontdubbelmarkering. Zaten die twee standen ín
/// <see cref="IMailOutbox.SendAsync"/>, dan zou een aanroeper ze pas kennen nadat hij zich al had
/// vastgelegd.</para>
/// </remarks>
public enum MailOutboxState
{
    /// <summary>Mailen is niet ingericht: er is geen endpoint of geen afzenderadres.</summary>
    /// <remarks>
    /// De eerste waarde, om dezelfde reden als bij <see cref="MailDelivery.Unknown"/>: de
    /// standaardwaarde hoort de stand te zijn waarin er niets de deur uit gaat.
    /// </remarks>
    NotConfigured,

    /// <summary>Proefdraaimodus: er wordt opgemaakt en niets verstuurd.</summary>
    DryRun,

    /// <summary>Er kan worden verstuurd.</summary>
    Ready,
}

/// <summary>
/// De verzendlaag: de enige plek in dit portaal waar een bericht de deur uit gaat.
/// </summary>
/// <remarks>
/// <para><strong>Waarom deze laag bestaat.</strong> Er waren twee plekken die zelf een
/// <c>EmailClient</c> bouwden en <c>SendAsync</c> aanriepen — <c>Soratus.Web/Services/LeadSink.cs</c>
/// voor terugbelverzoeken en de mailkant van het maandoverzicht — en de storingsmelder van fase 6 zou
/// de derde zijn geworden. Drie kopieën van één handeling is precies wat de knipregel dit project
/// heeft gekost (punt 13): die stond op drie plekken en liep binnen één dag uiteen.</para>
///
/// <para><strong>Wat er in deze laag zit is de verzendsemantiek en niet een omhulsel om
/// <c>SendAsync</c>.</strong> Vier besluiten, en ze horen bij elkaar:</para>
///
/// <list type="number">
///   <item><description>
///     <strong>Drie uitkomsten en geen twee.</strong> Zie <see cref="MailDelivery"/>.
///   </description></item>
///   <item><description>
///     <strong>Een <c>4xx</c> is niet-verstuurd, een <c>429</c> daaronder.</strong> Throttling
///     betekent "niet aangenomen" en niet "misschien wel". Al het andere is onbekend.
///   </description></item>
///   <item><description>
///     <strong>Uit "onbekend" komt niets automatisch.</strong> Geen <c>retry</c>, geen backoff, geen
///     tweede poging bij een tijdslimiet. Een mail is niet terug te halen, dus daar hoort een mens
///     aan te pas te komen.
///   </description></item>
///   <item><description>
///     <strong>De proefdraaimodus staat standaard aan.</strong> Zie
///     <see cref="PortalMailOptions.DryRun"/>. Hij zit in deze laag en niet bij één aanroeper, want
///     een ontwikkelmachine hoort geen enkele echte mail te versturen — ook geen storingsmelding.
///   </description></item>
/// </list>
///
/// <para><strong>Wat er per doel verschilt is alleen de opmaak en de ontvanger</strong>, en dat staat
/// dus buiten deze laag: <see cref="StatementMailComposer"/> voor de klant,
/// <c>Alerts/AgentAlertComposer</c> voor de operator.</para>
///
/// <para>Wat er bewust <em>niet</em> in zit: de marketingsite. Die is een eigen deployable en
/// authenticeert met een connection string in plaats van met een managed identity; zie
/// <see cref="PortalMailOptions"/>. Wat er nodig zou zijn om hem de derde aanroeper te maken staat in
/// het rapport bij deze wijziging en is uitdrukkelijk geen codewijziging alleen.</para>
/// </remarks>
public interface IMailOutbox
{
    /// <summary>
    /// Wat er met een bericht gebeurt als het nu wordt aangeboden.
    /// </summary>
    /// <remarks>
    /// Te lezen <em>vóór</em> het vastleggen van iets dat een verzending veronderstelt. Zie
    /// <see cref="MailOutboxState"/>.
    /// </remarks>
    MailOutboxState State { get; }

    /// <summary>
    /// Verstuurt één bericht.
    /// </summary>
    /// <param name="mail">Het opgemaakte bericht, met zijn ontvangers.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De uitkomst. Werpt niet bij een fout van de dienst.</returns>
    /// <remarks>
    /// <para><strong>Deze methode werpt niet bij een verzendfout, en dat is een eis en geen
    /// gemak.</strong> Een uitzondering die langs de aanroeper naar boven gaat, laat diens
    /// vastlegging op "onbekend" staan zonder dat er iets is vastgelegd over de oorzaak — en dan is
    /// er geen verschil meer tussen "geweigerd, dus zeker niets verstuurd" en "niet vast te stellen".
    /// Dat verschil is het hele punt: het eerste mag opnieuw, het tweede niet.</para>
    ///
    /// <para><strong>Wat hij wél doet is werpen als <see cref="State"/> niet
    /// <see cref="MailOutboxState.Ready"/> is.</strong> Dat is geen runtime-toestand maar een fout in
    /// de aanroeper: hij had de stand horen te lezen voordat hij zich vastlegde. Luidruchtig omvallen
    /// is daar de goede kant om fout te zitten — de andere kant is een proefdraai die stil echte mail
    /// verstuurt.</para>
    /// </remarks>
    Task<MailSendResult> SendAsync(OutgoingMail mail, CancellationToken cancellationToken = default);
}

/// <summary>
/// De verzendlaag op Azure Communication Services Email, met de managed identity van het portaal.
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
/// <c>LeadSink</c>, en hier met een eigen reden: het maandoverzicht gaat de deur uit tijdens een
/// <c>POST</c> van een operator die op een antwoord wacht. Wachten tot Communication Services klaar
/// is, betekent pollen — en een <c>POST</c> die dertig seconden hangt, wordt door de operator opnieuw
/// ingediend. Dat is precies het gedrag dat een dubbele mail oplevert. Wat we ervoor betalen:
/// "aangenomen" is niet "afgeleverd", en dat staat er ook zo op het scherm. Voor de storingsmelder
/// geldt hetzelfde met een ander argument: die draait elke minuut en mag niet op een dienst gaan
/// wachten die daar geen antwoordtijd voor belooft.</para>
///
/// <para><strong>De <see cref="EmailClient"/> wordt per verzending gemaakt en niet gecachet.</strong>
/// Anders dan bij <c>CosmosClientCache</c>, waar het cachen gemeten winst oplevert. Hier gaat het om
/// een handjevol berichten per maand: een client die maandenlang blijft staan voor een handeling die
/// zo zelden gebeurt, is een verbinding die vaker verloopt dan hij wordt gebruikt.</para>
/// </remarks>
internal sealed class AcsMailOutbox(
    IOptions<PortalMailOptions> options,
    TokenCredential credential,
    ILogger<AcsMailOutbox> logger) : IMailOutbox
{
    private readonly PortalMailOptions _options = options.Value;

    /// <inheritdoc />
    /// <remarks>
    /// De regel zelf staat op <see cref="PortalMailOptions.Outbox"/> en niet hier. Zie daar waarom: een
    /// testdubbel leest dezelfde methode, en een dubbel met een eigen kopie van deze beslissing dekt
    /// zijn eigen afwezigheid.
    /// </remarks>
    public MailOutboxState State => _options.Outbox();

    /// <inheritdoc />
    public async Task<MailSendResult> SendAsync(
        OutgoingMail mail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mail);

        // Niet de stand van hierboven opnieuw uitrekenen maar de afzender opvragen: dan is er geen
        // pad waarop de stand "Ready" zegt en er toch geen afzender is.
        var sender = _options.Sender();

        if (sender is null || _options.DryRun)
        {
            throw new InvalidOperationException(
                "Er is een mail aangeboden terwijl de verzendlaag op "
                + $"{(sender is null ? nameof(MailOutboxState.NotConfigured) : nameof(MailOutboxState.DryRun))} "
                + "staat. Lees IMailOutbox.State vóór het vastleggen van iets dat een verzending "
                + "veronderstelt; deze aanroep is een fout in de aanroeper en geen storing.");
        }

        var message = new EmailMessage(
            senderAddress: sender.FromAddress,
            content: new EmailContent(mail.Subject)
            {
                PlainText = mail.PlainText,
                Html = mail.Html,
            },
            recipients: new EmailRecipients([.. mail.Recipients.Select(address => new EmailAddress(address))]));

        // Antwoorden landen bij een mens en niet bij DoNotReply. Zie PortalMailOptions.ReplyToAddress:
        // er is vandaag precies één geverifieerd afzenderadres, en een bericht waarop je niet kunt
        // antwoorden stuurt de lezer naar de telefoon.
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
                "Bericht aangenomen door Communication Services, operatie {OperationId}, "
                + "{Count} ontvanger(s).",
                operation.Id,
                mail.Recipients.Count);

            return new MailSendResult(MailDelivery.Accepted, operation.Id);
        }
        catch (RequestFailedException exception) when (exception.Status is >= 400 and < 500)
        {
            // Een 4xx is een afwijzing: het bericht is niet aangenomen. Dat geldt ook voor 429 —
            // throttling is hier "niet aangenomen" en niet "misschien wel". Deze tak is dus zeker, en
            // alleen daarom mag een aanroeper er "zeker niets verstuurd" van maken.
            logger.LogError(
                exception,
                "Communication Services heeft het bericht geweigerd (status {Status}, code {Code}). "
                + "Er is niets verstuurd.",
                exception.Status,
                exception.ErrorCode);

            return new MailSendResult(MailDelivery.Refused, OperationId: null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Alles wat hier komt is onbekend en niet mislukt: een 5xx, een tijdslimiet, een
            // afgebroken verbinding, een geannuleerd verzoek, een onleesbare endpoint. Bij elk
            // daarvan kan het bericht zijn aangenomen en alleen het antwoord zijn weggevallen.
            //
            // OperationCanceledException wordt hier bewust óók als onbekend gelezen en niet
            // doorgegooid. Dat is tegen de gewoonte in, en het is hier de juiste keuze: de annulering
            // komt van een afgebroken HTTP-verzoek — een operator die zijn tabblad sluit, of een
            // portaal dat afsluit — en op dat moment is het bericht misschien al de deur uit.
            // Doorgooien zou de vastlegging van de aanroeper op onbekend laten staan zonder dat er
            // iets wordt vastgelegd, en dat is dezelfde uitkomst met minder informatie.
            logger.LogError(
                exception,
                "Het is niet vast te stellen of het bericht is verstuurd. Er wordt niets opnieuw "
                + "geprobeerd; de vastlegging blijft op onbekend staan.");

            return new MailSendResult(MailDelivery.Unknown, OperationId: null);
        }
    }
}
