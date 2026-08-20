using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Soratus.Agents.Telemetry.Internal;

namespace Soratus.Agents.Telemetry.Tests.Hulpmiddelen;

/// <summary>
/// Zet een echte host op met de bibliotheek erin en een opvangende sink in plaats van Cosmos.
/// </summary>
internal static class Proefagent
{
    /// <summary>
    /// De categorie waaronder de tests loggen.
    /// </summary>
    /// <remarks>
    /// Bewust buiten de naamruimte van de bibliotheek. De <c>SoratusLoggerProvider</c> filtert
    /// alles onder <c>Soratus.Agents.Telemetry.</c> weg om te voorkomen dat een waarschuwing over
    /// een volle buffer een nieuwe regel in diezelfde buffer zet — en de naamruimte van dit
    /// testproject valt daar ook onder. Een test die zijn eigen <c>ILogger&lt;T&gt;</c> gebruikt
    /// zou dus stil niets opvangen en altijd slagen.
    /// </remarks>
    internal const string Categorie = "Bakker.VoorraadSync";

    /// <summary>
    /// Draait <paramref name="werk"/> tegen een lopende host en geeft terug wat er is
    /// weggeschreven.
    /// </summary>
    internal static async Task<OpvangendeSink> DraaiAsync(Func<IServiceProvider, Task> werk)
    {
        var sink = new OpvangendeSink();

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

        builder.AddSoratusAgent(opties => opties.FlushInterval = TimeSpan.FromMilliseconds(20));

        // Cosmos eruit, opvangende sink erin. De rest van de keten blijft ongemoeid.
        builder.Services.RemoveAll<ITelemetrySink>();
        builder.Services.AddSingleton<ITelemetrySink>(sink);

        using IHost host = builder.Build();
        await host.StartAsync();

        await werk(host.Services);

        // Afsluiten sluit de kanalen en draait de buffer leeg, dus hierna staat alles in de sink.
        await host.StopAsync();

        return sink;
    }

    /// <summary>Logt één regel via het gewone <c>ILogger</c>-pad en geeft het resultaat terug.</summary>
    internal static async Task<OpvangendeSink> LogAsync(Action<ILogger> regel) =>
        await DraaiAsync(diensten =>
        {
            ILogger logger = diensten.GetRequiredService<ILoggerFactory>().CreateLogger(Categorie);
            regel(logger);
            return Task.CompletedTask;
        });
}
