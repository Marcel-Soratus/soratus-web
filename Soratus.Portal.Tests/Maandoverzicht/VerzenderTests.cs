using Azure;
using Azure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Soratus.Portal.Mail;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Maandoverzicht;

/// <summary>
/// De indeling die de echte verzender maakt tussen "zeker niets verstuurd" en "onbekend".
/// </summary>
/// <remarks>
/// <para>Deze tests raken Azure niet aan en versturen niets. Ze oefenen de twee <c>catch</c>-blokken
/// van <c>AcsStatementMailSender</c> uit door de aanmelding te laten falen: een
/// <see cref="TokenCredential"/> die werpt, komt bij het versturen langs dezelfde weg naar boven als
/// een fout van de dienst zelf.</para>
///
/// <para><strong>Wat hiermee dus níet is gemeten:</strong> hoe Communication Services zelf antwoordt.
/// Dat vraagt een echte verzending en die is niet gedaan. Wat wél is gemeten is de indeling, en die
/// is het besluit: een 4xx mag <c>notSent</c> zetten en al het andere niet.</para>
/// </remarks>
public class VerzenderTests
{
    [Fact]
    public async Task EenAfwijzingMetEen4xxLevertZekerNietsVerstuurdOp()
    {
        var uitkomst = await Verstuur(new RequestFailedException(403, "Verboden", "Forbidden", null));

        Assert.Equal(MailDelivery.Refused, uitkomst.Delivery);
        Assert.Null(uitkomst.OperationId);
    }

    [Fact]
    public async Task EenThrottlingLevertOokZekerNietsVerstuurdOp()
    {
        // Een 429 hoort bij de 4xx-tak: throttling betekent "niet aangenomen" en niet "misschien
        // wel". Zou hij als onbekend worden gelezen, dan blijft een maand die alleen te vroeg werd
        // verstuurd voor altijd op onbekend staan en moet een mens hem vrijgeven.
        var uitkomst = await Verstuur(new RequestFailedException(429, "Te veel", "TooManyRequests", null));

        Assert.Equal(MailDelivery.Refused, uitkomst.Delivery);
    }

    [Fact]
    public async Task EenServerfoutLevertOnbekendOpEnGeenMislukking()
    {
        var uitkomst = await Verstuur(new RequestFailedException(503, "Even niet", "ServiceUnavailable", null));

        Assert.Equal(MailDelivery.Unknown, uitkomst.Delivery);
    }

    [Fact]
    public async Task EenTijdslimietLevertOnbekendOp()
    {
        var uitkomst = await Verstuur(new TaskCanceledException("De tijd is om."));

        Assert.Equal(MailDelivery.Unknown, uitkomst.Delivery);
    }

    [Fact]
    public async Task EenGeannuleerdVerzoekLevertOnbekendOpEnWerptNiet()
    {
        // Tegen de gewoonte in: een OperationCanceledException wordt hier niet doorgegooid. De
        // annulering komt van een afgebroken HTTP-verzoek — een operator die zijn tabblad sluit — en
        // op dat moment kan het bericht al de deur uit zijn. Doorgooien zou de claim op onbekend laten
        // staan zonder dat er iets wordt vastgelegd: dezelfde uitkomst met minder informatie.
        var uitkomst = await Verstuur(new OperationCanceledException("Afgebroken."));

        Assert.Equal(MailDelivery.Unknown, uitkomst.Delivery);
    }

    [Fact]
    public async Task EenOnleesbareEndpointLevertOnbekendOpEnGeenUitzondering()
    {
        // De vangnettak. Een onleesbare endpoint hoort een inrichtingsfout te zijn en geen
        // uitzondering die langs de aanroeper omhoog gaat, want dan blijft de claim staan zonder dat
        // er iets over de oorzaak is vastgelegd.
        var verzender = new AcsStatementMailSender(
            new Werpendecredential(new RequestFailedException(403, "x")),
            NullLogger<AcsStatementMailSender>.Instance);

        var uitkomst = await verzender.SendAsync(
            new MailSender("dit is geen uri", "DoNotReply@soratus.com", ReplyToAddress: null),
            Mail());

        Assert.Equal(MailDelivery.Unknown, uitkomst.Delivery);
    }

    private static async Task<StatementSendResult> Verstuur(Exception fout)
    {
        var verzender = new AcsStatementMailSender(
            new Werpendecredential(fout),
            NullLogger<AcsStatementMailSender>.Instance);

        return await verzender.SendAsync(
            new MailSender(
                "https://acs-soratus-test.europe.communication.azure.com/",
                "DoNotReply@soratus.com",
                "hallo@soratus.com"),
            Mail());
    }

    private static StatementMail Mail() =>
        StatementMailComposer.Compose(
                "Acme Logistiek",
                Maandoverzichtbank.Volledig(),
                new StatementAddressing([Vasteportaalopslag.Beheerderadres], "Jan Acme"),
                "https://portal.soratus.com")
            .Mail!;

    /// <summary>Een credential die bij het ophalen van een token werpt.</summary>
    /// <remarks>
    /// Hiermee komt de fout op precies de plek naar boven waar een fout van de dienst zelf ook
    /// aankomt: binnen de <c>try</c> van <c>SendAsync</c>. Dat is genoeg om de indeling te meten en
    /// het raakt Azure niet aan.
    /// </remarks>
    private sealed class Werpendecredential(Exception fout) : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw fout;

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            throw fout;
    }
}
