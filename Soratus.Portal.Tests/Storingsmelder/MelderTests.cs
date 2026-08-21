using Soratus.Agents.Contracts;
using Soratus.Portal.Alerts;
using Soratus.Portal.Mail;
using Soratus.Portal.Tests.Hulpmiddelen;
using Soratus.Portal.Tests.Maandoverzicht;

namespace Soratus.Portal.Tests.Storingsmelder;

/// <summary>
/// De volgorde van de melder: ontdubbelen, afremmen, claimen, versturen, vastleggen.
/// </summary>
/// <remarks>
/// De echte <see cref="AgentFaultAlerter"/> met drie dubbels eronder. Wat hier gemeten wordt is niet
/// de ontdubbelregel — die staat in <c>OntdubbelingTests</c> — maar dat de melder hem gebruikt, in de
/// goede volgorde, en dat er niets de deur uit gaat zonder claim.
/// </remarks>
public class MelderTests
{
    [Fact]
    public async Task EenStoringLevertEenMeldingOpEnEenMarkering()
    {
        var bank = new Storingsmelderbank();
        bank.Bron.Klanten.Add(Storingsmelderbank.Klant([Storingsmelderbank.Mislukt("factuur-intake")]));

        Assert.Equal(1, await bank.RondeAsync());

        var mail = Assert.Single(bank.Verzender.Verstuurd);

        Assert.IsType<AgentAlertMail>(mail);
        Assert.Equal(["storingen@soratus.com"], mail.Recipients);

        var markering = bank.Markeringen.Document("acme-logistiek", "factuur-intake");

        Assert.NotNull(markering);
        Assert.Equal(AgentStatus.Failed, markering.Status);
        Assert.Equal(MailDelivery.Accepted, markering.Delivery);
        Assert.Equal(1, markering.Notifications);
    }

    [Fact]
    public async Task DeTweedeRondeBinnenHetVensterVerstuurtNiets()
    {
        // Dit is waar de opdracht om ging: ShouldAlert ontdubbelt niet, dus zonder deze eigenschap
        // mailt een melder die elke minuut draait zestig keer per uur over dezelfde mislukte run.
        var bank = new Storingsmelderbank();
        bank.Bron.Klanten.Add(Storingsmelderbank.Klant([Storingsmelderbank.Mislukt("factuur-intake")]));

        await bank.RondeAsync();

        bank.Klok.Vooruit(TimeSpan.FromMinutes(1));

        Assert.Equal(0, await bank.RondeAsync());
        Assert.Single(bank.Verzender.Verstuurd);
    }

    [Fact]
    public async Task ZestigRondenLangDezelfdeStoringLeverenEenMelding()
    {
        // De invariant en niet het gevolg: één uur elke minuut kijken over dezelfde storing hoort één
        // mail op te leveren en niet zestig. Deze test staat er naast de vorige omdat de vorige met
        // twee rondes ook groen blijft bij een venster van één minuut.
        var bank = new Storingsmelderbank();
        bank.Bron.Klanten.Add(Storingsmelderbank.Klant([Storingsmelderbank.Mislukt("factuur-intake")]));

        for (var ronde = 0; ronde < 60; ronde++)
        {
            await bank.RondeAsync();
            bank.Klok.Vooruit(TimeSpan.FromMinutes(1));
        }

        Assert.Single(bank.Verzender.Verstuurd);
    }

    [Fact]
    public async Task NaHetVensterKomtErEenTweedeMelding()
    {
        var bank = new Storingsmelderbank();
        bank.Bron.Klanten.Add(Storingsmelderbank.Klant([Storingsmelderbank.Mislukt("factuur-intake")]));

        await bank.RondeAsync();

        bank.Klok.Vooruit(bank.Opties.RepeatAfter + TimeSpan.FromMinutes(1));

        Assert.Equal(1, await bank.RondeAsync());
        Assert.Equal(2, bank.Verzender.Verstuurd.Count);

        var markering = bank.Markeringen.Document("acme-logistiek", "factuur-intake");

        Assert.NotNull(markering);
        Assert.Equal(2, markering.Notifications);

        // De eerste melding blijft staan. Zonder dat veld is "sinds wanneer weten we hiervan" niet meer
        // te beantwoorden zodra er één keer is herhaald.
        Assert.Equal(Testgegevens.Nu, markering.FirstNotifiedAt);
    }

