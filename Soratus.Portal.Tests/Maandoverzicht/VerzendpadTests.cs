using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Soratus.Portal.Mail;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Maandoverzicht;

/// <summary>
/// De volgorde en de uitkomsten van het verzendpad: claimen, versturen, vastleggen.
/// </summary>
/// <remarks>
/// <para>Dit is de scherpste test van dit werk, en de eis die hij meet staat niet in de spec maar in
/// de vaste stelregel van dit project: <em>"onbekend of het gelukt is" is een eigen toestand en geen
/// reden om het opnieuw te proberen.</em> Een mail is niet terug te halen, dus de vraag is niet of
/// het gelukkigste geval werkt maar wat er gebeurt bij een dubbele indiening, bij een mislukking
/// halverwege, en bij een uitkomst waarvan niemand weet of hij is aangekomen.</para>
/// </remarks>
public class VerzendpadTests
{
    [Fact]
    public async Task EenGelukteVerzendingLegtDeBedragenVastDieInDeMailStonden()
    {
        var bank = new Maandoverzichtbank();

        var uitkomst = await bank.Dienst.SendAsync(
            await bank.SchrijfrechtAsync(),
            Maandoverzichtbank.AfgeslotenMaand);

        Assert.Equal(StatementOutcomeKind.Sent, uitkomst.Kind);
        Assert.Single(bank.Verzender.Verstuurd);

        var bevestiging = bank.Bevestigingen.Document(Maandoverzichtbank.AfgeslotenMaand);

        Assert.NotNull(bevestiging);
        Assert.Equal(StatementSendState.Sent, bevestiging.State);
        Assert.Equal("operatie-0001", bevestiging.OperationId);

        // De bedragen staan in het document en niet alleen in de mail. Zonder deze drie regels is de
        // vraag "wat stond er in de mail die de klant heeft gekregen" alleen te beantwoorden met een
        // herberekening, en die geeft over een maand een ander getal.
        Assert.Equal(36.79m, bevestiging.AzureAmount);
        Assert.Equal(250.00m, bevestiging.ExtraHoursAmount);
        Assert.Equal(286.79m, bevestiging.Total);
    }

    [Fact]
    public async Task DeClaimStaatVoorDeMailEnNietErna()
    {
        var bank = new Maandoverzichtbank();

        // De verzender kijkt bij elke aanroep of er al een claim staat. De aantallen alleen meten
        // niets: claimen en versturen leveren beide een teller op, en die tellers zijn hetzelfde
        // ongeacht welke van de twee eerst gaat. Deze test viel bij de mutatietest door de mand en is
        // daarna scherper gemaakt.
        var scope = await bank.SchrijfrechtAsync();

        await bank.Dienst.SendAsync(scope, Maandoverzichtbank.AfgeslotenMaand);

        Assert.True(
            bank.Verzender.GeclaimdBijElkeVerzending,
            "Er is een mail verstuurd zonder dat er een claim stond. Dat is precies de volgorde "
            + "waarin een dubbele mail ontstaat: verstuurd, antwoord verloren, niets vastgelegd, "
            + "volgende poging verstuurt er een tweede.");

        Assert.Equal(1, bank.Bevestigingen.Claims);
        Assert.Single(bank.Verzender.Verstuurd);
        Assert.Equal(1, bank.Bevestigingen.Bevestigingen);
    }

