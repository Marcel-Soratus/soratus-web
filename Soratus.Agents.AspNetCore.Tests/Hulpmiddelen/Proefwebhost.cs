using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Soratus.Agents.Telemetry.Internal;

namespace Soratus.Agents.AspNetCore.Tests.Hulpmiddelen;

/// <summary>
/// Een echte ASP.NET Core-webapplicatie met een echte verzoekpijplijn, zonder poort en zonder
/// Cosmos.
/// </summary>
/// <remarks>
/// Bewust een echte host en niet een verzonnen <c>HttpContext</c>. De hele constructie hangt aan
/// twee dingen die alleen in een echte pijplijn bestaan: de metadata van het endpoint dat routing
/// heeft uitgekozen, en het moment waarop de <c>EndpointDataSource</c> gevuld is. Een test met een
/// nagebouwd verzoek bewijst juist die twee niet.
/// </remarks>
internal sealed class Proefwebhost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private Proefwebhost(WebApplication app, OpvangendeSink sink, StuurbareKlok klok)
    {
        _app = app;
        Sink = sink;
        Klok = klok;
        Client = app.GetTestClient();
    }

    internal OpvangendeSink Sink { get; }

    internal StuurbareKlok Klok { get; }

    internal HttpClient Client { get; }

    internal IServiceProvider Diensten => _app.Services;

    /// <summary>
    /// Zet een host op met <paramref name="endpoints"/> erin en start hem.
    /// </summary>
    /// <param name="endpoints">De endpoints, meestal met <c>WithSoratusAgent</c> erachter.</param>
    /// <param name="aanroeplaag">
    /// Of <c>UseSoratusAgentRuns</c> in de pijplijn komt. Op <c>false</c> voor de test die meet wat
    /// er gebeurt als iemand die regel vergeet.
    /// </param>
    /// <param name="extraConfiguratie">Configuratiesleutels die deze test wil overschrijven.</param>
    internal static async Task<Proefwebhost> StartAsync(
        Action<WebApplication> endpoints,
        bool aanroeplaag = true,
        Dictionary<string, string?>? extraConfiguratie = null)
    {
        var sink = new OpvangendeSink();
        var klok = new StuurbareKlok(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero));

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();

        var configuratie = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["SORATUS_CUSTOMER__ID"] = "mbv",
            ["SORATUS_AGENT__NAME"] = "mbv-web",
            // Let op de spelling: de configuratie wordt met Enum.TryParse gelezen, dus hier hoort
            // de naam uit de enum ('Production') en niet de vorm die in het JSON-document staat
            // ('prod'). Dat is een val die in de foutmelding van de bibliotheek zelf staat; zie het
            // rapport bij punt 42.
            ["SORATUS_AGENT__ENVIRONMENT"] = "Production",
            ["SORATUS_TELEMETRY__ENDPOINT"] = "https://localhost:8081/",
        };

        foreach ((string sleutel, string? waarde) in extraConfiguratie ?? [])
        {
            configuratie[sleutel] = waarde;
        }

        builder.Configuration.AddInMemoryCollection(configuratie);

        builder.AddSoratusWebAgents(opties => opties.FlushInterval = TimeSpan.FromMilliseconds(20));

        // Cosmos eruit, opvangende sink erin; de systeemklok eruit, een stuurbare erin. De rest van
        // de keten blijft ongemoeid — dat is het punt van deze opstelling.
        builder.Services.RemoveAll<ITelemetrySink>();
        builder.Services.AddSingleton<ITelemetrySink>(sink);
        builder.Services.RemoveAll<TimeProvider>();
        builder.Services.AddSingleton<TimeProvider>(klok);

        WebApplication app = builder.Build();
        app.UseRouting();

        if (aanroeplaag)
        {
            app.UseSoratusAgentRuns();
        }

        endpoints(app);

        await app.StartAsync();
        return new Proefwebhost(app, sink, klok);
    }

    /// <summary>Draait de schrijfbuffer leeg, zodat alles wat er in zit in de sink staat.</summary>
    /// <remarks>
    /// De buffer is met opzet niet-blokkerend: een agent wacht nooit op telemetrie. Een test die
    /// meteen na een verzoek in de sink kijkt, kijkt dus soms te vroeg. Kort wachten op wat er komen
    /// moet is eerlijker dan de bufferlaag in de test wegnemen — dan zou de test de keten meten die
    /// in productie niet bestaat.
    /// </remarks>
    internal async Task LeegdraaienAsync(Func<bool> totdat)
    {
        for (int poging = 0; poging < 100 && !totdat(); poging++)
        {
            await Task.Delay(20);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
