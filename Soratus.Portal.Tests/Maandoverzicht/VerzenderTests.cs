using Azure;
using Azure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Soratus.Portal.Mail;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Maandoverzicht;

/// <summary>
/// De indeling die de verzendlaag maakt tussen "zeker niets verstuurd" en "onbekend".
/// </summary>
/// <remarks>
/// <para>Deze tests raken Azure niet aan en versturen niets. Ze oefenen de twee <c>catch</c>-blokken
/// van <c>AcsMailOutbox</c> uit door de aanmelding te laten falen: een
/// <see cref="TokenCredential"/> die werpt, komt bij het versturen langs dezelfde weg naar boven als
/// een fout van de dienst zelf.</para>
///
/// <para><strong>Wat hiermee dus níet is gemeten:</strong> hoe Communication Services zelf antwoordt.
/// Dat vraagt een echte verzending en die is niet gedaan. Wat wél is gemeten is de indeling, en die
/// is het besluit: een 4xx mag "zeker niets verstuurd" opleveren en al het andere niet.</para>
///
/// <para><strong>Dit is de gedeelde laag en niet de mailkant van het maandoverzicht.</strong> De
/// indeling geldt dus ook voor de storingsmelder, en dat is de reden dat ze uit die kant is gehaald:
/// twee kopieën van deze drie takken zouden gaan schuiven, en dan is een <c>429</c> aan de ene kant
/// "niet verstuurd" en aan de andere "onbekend".</para>
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
        // annulering komt van een afgebroken HTTP-verzoek — een operator die zijn tabblad sluit, of een
        // portaal dat afsluit — en op dat moment kan het bericht al de deur uit zijn. Doorgooien zou de
        // vastlegging op onbekend laten staan zonder dat er iets wordt vastgelegd: dezelfde uitkomst
        // met minder informatie.
        var uitkomst = await Verstuur(new OperationCanceledException("Afgebroken."));

        Assert.Equal(MailDelivery.Unknown, uitkomst.Delivery);
    }

    [Fact]
    public async Task EenOnleesbareEndpointLevertOnbekendOpEnGeenUitzondering()
    {
        // De vangnettak. Een onleesbare endpoint hoort een inrichtingsfout te zijn en geen
        // uitzondering die langs de aanroeper omhoog gaat, want dan blijft de claim staan zonder dat
        // er iets over de oorzaak is vastgelegd.
        var uitkomst = await Laag(
                new RequestFailedException(403, "x"),
                Opties("dit is geen uri"))
            .SendAsync(Mail());

        Assert.Equal(MailDelivery.Unknown, uitkomst.Delivery);
    }

    [Fact]
    public void ZonderInrichtingStaatDeLaagOpNietIngerichtEnNietOpProefdraai()
    {
        // De volgorde van de twee vragen is niet vrij. Een omgeving zonder endpoint waar iemand DryRun
        // op false heeft gezet, hoort niet te melden dat hij gaat versturen.
        var leeg = new PortalMailOptions { DryRun = false };

        Assert.Equal(MailOutboxState.NotConfigured, leeg.Outbox());
        Assert.Equal(MailOutboxState.NotConfigured, Laag(new InvalidOperationException(), leeg).State);
    }

    [Fact]
    public async Task InProefdraaimodusWerptDeLaagInPlaatsVanTeVersturen()
    {
        // Dit is de enige plek waar de gedeelde laag mag werpen, en het is met opzet: een aanroeper die
        // de stand niet leest voordat hij zich vastlegt, hoort luidruchtig om te vallen. De andere kant
        // is een proefdraai die stil echte mail verstuurt.
        var opties = Opties("https://acs-soratus-test.europe.communication.azure.com/");
        opties.DryRun = true;

        var laag = Laag(new InvalidOperationException("mag niet gebeuren"), opties);

        Assert.Equal(MailOutboxState.DryRun, laag.State);

        var fout = await Assert.ThrowsAsync<InvalidOperationException>(() => laag.SendAsync(Mail()));

        Assert.Contains(nameof(MailOutboxState.DryRun), fout.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ZonderInrichtingWerptDeLaagOok()
    {
        var laag = Laag(new InvalidOperationException("mag niet gebeuren"), new PortalMailOptions());

        var fout = await Assert.ThrowsAsync<InvalidOperationException>(() => laag.SendAsync(Mail()));

        Assert.Contains(nameof(MailOutboxState.NotConfigured), fout.Message, StringComparison.Ordinal);
    }

    private static async Task<MailSendResult> Verstuur(Exception fout) =>
        await Laag(fout, Opties("https://acs-soratus-test.europe.communication.azure.com/"))
            .SendAsync(Mail());

    private static AcsMailOutbox Laag(Exception fout, PortalMailOptions opties) =>
        new(
            Options.Create(opties),
            new Werpendecredential(fout),
            NullLogger<AcsMailOutbox>.Instance);

    private static PortalMailOptions Opties(string endpoint) => new()
    {
        Endpoint = endpoint,
        FromAddress = "DoNotReply@soratus.com",
        ReplyToAddress = "hallo@soratus.com",
        DryRun = false,
    };

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