    [Fact]
    public async Task EenVerergeringWachtNietOpHetVenster()
    {
        var bank = new Storingsmelderbank();
        bank.Bron.Klanten.Add(Storingsmelderbank.Klant([Storingsmelderbank.Zwijgt("factuur-intake")]));

        await bank.RondeAsync();

        bank.Klok.Vooruit(TimeSpan.FromMinutes(1));
        bank.Bron.Klanten.Clear();
        bank.Bron.Klanten.Add(Storingsmelderbank.Klant([Storingsmelderbank.Mislukt("factuur-intake")]));

        Assert.Equal(1, await bank.RondeAsync());
        Assert.Equal(2, bank.Verzender.Verstuurd.Count);
    }

    [Fact]
    public async Task DrieDienstenInEenProcesLeverenEenMeldingEnDrieMarkeringen()
    {
        // §42: één oorzaak, drie diensten, één mail. Zonder de groepering zouden er drie uitgaan, en dan
        // is de derde de reden dat de eerste ook niet meer wordt gelezen.
        var start = Testgegevens.Nu - TimeSpan.FromHours(3);
        var bank = new Storingsmelderbank();

        bank.Bron.Klanten.Add(Storingsmelderbank.Klant(
        [
            Storingsmelderbank.Zwijgt("boekhoud-chat", start),
            Storingsmelderbank.Zwijgt("financieel-overzicht", start),
            Storingsmelderbank.Zwijgt("declaraties-import", start),
        ]));

        Assert.Equal(1, await bank.RondeAsync());
        Assert.Single(bank.Verzender.Verstuurd);

        // Drie claims, want de ontdubbeling hangt per agent. Ze horen wél alle drie te staan: anders zou
        // een dienst die morgen als enige nog stuk is niet als "al gemeld" gelden.
        Assert.Equal(3, bank.Markeringen.Claims);
        Assert.NotNull(bank.Markeringen.Document("acme-logistiek", "boekhoud-chat"));
        Assert.NotNull(bank.Markeringen.Document("acme-logistiek", "declaraties-import"));
        Assert.NotNull(bank.Markeringen.Document("acme-logistiek", "financieel-overzicht"));
    }

    [Fact]
    public async Task EenTweedeInstantieVerstuurtNiets()
    {
        // De claim gaat vóór de mail. Botst hij, dan doet een andere instantie deze melding en verstuurt
        // deze er niets. Zonder die volgorde krijgen twee instanties op een portaal met twee instanties
        // elke storing twee keer gemeld.
        var bank = new Storingsmelderbank();
        bank.Bron.Klanten.Add(Storingsmelderbank.Klant([Storingsmelderbank.Mislukt("factuur-intake")]));
        bank.Markeringen.AndereInstantieWasEerder = true;

        Assert.Equal(0, await bank.RondeAsync());
        Assert.Empty(bank.Verzender.Verstuurd);
    }

