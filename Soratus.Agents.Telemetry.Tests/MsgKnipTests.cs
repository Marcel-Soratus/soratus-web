using System.Buffers;
using System.Text;
using Soratus.Agents.Contracts;

namespace Soratus.Agents.Telemetry.Tests;

/// <summary>
/// De knip op <c>msg</c>: alleen de eerste regel blijft over, de rest gaat naar
/// <c>extra.msgOverflow</c>.
/// </summary>
/// <remarks>
/// <para>Deze regel bestaat omdat <c>msg</c> door de klant wordt gelezen en <c>extra</c> niet. Een
/// verificatie over negentien agents vond een <c>payload.dump</c> met zestien regels stacktrace in
/// <c>msg</c> — bronpaden, klasse- en methodenamen, zichtbaar voor een klant. Aan de leeskant is
/// dat niet te dichten: de inhoud kan in elk vrij tekstveld staan, niet alleen in het veld dat je
/// afschermt.</para>
///
/// <para>Het mechanisme is de regelovergang en niet de lengte. Dat is geen detail maar het hele
/// punt: gemeten over de 93 klantzichtbare logregels was de langste legitieme eerste regel 1417
/// tekens. Elke lengtegrens die een stacktrace tegenhoudt verminkt dus ook geldig proza, en elke
/// grens die het proza spaart laat de stacktrace er deels door. Vandaar
/// <see cref="LaatEenLangeRegelZonderRegelovergangOngemoeid"/> — zonder die test kapt iemand later
/// "voor de zekerheid" alsnog op lengte, en dan is de maatregel omgeslagen in het probleem.</para>
/// </remarks>
public class MsgKnipTests
{
    /// <summary>
    /// De grens waarmee getest wordt is de echte standaardwaarde, niet een kopie ervan.
    /// </summary>
    /// <remarks>
    /// Hier stond <c>8_000</c>. Dat leest hetzelfde en test iets anders: zou iemand
    /// <see cref="MessageTruncation.DefaultMaxLength"/> wijzigen, dan bleef deze suite de oude
    /// grens uitoefenen en groen staan, terwijl de bibliotheek zich anders gedraagt. Een test die
    /// zijn invoer hardcodeert, meet niet meer waar hij over beweert.
    /// </remarks>
    private const int Grens = MessageTruncation.DefaultMaxLength;

    private const string Frame =
        "   at Soratus.Sync.Validators.StockLineValidator.Validate(StockLine line) in /src/Sync/StockLineValidator.cs:line 42";

    /// <summary>De ontlede vorm van "é": een gewone e plus een combineerteken.</summary>
    private const string OntledeE = "é";

    [Fact]
    public void KniptOpDeEersteRegelovergang()
    {
        (string bericht, string? overloop) = MessageTruncation.Cut(
            "De voorraadregels konden niet worden gevalideerd.\n" + Frame + "\n" + Frame,
            Grens);

        Assert.Equal("De voorraadregels konden niet worden gevalideerd." + MessageTruncation.Marker, bericht);
        Assert.Equal(Frame + "\n" + Frame, overloop);
    }

