using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Soratus.Agents.Telemetry;
using Soratus.Agents.Telemetry.HostedAgents;
using Soratus.Portal.Alerts;
using Soratus.Portal.Data;
using Soratus.Portal.Platform;
using Soratus.Portal.Security;
using Soratus.Portal.Tests.Maandoverzicht;

namespace Soratus.Portal.Tests.Platform;

/// <summary>
/// Dat het portaal zich als agent-host aansluit zonder er ooit aan onderdoor te gaan, en dat de
/// schrijfkant en de leeskant naar dezelfde database wijzen.
/// </summary>
/// <remarks>
/// <para><strong>Twee eigenschappen dragen dit bestand, en ze zijn beide een <em>afwezigheid</em>.</strong>
/// De eerste: geen enkele stand van de telemetrieconfiguratie mag het opstarten van het portaal
/// tegenhouden. Dat is een bewuste afwijking van wat de bibliotheek zelf wil — bij een agent is de
/// telemetrie de hele opdracht, hier is het bijzaak — en het is precies de klasse storing die dit
/// portaal vandaag heeft platgelegd: een inrichtingsfout die in een achtergronddienst werd gelezen en
/// de host meenam.</para>
///
/// <para>De tweede: bij een mislukte aansluiting mag er geen <em>halve</em> registratie achterblijven.
/// Dat is de eigenschap waarop de <c>try</c> in <see cref="PlatformAgents"/> rust, en zonder test is
/// het een aanname over andermans code.</para>
///
/// <para><strong><c>/healthz</c> bewijst hier niets</strong> en staat er daarom niet in. Die controle
/// raakt met opzet geen enkele afhankelijkheid, dus hij kan een kapotte container of een gebroken
/// optiebinding niet zien. Wat er wél iets bewijst is <c>/</c>: om daar een doorverwijzing naar de
/// aanmelding te geven moet de app zijn container hebben gebouwd, zijn configuratie hebben gebonden en
/// de aanmeldketen hebben opgezet.</para>
/// </remarks>
public sealed class PlatformtelemetrieTests
{
    private const string Endpoint = "https://cosmos-soratus-prod.documents.azure.com:443/";

