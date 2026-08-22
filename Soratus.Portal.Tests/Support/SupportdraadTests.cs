using Soratus.Portal.Support;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Support;

/// <summary>
/// De draad zelf: waar de berichten staan, in welke volgorde, en wie er als afzender op komt.
/// </summary>
public class SupportdraadTests
{
    [Fact]
    public void DeDocumentsleutelSorteertChronologisch()
    {
        // Hier hangt het bladeren aan. CosmosSupportStore sorteert met ORDER BY c.id DESC en gebruikt
        // c.id < @before als grens; werkt die sleutel niet chronologisch, dan verandert die query stil
        // van "de vorige vijftig" in "vijftig willekeurige" — zonder fout en zonder logregel.
        var eerder = SupportDocumentKeys.Id(Testgegevens.Nu, "a");
        var later = SupportDocumentKeys.Id(Testgegevens.Nu.AddMilliseconds(1), "b");
        var veelLater = SupportDocumentKeys.Id(Testgegevens.Nu.AddYears(1), "c");

        Assert.True(string.CompareOrdinal(eerder, later) < 0);
        Assert.True(string.CompareOrdinal(later, veelLater) < 0);

        // Ook over een maand- en een jaargrens, want daar gaat een zelfgemaakt formaat mis: op
        // "d-M-yyyy" of zonder nulopvulling sorteert 2 vóór 10.
        Assert.True(string.CompareOrdinal(
            SupportDocumentKeys.Id(new DateTimeOffset(2026, 9, 30, 23, 59, 59, TimeSpan.Zero), "x"),
            SupportDocumentKeys.Id(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), "x")) < 0);
    }

    [Fact]
    public void DezelfdeInhoudBinnenDezelfdeMillisecondeLevertDezelfdeSleutel()
    {
        // Dat is het slot op een dubbel verstuurd formulier: static SSR, dus er is geen JavaScript dat
        // de knop uitzet. Twee klikken vallen binnen dezelfde milliseconde en krijgen dezelfde sleutel,
        // en de tweede loopt op een 409.
        Assert.Equal(
            SupportDocumentKeys.Id(Testgegevens.Nu, "klant|Jan|Draait alles?"),
            SupportDocumentKeys.Id(Testgegevens.Nu, "klant|Jan|Draait alles?"));

        Assert.NotEqual(
            SupportDocumentKeys.Id(Testgegevens.Nu, "klant|Jan|Draait alles?"),
            SupportDocumentKeys.Id(Testgegevens.Nu, "klant|Jan|Draait alles"));
    }

    [Fact]
    public async Task EenVraagVanDeKlantKrijgtAltijdDeKlantAlsAfzender()
    {
        // De afzender is geen parameter, dus er bestaat geen aanroep waarmee een klant een bericht van
        // Soratus of van de eerstelijn in zijn eigen draad zet.
        var opslag = new Vasteportaalopslag();
        var scope = await Weergavelaag.Klantscope();

        var uitkomst = await opslag.PostQuestionAsync(
            scope,
            new SupportQuestion { Author = "Jan Bakker", Text = "Draait de voorraad-sync?" });

        Assert.True(uitkomst.IsSaved);
        Assert.Equal(SupportAuthor.Customer, uitkomst.Value!.Author);
        Assert.Equal("Jan Bakker", uitkomst.Value.Who);
        Assert.Null(uitkomst.Value.GroundKind);
        Assert.Null(uitkomst.Value.Escalation);
    }

    [Fact]
    public async Task EenAntwoordVanDeOperatorKrijgtDeNaamUitDeScopeEnNietUitHetFormulier()
    {
        var opslag = new Vasteportaalopslag();
        var scope = await Weergavelaag.Schrijfscope();

        var uitkomst = await opslag.PostReplyAsync(
            scope,
            new SupportReply { Text = "De sync liep vast op een locatiecode; we pakken het op." });

        Assert.True(uitkomst.IsSaved);
        Assert.Equal(SupportAuthor.Soratus, uitkomst.Value!.Author);
        Assert.Equal(scope.Actor, uitkomst.Value.Who);
    }

    [Fact]
    public async Task EenLeegBerichtWordtGeweigerdEnNietAlsLeegDocumentVastgelegd()
    {
        var opslag = new Vasteportaalopslag();
        var scope = await Weergavelaag.Klantscope();

        var uitkomst = await opslag.PostQuestionAsync(
            scope,
            new SupportQuestion { Author = "Jan Bakker", Text = "   \n\n  " });

        Assert.False(uitkomst.IsSaved);
        Assert.Empty(opslag.Supportberichten());
    }

    [Fact]
    public async Task EenVraagZonderVastTeStellenAfzenderWordtGeweigerd()
    {
        // De naam komt uit de aanmelding. Is die er niet, dan hoort er geen bericht te komen op naam
        // van niemand — een draad waarin een bericht staat dat niemand heeft geschreven is niet te
        // lezen.
        var opslag = new Vasteportaalopslag();
        var scope = await Weergavelaag.Klantscope();

        var uitkomst = await opslag.PostQuestionAsync(
            scope,
            new SupportQuestion { Author = "  ", Text = "Draait alles?" });

        Assert.False(uitkomst.IsSaved);
        Assert.Empty(opslag.Supportberichten());
    }

    [Fact]
    public async Task DeDraadWordtOudsteEerstGelezen()
    {
        var opslag = new Vasteportaalopslag();
        var scope = await Weergavelaag.Klantscope();

        await opslag.PostQuestionAsync(scope, Vraag("Eerste"));
        await opslag.PostQuestionAsync(scope, Vraag("Tweede"));
        await opslag.PostQuestionAsync(scope, Vraag("Derde"));

        var deel = await opslag.ReadThreadAsync(scope, SupportThreadQuery.Newest());

        Assert.Equal(
            ["Eerste", "Tweede", "Derde"],
            deel.Messages.Select(m => m.Text));
        Assert.Null(deel.OlderThan);
    }

    [Fact]
    public async Task EenLangeDraadLevertHetRecentsteDeelMetEenGrensNaarHetOudere()
    {
        // Zonder de tweede vorm van SupportThreadQuery zou een lange draad zijn oudste berichten stil
        // onbereikbaar maken. Deze test legt vast dat ze te bereiken zijn én dat het recentste deel
        // begrensd is.
        var opslag = new Vasteportaalopslag();
        var scope = await Weergavelaag.Klantscope();

        var totaal = SupportThreadQuery.PageSize + 5;

        for (var i = 1; i <= totaal; i++)
        {
            await opslag.PostQuestionAsync(scope, Vraag($"Bericht {i:D3}"));
        }

        var nieuwste = await opslag.ReadThreadAsync(scope, SupportThreadQuery.Newest());

        Assert.Equal(SupportThreadQuery.PageSize, nieuwste.Messages.Count);
        Assert.Equal($"Bericht {totaal:D3}", nieuwste.Messages[^1].Text);
        Assert.NotNull(nieuwste.OlderThan);

        var ouder = await opslag.ReadThreadAsync(
            scope,
            SupportThreadQuery.Before(nieuwste.OlderThan));

        Assert.Equal(5, ouder.Messages.Count);
        Assert.Equal("Bericht 001", ouder.Messages[0].Text);
        Assert.Null(ouder.OlderThan);

        // En geen overlap: het oudere deel eindigt precies waar het nieuwere begint.
        Assert.Equal(nieuwste.Messages[0].Text, $"Bericht {6:D3}");
        Assert.Equal("Bericht 005", ouder.Messages[^1].Text);
    }

    [Fact]
    public async Task EenVerzonnenGrensLevertEenLeegDeelOpEnGeenFout()
    {
        // De grens komt uit de adresbalk. Hij gaat als parameter naar Cosmos en wordt daar vergeleken,
        // niet samengevoegd; een verzonnen waarde hoort dus niets op te leveren en niet te werpen.
        var opslag = new Vasteportaalopslag();
        var scope = await Weergavelaag.Klantscope();

        await opslag.PostQuestionAsync(scope, Vraag("Draait alles?"));

        var leeg = await opslag.ReadThreadAsync(scope, SupportThreadQuery.Before("aaa-niets"));
        Assert.Empty(leeg.Messages);

        // En een lege grens valt terug op het recentste deel, want dat is wat iemand met een afgekapte
        // link bedoelde.
        var terugval = await opslag.ReadThreadAsync(scope, SupportThreadQuery.Before("   "));
        Assert.Single(terugval.Messages);
    }

    [Fact]
    public async Task DeDraadVanEenAndereKlantKomtHierNietUit()
    {
        // De partitiesleutel komt uit de scope. Er is geen aanroep waarmee je met de scope van klant A
        // de draad van klant B leest, en deze test bewijst dat de fixture dat ook zo doet — anders zou
        // elke andere test in dit bestand groen staan om de verkeerde reden.
        var opslag = new Vasteportaalopslag();
        var scope = await Weergavelaag.Klantscope();

        opslag.ZetSupportbericht(
            Bericht("Bericht van een andere klant", SupportAuthor.Customer),
            klant: "bakker-bv");

        var deel = await opslag.ReadThreadAsync(scope, SupportThreadQuery.Newest());

        Assert.Empty(deel.Messages);
    }

    private static SupportQuestion Vraag(string tekst) =>
        new() { Author = "Jan Bakker", Text = tekst };

    internal static SupportMessageDocument Bericht(
        string tekst,
        SupportAuthor afzender,
        SupportGroundKind? kind = null,
        string? key = null,
        SupportEscalation? escalatie = null,
        string? wie = "Jan Bakker",
        DateTimeOffset? wanneer = null,
        string klant = Vasteportaalopslag.Standaardklant)
    {
        var moment = wanneer ?? Testgegevens.Nu;

        return new SupportMessageDocument
        {
            Id = SupportDocumentKeys.Id(moment, $"{afzender}|{tekst}"),
            PartitionKey = klant,
            CustomerId = klant,
            Author = afzender,
            Who = wie,
            Text = tekst,
            GroundKind = kind,
            GroundKey = key,
            Escalation = escalatie,
            CreatedAt = moment,
        };
    }
}