    [Fact]
    public void LaatGeenSpoorVanDeStacktraceInHetBericht()
    {
        (string bericht, _) = MessageTruncation.Cut("Een zin.\n" + Frame, Grens);

        Assert.DoesNotContain("/src/", bericht, StringComparison.Ordinal);
        Assert.DoesNotContain("at Soratus", bericht, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void KniptOpElkeVormVanRegelovergang(string overgang)
    {
        (string bericht, string? overloop) = MessageTruncation.Cut("Een zin." + overgang + Frame, Grens);

        Assert.Equal("Een zin." + MessageTruncation.Marker, bericht);
        Assert.Equal(Frame, overloop);
    }

    [Fact]
    public void LaatEenLangeRegelZonderRegelovergangOngemoeid()
    {
        // 1417 is de langste legitieme eerste regel die in de opslag is gemeten.
        string proza = new('a', 1_417);

        (string bericht, string? overloop) = MessageTruncation.Cut(proza, Grens);

        Assert.Equal(proza, bericht);
        Assert.Null(overloop);
    }

    [Fact]
    public void LaatEenRegelTotAanDeHygienegrensOngemoeid()
    {
        string proza = new('a', Grens);

        (string bericht, string? overloop) = MessageTruncation.Cut(proza, Grens);

        Assert.Equal(proza, bericht);
        Assert.Null(overloop);
    }

    [Fact]
    public void LaatEenNormaleZinOngemoeid()
    {
        (string bericht, string? overloop) = MessageTruncation.Cut("Factuur INV-2291 verwerkt.", Grens);

        Assert.Equal("Factuur INV-2291 verwerkt.", bericht);
        Assert.Null(overloop);
    }

    [Theory]
    [InlineData("Factuur INV-2291 verwerkt.\n")]
    [InlineData("Factuur INV-2291 verwerkt.\r\n")]
    [InlineData("Factuur INV-2291 verwerkt.\n\n\n")]
    public void EenAfsluitendeRegelovergangIsGeenOverloop(string bericht)
    {
        // Zonder deze regel zou elk bericht dat toevallig op een newline eindigt een misleidende
        // "(ingekort)" krijgen, en zou het uitklappaneel een lege overloop tonen.
        (string resultaat, string? overloop) = MessageTruncation.Cut(bericht, Grens);

        Assert.Equal("Factuur INV-2291 verwerkt.", resultaat);
        Assert.Null(overloop);
    }

    [Fact]
    public void HoudtDeRegelafbrekingenInDeOverloopOnveranderd()
    {
        (_, string? overloop) = MessageTruncation.Cut("Een zin.\nEerste\r\nTweede\rDerde", Grens);

        Assert.Equal("Eerste\r\nTweede\rDerde", overloop);
    }

    [Fact]
    public void GeeftEenLeegBerichtEenLeesbareWaarde()
    {
        Assert.Equal("(geen bericht)", MessageTruncation.Cut(null, Grens).Message);
        Assert.Equal("(geen bericht)", MessageTruncation.Cut(string.Empty, Grens).Message);
    }

    [Fact]
    public void KniptNietMiddenInEenSurrogaatpaar()
    {
        // Elke noot is twee UTF-16-tekens. Een knip op een oneven plek laat een losse surrogaat
        // achter; dat is ongeldige UTF-16 en breekt de serialisatie of de weergave. Dit defect is
        // aan de weergavekant al één keer echt aangetroffen, in een `message[..400]`.
        string noten = string.Concat(Enumerable.Repeat("\U0001D11E", Grens));

        (string bericht, string? overloop) = MessageTruncation.Cut(noten, Grens);

        Assert.NotNull(overloop);
        string kop = bericht[..^MessageTruncation.Marker.Length];
        Assert.Equal(OperationStatus.Done, Rune.DecodeLastFromUtf16(kop, out _, out _));
        Assert.Equal(OperationStatus.Done, Rune.DecodeFromUtf16(overloop, out _, out _));
    }

    [Fact]
    public void KniptNietMiddenInEenSamengesteldeGlyph()
    {
        // Twee UTF-16-tekens die samen één glyph vormen. Knippen tussen de twee levert een andere
        // letter dan er stond. Met opzet de ontlede vorm: de vooraf samengestelde U+00E9 is één
        // teken en toont niets aan.
        string accenten = string.Concat(Enumerable.Repeat(OntledeE, Grens));

        (string bericht, _) = MessageTruncation.Cut(accenten, Grens);

        string kop = bericht[..^MessageTruncation.Marker.Length];
        Assert.Equal(0, kop.Length % 2);
        Assert.Equal('e', kop[^2]);
        Assert.Equal('́', kop[^1]);
    }

    [Fact]
    public void HoudtHetBerichtBinnenDeHygienegrens()
    {
        (string bericht, _) = MessageTruncation.Cut(new string('a', (Grens * 3) + 1), Grens);

        Assert.True(bericht.Length <= Grens, $"bericht was {bericht.Length} tekens");
        Assert.EndsWith(MessageTruncation.Marker, bericht, StringComparison.Ordinal);
    }

    [Fact]
    public void ZetBijEenDubbeleKnipDeHeleRestInEenDoorlopendeOverloop()
    {
        // Slaan de hygiënegrens en de regelovergang beide toe, dan is de overloop één
        // aaneengesloten stuk van de oorspronkelijke tekst — geen twee helften die weer aan elkaar
        // zijn geplakt, want dan zou er een regelovergang bij komen die er niet stond.
        string eersteRegel = new('a', Grens + 500);
        string origineel = eersteRegel + "\n" + Frame;

        (string bericht, string? overloop) = MessageTruncation.Cut(origineel, Grens);

        Assert.NotNull(overloop);
        Assert.Equal(origineel, bericht[..^MessageTruncation.Marker.Length] + overloop);
    }

    [Fact]
    public void ShortenLaatEenTekstBinnenDeGrensOngemoeid()
    {
        Assert.Equal("Eerste\nTweede", MessageTruncation.Shorten("Eerste\nTweede", 100));
    }

    [Fact]
    public void ShortenBehoudtRegelovergangen()
    {
        // Shorten is voor tekst die meerregelig mág zijn — de overloop van Cut is dat geval.
        string lang = string.Join('\n', Enumerable.Repeat(Frame, 200));

        string kort = MessageTruncation.Shorten(lang, 1_000);

        Assert.True(kort.Length <= 1_000, $"was {kort.Length}");
        Assert.Contains('\n', kort);
        Assert.EndsWith(MessageTruncation.Marker, kort, StringComparison.Ordinal);
    }

    [Fact]
    public void ShortenKniptNietMiddenInEenSurrogaatpaar()
    {
        // Dit is de plek waar de begrenzing van de overloop de fout maakte die Cut juist voorkomt:
        // een ruwe value[..max] halveert hier een noot van twee UTF-16-tekens.
        string noten = string.Concat(Enumerable.Repeat("\U0001D11E", 2_000));

        string kort = MessageTruncation.Shorten(noten, 501);

        Assert.True(kort.Length <= 501, $"was {kort.Length}");
        Assert.Equal(
            OperationStatus.Done,
            Rune.DecodeLastFromUtf16(kort.AsSpan(0, kort.Length - MessageTruncation.Marker.Length), out _, out _));
    }

    [Fact]
    public void ShortenKniptNietMiddenInEenSamengesteldeGlyph()
    {
        string accenten = string.Concat(Enumerable.Repeat(OntledeE, 2_000));

        string kop = MessageTruncation.Shorten(accenten, 501)[..^MessageTruncation.Marker.Length];

        Assert.Equal(0, kop.Length % 2);
        Assert.Equal('e', kop[^2]);
        Assert.Equal('́', kop[^1]);
    }

    [Fact]
    public void DeAssertieBijHetOpstartenSlaagt()
    {
        // Deze assertie loopt bij elke start van elke agent. Slaat hij om, dan valt elke agent om
        // in plaats van stil een stacktrace naar een klant te schrijven.
        MessageTruncation.AssertContract();
    }
}
