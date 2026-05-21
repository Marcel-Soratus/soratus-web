using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Soratus.Web.Components.Sections;
using Soratus.Web.Models;

namespace Soratus.Web.Tests;

public class SectionRenderTests : BunitContext
{
    public SectionRenderTests()
    {
        Services.Configure<BrandOptions>(o =>
        {
            o.Stats = new BrandStats { ActiveProjects = 47, AgentsInProduction = 12, ResponseTimeHours = 4 };
            o.Marquee = new List<string> { "Agentic AI", "LLM-native" };
        });
        Services.Configure<CompanyOptions>(o =>
        {
            o.LegalName = "Soratus B.V.";
            o.Email = "hallo@soratus.com";
            o.Url = "https://soratus.com";
        });
    }

    [Fact]
    public void Hero_RendersStatsFromOptions()
    {
        var cut = Render<Hero>();
        Assert.Contains("47 actieve projecten", cut.Markup);
        Assert.Contains("12 AI-agents in productie", cut.Markup);
        Assert.Contains("Antwoord", cut.Markup);
    }

    [Fact]
    public void Marquee_DoublesItemsForSeamlessLoop()
    {
        var cut = Render<Marquee>();
        var count = System.Text.RegularExpressions.Regex.Matches(cut.Markup, "Agentic AI").Count;
        Assert.Equal(2, count);
    }

    [Fact]
    public void HowWeWork_RendersFourSteps()
    {
        var cut = Render<HowWeWork>();
        Assert.Contains("STAP 01", cut.Markup);
        Assert.Contains("STAP 02", cut.Markup);
        Assert.Contains("STAP 03", cut.Markup);
        Assert.Contains("STAP 04", cut.Markup);
    }

    [Fact]
    public void Branches_RendersAllEight()
    {
        var cut = Render<Branches>();
        Assert.Contains("Retail &amp; E-commerce", cut.Markup);
        Assert.Contains("Jouw branche", cut.Markup);
    }
}
