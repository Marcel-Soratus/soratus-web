using Soratus.Agents.Contracts;
using Soratus.Portal.Alerts;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Storingsmelder;

/// <summary>
/// Wat er in een storingsmelding staat, en waarom dat meer is dan in een klantmail mag.
/// </summary>
/// <remarks>
/// <para><strong>Deze tests staan er om het tegenovergestelde vast te leggen van
/// <c>MailinhoudTests</c>.</strong> Daar wordt gemeten dat er géén foutmelding, géén typenaam en géén
/// intern gegeven in de mail komt; hier dat ze er wél in staan. De grond is de koppelingentabel bij §5:
/// storingsmeldingen gaan naar Soratus en het maandoverzicht naar de klant. Zou iemand hier ooit
/// "voorzichtigheid" toevoegen, dan haalt hij precies de informatie weg waarvoor de mail bestaat, en
/// deze tests worden dan rood in plaats van dat het niemand opvalt.</para>
/// </remarks>
public class MeldinginhoudTests
{
    [Fact]
    public void DeMeldingDraagtDeVolledigeFoutmeldingEnHetVolledigeFouttype()
    {
        var mail = Meld([Storingsmelderbank.Mislukt("factuur-intake")]);

        // De volledige naamruimte en niet de korte naam. Punt 14 met de andere lezer ervoor:
        // Sync.ValidationException en Mail.ValidationException zijn twee verschillende defecten, en de
        // korte naam gooit juist het nuttige deel weg.
        Assert.Contains("SoratusAgent.Sync.ValidationException", mail.PlainText, StringComparison.Ordinal);
        Assert.Contains("SoratusAgent.Sync.ValidationException", mail.Html, StringComparison.Ordinal);
        Assert.Contains("Regel 41 mist een grootboekrekening.", mail.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void EenStacktraceInDeFoutmeldingWordtNietAfgeknipt()
    {
        // Punt 13 knipt msg af op de eerste regelovergang omdat de klant hem leest. Hier is er geen
        // klant, en dan is een stacktrace precies wat een operator nodig heeft. De knip hoort dus niet
        // op dit pad te staan, en deze test is de enige plek waar dat vastligt.
        var stapel = "De inlezing is mislukt.\n"
            + "   at SoratusAgent.Sync.Validate(Row row) in /src/Sync/Validate.cs:line 41\n"
            + "   at SoratusAgent.Sync.Run(CancellationToken token) in /src/Sync/Run.cs:line 12";

        var mislukt = Storingsmelderbank.Mislukt("factuur-intake");

        var mail = Meld(
        [
            mislukt with { LastCompletedRun = mislukt.LastCompletedRun! with { ErrorMessage = stapel } },
        ]);

        Assert.Contains("/src/Sync/Validate.cs:line 41", mail.PlainText, StringComparison.Ordinal);
        Assert.Contains("/src/Sync/Run.cs:line 12", mail.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void DeMeldingNoemtDeAgentDeKlantEnHetPortaaladres()
    {
        var mail = Meld([Storingsmelderbank.Mislukt("factuur-intake")]);

        Assert.Contains("factuur-intake", mail.Subject, StringComparison.Ordinal);
        Assert.Contains("Acme Logistiek", mail.Subject, StringComparison.Ordinal);
        Assert.Contains(
            "https://portal.soratus.com/klant/acme-logistiek/agents/factuur-intake",
            mail.PlainText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BijDrieDienstenNoemtDeMeldingDatHetEenProcesIs()
    {
        // Zonder die zin leest een operator drie storingen in plaats van één oorzaak, en dan gaat hij
        // drie dingen zoeken.
        var start = Testgegevens.Nu - TimeSpan.FromHours(3);

        var mail = Meld(
        [
            Storingsmelderbank.Zwijgt("boekhoud-chat", start),
            Storingsmelderbank.Zwijgt("financieel-overzicht", start),
            Storingsmelderbank.Zwijgt("declaraties-import", start),
        ]);

        Assert.Contains("3 diensten in één host", mail.Subject, StringComparison.Ordinal);
        Assert.Contains("hetzelfde proces", mail.PlainText, StringComparison.Ordinal);
        Assert.Contains("één oorzaak", mail.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void DeMeldingNoemtDeStarttijdVanHetProces()
    {
        // Het diagnostische paar uit punt 42: schuift die tijd bij elke melding op, dan wordt het proces
        // telkens uitgeladen (Always On); blijft hij staan terwijl de hartslag stokt, dan is er iets mis
        // in het proces zelf.
        var mail = Meld([Storingsmelderbank.Zwijgt("boekhoud-chat")]);

        Assert.Contains("Het proces draait sinds", mail.PlainText, StringComparison.Ordinal);
        Assert.Contains("Schuift die tijd bij elke melding mee", mail.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void DeMeldingZegtWanneerHijZichHerhaalt()
    {
        // Zonder die regel weet een lezer niet of het stilvallen van de meldingen betekent dat de storing
        // over is of dat de melder is gestopt.
        var mail = Meld([Storingsmelderbank.Mislukt("factuur-intake")]);

        Assert.Contains("komt deze melding elke", mail.PlainText, StringComparison.Ordinal);
        Assert.Contains("Bij herstel volgt er geen bericht", mail.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void ErStaatGeenRelatieveTijdInDeMelding()
    {
        // Op het scherm is "11 min geleden" het juiste; in een postbus is het onwaar zodra de mail een
        // uur ongelezen blijft. De absolute tijd staat er wel.
        var mail = Meld([Storingsmelderbank.Mislukt("factuur-intake")]);

        Assert.DoesNotContain("geleden", mail.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain("geleden", mail.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void BeideLichamenDragenDezelfdeFeiten()
    {
        // Uit één lijst en niet uit twee opmaakfuncties. Zou elk lichaam zijn eigen feiten samenstellen,
        // dan hangt het van de postbus af wat een operator te zien krijgt.
        var mail = Meld([Storingsmelderbank.Mislukt("factuur-intake")]);

        foreach (var feit in new[] { "Klant", "Type", "Versie", "Zwijgt", "RunId", "Fouttype", "Foutmelding" })
        {
            Assert.Contains(feit, mail.PlainText, StringComparison.Ordinal);
            Assert.Contains(feit, mail.Html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EenAgentZonderFoutmeldingKrijgtGeenLegeRegel()
    {
        // Een regel "Foutmelding —" bij een agent die er geen heeft, is een regel die zegt dat er iets
        // ontbreekt waar niets hoort te staan.
        var mail = Meld([Storingsmelderbank.Zwijgt("boekhoud-chat")]);

        Assert.DoesNotContain("Fouttype", mail.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain("Foutmelding", mail.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void DeHtmlCodeertElkeIngevoegdeWaarde()
    {
        var mislukt = Storingsmelderbank.Mislukt("factuur-intake");

        var mail = Meld(
            [
                mislukt with
                {
                    LastCompletedRun = mislukt.LastCompletedRun! with
                    {
                        ErrorMessage = "<script>alert(1)</script>",
                    },
                },
            ]);

        Assert.DoesNotContain("<script>", mail.Html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", mail.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void DeKlantnaamWordtOpDeEersteRegelAfgeknipt()
    {
        // De onderwerpregel is één regel, en dat is dezelfde knip als bij het maandoverzicht — één
        // definitie, in MailText, en niet een tweede hier.
        var mail = AgentAlertComposer.Compose(
            new AgentFaultGroup(
                "acme-logistiek",
                "Acme Logistiek\nregel twee die er niet hoort te staan",
                Testgegevens.Nu - TimeSpan.FromHours(3),
                Groep([Storingsmelderbank.Mislukt("factuur-intake")])),
            ["storingen@soratus.com"],
            Testgegevens.Nu,
            "https://portal.soratus.com",
            TimeSpan.FromHours(6));

        Assert.DoesNotContain("regel twee", mail.Subject, StringComparison.Ordinal);
        Assert.Contains("Acme Logistiek", mail.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public void ZonderOntvangerWordtErNietsOpgemaakt() =>
        Assert.Throws<ArgumentException>(() => AgentAlertComposer.Compose(
            new AgentFaultGroup(
                "acme-logistiek",
                "Acme Logistiek",
                Testgegevens.Nu,
                Groep([Storingsmelderbank.Mislukt("factuur-intake")])),
            [],
            Testgegevens.Nu,
            "https://portal.soratus.com",
            TimeSpan.FromHours(6)));

    private static AgentAlertMail Meld(IReadOnlyList<Data.AgentSnapshot> agents)
    {
        var groep = Assert.Single(
            AgentFaults.From([Storingsmelderbank.Klant(agents)], Testgegevens.Nu));

        return AgentAlertComposer.Compose(
            groep,
            ["storingen@soratus.com"],
            Testgegevens.Nu,
            "https://portal.soratus.com",
            TimeSpan.FromHours(6));
    }

    private static IReadOnlyList<AgentFault> Groep(IReadOnlyList<Data.AgentSnapshot> agents) =>
        AgentFaults.From([Storingsmelderbank.Klant(agents)], Testgegevens.Nu)[0].Faults;
}
