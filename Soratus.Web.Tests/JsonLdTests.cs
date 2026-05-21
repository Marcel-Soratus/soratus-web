using System.Text.Json;
using Soratus.Web.Models;
using Soratus.Web.Services;

namespace Soratus.Web.Tests;

public class JsonLdTests
{
    [Fact]
    public void Build_ProducesValidJson()
    {
        var co = new CompanyOptions
        {
            LegalName = "Soratus B.V.",
            Kvk = "68752326",
            Email = "hallo@soratus.com",
            Country = "Nederland",
            Url = "https://soratus.com"
        };

        var json = JsonLd.Build(co);

        // Should parse without exception
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("https://schema.org", doc.RootElement.GetProperty("@context").GetString());

        var graph = doc.RootElement.GetProperty("@graph").EnumerateArray().ToArray();
        Assert.Contains(graph, e => e.GetProperty("@type").GetString() == "Organization");
        Assert.Contains(graph, e => e.GetProperty("@type").GetString() == "WebSite");
        Assert.Contains(graph, e => e.GetProperty("@type").GetString() == "ProfessionalService");
        Assert.Contains(graph, e => e.GetProperty("@type").GetString() == "FAQPage");
    }
}