    [Fact]
    public async Task EenTweedeVerzendingVanDezelfdeMaandLevertGeenTweedeMail()
    {
        var bank = new Maandoverzichtbank();
        var scope = await bank.SchrijfrechtAsync();

        await bank.Dienst.SendAsync(scope, Maandoverzichtbank.AfgeslotenMaand);
        var tweede = await bank.Dienst.SendAsync(scope, Maandoverzichtbank.AfgeslotenMaand);

        Assert.Equal(StatementOutcomeKind.Blocked, tweede.Kind);
        Assert.Single(bank.Verzender.Verstuurd);
        Assert.Contains("al verstuurd", tweede.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EenOnbekendeUitkomstBlijftOnbekendEnWordtNietOpnieuwGeprobeerd()
    {
        var bank = new Maandoverzichtbank();
        bank.Verzender.Uitkomst = MailDelivery.Unknown;

        var scope = await bank.SchrijfrechtAsync();
        var eerste = await bank.Dienst.SendAsync(scope, Maandoverzichtbank.AfgeslotenMaand);

        Assert.Equal(StatementOutcomeKind.Unknown, eerste.Kind);
        Assert.Equal(
            StatementSendState.Unknown,
            bank.Bevestigingen.Document(Maandoverzichtbank.AfgeslotenMaand)!.State);

        // En nu het belangrijkste: de tweede poging gaat niet door. Dit is de plek waar de neiging om
        // "mislukt, dus opnieuw" te doen het sterkst is en waar dat de duurste gok is — bij een
        // tijdslimiet kan het bericht zijn aangenomen en alleen het antwoord zijn weggevallen.
        var tweede = await bank.Dienst.SendAsync(scope, Maandoverzichtbank.AfgeslotenMaand);

        Assert.Equal(StatementOutcomeKind.Blocked, tweede.Kind);
        Assert.Single(bank.Verzender.Verstuurd);
        Assert.Contains("niet bekend of het overzicht is aangekomen", tweede.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NaEenVaststellingMagErOpnieuwWordenVerstuurd()
    {
        var bank = new Maandoverzichtbank();
        bank.Verzender.Uitkomst = MailDelivery.Unknown;

        var scope = await bank.SchrijfrechtAsync();

        await bank.Dienst.SendAsync(scope, Maandoverzichtbank.AfgeslotenMaand);

        var vastgesteld = await bank.Bevestigingen.ReleaseAsync(
            scope,
            new StatementRelease(
                Maandoverzichtbank.AfgeslotenMaand,
                "Gebeld met de contactpersoon: niets ontvangen.",
                BasedOnETag: null));

        Assert.True(vastgesteld.IsSaved);
        Assert.Equal(StatementSendState.NotSent, vastgesteld.Value!.State);

        bank.Verzender.Uitkomst = MailDelivery.Accepted;

        var opnieuw = await bank.Dienst.SendAsync(scope, Maandoverzichtbank.AfgeslotenMaand);

        Assert.Equal(StatementOutcomeKind.Sent, opnieuw.Kind);
        Assert.Equal(2, bank.Verzender.Verstuurd.Count);

        var bevestiging = bank.Bevestigingen.Document(Maandoverzichtbank.AfgeslotenMaand)!;

        // Het aantal pogingen loopt op en de vaststelling blijft staan. Dat de klant twee overzichten
        // over dezelfde maand heeft gekregen hoort op het scherm te staan en niet uit tijdstempels te
        // reconstrueren zijn.
        Assert.Equal(2, bevestiging.Attempts);
        Assert.Equal("Gebeld met de contactpersoon: niets ontvangen.", bevestiging.ReleaseNote);
    }

    [Fact]
    public async Task EenAfwijzingDoorDeDienstLevertNietVerstuurdOpEnGeenOnbekend()
    {
        var bank = new Maandoverzichtbank();
        bank.Verzender.Uitkomst = MailDelivery.Refused;

        var uitkomst = await bank.Dienst.SendAsync(
            await bank.SchrijfrechtAsync(),
            Maandoverzichtbank.AfgeslotenMaand);

        Assert.Equal(StatementOutcomeKind.NotSent, uitkomst.Kind);
        Assert.Equal(
            StatementSendState.NotSent,
            bank.Bevestigingen.Document(Maandoverzichtbank.AfgeslotenMaand)!.State);
    }

    [Fact]
    public async Task EenProefdraaiVerstuurtNietsEnLegtNietsVast()
    {
        var bank = new Maandoverzichtbank(opties: new PortalMailOptions
        {
            Endpoint = "https://acs-soratus-test.europe.communication.azure.com/",
            FromAddress = "DoNotReply@soratus.com",
            DryRun = true,
        });

        var uitkomst = await bank.Dienst.SendAsync(
            await bank.SchrijfrechtAsync(),
            Maandoverzichtbank.AfgeslotenMaand);

        Assert.Equal(StatementOutcomeKind.DryRun, uitkomst.Kind);
        Assert.Empty(bank.Verzender.Verstuurd);
        Assert.Equal(0, bank.Bevestigingen.Claims);
        Assert.Null(bank.Bevestigingen.Document(Maandoverzichtbank.AfgeslotenMaand));

        // En de proefdraai toont wél wat er zou zijn verstuurd. Een proefdraaimodus die niets laat
        // zien, bewijst niets.
        Assert.NotNull(uitkomst.Preview);
        Assert.Contains(Vasteportaalopslag.Beheerderadres, uitkomst.Preview.Recipients);
        Assert.StartsWith("PROEFDRAAI", uitkomst.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EenLopendeMaandWordtGeweigerdVoorDatErIetsWordtGelezen()
    {
        var bank = new Maandoverzichtbank();

        var uitkomst = await bank.Dienst.SendAsync(
            await bank.SchrijfrechtAsync(),
            Maandoverzichtbank.LopendeMaand);

        Assert.Equal(StatementOutcomeKind.Refused, uitkomst.Kind);
        Assert.Equal(StatementRefusal.MonthNotClosed, uitkomst.Refusal);
        Assert.Empty(bank.Verzender.Verstuurd);
        Assert.Equal(0, bank.Bevestigingen.Claims);
    }

    [Theory]
    [InlineData("augustus")]
    [InlineData("08-2026")]
    [InlineData("2026-13")]
    [InlineData("")]
    public async Task WatGeenMaandIsWordtGeweigerd(string maand)
    {
        var bank = new Maandoverzichtbank();

        var uitkomst = await bank.Dienst.SendAsync(await bank.SchrijfrechtAsync(), maand);

        Assert.Equal(StatementRefusal.MonthNotClosed, uitkomst.Refusal);
        Assert.Empty(bank.Verzender.Verstuurd);
    }

    [Fact]
    public async Task ZonderIngerichteMailWordtErNietsGeclaimd()
    {
        var bank = new Maandoverzichtbank(opties: new PortalMailOptions { DryRun = false });

        var uitkomst = await bank.Dienst.SendAsync(
            await bank.SchrijfrechtAsync(),
            Maandoverzichtbank.AfgeslotenMaand);

        Assert.Equal(StatementRefusal.MailNotConfigured, uitkomst.Refusal);
        Assert.Equal(0, bank.Bevestigingen.Claims);
    }

    [Fact]
    public async Task EenOnbekendBedragWordtNietNulMaarEenWeigering()
    {
        var bank = new Maandoverzichtbank(
            Maandoverzichtbank.Volledig() with { AzureAmount = null, Total = null });

        var uitkomst = await bank.Dienst.SendAsync(
            await bank.SchrijfrechtAsync(),
            Maandoverzichtbank.AfgeslotenMaand);

        Assert.Equal(StatementRefusal.AmountUnknown, uitkomst.Refusal);
        Assert.Empty(bank.Verzender.Verstuurd);
        Assert.Equal(0, bank.Bevestigingen.Claims);
    }

    [Fact]
    public async Task EenOnvolledigeMetingWordtGeweigerd()
    {
        var bank = new Maandoverzichtbank(
            Maandoverzichtbank.Volledig() with { AmountsAreComplete = false });

        var uitkomst = await bank.Dienst.SendAsync(
            await bank.SchrijfrechtAsync(),
            Maandoverzichtbank.AfgeslotenMaand);

        Assert.Equal(StatementRefusal.AmountsIncomplete, uitkomst.Refusal);
        Assert.Empty(bank.Verzender.Verstuurd);
    }

    [Fact]
    public async Task ZonderMetingWordtErNietGemaild()
    {
        var bank = new Maandoverzichtbank(bedragen: null);
        bank.Bedragen = null;

        var uitkomst = await bank.Dienst.SendAsync(
            await bank.SchrijfrechtAsync(),
            Maandoverzichtbank.AfgeslotenMaand);

        Assert.Equal(StatementRefusal.NoFigures, uitkomst.Refusal);
        Assert.Empty(bank.Verzender.Verstuurd);
    }

    [Fact]
    public async Task ZonderContactpersoonWordtErNietGemaild()
    {
        var bank = new Maandoverzichtbank(zonderToegang: true);

        var uitkomst = await bank.Dienst.SendAsync(
            await bank.SchrijfrechtAsync(),
            Maandoverzichtbank.AfgeslotenMaand);

        Assert.Equal(StatementRefusal.NoRecipient, uitkomst.Refusal);
        Assert.Empty(bank.Verzender.Verstuurd);
        Assert.Equal(0, bank.Bevestigingen.Claims);
    }

    [Fact]
    public async Task EenWeigeringLaatGeenHalveBevestigingAchter()
    {
        // De reden dat elke weigering vóór de claim staat: een weigering hoort spoorloos te zijn.
        // Staat er een document, dan is later niet te zien of er wel of niet is gemaild.
        foreach (var bank in new[]
        {
            new Maandoverzichtbank(Maandoverzichtbank.Volledig() with { Total = null }),
            new Maandoverzichtbank(Maandoverzichtbank.Volledig() with { AmountsAreComplete = false }),
            new Maandoverzichtbank(zonderToegang: true),
        })
        {
            await bank.Dienst.SendAsync(
                await bank.SchrijfrechtAsync(),
                Maandoverzichtbank.AfgeslotenMaand);

            Assert.Null(bank.Bevestigingen.Document(Maandoverzichtbank.AfgeslotenMaand));
        }
    }

    [Fact]
    public async Task DeMeldingBijEenOnbekendeUitkomstZegtNietDatHetIsMislukt()
    {
        var bank = new Maandoverzichtbank();
        bank.Verzender.Uitkomst = MailDelivery.Unknown;

        var uitkomst = await bank.Dienst.SendAsync(
            await bank.SchrijfrechtAsync(),
            Maandoverzichtbank.AfgeslotenMaand);

        // Grof, en precies grof genoeg. De fout die deze test voorkomt is dat iemand de melding later
        // "duidelijker" maakt door er "mislukt" of "niet verstuurd" van te maken — en dan probeert de
        // volgende operator het opnieuw en staat er twee keer hetzelfde in de postbus van de klant.
        Assert.Contains("ONBEKEND", uitkomst.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("mislukt", uitkomst.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("niet verstuurd", uitkomst.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeWeergaveKentDeAfgeslotenMaandenEnNietDeLopende()
    {
        var bank = new Maandoverzichtbank();
        var scope = await bank.SchrijfrechtAsync();

        var weergave = new StatementViews(
                bank.Bevestigingen,
                Options.Create(bank.Opties),
                new Stilstaandeklok(Testgegevens.Nu))
            .BuildStatementsAsync(scope, 2026);

        var view = await weergave;

        Assert.Equal(Maandoverzichtbank.AfgeslotenMaand, view.DefaultMonth);
        Assert.DoesNotContain(Maandoverzichtbank.LopendeMaand, view.Months);
        Assert.Contains(Maandoverzichtbank.AfgeslotenMaand, view.Months);
        Assert.False(view.HasRow(Maandoverzichtbank.AfgeslotenMaand));
    }
}
