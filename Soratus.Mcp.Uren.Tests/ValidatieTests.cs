using Soratus.Mcp.Uren;

namespace Soratus.Mcp.Uren.Tests;

/// <summary>
/// Wat er wordt geweigerd voordat er iets de deur uit gaat.
/// </summary>
/// <remarks>
/// <para>Dezelfde regel die het seed-gereedschap volgt: een fout in de invoer wordt gemeld en er
/// wordt niets weggeschreven. Bij uren is dat zwaarder dan bij telemetrie — een verkeerde urenregel
/// gaat op een factuur, en iemand moet hem terugvinden en afwijzen.</para>
///
/// <para>Let op wat hier <em>niet</em> wordt getest, want dat is een besluit en geen gat: of een
/// klant of een categorie bestáát. Dat weet alleen het portaal, en die vraag hoort daar te worden
/// gesteld. Zie <see cref="DeCategorielijstStaatNietInDitProject"/>.</para>
/// </remarks>
public class ValidatieTests
{
    private static readonly DateTimeOffset Nu = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<string> Keur(
        string? klant = "bakker",
        string? maand = "2026-08",
        decimal uren = 3.5m,
        string? categorie = "Ontwikkeling",
        string? omschrijving = "Koppeling met de voorraadservice afgemaakt.",
        IReadOnlyList<string>? toegestaan = null) =>
        HourBookingValidation.Check(
            new HourBookingInput(klant, maand, uren, categorie, omschrijving),
            toegestaan ?? [],
            Nu);

    [Fact]
    public void EenGeldigeBoekingLevertGeenEnkeleMelding()
    {
        Assert.Empty(Keur());
    }

    [Fact]
    public void EenBedrijfsnaamInPlaatsVanEenSlugWordtGeweigerd()
    {
        // De plausibelste vergissing van een taalmodel: het kent de bedrijfsnaam uit het gesprek en
        // niet de slug uit de URL. De melding moet daarom zeggen waar de slug te vinden is.
        string melding = Assert.Single(Keur(klant: "Bakker Techniek B.V."));

        Assert.Contains("geen klantslug", melding, StringComparison.Ordinal);
        Assert.Contains("/klant/", melding, StringComparison.Ordinal);
    }

    [Fact]
    public void OfEenKlantBestaatIsEenVraagVoorHetPortaal()
    {
        // 'kroon' heeft de vorm van een slug. Of die klant bestaat weet deze kant niet, en gokken zou
        // erger zijn dan doorlaten: een lokale klantenlijst die achterloopt weigert een echte klant.
        Assert.Empty(Keur(klant: "kroon"));
    }

