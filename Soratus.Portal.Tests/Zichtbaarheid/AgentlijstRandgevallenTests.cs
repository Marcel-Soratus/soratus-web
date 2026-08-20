using Bunit;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Zichtbaarheid;

/// <summary>
/// De randen van de agentlijst (§3.2): een slug in een afwijkende schrijfwijze, en de weg terug
/// naar het keuzescherm.
/// </summary>
/// <remarks>
/// Twee dingen die in de gewone gang van zaken nooit opvallen. De schrijfwijze van de slug niet,
/// omdat vrijwel elke URL wordt aangeklikt in plaats van getypt; de knop naar het keuzescherm niet,
/// omdat de meeste klanten één omgeving hebben en hem dus horen te missen. Precies daarom staan ze
/// hier: een fout die niemand tegenkomt tijdens het bouwen komt iemand tegen in productie.
/// </remarks>
public class AgentlijstRandgevallenTests : Portaalrendertest
{
    private static Type Agents =>
        Paginaverzameling.MetRoute("/klant/{Slug}/agents")
        ?? throw new InvalidOperationException(
            "Er staat geen pagina op route '/klant/{Slug}/agents'. Is de route hernoemd, dan " +
            "hoort deze test mee te verhuizen — niet te verdwijnen.");

    /// <summary>De canonieke slug uit de klantenlijst, in kleine letters met een streepje.</summary>
    private const string CanoniekeSlug = "acme-logistiek";

    /// <summary>
    /// Dezelfde klant, anders geschreven. <c>ConfigurationCustomerDirectory</c> vergelijkt
    /// hoofdletterongevoelig, dus deze URL resolvet gewoon.
    /// </summary>
    private const string SlugUitDeUrl = "ACME-Logistiek";

    // ── De subnav volgt het viewmodel en niet de URL ─────────────────────────────────────────

    [Fact]
    public void DeSubnavGebruiktDeCanoniekeSlugEnNietDieUitDeUrl()
    {
        // /klant/ACME-Logistiek/agents werkt, en daar is niets tegen. Maar zes navigatielinks die
        // de schrijfwijze uit de URL overnemen dragen die vreemde vorm de rest van het portaal in:
        // elk volgend scherm krijgt hem weer mee, hij belandt in gedeelde links en in de
        // browsergeschiedenis, en er ontstaan twee URL's voor één klant. De slug komt daarom uit
        // het viewmodel — dat is de vorm zoals de resolver hem heeft geaccepteerd.
        MeldKlantAan();

        var cut = RenderPagina(Agents, SlugUitDeUrl);

        var hrefs = cut.FindAll("a.customer-nav__item")
            .Select(a => a.GetAttribute("href"))
            .ToArray();

        Assert.Equal(6, hrefs.Length);
        Assert.All(hrefs, href =>
            Assert.StartsWith($"/klant/{CanoniekeSlug}/", href, StringComparison.Ordinal));
        Assert.DoesNotContain(SlugUitDeUrl, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DeSubnavGebruiktDeCanoniekeSlugOokInDeOperatorweergave()
    {
        // Dezelfde pagina, de andere tak. De operatorweergave heeft zijn eigen viewmodel en dus
        // zijn eigen regel om te vergeten.
        MeldOperatorAan();

        var cut = RenderPagina(Agents, SlugUitDeUrl);

        var hrefs = cut.FindAll("a.customer-nav__item")
            .Select(a => a.GetAttribute("href"))
            .ToArray();

        Assert.Equal(6, hrefs.Length);
        Assert.All(hrefs, href =>
            Assert.StartsWith($"/klant/{CanoniekeSlug}/", href, StringComparison.Ordinal));
        Assert.DoesNotContain(SlugUitDeUrl, cut.Markup, StringComparison.Ordinal);
    }

    // ── "Mijn omgevingen" hoort bij meer dan één omgeving ────────────────────────────────────

    [Fact]
    public void EenKlantgebruikerMetTweeOmgevingenKrijgtDeWegTerugNaarHetKeuzescherm()
    {
        // Zonder deze knop is het woordmerk de enige weg terug naar het keuzescherm, en dat weet
        // niemand.
        MeldKlantAan(Autorisatiebron.TweeOmgevingenVoorDezelfdeGebruiker());

        var cut = RenderPagina(Agents, CanoniekeSlug);

        Assert.NotNull(cut.Find(".toolbar"));
        Assert.Contains("Mijn omgevingen", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenKlantgebruikerMetEenOmgevingZietGeenWegNaarEenKeuzeDieNietBestaat()
    {
        // De standaardklantenlijst geeft deze gebruiker precies één omgeving. Een knop naar het
        // keuzescherm zou hem naar een keuze tussen één ding brengen.
        MeldKlantAan();

        var cut = RenderPagina(Agents, CanoniekeSlug);

        // Eerst bewijzen dat de pagina er wél staat: anders is de afwezigheid hieronder geen
        // keuze maar een storing.
        Assert.NotNull(cut.Find(".page-head"));
        Assert.Empty(cut.FindAll(".toolbar"));
        Assert.DoesNotContain("Mijn omgevingen", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ZonderEnkeleOmgevingIsErGeenKlantweergaveOmDeKnopInTeZetten()
    {
        // Het derde geval, en de reden dat het erbij staat: nul omgevingen is met de echte
        // resolver geen tak van dit scherm maar een 404. Wie deze pagina als klantgebruiker ziet,
        // heeft er recht op — en dan is zijn eigen omgeving er ook. "Mijn omgevingen" ontbreekt
        // hier dus niet omdat de telling nul is, maar omdat er niets is gerenderd.
        //
        // Zonder deze test zou een dubbel die een lege lijst teruggeeft de indruk wekken dat de
        // grens tussen nul en één is gemeten. Dat is niet zo; gemeten is de grens tussen één en
        // twee, in de twee tests hierboven.
        MeldKlantAan(Autorisatiebron.ZonderToegangVoorDeTestgebruiker());

        var cut = RenderPagina(Agents, CanoniekeSlug);

        Assert.True(
            string.IsNullOrWhiteSpace(cut.Markup),
            "Een klantgebruiker zonder recht op deze klant hoort een lege pagina te krijgen — " +
            "404 en geen 403, want een statuscode die verschilt verklapt dat de klant bestaat. " +
            $"Er staat nu: {cut.Markup}");
        Assert.DoesNotContain("Mijn omgevingen", cut.Markup, StringComparison.Ordinal);
    }
}