    // ── Het echte portaal ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HetEchtePortaalStuurtDeVoorpaginaNogNaarDeAanmelding()
    {
        // De meting die /healthz niet kan doen. Een omgevallen host geeft hier 503; een gebroken
        // optiebinding of een onvervulbare registratie komt niet eens tot een antwoord.
        using var client = Portaal.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var antwoord = await client.GetAsync(new Uri("/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Found, antwoord.StatusCode);

        // Naar Entra en niet naar een eigen pagina: dat de aanmeldketen werkelijk is opgezet is wat
        // deze meting toevoegt aan "de app antwoordt".
        Assert.Contains(
            "login.microsoftonline.com",
            antwoord.Headers.Location!.OriginalString,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeTweeBeheeragentsZijnZonderTelemetrieUitDeContainerTeBouwen()
    {
        // De optionele afhankelijkheid, en dit is de aanname die gemeten hóórt te worden: lost
        // ActivatorUtilities een niet-geregistreerde ISoratusHostedAgents op als de standaardwaarde? In
        // de configuratie van het portaal staat geen PlatformTelemetry:AccountEndpoint, dus die dienst
        // staat er niet — en zou dit niet werken, dan start het portaal in productie niet.
        Assert.Null(Portaal.Services.GetService<ISoratusHostedAgents>());
        Assert.NotNull(ActivatorUtilities.CreateInstance<AzureCostCollector>(Portaal.Services));
        Assert.NotNull(ActivatorUtilities.CreateInstance<AgentFaultAlerter>(Portaal.Services));
    }

    // ── Het aansluiten zelf ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void MetEenIngerichteEndpointWordenBeideBeheeragentsAangekondigd()
    {
        var builder = Bouwer(new Dictionary<string, string?>
        {
            ["PlatformTelemetry:AccountEndpoint"] = Endpoint,
        });

        var uitkomst = builder.AddSoratusPlatformAgents();

        Assert.True(uitkomst.Published);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, uitkomst.Level);
        Assert.Contains("platform-telemetry", uitkomst.Explanation, StringComparison.Ordinal);

        Assert.Contains(builder.Services, d => d.ServiceType == typeof(ISoratusHostedAgents));

        var aankondigingen = Aankondigingen(builder);

        Assert.Equal(
            [PlatformAgentNames.Costs, PlatformAgentNames.Alerts],
            [.. aankondigingen.Select(a => a.AgentName).Order(StringComparer.Ordinal)]);

        // §4 noemt ze zo, en de seed-data van de interne klant ook. Zou een van deze namen verschuiven,
        // dan komt er een tweede rij in het overzicht naast een rij die niets meer schrijft.
        Assert.Equal("kosten-collector", PlatformAgentNames.Costs);
        Assert.Equal("storingsmelder", PlatformAgentNames.Alerts);
    }

    [Fact]
    public void ZonderEndpointWordtErNietsGeregistreerdEnZegtHetPortaalDatHardop()
    {
        var builder = Bouwer([]);

        var uitkomst = builder.AddSoratusPlatformAgents();

        Assert.False(uitkomst.Published);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, uitkomst.Level);
        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(ISoratusHostedAgents));

        // Dat het niet stil gebeurt is het punt: een leeg beheeroverzicht is anders niet van een
        // kapotte inrichting te onderscheiden.
        Assert.Contains("stilgevallen", uitkomst.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void MetDeVlagUitGebeurtErNietsEnStaatDatErOok()
    {
        var builder = Bouwer(new Dictionary<string, string?>
        {
            ["PlatformTelemetry:AccountEndpoint"] = Endpoint,
            ["PlatformTelemetry:Enabled"] = "false",
        });

        var uitkomst = builder.AddSoratusPlatformAgents();

        Assert.False(uitkomst.Published);
        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(ISoratusHostedAgents));
    }

    [Fact]
    public void EenMislukteAansluitingLaatGeenHalveRegistratieAchter()
    {
        // SORATUS_AGENT__SCHEDULE hoort bij één agent per proces en werpt op een host die er meer
        // herbergt. Wat hier wordt gemeten is niet die fout maar wat er van de container overblijft:
        // niets. Zou de bibliotheek halverwege haar registraties werpen, dan zou een try eromheen een
        // half ingerichte container achterlaten — en dat is erger dan niet aangesloten zijn.
        var builder = Bouwer(new Dictionary<string, string?>
        {
            ["PlatformTelemetry:AccountEndpoint"] = Endpoint,
            ["SORATUS_AGENT:SCHEDULE"] = "0 4 * * *",
        });

        var voor = builder.Services.Count;
        var uitkomst = builder.AddSoratusPlatformAgents();

        Assert.False(uitkomst.Published);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, uitkomst.Level);
        Assert.Equal(voor, builder.Services.Count);
        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(ISoratusHostedAgents));
        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(IHostedAgentSource));
        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(AgentIdentity));
    }

    [Fact]
    public void EenOnbruikbareEndpointLegtHetOpstartenNietPlat()
    {
        // Geen https en geen loopback: de bibliotheek werpt. Voor een agent is dat het juiste gedrag;
        // hier mag het hoogstens een error-regel worden.
        var builder = Bouwer(new Dictionary<string, string?>
        {
            ["PlatformTelemetry:AccountEndpoint"] = "ftp://cosmos.example",
        });

        var uitkomst = builder.AddSoratusPlatformAgents();

        Assert.False(uitkomst.Published);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, uitkomst.Level);
    }

    // ── De leeskant wijst naar dezelfde database ─────────────────────────────────────────────────

    [Fact]
    public void DeInterneBeheerklantLeestWaarHetPortaalZijnAgentsHeenSchrijft()
    {
        var lijst = Klantenlijst(Ingericht(), Intern(), Klant("bakker"));

        Assert.Equal("platform-telemetry", lijst.Find("soratus")!.Telemetry!.Database);

        // En een gewone klant verhuist niet mee. Dit is de kant die eronder ligt: het portaal heeft op
        // de klanttelemetrie met opzet alleen leesrecht, en die grens verschuift niet doordat het zijn
        // eigen agents ergens anders neerzet.
        Assert.Equal("telemetry", lijst.Find("bakker")!.Telemetry!.Database);
    }

    [Fact]
    public void ZolangDePlatformtelemetrieNietIsIngerichtVerandertErNietsVoorDeInterneKlant()
    {
        // Dit sluit de tussenstand af. De app-setting komt uit dezelfde uitrol die de database
        // aanmaakt, dus zolang die er niet is bestaat de database ook niet — en dan zou de interne klant
        // naar een 404 wijzen en als "status onbekend" op het overzicht komen.
        var lijst = Klantenlijst(platform: null, Intern());

        Assert.Equal("telemetry", lijst.Find("soratus")!.Telemetry!.Database);
    }

    [Fact]
    public void EenUitdrukkelijkeWaardeOpDeKlantZelfWintOokBijDeInterneKlant()
    {
        // Zodra het platform een eigen account krijgt, is dat de plek waar dat komt te staan. Een
        // configuratiesectie hoort niet stil te overrulen wat iemand heeft vastgelegd.
        var intern = Intern();
        intern.TelemetryDatabase = "eigen-telemetrie";

        var lijst = Klantenlijst(Ingericht(), intern);

        Assert.Equal("eigen-telemetrie", lijst.Find("soratus")!.Telemetry!.Database);
    }

    [Fact]
    public void DeSchrijfkantEnDeLeeskantKomenUitDezelfdeSectie()
    {
        // De invariant en niet zijn gevolg. Zouden dit twee configuraties zijn, dan bestaat de toestand
        // "het portaal publiceert netjes in de ene database en het scherm kijkt in de andere" — en die
        // levert geen fout op maar een leeg overzicht.
        var opties = new PlatformTelemetryOptions
        {
            AccountEndpoint = Endpoint,
            Database = "een-heel-andere-database",
            CustomerId = "soratus",
        };

        var builder = Bouwer(new Dictionary<string, string?>
        {
            ["PlatformTelemetry:AccountEndpoint"] = opties.AccountEndpoint,
            ["PlatformTelemetry:Database"] = opties.Database,
            ["PlatformTelemetry:CustomerId"] = opties.CustomerId,
        });

        builder.AddSoratusPlatformAgents();

        // De schrijfkant: de bibliotheek leest haar eigen sleutels, en die zijn uit deze sectie gevuld.
        Assert.Equal(opties.Database, builder.Configuration["SORATUS_TELEMETRY:DATABASE"]);
        Assert.Equal(opties.AccountEndpoint, builder.Configuration["SORATUS_TELEMETRY:ENDPOINT"]);
        Assert.Equal(opties.CustomerId, builder.Configuration["SORATUS_CUSTOMER:ID"]);

        // De leeskant: dezelfde sectie, dezelfde database.
        Assert.Equal(opties.Database, Klantenlijst(opties, Intern()).Find("soratus")!.Telemetry!.Database);
    }

    // ── Hulpmiddelen ────────────────────────────────────────────────────────────────────────────

    private static PlatformTelemetryOptions Ingericht() => new() { AccountEndpoint = Endpoint };

    private static CustomerRecord Intern() => new()
    {
        Id = "soratus",
        Name = "Soratus — intern beheer",
        IsInternal = true,
    };

    private static CustomerRecord Klant(string id) => new() { Id = id, Name = id };

    private static ICustomerDirectory Klantenlijst(
        PlatformTelemetryOptions? platform,
        params CustomerRecord[] klanten) =>
        new CustomerDirectory(
            Options.Create(new PortalCustomerOptions { Customers = [.. klanten] }),
            Options.Create(new PortalTelemetryOptions { AccountEndpoint = Endpoint, Database = "telemetry" }),
            Options.Create(platform ?? new PlatformTelemetryOptions { AccountEndpoint = null }));

    /// <summary>Een verse webbouwer met alleen deze configuratie erin.</summary>
    private static WebApplicationBuilder Bouwer(Dictionary<string, string?> configuratie)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.AddInMemoryCollection(configuratie);
        return builder;
    }

    /// <summary>De aankondigingen die als bron in de container staan.</summary>
    private static IReadOnlyList<HostedAgentDeclaration> Aankondigingen(WebApplicationBuilder builder) =>
    [
        .. builder.Services
            .Where(d => d.ServiceType == typeof(IHostedAgentSource) && d.ImplementationInstance is IHostedAgentSource)
            .SelectMany(d => ((IHostedAgentSource)d.ImplementationInstance!).GetAgents()),
    ];

    /// <summary>
    /// Eén draaiend portaal voor dit hele testproces, dat nooit wordt opgeruimd.
    /// </summary>
    /// <remarks>
    /// <para><strong>Dit is een reparatie op een gemeten flakiness die ik zelf had gemaakt, en de
    /// oorzaak staat al opgeschreven in <c>Urenapihost</c>.</strong> Daar staat: een
    /// <c>SymmetricSecurityKey</c> draagt zijn eigen <c>CryptoProviderFactory</c> met een cache, en
    /// validaties van een tweede host gaan stuk "zodra de eerste host was opgeruimd" — dan komt alles
    /// als <c>401</c> terug. Mijn eerste versie maakte per test een portaal aan, deed er een verzoek
    /// door de aanmeldketen op, en ruimde het weer op. Gemeten over zes volle runs van dit
    /// testproject: <strong>drie keer rood met vijftien tot eenentwintig gevallen tests op
    /// <c>/api/uren</c></strong> — allemaal een <c>401</c> waar een <c>400</c> of een <c>422</c> hoorde
    /// te staan. Dezelfde zes runs op de onveranderde boom: zes keer groen. Het was dus van mij.</para>
    ///
    /// <para>Wat het niet was: langzaam. Twee tests die na twee seconden omvielen zonder een portaal
    /// aan te maken deden niets, en twee tests die een portaal aanmaakten en het netjes opruimden
    /// zonder erdoorheen te verzoeken deden ook niets. Het was de combinatie — een verzoek door de
    /// aanmeldketen op een host die daarna wordt opgeruimd — en dat is precies de opstelling die
    /// <c>Urenapihost</c> beschrijft.</para>
    ///
    /// <para><strong>Niet opruimen is hier het antwoord en niet luiheid.</strong> Er is één portaal per
    /// testproces, het leeft zolang het proces leeft, en er is dus geen moment waarop een ándere host
    /// zijn ondertekenaars kwijtraakt. Hetzelfde wat <c>Urenapicollectie</c> met een collectiefixture
    /// doet, en om dezelfde reden. Het besturingssysteem ruimt op bij het afsluiten; een
    /// <c>Dispose</c> die precies het gedrag terugbrengt dat hier is gerepareerd, hoort er niet te
    /// staan.</para>
    /// </remarks>
    private static readonly Portaalhost Portaal = new();

    /// <summary>Het echte portaal, met alleen de twee achtergronddiensten eruit die Cosmos opzoeken.</summary>
    private sealed class Portaalhost : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>(typeof(TelemetryWarmup));
                services.RemoveAll<IHostedService>(typeof(PortalDirectoryRefresh));
            });
        }
    }
}
