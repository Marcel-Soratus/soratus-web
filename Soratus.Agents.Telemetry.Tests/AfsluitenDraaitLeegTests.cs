using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry.Internal;
using Soratus.Agents.Telemetry.Logging;
using Soratus.Agents.Telemetry.Tests.Hulpmiddelen;

namespace Soratus.Agents.Telemetry.Tests;

/// <summary>
/// Wat er is weggeschreven vlak vóór het afsluiten, moet aankomen.
/// </summary>
/// <remarks>
/// <para><strong>Dit is het gewone geval voor een kortlevende agent en niet een randgeval.</strong>
/// Een taak die twee seconden werkt en afsluit, doet dat binnen elk redelijk doorschrijfinterval.
/// Draait de buffer dan niet leeg, dan verliest die agent niet zijn laatste regel maar <em>al</em>
/// zijn telemetrie — en stil: de kanalen worden netjes afgesloten, er valt niets in de teller van
/// weggegooide regels, en er staat geen waarschuwing in de log. In het portaal ziet dat uit als een
/// agent die niets te melden had.</para>
///
/// <para>De aanleiding was een testrun op een Linux-runner waar één van de drie
/// regelovergangvarianten van <c>MsgKnipViaSchrijfpadenTests</c> rood ging met een <em>lege</em>
/// sink terwijl de andere twee slaagden. Dat is geen verschil per regelovergang: die variant verloor
/// een race. De pompen werden gestart in het lijf van <c>ExecuteAsync</c>, en dat is niet
/// gegarandeerd gelopen als <c>StartAsync</c> terugkomt — dus stopte de host binnen dat venster, dan
/// sloeg het leegdraaien zichzelf over.</para>
///
/// <para><strong>Wat de twee gedragstests wél en niet meten.</strong> Ze pinnen het afleverpad vast:
/// met een doorschrijfinterval dat veel langer is dan de host leeft, kan alléén het leegdraaien bij
/// afsluiten de regel hebben bezorgd. Ze reproduceren de oorspronkelijke race <em>niet</em> — die hing
/// van de planner af. Gemeten en niet aangenomen: met de reparatie weggehaald bleven alle 82 tests zes
/// runs op rij groen op Windows, terwijl dezelfde code op een Linux-runner rood ging.</para>
///
/// <para>Daarom staat de derde test er, en die is de enige die de fout werkelijk tegenhoudt: hij pint
/// de <em>invariant</em> vast in plaats van zijn gevolg. Een test die van de planner afhangt is flaky,
/// en flaky is erger dan ongedekt — maar "ongedekt" was hier ook niet goed genoeg.</para>
/// </remarks>
public class AfsluitenDraaitLeegTests
{
    /// <summary>
    /// Ruim langer dan de host in deze tests leeft. Zo is de periodieke lus uitgesloten als bezorger
    /// en blijft alleen het leegdraaien bij afsluiten over.
    /// </summary>
    private static readonly TimeSpan GeenPeriodiekeKans = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task EenRegelVanVlakVoorHetAfsluitenKomtAan()
    {
        OpvangendeSink sink = await StartLogEnStopAsync(logger =>
            logger.AgentEvent("afsluit.proef", "Eén regel, en dan meteen afsluiten."));

        LogRecord regel = Assert.Single(sink.Logs, r => r.Event == "afsluit.proef");

        Assert.Equal("Eén regel, en dan meteen afsluiten.", regel.Message);
    }

    [Fact]
    public async Task AlleRegelsKomenAanEnNietAlleenDeLaatste()
    {
        // Eén regel bewijst dat er een leegdraaipad is; twintig bewijzen dat het pad de hele buffer
        // neemt en niet toevallig het laatste dat erin ging.
        OpvangendeSink sink = await StartLogEnStopAsync(logger =>
        {
            for (var i = 0; i < 20; i++)
            {
                logger.AgentEvent("afsluit.reeks", $"Regel {i}.");
            }
        });

        Assert.Equal(20, sink.Logs.Count(r => r.Event == "afsluit.reeks"));
    }

    [Fact]
    public async Task ZodraDeHostIsGestartIsHetLeegdraaipadGewapend()
    {
        // De enige van de drie die de oorspronkelijke fout tegenhoudt. Hij meet niet het gevolg —
        // "komt de regel aan" — want dat gevolg hangt af van de planner en is op deze machine niet te
        // betrappen. Hij meet de invariant: als StartAsync terugkomt, moet StopAsync iets hebben om
        // op te wachten. Zet iemand de pompen terug in ExecuteAsync, dan gaat deze regel rood.
        var sink = new OpvangendeSink();

        HostApplicationBuilder builder = Bouwer(sink);

        using IHost host = builder.Build();

        TelemetryWriter schrijver = host.Services
            .GetServices<IHostedService>()
            .OfType<TelemetryWriter>()
            .Single();

        Assert.False(
            schrijver.DrainPathArmed,
            "Vóór het starten hoort er nog niets te lopen. Staat dit al aan, dan meet de assertie " +
            "hieronder niets meer.");

        await host.StartAsync();

        Assert.True(
            schrijver.DrainPathArmed,
            "StartAsync is teruggekomen zonder dat de pompen lopen. StopAsync draait de buffer dan " +
            "niet leeg, en alles wat een kortlevende agent heeft weggeschreven is stil verdwenen: de " +
            "kanalen sluiten netjes, er valt niets in de teller van weggegooide regels, en er staat " +
            "geen waarschuwing in de log.");

        await host.StopAsync();
    }

    /// <summary>
    /// Start een host met een doorschrijfinterval dat nooit aan de beurt komt, logt, en stopt.
    /// </summary>
    /// <param name="regels">Wat er wordt gelogd, tussen starten en stoppen.</param>
    /// <returns>Wat er is weggeschreven.</returns>
    private static async Task<OpvangendeSink> StartLogEnStopAsync(Action<ILogger> regels)
    {
        var sink = new OpvangendeSink();

        using IHost host = Bouwer(sink).Build();

        await host.StartAsync();

        regels(host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(Proefagent.Categorie));

        await host.StopAsync();

        return sink;
    }

    /// <summary>
    /// Een host met de bibliotheek erin, de opgegeven sink in plaats van Cosmos, en een
    /// doorschrijfinterval dat binnen een test nooit aan de beurt komt.
    /// </summary>
    /// <param name="sink">De sink die opvangt wat er wordt weggeschreven.</param>
    /// <returns>De bouwer.</returns>
    private static HostApplicationBuilder Bouwer(OpvangendeSink sink)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SORATUS_CUSTOMER__ID"] = "bakker",
            ["SORATUS_AGENT__NAME"] = "bakker-voorraad-sync",
            ["SORATUS_TELEMETRY__ENDPOINT"] = "https://localhost:8081/",
        });

        builder.AddSoratusAgent(opties => opties.FlushInterval = GeenPeriodiekeKans);

        builder.Services.RemoveAll<ITelemetrySink>();
        builder.Services.AddSingleton<ITelemetrySink>(sink);

        return builder;
    }
}
