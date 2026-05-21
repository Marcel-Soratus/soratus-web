namespace Soratus.Web.Models;

public sealed class BrandOptions
{
    public BrandStats Stats { get; set; } = new();
    public List<string> Marquee { get; set; } = new();
    public List<string> CapabilityPills { get; set; } = new();
}

public sealed class BrandStats
{
    public int ActiveProjects { get; set; }
    public int AgentsInProduction { get; set; }
    public int ResponseTimeHours { get; set; }
}

public sealed class CompanyOptions
{
    public string LegalName { get; set; } = "";
    public string Kvk { get; set; } = "";
    public string VatId { get; set; } = "";
    public string Email { get; set; } = "";
    public string Country { get; set; } = "";
    public string Url { get; set; } = "";
}

public sealed class AnthropicOptions
{
    public string BaseUrl { get; set; } = "https://api.anthropic.com";
    public string Model { get; set; } = "claude-haiku-4-5";
    public int MaxTokens { get; set; } = 1024;
    public string ApiKey { get; set; } = "";
}

public sealed class SendGridOptions
{
    public string ApiKey { get; set; } = "";
    public string FromAddress { get; set; } = "noreply@soratus.com";
    public string FromName { get; set; } = "Soratus website";
}
