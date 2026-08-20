using Bunit;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Zichtbaarheid;

/// <summary>
/// De landingsroute <c>/</c>: waar iedereen binnenkomt, en waar de rollen uit elkaar gaan.
/// </summary>
/// <remarks>
/// Deze pagina hoort bij de zichtbaarheidstests omdat hij het enige adres is dat beide rollen
/// kennen. Stuurt hij een klantgebruiker naar het operatoroverzicht, dan is de rolscheiding op de
/// meest voor de hand liggende plek al mis — en dan valt dat op geen enkele andere pagina meer op.
///
/// Een doorstuur rendert niets. De assertie gaat dus over de navigatie en niet over markup; wie
/// hier alleen naar de markup zou kijken, ziet twee lege pagina's en denkt dat het klopt.
/// </remarks>
public class LandingsrouteTests : Portaalrendertest
{
    private static Type Start =>
        Paginaverzameling.MetNaam("Soratus.Portal.Components.Pages.Start")
        ?? throw new InvalidOperationException(
            "De pagina Soratus.Portal.Components.Pages.Start bestaat niet. Is de landingsroute " +
            "hernoemd, dan hoort deze test mee te verhuizen — niet te verdwijnen.");

    [Fact]
    public void EenOperatorGaatVanafDeLandingsrouteNaarHetKlantoverzicht()
    {
        MeldOperatorAan();

        RenderPagina(Start);

        Assert.Equal("/overzicht", Doorstuurdoel());
    }

    [Fact]
    public void EenKlantgebruikerMetEenOmgevingGaatMeteenNaarZijnEigenAgents()
    {
        MeldKlantAan();

        RenderPagina(Start);

        Assert.Equal("/klant/acme-logistiek/agents", Doorstuurdoel());
    }

    [Fact]
    public void EenKlantgebruikerKomtNooitOpHetKlantoverzichtUit()
    {
        // De scherpste vorm: welk pad de landingsroute ook kiest, het is niet het overzicht.
        MeldKlantAan();

        RenderPagina(Start);

        Assert.NotEqual("/overzicht", Doorstuurdoel());
    }

    [Fact]
    public void EenKlantgebruikerMetTweeOmgevingenKrijgtEenKeuzeEnGeenGok()
    {
        // Gokken welke omgeving iemand bedoelde is erger dan één klik vragen — en een verkeerde
        // gok is hier een verkeerde klant.
        MeldKlantAan(Autorisatiebron.TweeOmgevingenVoorDezelfdeGebruiker());

        var cut = RenderPagina(Start);

        Assert.Null(Doorstuurdoel());
        Assert.Contains("Kies een omgeving", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Acme Logistiek", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Acme Retail", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DeKeuzelijstToontAlleenDeEigenOmgevingen()
    {
        var klanten = Autorisatiebron.TweeOmgevingenVoorDezelfdeGebruiker()
            .Concat(Autorisatiebron.ZonderToegangVoorDeTestgebruiker())
            .ToArray();

        MeldKlantAan(klanten);

        var cut = RenderPagina(Start);

        Assert.Contains("Acme Logistiek", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Bakker", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenAangemeldeGebruikerZonderOmgevingKrijgtEenEerlijkeUitlegEnGeen404()
    {
        // Deze gebruiker bestáát en is aangemeld; er hangt alleen nog geen omgeving aan zijn
        // account. Dat is een inrichtingsstap van Soratus en geen fout van hem.
        MeldKlantAan(Autorisatiebron.ZonderToegangVoorDeTestgebruiker());

        var cut = RenderPagina(Start);

        Assert.Null(Doorstuurdoel());
        Assert.Contains("Nog geen omgeving", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DeLandingsrouteVerklaptGeenKlantWaarDeGebruikerNietBijHoort()
    {
        MeldKlantAan(Autorisatiebron.ZonderToegangVoorDeTestgebruiker());

        var cut = RenderPagina(Start);

        Assert.DoesNotContain("Bakker", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("bakker-bv", cut.Markup, StringComparison.Ordinal);
    }
}
