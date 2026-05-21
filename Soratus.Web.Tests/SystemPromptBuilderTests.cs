using Microsoft.Extensions.Options;
using Soratus.Web.Models;
using Soratus.Web.Services;

namespace Soratus.Web.Tests;

public class SystemPromptBuilderTests
{
    [Fact]
    public void Build_IncludesCompanyDetailsAndStats()
    {
        var brand = Options.Create(new BrandOptions
        {
            Stats = new BrandStats { ActiveProjects = 47, AgentsInProduction = 12, ResponseTimeHours = 4 }
        });
        var company = Options.Create(new CompanyOptions
        {
            LegalName = "Soratus B.V.",
            Kvk = "68752326",
            Email = "hallo@soratus.com",
            Country = "Nederland"
        });

        var prompt = new SystemPromptBuilder(brand, company).Build();

        Assert.Contains("Soratus B.V.", prompt);
        Assert.Contains("KvK 68752326", prompt);
        Assert.Contains("47 actieve projecten", prompt);
        Assert.Contains("12 AI-agents in productie", prompt);
        Assert.Contains("hallo@soratus.com", prompt);
        Assert.Contains("Nederlands", prompt);
        Assert.DoesNotContain("English", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_KeepsResponseUnderFourSentencesGuideline()
    {
        var brand = Options.Create(new BrandOptions { Stats = new BrandStats() });
        var company = Options.Create(new CompanyOptions());
        var prompt = new SystemPromptBuilder(brand, company).Build();
        Assert.Contains("onder de 4 zinnen", prompt);
    }
}
