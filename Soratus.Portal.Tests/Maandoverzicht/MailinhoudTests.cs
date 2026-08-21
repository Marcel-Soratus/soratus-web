using Soratus.Agents.Contracts;
using Soratus.Portal.Mail;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Maandoverzicht;

/// <summary>
/// Wat er in de mail staat, en vooral: wat er niet in staat.
/// </summary>
/// <remarks>
/// <para>Punt 13 en punt 14 van de fase-0-afwijkingen gaan over vrije tekst die door onze eigen
/// systemen is geschreven en bij een klant belandt: een stacktrace in een logbericht, een .NET-
/// typenaam in een tooltip. Beide keren stond die tekst op een <em>scherm</em>. Een mail is dezelfde
/// klasse fout met een duurdere afloop, want er is geen operator die er nog naar kijkt en er is geen
/// verversing die hem weghaalt.</para>
///
/// <para>Deze tests meten daarom niet dat de mail er goed uitziet maar dat bepaalde soorten tekst er
/// niet in kunnen komen.</para>
/// </remarks>
public class MailinhoudTests
{
    private const string Klantnaam = "Acme Logistiek";

    private static readonly StatementAddressing Adressering =
        new([Vasteportaalopslag.Beheerderadres], "Jan Acme");

    [Fact]
    public void EenStacktraceInDeKlantnaamHaaltDeEersteRegelNietVoorbij()
    {
        // Precies het geval uit punt 13: legitiem proza op de eerste regel, en daarachter zestien
        // regels met /src/-paden, klassenamen en regelnummers. Hier in het naamveld van een klant,
        // want dat is het veld dat in de onderwerpregel van een mail terechtkomt.
        var vervuild =
            "Acme Logistiek\n"
            + "   at SoratusAgent.Sync.Validate(Order order) in /src/agents/sync/Validator.cs:line 88\n"
            + "   at SoratusAgent.Sync.Run() in /src/agents/sync/Runner.cs:line 12";

        var mail = Opgemaakt(vervuild);

        Assert.DoesNotContain("/src/", mail.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("SoratusAgent", mail.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("/src/", mail.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain("/src/", mail.Html, StringComparison.Ordinal);
        Assert.Contains("Acme Logistiek", mail.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public void DeOnderwerpregelIsAltijdEenRegel()
    {
        foreach (var naam in new[]
        {
            "Acme\nLogistiek",
            "Acme\r\nLogistiek",
            "Acme\rLogistiek",
            "Acme\tLogistiek",
            "Acme\u2028Logistiek",
            "Acme\u0085Logistiek",
        })
        {
            var subject = StatementText.Subject(naam, "2026-07");

            Assert.DoesNotContain('\n', subject);
            Assert.DoesNotContain('\r', subject);
            Assert.DoesNotContain('\t', subject);
            Assert.DoesNotContain('\u2028', subject);
            Assert.DoesNotContain('\u0085', subject);
        }
    }

    [Fact]
    public void DeKnipKomtUitHetContractEnNietUitEenEigenKopie()
    {
        // Punt 13: één definitie van "één regel". Deze test bewijst dat de mailkant hem niet zelf
        // heeft nagebouwd — de markering komt letterlijk uit Soratus.Agents.Contracts.
        var geknipt = MailText.OneLine("Eerste regel\ntweede regel", 200);

        Assert.EndsWith(MessageTruncation.Marker, geknipt, StringComparison.Ordinal);
        Assert.StartsWith("Eerste regel", geknipt, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlInEenKlantnaamKomtGecodeerdInDeMail()
    {
        var mail = Opgemaakt("Acme <script>alert(1)</script> Logistiek");

        Assert.DoesNotContain("<script>", mail.Html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", mail.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void DeMailBevatGeenUrenregelsEnGeenOmschrijvingen()
    {
        var mail = Opgemaakt();

        // De omschrijving van een urenregel is vrije tekst die uit een koppeling kan komen — de
        // MCP-server neemt hem over uit een gesprek met een taalmodel. Die tekst hoort achter een
        // aanmelding te blijven waar een mens hem kan lezen en corrigeren. De mail verwijst daarom
        // naar het portaal in plaats van de regels op te nemen.
        Assert.Contains("/klant/acme-logistiek/uren?maand=2026-07", mail.PlainText, StringComparison.Ordinal);
        Assert.Contains("specificatie", mail.PlainText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeMailNoemtGeenOpslagpercentageEnGeenDienstuitsplitsing()
    {
        var mail = Opgemaakt();

        foreach (var verboden in new[]
        {
            "opslag", "beheeropslag", "%", "resource group", "subscription",
            "App Service", "Cosmos", "Log Analytics", "Key Vault",
        })
        {
            Assert.DoesNotContain(verboden, mail.PlainText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(verboden, mail.Html, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DeMailNoemtNietsOverDeFiatteringsstroom()
    {
        var mail = Opgemaakt();

        // De acceptatie van fase 3 is dat de klant niets van die stroom ziet. Een mail is de
        // makkelijkste plek om die eis alsnog te breken, want er kijkt niemand mee.
        foreach (var verboden in new[] { "fiatt", "te fiatteren", "afgewezen", "pending", "approv" })
        {
            Assert.DoesNotContain(verboden, mail.PlainText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(verboden, mail.Html, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void HetWoordOnbekendStaatNooitInEenMail()
    {
        var mail = Opgemaakt();

        // Een onbekend bedrag levert een weigering op en geen mail met een gat. Deze test dekt de
        // reparatie die zich aanbiedt: een opmaakfunctie die van null "onbekend" maakt.
        Assert.DoesNotContain("onbekend", mail.PlainText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("null", mail.PlainText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("—", mail.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void BeideLichamenNoemenDezelfdeBedragen()
    {
        var mail = Opgemaakt();

        // Een klant met afbeeldingen uit hoort niet uit een ander bedrag te lezen dan een klant met
        // afbeeldingen aan.
        foreach (var bedrag in new[] { "36,79", "250,00", "286,79" })
        {
            Assert.Contains(bedrag, mail.PlainText, StringComparison.Ordinal);
            Assert.Contains(bedrag, mail.Html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ZonderNaamStaatErBesteRelatieEnGeenAdres()
    {
        var zonderNaam = new StatementAddressing([Vasteportaalopslag.Beheerderadres], ContactName: null);

        var mail = StatementMailComposer
            .Compose(Klantnaam, Maandoverzichtbank.Volledig(), zonderNaam, "https://portal.soratus.com")
            .Mail!;

        Assert.Contains("Beste relatie,", mail.PlainText, StringComparison.Ordinal);

        // Het adres is uitdrukkelijk niet de terugvaloptie: bij twee ontvangers zou de aanhef het
        // adres van de één aan de ander verraden.
        Assert.DoesNotContain(Vasteportaalopslag.Beheerderadres, mail.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void BijTweeOntvangersStaatErGeenNaamInDeAanhef()
    {
        var twee = new StatementAddressing(
            ["directie@acme-logistiek.nl", "financien@acme-logistiek.nl"],
            ContactName: null);

        var mail = StatementMailComposer
            .Compose(Klantnaam, Maandoverzichtbank.Volledig(), twee, "https://portal.soratus.com")
            .Mail!;

        Assert.Contains("Beste relatie,", mail.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void ZonderVastgelegdeBundelStaatErGeenUrenregel()
    {
        var mail = StatementMailComposer.Compose(
                Klantnaam,
                Maandoverzichtbank.Volledig() with { BundledHours = null },
                Adressering,
                "https://portal.soratus.com")
            .Mail!;

        // Punt 19 en punt 15: geen bundel vastgelegd is niet "0 uur bundel". Er staat dan geen regel
        // in plaats van een afspraak die niet bestaat.
        Assert.DoesNotContain("in de bundel", mail.PlainText, StringComparison.Ordinal);
        Assert.Contains("286,79", mail.PlainText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null, null, StatementRefusal.AmountUnknown)]
    [InlineData(36.79, null, 286.79, StatementRefusal.AmountUnknown)]
    [InlineData(36.79, 250.00, null, StatementRefusal.AmountUnknown)]
    public void EenOntbrekendBedragLevertEenWeigeringEnGeenMail(
        double? azure,
        double? uren,
        double? totaal,
        StatementRefusal verwacht)
    {
        var uitkomst = StatementMailComposer.Compose(
            Klantnaam,
            Maandoverzichtbank.Volledig() with
            {
                AzureAmount = (decimal?)azure,
                ExtraHoursAmount = (decimal?)uren,
                Total = (decimal?)totaal,
            },
            Adressering,
            "https://portal.soratus.com");

        Assert.False(uitkomst.IsComposed);
        Assert.Equal(verwacht, uitkomst.Refusal);
    }

    [Fact]
    public void EenAdresseringZonderOntvangerLevertGeenMailOp()
    {
        // Deze test bestaat door een mutatietest. Het weghalen van de ontvangerscontrole in de
        // opmaakfunctie maakte niets rood: de weigering valt in de praktijk al eerder, bij
        // StatementRecipients.Resolve, dus de tweede controle was ongemeten. Een slot dat niemand
        // beproeft is geen slot — en dit slot is er voor een aanroeper die zelf een
        // StatementAddressing samenstelt, en die bestaat zodra iemand een tweede pad naar de mail
        // bouwt.
        var leeg = new StatementAddressing([], ContactName: null);

        var uitkomst = StatementMailComposer.Compose(
            Klantnaam,
            Maandoverzichtbank.Volledig(),
            leeg,
            "https://portal.soratus.com");

        Assert.False(uitkomst.IsComposed);
        Assert.Equal(StatementRefusal.NoRecipient, uitkomst.Refusal);
    }

    [Fact]
    public void ElkeWeigeringHeeftEenEigenNederlandseTekst()
    {
        // Zonder deze test blijft een nieuwe waarde in StatementRefusal stil zonder tekst, en dan
        // staat er op het scherm een lege melding op de plek waar hoort te staan waarom er niets is
        // gemaild.
        foreach (var refusal in Enum.GetValues<StatementRefusal>())
        {
            var tekst = StatementText.Refusal(refusal);

            Assert.False(string.IsNullOrWhiteSpace(tekst));
            Assert.EndsWith(".", tekst.Trim(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GeenEnkeleWeigeringstekstDraagtEenInterneAanduiding()
    {
        // Deze teksten staan op een operatorscherm en horen daar te blijven — maar ze zijn ook het
        // soort tekst dat iemand later "handig" in een mail zet. Ze bevatten daarom geen pad, geen
        // typenaam en geen configuratiesleutel.
        foreach (var refusal in Enum.GetValues<StatementRefusal>())
        {
            var tekst = StatementText.Refusal(refusal);

            Assert.DoesNotContain("/src/", tekst, StringComparison.Ordinal);
            Assert.DoesNotContain("Exception", tekst, StringComparison.Ordinal);
            Assert.DoesNotContain("Soratus.Portal", tekst, StringComparison.Ordinal);
        }
    }

    private static StatementMail Opgemaakt(string? klantnaam = null) =>
        StatementMailComposer.Compose(
                klantnaam ?? Klantnaam,
                Maandoverzichtbank.Volledig(),
                Adressering,
                "https://portal.soratus.com")
            .Mail
        ?? throw new InvalidOperationException(
            "De mail is niet opgemaakt, dus deze test meet niets.");
}
