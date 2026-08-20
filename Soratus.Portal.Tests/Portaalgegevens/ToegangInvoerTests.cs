using Soratus.Portal.Data;

namespace Soratus.Portal.Tests.Portaalgegevens;

/// <summary>
/// Toegang uitdelen: het e-mailadres en de aanduiding binnen de klant (§3.5).
/// </summary>
/// <remarks>
/// De controle op het adres is opzettelijk krap en niet slim: het adres moet straks in Entra bestaan
/// en dát is de echte toets. Wat hier wordt tegengehouden zijn de fouten die een document met een
/// onbruikbare sleutel opleveren — de sleutel is namelijk het adres.
/// </remarks>
public class ToegangInvoerTests
{
    // ── Het adres wordt de documentsleutel ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("Planning@BakkerLogistiek.nl", "planning@bakkerlogistiek.nl")]
    [InlineData("  jan@x.nl  ", "jan@x.nl")]
    [InlineData("JAN@X.NL", "jan@x.nl")]
    [InlineData(null, "")]
    public void EenAdresWordtNaarEenVormGenormaliseerd(string? ingetypt, string verwacht) =>
        Assert.Equal(verwacht, PortalEmail.Normalize(ingetypt));

    [Fact]
    public void TweeSchrijfwijzenVanHetzelfdeAdresLeverenEenSleutelOp()
    {
        // Zouden ze twee sleutels opleveren, dan bestaan er twee toegangen voor één persoon en doet
        // er één intrekken niets. Entra vergelijkt hoofdletterongevoelig, dus dan zou hij binnen
        // blijven komen via de regel die niemand ziet.
        var eerste = PortalDocumentIds.Access(PortalEmail.Normalize("Jan@X.nl"));
        var tweede = PortalDocumentIds.Access(PortalEmail.Normalize("jan@x.NL"));

        Assert.Equal(eerste, tweede);
    }

    [Theory]
    [InlineData("planning@bakkerlogistiek.nl")]
    [InlineData("jan.de.vries@x.co.uk")]
    [InlineData("a@b.nl")]
    [InlineData("jan+portaal@x.nl")]
    public void EenGewoonAdresKlopt(string email) =>
        Assert.Null(PortalEmail.Validate(email));

    [Theory]
    [InlineData(null, "leeg")]
    [InlineData("", "leeg")]
    [InlineData("   ", "witruimte")]
    [InlineData("jan", "geen @")]
    [InlineData("@x.nl", "niets voor de @")]
    [InlineData("jan@", "niets achter de @")]
    [InlineData("jan@@x.nl", "twee @-tekens")]
    [InlineData("jan@x@y.nl", "twee @-tekens")]
    [InlineData("jan@localhost", "domein zonder punt")]
    [InlineData("jan de vries@x.nl", "spatie")]
    [InlineData("jan/x@y.nl", "schuine streep — verboden in een Cosmos-id")]
    [InlineData("jan\\x@y.nl", "backslash — verboden in een Cosmos-id")]
    [InlineData("jan#x@y.nl", "hekje — verboden in een Cosmos-id")]
    [InlineData("jan?x@y.nl", "vraagteken — verboden in een Cosmos-id")]
    public void EenAdresDatGeenSleutelKanZijnWordtGeweigerd(string? email, string waarom)
    {
        Assert.NotNull(PortalEmail.Validate(email));
        Assert.False(string.IsNullOrWhiteSpace(waarom));
    }

    [Fact]
    public void EenBuitensporigLangAdresWordtGeweigerd()
    {
        var lang = new string('a', 250) + "@x.nl";

        Assert.NotNull(PortalEmail.Validate(lang));
    }

    // ── De aanduiding binnen de klant ───────────────────────────────────────────────────────────

    [Fact]
    public void ErZijnTweeAanduidingenEnBeideGevenAlleenLeesrecht()
    {
        // Ze staan zo in §3.5 en ze blijven staan, maar er is geen klantrol die iets mag wijzigen:
        // alleen Soratus deelt toegang uit en alleen Soratus bewerkt het contract. Deze test zegt
        // dat de lijst niet groeit zonder dat iemand erover nadenkt.
        Assert.Equal(
            [PortalAccessRoles.Administrator, PortalAccessRoles.Reader],
            PortalAccessRoles.All);
    }

    [Theory]
    [InlineData("Beheerder klant")]
    [InlineData("Lezer")]
    public void EenBestaandeAanduidingWordtHerkend(string rol) =>
        Assert.True(PortalAccessRoles.IsKnown(rol));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Soratus-operator")]
    [InlineData("Beheerder")]
    [InlineData("beheerder klant")]
    [InlineData("LEZER")]
    public void EenAndereAanduidingWordtNietHerkend(string? rol) =>
        Assert.False(PortalAccessRoles.IsKnown(rol));

    [Fact]
    public void DeOperatorrolIsGeenAanduidingBinnenEenKlant()
    {
        // De mockup zet "Soratus-operator" in de toegangslijst van de interne klant. Zou die hier
        // door mogen, dan zou een toegangsdocument suggereren dat er operatorrechten uit een
        // portaalgegeven volgen. Operator worden gebeurt in Entra.
        var grant = new AccessGrant { Email = "marcel@soratus.com", Role = "Soratus-operator" };

        Assert.NotNull(grant.Validate());
    }

    // ── Het formulier als geheel ────────────────────────────────────────────────────────────────

    [Fact]
    public void EenToegangMetEenGeldigAdresEnEenBestaandeAanduidingKlopt()
    {
        var grant = new AccessGrant
        {
            Email = "Planning@BakkerLogistiek.nl",
            Name = "Planning",
            Role = PortalAccessRoles.Administrator,
        };

        Assert.Null(grant.Validate());
    }

    [Fact]
    public void DeStandaardaanduidingIsDeSmalste()
    {
        // Een formulier dat niets kiest hoort het minste recht te geven. Beide aanduidingen mogen
        // vandaag hetzelfde, dus dit is een keuze voor als dat verandert — en dan is de zachtste
        // fout de standaard.
        Assert.Equal(PortalAccessRoles.Reader, new AccessGrant().Role);
    }

    [Fact]
    public void EenToegangMetEenOnbruikbaarAdresWordtGeweigerdVoorDeAanduiding()
    {
        // De melding hoort over het adres te gaan en niet over de rol: dat is het veld waar de
        // gebruiker iets aan kan doen.
        var grant = new AccessGrant { Email = "geen-adres", Role = "Onbekend" };

        Assert.Contains("e-mailadres", grant.Validate(), StringComparison.OrdinalIgnoreCase);
    }
}