    [Fact]
    public void DeLokaleKlantbeperkingHoudtEenAndereKlantTegenEnZegtDatHijLokaalIs()
    {
        string melding = Assert.Single(Keur(klant: "vandijk", toegestaan: ["bakker"]));

        Assert.Contains("'bakker'", melding, StringComparison.Ordinal);
        // De melding moet eerlijk zijn over waar de grens staat: op deze machine, niet in het
        // portaal. Anders leest iemand hier een autorisatiegarantie die er niet is.
        Assert.Contains("op deze machine", melding, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("augustus")]
    [InlineData("08-2026")]
    [InlineData("2026-8")]
    [InlineData("2026-08-20")]
    [InlineData("2026/08")]
    [InlineData("2026-13")]
    public void EenMaandDieGeenMaandIsWordtGeweigerd(string maand)
    {
        string melding = Assert.Single(Keur(maand: maand));

        Assert.Contains("geen maand", melding, StringComparison.Ordinal);
        Assert.Contains("2026-08", melding, StringComparison.Ordinal);
    }

    [Fact]
    public void DeHuidigeMaandMag()
    {
        Assert.Empty(Keur(maand: "2026-08"));
    }

    [Fact]
    public void EenMaandInDeToekomstWordtGeweigerd()
    {
        string melding = Assert.Single(Keur(maand: "2026-09"));

        Assert.Contains("in de toekomst", melding, StringComparison.Ordinal);
    }

    [Fact]
    public void EenVorigeMaandMagWel()
    {
        // Uren van vorige maand naboeken is gewoon werk. Alleen een jaartal dat niet kan is fout.
        Assert.Empty(Keur(maand: "2026-05"));
    }

    [Fact]
    public void EenTypefoutInHetJaartalWordtGeweigerdMetEenVoorstel()
    {
        string melding = Assert.Single(Keur(maand: "2016-08"));

        Assert.Contains("2026-08", melding, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-8.5)]
    public void NulOfNegatieveUrenWordenGeweigerd(decimal uren)
    {
        string melding = Assert.Single(Keur(uren: uren));

        Assert.Contains("uren:", melding, StringComparison.Ordinal);
        Assert.Contains("meer dan nul", melding, StringComparison.Ordinal);
    }

    [Fact]
    public void EenCijferTeVeelWordtGeweigerd()
    {
        string melding = Assert.Single(Keur(uren: 350m));

        Assert.Contains("168 uur", melding, StringComparison.Ordinal);
    }

    [Fact]
    public void DrieDecimalenWordenGeweigerdEnNietStilAfgerond()
    {
        // Stil afronden verandert een bedrag zonder dat iemand het heeft gezien. Dat is precies het
        // soort stille onwaarheid waar deze server voor bestaat.
        string melding = Assert.Single(Keur(uren: 3.333m));

        Assert.Contains("decimalen", melding, StringComparison.Ordinal);
    }

    [Fact]
    public void TweeDecimalenMogen()
    {
        Assert.Empty(Keur(uren: 3.25m));
    }

    [Fact]
    public void EenLegeCategorieWordtGeweigerdEnVerwijstNaarHetPortaal()
    {
        string melding = Assert.Single(Keur(categorie: "   "));

        Assert.Contains("categorie:", melding, StringComparison.Ordinal);
        Assert.Contains("geen eigen lijst", melding, StringComparison.Ordinal);
    }

    [Fact]
    public void DeCategorielijstStaatNietInDitProject()
    {
        // Het portaal is de eigenaar (HourCategories.Bookable / IsBookable). Een gekopieerde lijst die
        // hier zou valideren, weigert bij achterlopen een geldige boeking of laat een categorie door
        // die net is afgeschaft — het verkeerde antwoord met gezag. In de tóolbeschrijving mag de lijst
        // wél staan: die kost bij achterlopen één afwijzing en herstelt zichzelf.
        Assert.Empty(Keur(categorie: "Iets Wat Niet Bestaat"));
        Assert.Empty(Keur(categorie: "ontwikkeling"));
        Assert.Empty(Keur(categorie: "Correctie"));
    }

    [Fact]
    public void EenHeleOmschrijvingInHetCategorieveldWordtGeweigerd()
    {
        // Dit is een vormtoets en geen lijsttoets: een tekst van honderd tekens is geen categorie, en
        // dat hoeft geen netwerkverzoek te kosten.
        string melding = Assert.Single(Keur(
            categorie: new string('a', HourBookingValidation.MaxCategoryLength + 1)));

        Assert.Contains("geen categorie maar een tekst", melding, StringComparison.Ordinal);
    }

    [Fact]
    public void EenLegeOmschrijvingWordtGeweigerd()
    {
        string melding = Assert.Single(Keur(omschrijving: "   "));

        Assert.Contains("omschrijving:", melding, StringComparison.Ordinal);
        Assert.Contains("fiatteren", melding, StringComparison.Ordinal);
    }

    [Fact]
    public void EenOmschrijvingMetMeerDanEenRegelWordtGeweigerdEnNietAfgeknipt()
    {
        // Anders dan in het agentcontract, waar de bibliotheek knipt. Daar is de schrijver een
        // achtergrondproces dat niet kan worden gevraagd het over te doen, en verhuist de overloop
        // naar extra. Hier zit er een aanroeper aan de andere kant, en een urenregel heeft geen veld
        // om de rest in te bewaren — dan is knippen informatie weggooien.
        string melding = Assert.Single(Keur(
            omschrijving: "Voorraadsync afgemaakt.\n   at Soratus.Sync.Validate() in /src/Sync.cs:line 42"));

        Assert.Contains("meer dan één regel", melding, StringComparison.Ordinal);
        Assert.Contains("geweigerd", melding, StringComparison.Ordinal);
    }

    [Fact]
    public void EenTeLangeOmschrijvingWordtGeweigerd()
    {
        string melding = Assert.Single(Keur(
            omschrijving: new string('a', HourBookingValidation.MaxNoteLength + 1)));

        Assert.Contains("tekens", melding, StringComparison.Ordinal);
    }

    [Fact]
    public void AlleFoutenKomenInEenKeerTerug()
    {
        // Drie keer heen en weer voor drie fouten in dezelfde aanroep kost drie keer een mens die
        // wacht.
        IReadOnlyList<string> meldingen = Keur(
            klant: "Bakker B.V.",
            maand: "augustus",
            uren: 0m,
            categorie: "",
            omschrijving: "");

        Assert.Equal(5, meldingen.Count);
    }

    [Fact]
    public void HetVerzoekNormaliseertDeKlantslugEnKniptWitruimteWeg()
    {
        HourBookingRequest verzoek = HourBookingValidation.ToRequest(
            new HourBookingInput("  BAKKER  ", " 2026-08 ", 3.5m, " Ontwikkeling ", "  Iets gedaan.  "));

        Assert.Equal("bakker", verzoek.CustomerId);
        Assert.Equal("2026-08", verzoek.Month);
        Assert.Equal("Ontwikkeling", verzoek.Category);
        Assert.Equal("Iets gedaan.", verzoek.Note);
    }

    [Fact]
    public void ToRequestWeigertEenBoekingDieNietDoorCheckIsGegaan()
    {
        Assert.Throws<InvalidOperationException>(() =>
            HourBookingValidation.ToRequest(new HourBookingInput("bakker", "2026-08", 1m, "Beheer", "")));
    }
}
