using Microsoft.Extensions.Configuration;
using Soratus.Mcp.Uren;

namespace Soratus.Mcp.Uren.Tests;

/// <summary>
/// De server valt bij het opstarten om als de configuratie niet klopt.
/// </summary>
/// <remarks>
/// Dezelfde houding als <c>AddSoratusAgent</c>. Bij een MCP-server weegt hij zwaarder dan bij een
/// agent: de aanroeper ziet bij een half opgestarte server alleen dát de tool er niet is, nooit
/// waarom. De reden hoort dus op stderr te staan voordat er iets luistert.
/// </remarks>
public class ConfiguratieTests
{
    private const string Client = "6b1a4c0e-0000-4000-8000-000000000001";
    private const string Tenant = "6b1a4c0e-0000-4000-8000-000000000002";

    private static IConfiguration Config(params (string Key, string Value)[] waarden) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(waarden.Select(static w => new KeyValuePair<string, string?>(w.Key, w.Value)))
            .Build();

    private static string Melding(params (string Key, string Value)[] waarden) =>
        Assert.Throws<InvalidOperationException>(() => UrenConfiguration.Resolve(Config(waarden))).Message;

    [Fact]
    public void EenVolledigeConfiguratieWerkt()
    {
        UrenOptions options = UrenConfiguration.Resolve(Config(
            (UrenConfiguration.PortalKey, "https://portal.soratus.com"),
            (UrenConfiguration.ScopeKey, "api://soratus-portal/.default"),
            (UrenConfiguration.ClientIdKey, Client),
            (UrenConfiguration.TenantIdKey, Tenant),
            (UrenConfiguration.CustomersKey, "bakker, vandijk"),
            (UrenConfiguration.TimeoutKey, "15")));

        Assert.Equal(new Uri("https://portal.soratus.com"), options.PortalBaseAddress);
        Assert.Equal("api://soratus-portal/.default", options.Scope);
        Assert.Equal(["bakker", "vandijk"], options.AllowedCustomers);
        Assert.Equal(TimeSpan.FromSeconds(15), options.Timeout);
        Assert.False(options.DryRun);
    }

    [Fact]
    public void EenOmgevingsvariabeleMetDubbelLiggendStreepjeWordtOokGelezen()
    {
        // Een omgevingsvariabele SORATUS_UREN__PORTAL komt in de configuratie terecht als
        // SORATUS_UREN:PORTAL. Beide vormen moeten werken, net als in de telemetriebibliotheek.
        UrenOptions options = UrenConfiguration.Resolve(Config(
            ("SORATUS_UREN:PORTAL", "https://portal.soratus.com"),
            ("SORATUS_UREN:SCOPE", "api://soratus-portal/.default"),
            ("SORATUS_UREN:CLIENT_ID", Client),
            ("SORATUS_UREN:TENANT_ID", Tenant)));

        Assert.Equal(new Uri("https://portal.soratus.com"), options.PortalBaseAddress);
    }

    [Fact]
    public void ZonderPortaalValtHijOmMetDeSleutelInDeMelding()
    {
        Assert.Contains(UrenConfiguration.PortalKey, Melding(), StringComparison.Ordinal);
    }

    [Fact]
    public void ZonderScopeValtHijOmEnWijstHijNaarDeProefdraaimodus()
    {
        string melding = Melding((UrenConfiguration.PortalKey, "https://portal.soratus.com"));

        Assert.Contains(UrenConfiguration.ScopeKey, melding, StringComparison.Ordinal);
        Assert.Contains(UrenConfiguration.DryRunKey, melding, StringComparison.Ordinal);
    }

    [Fact]
    public void ProefdraaienMagZonderScope()
    {
        UrenOptions options = UrenConfiguration.Resolve(Config(
            (UrenConfiguration.PortalKey, "https://portal.soratus.com"),
            (UrenConfiguration.DryRunKey, "true")));

        Assert.True(options.DryRun);
        Assert.Equal(string.Empty, options.Scope);
    }

    [Fact]
    public void EenPortaalZonderTlsWordtGeweigerd()
    {
        Assert.Contains(
            "absolute https-URL",
            Melding((UrenConfiguration.PortalKey, "http://portal.soratus.com")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void LokaalMagHttpOverLoopback()
    {
        UrenOptions options = UrenConfiguration.Resolve(Config(
            (UrenConfiguration.PortalKey, "http://localhost:5001"),
            (UrenConfiguration.ScopeKey, "api://soratus-portal/.default"),
            (UrenConfiguration.ClientIdKey, Client),
            (UrenConfiguration.TenantIdKey, Tenant)));

        Assert.Equal(new Uri("http://localhost:5001"), options.PortalBaseAddress);
    }

    [Fact]
    public void EenQuerystringOpDeBasisUrlWordtGeweigerd()
    {
        // Een basis-URL heeft geen querystring. Staat er wél een, dan heeft iemand er een SAS-token
        // of een sleutel in geplakt, en die zou dan in elk verzoek meereizen.
        string melding = Melding(
            (UrenConfiguration.PortalKey, "https://portal.soratus.com?sig=abc123"));

        Assert.Contains("querystring", melding, StringComparison.Ordinal);
        Assert.Contains("sleutel", melding, StringComparison.Ordinal);
    }

    [Fact]
    public void EenScopeZonderDefaultWordtGeweigerd()
    {
        string melding = Melding(
            (UrenConfiguration.PortalKey, "https://portal.soratus.com"),
            (UrenConfiguration.ScopeKey, "api://soratus-portal/Uren.Boeken"));

        Assert.Contains("/.default", melding, StringComparison.Ordinal);
    }

    [Fact]
    public void EenOnleesbareProefdraaiwaardeValtNietStilTerugOpBoeken()
    {
        // "DROOGLOOP=1" betekent "boek niets". Dat stilzwijgend als false lezen is precies de
        // verkeerde kant om fout te gaan.
        string melding = Melding(
            (UrenConfiguration.PortalKey, "https://portal.soratus.com"),
            (UrenConfiguration.ScopeKey, "api://soratus-portal/.default"),
            (UrenConfiguration.DryRunKey, "1"));

        Assert.Contains(UrenConfiguration.DryRunKey, melding, StringComparison.Ordinal);
    }

    [Fact]
    public void EenBedrijfsnaamInDeKlantenlijstWordtGeweigerd()
    {
        string melding = Melding(
            (UrenConfiguration.PortalKey, "https://portal.soratus.com"),
            (UrenConfiguration.ScopeKey, "api://soratus-portal/.default"),
            (UrenConfiguration.CustomersKey, "Bakker Techniek B.V."));

        Assert.Contains("geen klantslug", melding, StringComparison.Ordinal);
    }

    [Fact]
    public void EenTijdslimietBuitenHetBereikWordtGeweigerd()
    {
        string melding = Melding(
            (UrenConfiguration.PortalKey, "https://portal.soratus.com"),
            (UrenConfiguration.ScopeKey, "api://soratus-portal/.default"),
            (UrenConfiguration.TimeoutKey, "0"));

        Assert.Contains(UrenConfiguration.TimeoutKey, melding, StringComparison.Ordinal);
    }

    [Fact]
    public void ZonderKlantenlijstIsErGeenLokaleBeperking()
    {
        UrenOptions options = UrenConfiguration.Resolve(Config(
            (UrenConfiguration.PortalKey, "https://portal.soratus.com"),
            (UrenConfiguration.ScopeKey, "api://soratus-portal/.default"),
            (UrenConfiguration.ClientIdKey, Client),
            (UrenConfiguration.TenantIdKey, Tenant)));

        Assert.Empty(options.AllowedCustomers);
    }
}