    [Fact]
    public async Task DeMeldingNoemtAlleenWatDezeInstantieHeeftGeclaimd()
    {
        // Het gedeeltelijke geval, en de gedeeltelijke race die §42 niet dicht: van drie diensten in één
        // host claimt de andere instantie er één. Wat dan moet gelden is dat élke mail precies noemt wat
        // hij heeft geclaimd — anders noemen twee mails dezelfde dienst, en dan is de operator twee keer
        // op zoek naar hetzelfde.
        //
        // Deze test bestaat door een mutatie: het samenstellen van de mail uit de volledige groep in
        // plaats van uit de geclaimde agents maakte niets rood. Dat was een gat.
        var start = Testgegevens.Nu - TimeSpan.FromHours(3);
        var bank = new Storingsmelderbank();

        bank.Bron.Klanten.Add(Storingsmelderbank.Klant(
        [
            Storingsmelderbank.Zwijgt("boekhoud-chat", start),
            Storingsmelderbank.Zwijgt("financieel-overzicht", start),
            Storingsmelderbank.Zwijgt("declaraties-import", start),
        ]));

        bank.Markeringen.BotstOp.Add("financieel-overzicht");

        Assert.Equal(1, await bank.RondeAsync());

        var mail = Assert.Single(bank.Verzender.Verstuurd);

        Assert.Contains("boekhoud-chat", mail.PlainText, StringComparison.Ordinal);
        Assert.Contains("declaraties-import", mail.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain("financieel-overzicht", mail.PlainText, StringComparison.Ordinal);

        // En de onderwerpregel telt mee wat er werkelijk in staat.
        Assert.Contains("2 diensten", mail.Subject, StringComparison.Ordinal);

        // Twee markeringen en twee bevestigingen, niet drie.
        Assert.Equal(2, bank.Markeringen.Bevestigingen);
        Assert.Null(bank.Markeringen.Document("acme-logistiek", "financieel-overzicht"));
    }

    [Fact]
    public async Task InProefdraaimodusGaatErNietsUitEnWordtErNietsVastgelegd()
    {
        // §29.8, hier met een eigen gevolg: een proefdraai die een markering achterlaat zou de echte
        // storing daarna zes uur lang onderdrukken.
        var bank = new Storingsmelderbank(mailopties: Droog());
        bank.Bron.Klanten.Add(Storingsmelderbank.Klant([Storingsmelderbank.Mislukt("factuur-intake")]));

        Assert.Equal(1, await bank.RondeAsync());

        Assert.Empty(bank.Verzender.Verstuurd);
        Assert.Equal(0, bank.Markeringen.Claims);
        Assert.Null(bank.Markeringen.Document("acme-logistiek", "factuur-intake"));
    }

    [Fact]
    public async Task ZonderIngerichteMailWordtErNietsGelezen()
    {
        // Een ronde die toch niets kan versturen hoort geen query's te kosten, en al helemaal geen
        // markeringen te zetten.
        var bank = new Storingsmelderbank(mailopties: new PortalMailOptions());
        bank.Bron.Klanten.Add(Storingsmelderbank.Klant([Storingsmelderbank.Mislukt("factuur-intake")]));

        Assert.Equal(0, await bank.RondeAsync());
        Assert.Equal(0, bank.Bron.Aanroepen);
        Assert.Equal(0, bank.Markeringen.Claims);
    }

    [Fact]
    public async Task ZonderBruikbareOntvangerWordtErNietsGelezen()
    {
        var bank = new Storingsmelderbank(new AgentAlertOptions { Recipients = ["geen adres"] });
        bank.Bron.Klanten.Add(Storingsmelderbank.Klant([Storingsmelderbank.Mislukt("factuur-intake")]));

        Assert.Equal(0, await bank.RondeAsync());
        Assert.Equal(0, bank.Bron.Aanroepen);
        Assert.Empty(bank.Verzender.Verstuurd);
    }

    [Fact]
    public async Task EenOnbruikbaarAdresHoudtDeMeldingNietTegen()
    {
        // Andersom dan bij het maandoverzicht, en met opzet: een storingsmelding die niemand bereikt
        // omdat er een tikfout in het tweede adres staat, is erger dan één die één van de twee lezers
        // bereikt.
        var bank = new Storingsmelderbank(new AgentAlertOptions
        {
            Recipients = ["storingen@soratus.com", "kapot adres"],
        });

        bank.Bron.Klanten.Add(Storingsmelderbank.Klant([Storingsmelderbank.Mislukt("factuur-intake")]));

        Assert.Equal(1, await bank.RondeAsync());
        Assert.Equal(["storingen@soratus.com"], Assert.Single(bank.Verzender.Verstuurd).Recipients);
    }

    [Fact]
    public async Task DeVlagUitLevertGeenEnkeleAanroepOp()
    {
        // Punt 41, gat 3: met de vlag alleen in ExecuteAsync is er geen test die hem kan bewijzen zonder
        // de dagelijkse lus te draaien, en die hangt met een klok die niet wacht. Vandaar dat hij óók
        // bovenaan RunAsync staat.
        var bank = new Storingsmelderbank(new AgentAlertOptions
        {
            Enabled = false,
            Recipients = ["storingen@soratus.com"],
        });

        bank.Bron.Klanten.Add(Storingsmelderbank.Klant([Storingsmelderbank.Mislukt("factuur-intake")]));

        Assert.Equal(0, await bank.RondeAsync());
        Assert.Equal(0, bank.Bron.Aanroepen);
    }

    [Fact]
    public async Task DeRemLaatDeRestVoorDeVolgendeRondeStaan()
    {
        // Valt de telemetrieopslag weg, dan zwijgt élke agent van élke klant. De rem staat vóór de
        // claim, dus wat er niet uitgaat wordt ook niet vastgelegd en komt de volgende ronde weer in
        // aanmerking.
        var bank = new Storingsmelderbank(new AgentAlertOptions
        {
            Recipients = ["storingen@soratus.com"],
            MaxMailsPerRun = 2,
        });

        for (var nummer = 0; nummer < 5; nummer++)
        {
            bank.Bron.Klanten.Add(Storingsmelderbank.Klant(
                [Storingsmelderbank.Mislukt($"agent-{nummer}")],
                $"klant-{nummer}",
                $"Klant {nummer}"));
        }

        Assert.Equal(2, await bank.RondeAsync());
        Assert.Equal(2, bank.Markeringen.Claims);

        Assert.Equal(2, await bank.RondeAsync());
        Assert.Equal(4, bank.Verzender.Verstuurd.Count);
    }

    [Fact]
    public async Task EenHersteldeAgentWordtAfgeslotenEnDaarnaMeteenOpnieuwGemeld()
    {
        var bank = new Storingsmelderbank();
        bank.Bron.Klanten.Add(Storingsmelderbank.Klant([Storingsmelderbank.Mislukt("factuur-intake")]));

        await bank.RondeAsync();

        // Hersteld.
        bank.Klok.Vooruit(TimeSpan.FromMinutes(1));
        bank.Bron.Klanten.Clear();
        bank.Bron.Klanten.Add(Storingsmelderbank.Klant([Storingsmelderbank.Gezond("factuur-intake")]));

        await bank.RondeAsync();

        Assert.Equal(1, bank.Markeringen.Afsluitingen);
        Assert.NotNull(bank.Markeringen.Document("acme-logistiek", "factuur-intake")?.ClearedAt);

        // En weer stuk, ruim binnen het herhaalvenster. Een storing die weg was en terugkomt is een
        // nieuwe storing.
        bank.Klok.Vooruit(TimeSpan.FromMinutes(1));
        bank.Bron.Klanten.Clear();
        bank.Bron.Klanten.Add(Storingsmelderbank.Klant([Storingsmelderbank.Mislukt("factuur-intake")]));

        Assert.Equal(1, await bank.RondeAsync());
        Assert.Equal(2, bank.Verzender.Verstuurd.Count);

        var markering = bank.Markeringen.Document("acme-logistiek", "factuur-intake");

        Assert.NotNull(markering);
        Assert.Null(markering.ClearedAt);

        // De teller begint opnieuw: dit is een nieuwe storingsperiode en geen tweede melding over de
        // oude. Zou hij doortellen, dan zou "sinds wanneer" naar gisteren wijzen bij een storing van een
        // minuut oud.
        Assert.Equal(1, markering.Notifications);
    }

    [Fact]
    public async Task EenKlantDieNietTeLezenWasHoudtZijnMarkering()
    {
        // Het subtiele geval. "Wij konden niet lezen" is geen bewijs dat de agent in orde is; zou de
        // markering worden afgesloten, dan geldt de storing bij de volgende ronde als nieuw en gaat er
        // weer een mail uit. Een hapering in Cosmos zou zo een mailstroom worden.
        var bank = new Storingsmelderbank();
        bank.Bron.Klanten.Add(Storingsmelderbank.Klant([Storingsmelderbank.Mislukt("factuur-intake")]));

        await bank.RondeAsync();

        bank.Klok.Vooruit(bank.Opties.RepeatAfter + TimeSpan.FromMinutes(1));
        bank.Bron.Klanten.Clear();
        bank.Bron.Klanten.Add(new CustomerAgentScan(
            "acme-logistiek",
            "Acme Logistiek",
            Agents: [],
            Unavailable: "CosmosException"));

        Assert.Equal(0, await bank.RondeAsync());
        Assert.Equal(0, bank.Markeringen.Afsluitingen);
        Assert.Null(bank.Markeringen.Document("acme-logistiek", "factuur-intake")?.ClearedAt);
    }

    [Fact]
    public async Task EenGeweigerdeVerzendingWordtNietOpnieuwGeprobeerd()
    {
        // Geen retry, ook niet bij een uitkomst die zeker "niet verstuurd" is. Een 4xx is hier vrijwel
        // altijd een inrichtingsfout en die gaat niet over binnen een minuut; elke minuut opnieuw
        // proberen zou een storing in het melden verergeren tot een storing bij de dienstverlener.
        var bank = new Storingsmelderbank();
        bank.Bron.Klanten.Add(Storingsmelderbank.Klant([Storingsmelderbank.Mislukt("factuur-intake")]));
        bank.Verzender.Uitkomst = MailDelivery.Refused;

        await bank.RondeAsync();

        Assert.Equal(
            MailDelivery.Refused,
            bank.Markeringen.Document("acme-logistiek", "factuur-intake")?.Delivery);

        bank.Klok.Vooruit(TimeSpan.FromMinutes(1));

        Assert.Equal(0, await bank.RondeAsync());
        Assert.Single(bank.Verzender.Verstuurd);
    }

    [Fact]
    public async Task EenOnbekendeUitkomstWordtOokNietOpnieuwGeprobeerd()
    {
        var bank = new Storingsmelderbank();
        bank.Bron.Klanten.Add(Storingsmelderbank.Klant([Storingsmelderbank.Mislukt("factuur-intake")]));
        bank.Verzender.Uitkomst = MailDelivery.Unknown;

        await bank.RondeAsync();

        bank.Klok.Vooruit(TimeSpan.FromMinutes(1));

        Assert.Equal(0, await bank.RondeAsync());
        Assert.Single(bank.Verzender.Verstuurd);
    }

    [Fact]
    public async Task ErWordtGeclaimdVoordatErWordtVerstuurd()
    {
        // De volgorde zelf, en niet het aantal. Claimen en versturen leveren beide een teller op, en die
        // tellers zijn hetzelfde ongeacht welke van de twee eerst gaat.
        var bank = new Storingsmelderbank();
        bank.Bron.Klanten.Add(Storingsmelderbank.Klant([Storingsmelderbank.Mislukt("factuur-intake")]));

        await bank.RondeAsync();

        // De markering staat er en draagt de uitkomst van de verzending. Dat kan alleen als de claim
        // eerst ging: de bevestiging vervangt het document dat de claim heeft geschreven.
        var markering = bank.Markeringen.Document("acme-logistiek", "factuur-intake");

        Assert.NotNull(markering);
        Assert.Equal(1, bank.Markeringen.Bevestigingen);
        Assert.Equal(Testgegevens.Nu, markering.NotifiedAt);
    }

    private static PortalMailOptions Droog()
    {
        var opties = Maandoverzichtbank.Ingericht();
        opties.DryRun = true;

        return opties;
    }
}
