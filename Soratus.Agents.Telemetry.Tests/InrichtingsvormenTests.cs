using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry.Internal;
using Soratus.Agents.Telemetry.Tests.Hulpmiddelen;

namespace Soratus.Agents.Telemetry.Tests;

/// <summary>
/// Twee dingen die een agent stil verkeerd of stil onzichtbaar maakten.
/// </summary>
public class InrichtingsvormenTests
{
    [Theory]
    [InlineData("prod", AgentEnvironment.Production)]
    [InlineData("acc", AgentEnvironment.Acceptance)]
    [InlineData("dev", AgentEnvironment.Development)]
    [InlineData("PROD", AgentEnvironment.Production)]
    public void DeVormUitDeDocumentenWordtAangenomen(string waarde, AgentEnvironment verwacht)
    {
        // Dit is de vorm die in élk telemetriedocument en op élk scherm staat. Wie overtypt wat hij
        // voor zich ziet, hoort geen inrichtingsfout te krijgen — en de foutmelding bij een agent in
        // Azure zonder deze sleutel wees precies deze vorm aan, terwijl de parser hem weigerde.
        using IHost host = Host(waarde);

        Assert.Equal(verwacht, host.Services.GetRequiredService<AgentIdentity>().Environment);
    }

    [Theory]
    [InlineData("Production", AgentEnvironment.Production)]
    [InlineData("Acceptance", AgentEnvironment.Acceptance)]
    [InlineData("development", AgentEnvironment.Development)]
    public void DeNaamVanHetLidBlijftOokWerken(string waarde, AgentEnvironment verwacht)
    {
        // De spiegel. Verruimen mag niets afpakken van wie het al goed had.
        using IHost host = Host(waarde);

        Assert.Equal(verwacht, host.Services.GetRequiredService<AgentIdentity>().Environment);
    }

    [Fact]
    public void EenOnbekendeWaardeNoemtDeVormenDieEenLezerHeeftGezien()
    {
        InvalidOperationException fout = Assert.Throws<InvalidOperationException>(() => Host("productie"));

        Assert.Contains("productie", fout.Message, StringComparison.Ordinal);

        // De opsomming noemt de documentvorm en niet de C#-naam. Noemde hij de C#-namen, dan wees de
        // melding een andere weg dan de rest van het systeem laat zien — en dat was precies het
        // gebrek: drie plekken, drie schrijfwijzen.
        Assert.Contains("prod", fout.Message, StringComparison.Ordinal);
        Assert.Contains("acc", fout.Message, StringComparison.Ordinal);
        Assert.Contains("dev", fout.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Production", fout.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ZodraDeHostIsGestartHeeftDeAgentZichAangemeld()
    {
        // De invariant en niet zijn gevolg. De eerste registratie stond in ExecuteAsync, en dat lijf
        // is niet gegarandeerd gelopen als StartAsync terugkomt — dus een agent die start, werkt en
        // meteen afsluit meldde zich helemaal niet. Dat is erger dan een ontbrekende logregel: dan
        // bestaat hij niet in het portaal en is er geen rij om iets aan te zien.
        //
        // Waarom niet meten of het document aankomt: dat hangt af van de planner, en een test die
        // van de planner afhangt is flaky. Gemeten in een eerdere ronde: dezelfde fout in de
        // schrijver bleef zes runs op rij groen op Windows terwijl hij op Linux rood ging.
        using IHost host = Host("dev");

        AgentRegistrationService dienst = host.Services
            .GetServices<IHostedService>()
            .OfType<AgentRegistrationService>()
            .Single();

        Assert.False(
            dienst.Announced,
            "Vóór het starten hoort er nog niets gemeld te zijn. Staat dit al aan, dan meet de " +
            "assertie hieronder niets meer.");

        await host.StartAsync();

        Assert.True(
            dienst.Announced,
            "StartAsync is teruggekomen zonder dat deze agent zich heeft aangemeld. Sluit het proces " +
            "nu af, dan staat er nooit een registratie in de opslag en bestaat de agent niet in het " +
            "portaal — zonder dat er ergens een fout wordt gemeld.");

        await host.StopAsync();
    }

    /// <summary>Een host met de bibliotheek erin en de opgegeven omgevingswaarde.</summary>
    /// <param name="omgeving">De waarde voor <c>SORATUS_AGENT__ENVIRONMENT</c>.</param>
    /// <returns>De host, nog niet gestart.</returns>
    private static IHost Host(string omgeving)
    {
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateEmptyApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = Environments.Development });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SORATUS_CUSTOMER__ID"] = "bakker",
            ["SORATUS_AGENT__NAME"] = "bakker-voorraad-sync",
            ["SORATUS_TELEMETRY__ENDPOINT"] = "https://localhost:8081/",
            ["SORATUS_AGENT__ENVIRONMENT"] = omgeving,
        });

        builder.AddSoratusAgent(opties => opties.FlushInterval = TimeSpan.FromMinutes(5));

        builder.Services.RemoveAll<ITelemetrySink>();
        builder.Services.AddSingleton<ITelemetrySink>(new OpvangendeSink());

        return builder.Build();
    }
}
