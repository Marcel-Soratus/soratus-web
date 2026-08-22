using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Soratus.Support.FirstLine.Tests;

/// <summary>
/// De schakelaar, de vier standen, en dat de onveilige stand iets is dat iemand aanzet.
/// </summary>
public class EerstelijninstellingenTests
{
    private static FirstLineOptions Ingericht(bool aan) => new()
    {
        Endpoint = "https://aoai-soratus-prod.openai.azure.com/",
        Deployment = "gpt-4o-mini",
        Enabled = aan,
    };

    // ── De standaardstand ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeStandaardIsUit()
    {
        // De belangrijkste meting van dit bestand, en dezelfde als op PortalMailOptions.DryRun. Een
        // aanroep aan een taalmodel kost geld en gaat naar een externe dienst; de onveilige stand
        // hoort iets te zijn dat iemand aanzet, niet iets dat je vergeet uit te zetten.
        Assert.False(new FirstLineOptions().Enabled);
        Assert.Equal(FirstLineState.TurnedOff, Ingericht(aan: false).State(isDevelopment: false));
    }

    [Fact]
    public void ErStaatGeenSleutelveldOpDeInstellingen()
    {
        // Geen ApiKey, geen Secret, geen ConnectionString — niet omdat het niet mag maar omdat er geen
        // veld voor is. De marketingsite gebruikt hetzelfde Azure OpenAI-account mét een api-key; dat
        // pad is met opzet niet gekopieerd.
        var verdacht = typeof(FirstLineOptions)
            .GetProperties()
            .Select(p => p.Name)
            .Where(naam =>
                naam.Contains("Key", StringComparison.OrdinalIgnoreCase)
                || naam.Contains("Secret", StringComparison.OrdinalIgnoreCase)
                || naam.Contains("Password", StringComparison.OrdinalIgnoreCase)
                || naam.Contains("ConnectionString", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(
            verdacht.Length == 0,
            "FirstLineOptions heeft een veld dat naar een geheim ruikt: "
            + string.Join(", ", verdacht) + ". De aanroep gaat met de managed identity van het "
            + "portaal; een tweede authenticatievorm in hetzelfde proces is de enige die een mens "
            + "moet roteren.");
    }

    [Fact]
    public void ErStaatGeenModelnaamAlsStandaardwaarde()
    {
        // §46.9: in deze code staat geen modelnaam. Een standaardmodel zou een keuze zijn die niemand
        // heeft gemaakt en die in de kosten van iemand anders landt.
        Assert.Null(new FirstLineOptions().Deployment);
        Assert.Null(new FirstLineOptions().Endpoint);
    }

    // ── De vier standen en hun volgorde ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true, true, FirstLineState.DevelopmentMachine)]
    [InlineData(true, false, FirstLineState.DevelopmentMachine)]
    [InlineData(false, true, FirstLineState.Ready)]
    [InlineData(false, false, FirstLineState.TurnedOff)]
    public void DeOntwikkelmachineGaatVoorAlleAndereRedenen(
        bool ontwikkel,
        bool aan,
        FirstLineState verwacht)
    {
        // Andere volgorde dan bij de mail, met een eigen reden: op een ontwikkelmachine draait de
        // eerstelijn nooit, wat er ook in de configuratie staat. Zou "uitgezet" hier voorop staan, dan
        // wijst de melding een handeling aan die niets verandert.
        Assert.Equal(verwacht, Ingericht(aan).State(ontwikkel));
    }

    [Fact]
    public void NietIngerichtGaatVoorUitgezet()
    {
        // Dezelfde volgorde en dezelfde reden als bij PortalMailOptions.Outbox(): een omgeving zonder
        // endpoint hoort niet te melden dat hij is uitgezet, want aanzetten helpt dan niet.
        var leeg = new FirstLineOptions { Enabled = false };

        Assert.Equal(FirstLineState.NotConfigured, leeg.State(isDevelopment: false));
    }

    [Fact]
    public void DeEersteEnumwaardeIsDeVeilige()
    {
        // Dezelfde regel als bij SupportGroundKind.Unknown en MailOutboxState.NotConfigured: de waarde
        // van een niet-gezette enum hoort de veilige te zijn.
        Assert.Equal(FirstLineState.NotConfigured, default);
    }

    // ── Het adres ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HetAdresIsHetEndpointMetDeDeploymentEnDeApiversie()
    {
        var adres = Ingericht(aan: true).CompletionsUri();

        Assert.Equal(
            "https://aoai-soratus-prod.openai.azure.com/openai/deployments/gpt-4o-mini/chat/"
            + "completions?api-version=2024-10-21",
            adres?.ToString());
    }

    [Theory]
    [InlineData(null, "gpt-4o-mini")]
    [InlineData("", "gpt-4o-mini")]
    [InlineData("   ", "gpt-4o-mini")]
    [InlineData("https://aoai-soratus-prod.openai.azure.com/", null)]
    [InlineData("https://aoai-soratus-prod.openai.azure.com/", "  ")]
    [InlineData("geen adres", "gpt-4o-mini")]
    [InlineData("ftp://aoai-soratus-prod.openai.azure.com/", "gpt-4o-mini")]
    public void ZonderVolledigAdresIsErNietsOmAanTeRoepen(string? endpoint, string? deployment)
    {
        // Eén methode die de drie voorwaarden samen neemt, zodat er geen aanroeper is die er twee van
        // controleert en de derde vergeet. Leeg is afwezig en niet ongeldig: een app-setting met een
        // lege waarde heeft dit portaal al een keer plat gelegd.
        var instellingen = new FirstLineOptions { Endpoint = endpoint, Deployment = deployment };

        Assert.Null(instellingen.CompletionsUri());
        Assert.Equal(FirstLineState.NotConfigured, instellingen.State(isDevelopment: false));
    }

    [Fact]
    public void EenDeploymentnaamMetEenSchuineStreepBreektHetAdresNiet()
    {
        // Vangnet en geen verdediging: deze waarde komt uit onze eigen configuratie. Hij staat er voor
        // de dag dat hij ergens anders vandaan komt — dezelfde tweede laag als in de sprintlane.
        var adres = new FirstLineOptions
        {
            Endpoint = "https://aoai-soratus-prod.openai.azure.com/",
            Deployment = "../../beheer",
        }.CompletionsUri();

        Assert.Equal(
            "https://aoai-soratus-prod.openai.azure.com/openai/deployments/..%2F..%2Fbeheer/chat/"
            + "completions?api-version=2024-10-21",
            adres?.ToString());
    }

    // ── Wat er gelogd wordt ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void AangezetZonderInrichtingIsEenWaarschuwingEnDeRestNiet()
    {
        // Uitgezet is de standaardstand en dus geen probleem; een waarschuwing bij elke start zou ruis
        // zijn, en ruis is precies wat later een échte waarschuwing onzichtbaar maakt. Aangezet zonder
        // endpoint is iemand die dacht dat hij het had aangezet.
        var aangezetLeeg = AddSoratusFirstLineExtensions.Describe(
            new FirstLineOptions { Enabled = true },
            isDevelopment: false);

        Assert.Equal(LogLevel.Warning, aangezetLeeg.Level);
        Assert.Equal(FirstLineState.NotConfigured, aangezetLeeg.State);

        foreach (var stil in new[]
        {
            AddSoratusFirstLineExtensions.Describe(new FirstLineOptions(), isDevelopment: false),
            AddSoratusFirstLineExtensions.Describe(Ingericht(aan: false), isDevelopment: false),
            AddSoratusFirstLineExtensions.Describe(Ingericht(aan: true), isDevelopment: true),
            AddSoratusFirstLineExtensions.Describe(Ingericht(aan: true), isDevelopment: false),
        })
        {
            Assert.Equal(LogLevel.Information, stil.Level);
        }
    }

    [Fact]
    public void ElkeStandHeeftEenEigenRegelDieDeRedenNoemt()
    {
        var regels = new[]
        {
            AddSoratusFirstLineExtensions.Describe(new FirstLineOptions(), false).Explanation,
            AddSoratusFirstLineExtensions.Describe(new FirstLineOptions { Enabled = true }, false).Explanation,
            AddSoratusFirstLineExtensions.Describe(Ingericht(false), false).Explanation,
            AddSoratusFirstLineExtensions.Describe(Ingericht(true), true).Explanation,
            AddSoratusFirstLineExtensions.Describe(Ingericht(true), false).Explanation,
        };

        // Vijf verschillende regels: een niet-aangesloten eerstelijn hoort te zeggen waarom, want
        // anders is een supportscherm zonder AI-antwoorden niet van een kapotte inrichting te
        // onderscheiden.
        Assert.Equal(5, regels.Distinct(StringComparer.Ordinal).Count());
        Assert.All(regels, regel => Assert.False(string.IsNullOrWhiteSpace(regel)));

        // De aangesloten regel noemt de deployment: §46.9 zegt dat wie welk model heeft gedraaid in de
        // logregel hoort, bij de operator, en niet op een bericht van een klant.
        Assert.Contains("gpt-4o-mini", regels[4], StringComparison.Ordinal);
    }

    // ── De registratie ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("true", false, true)]
    [InlineData("false", false, false)]
    [InlineData("true", true, false)]
    public void ErStaatAlleenEenKiezerInDeContainerAlsHijMagDraaien(
        string aan,
        bool ontwikkel,
        bool verwacht)
    {
        // De schakelaar zit in de registratie en niet in het gedrag. Een geregistreerde-maar-uitgezette
        // eerstelijn zou "er kijkt een agent mee" op het scherm zetten en daarna elke vraag escaleren:
        // een storing die zich voordoet als werkende functionaliteit, en precies wat §46.9 afwees.
        var bouwer = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = ontwikkel ? Environments.Development : Environments.Production,
        });

        bouwer.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PortalFirstLine:Enabled"] = aan,
            ["PortalFirstLine:Endpoint"] = "https://aoai-soratus-prod.openai.azure.com/",
            ["PortalFirstLine:Deployment"] = "gpt-4o-mini",
        });

        var uitkomst = bouwer.AddSoratusFirstLine();

        Assert.Equal(verwacht, uitkomst.IsReady);

        var kiezer = bouwer.Services.FirstOrDefault(
            dienst => dienst.ServiceType == typeof(IFirstLineChooser));

        Assert.Equal(verwacht, kiezer is not null);

        if (verwacht)
        {
            // Scoped en niet singleton: dit hangt aan één vraag van één mens, niet aan een
            // achtergronddienst die zolang het portaal draait blijft leven.
            Assert.Equal(ServiceLifetime.Scoped, kiezer!.Lifetime);
        }
    }

    [Fact]
    public void DeRegistratieWerptNietOpEenOnleesbareInstelling()
    {
        // Een inrichtingsfout die het opstarten tegenhoudt neemt /healthz mee en rolt daarmee de
        // uitrol terug. Dezelfde afweging als bij PortalData, PortalMail, PortalCosts en PortalAlerts,
        // en de reden dat er ook geen ValidateOnStart op staat.
        var bouwer = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production,
        });

        bouwer.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PortalFirstLine:Enabled"] = "misschien",
            ["PortalFirstLine:TimeoutSeconds"] = "een uur",
        });

        var uitkomst = bouwer.AddSoratusFirstLine();

        Assert.False(uitkomst.IsReady);
        Assert.Equal(FirstLineState.NotConfigured, uitkomst.State);
    }

    [Fact]
    public void DeRegistratieZetGeenEigenCredentialNeer()
    {
        // Het portaal heeft er al één — dezelfde managed identity voor Cosmos, de Communication Service
        // en nu het taalmodel. Een eigen DefaultAzureCredential zou een tweede tokencache zijn en een
        // tweede plek waar de identiteit wordt gekozen.
        var bouwer = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production,
        });

        bouwer.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PortalFirstLine:Enabled"] = "true",
            ["PortalFirstLine:Endpoint"] = "https://aoai-soratus-prod.openai.azure.com/",
            ["PortalFirstLine:Deployment"] = "gpt-4o-mini",
        });

        bouwer.AddSoratusFirstLine();

        Assert.DoesNotContain(
            bouwer.Services,
            dienst => dienst.ServiceType == typeof(Azure.Core.TokenCredential));
    }
}
