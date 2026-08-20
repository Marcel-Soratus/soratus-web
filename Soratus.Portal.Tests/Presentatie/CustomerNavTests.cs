using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Soratus.Portal.Components.Layout;

namespace Soratus.Portal.Tests.Presentatie;

/// <summary>
/// De klantnavigatie wijst naar de klant in de route, en licht op bij het juiste scherm.
/// </summary>
/// <remarks>
/// Deze tests bestaan om één specifieke fout te vangen: <c>NavLinkMatch.Prefix</c> op zes routes
/// die allemaal met <c>/klant/{slug}/</c> beginnen. Zou NavLink op tekens vergelijken in plaats
/// van op padsegmenten, dan zou een pad als <c>/klant/x/agents</c> mee kunnen oplichten op een
/// ander scherm. Het portaal zit achter Entra, dus dit is niet in de browser na te lopen zonder
/// aan te melden; hier wel.
///
/// De slug staat met opzet in de route en niet in de sessie: zie de opmerking bovenaan
/// <c>CustomerNav.razor</c>.
/// </remarks>
public class CustomerNavTests : BunitContext
{
    private const string Slug = "acme-logistiek";

    private static readonly string[] Schermen =
        ["agents", "sprint", "contract", "uren", "facturatie", "support"];

    [Fact]
    public void ElkItemWijstNaarDeKlantUitDeRoute()
    {
        var cut = Render<CustomerNav>(p => p.Add(c => c.Slug, Slug));

        var hrefs = cut.FindAll("a.customer-nav__item")
            .Select(a => a.GetAttribute("href"))
            .ToArray();

        Assert.Equal(Schermen.Select(s => $"/klant/{Slug}/{s}").ToArray(), hrefs);
    }

    [Theory]
    [InlineData("agents", "Agents")]
    [InlineData("sprint", "Sprint")]
    [InlineData("contract", "Contract")]
    [InlineData("uren", "Uren")]
    [InlineData("facturatie", "Facturatie")]
    [InlineData("support", "Support")]
    public void OpEenSchermLichtPreciesEenItemOp(string segment, string label)
    {
        Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/klant/{Slug}/{segment}");

        var cut = Render<CustomerNav>(p => p.Add(c => c.Slug, Slug));

        var actief = cut.FindAll("a.customer-nav__item.active");

        Assert.Single(actief);
        Assert.Equal(label, actief[0].TextContent.Trim());
    }

    [Fact]
    public void AgentsLichtNietOpOpEenAnderScherm()
    {
        // De concrete val: /klant/x/agents en /klant/x/uren delen de eerste drie segmenten.
        Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/klant/{Slug}/uren");

        var cut = Render<CustomerNav>(p => p.Add(c => c.Slug, Slug));

        var agents = cut.FindAll("a.customer-nav__item")
            .Single(a => a.TextContent.Trim() == "Agents");

        Assert.DoesNotContain("active", agents.ClassList);
    }

    [Fact]
    public void OpHetOverzichtLichtErNietsOp()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/overzicht");

        var cut = Render<CustomerNav>(p => p.Add(c => c.Slug, Slug));

        Assert.Empty(cut.FindAll("a.customer-nav__item.active"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EenLeegSlugValtHard(string slug) =>
        // Zonder slug zou er "/klant//agents" uitkomen: een link die er goed uitziet en nergens
        // heen gaat. Dat is een programmeerfout en die hoort niet stil door te lopen.
        Assert.Throws<ArgumentException>(() => Render<CustomerNav>(p => p.Add(c => c.Slug, slug)));

    [Fact]
    public void DeNavigatieHeeftEenEigenToegankelijkeNaam() =>
        // Er staan twee navigaties op één pagina; zonder label kan een schermlezer ze niet
        // onderscheiden.
        Assert.Equal("Klantmenu", Render<CustomerNav>(p => p.Add(c => c.Slug, Slug))
            .Find("nav").GetAttribute("aria-label"));
}
