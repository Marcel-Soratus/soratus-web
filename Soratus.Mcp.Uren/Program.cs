using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using Soratus.Mcp.Uren;

// Twee commando's naast de MCP-modus. Ze bestaan omdat de aanmelding anders het enige stuk is dat
// nooit gedraaid heeft: de proefdraaimodus slaat hem over en het endpoint bestaat nog niet, dus de
// eerste echte boeking zou tegelijk de eerste aanmeldpoging zijn.
string? verb = args.Length > 0 ? args[0].ToLowerInvariant() : null;

if (verb is "aanmelden" or "controleer")
{
    IConfiguration commandConfiguration = new ConfigurationBuilder()
        .AddEnvironmentVariables()
        .Build();

    UrenOptions commandOptions;
    try
    {
        commandOptions = UrenConfiguration.Resolve(commandConfiguration);
    }
    catch (InvalidOperationException exception)
    {
        await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
        return 1;
    }

    if (commandOptions.DryRun)
    {
        await Console.Error
            .WriteLineAsync(
                $"{UrenConfiguration.DryRunKey} staat aan, dus er is geen scope en geen client " +
                "ingesteld en er valt niets aan te melden. Zet hem uit om de aanmelding te " +
                "controleren.")
            .ConfigureAwait(false);
        return 1;
    }

    return verb == "aanmelden"
        ? await SignInCommand.SignInAsync(commandOptions, CancellationToken.None).ConfigureAwait(false)
        : await SignInCommand.CheckAsync(commandOptions, CancellationToken.None).ConfigureAwait(false);
}

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Stdout is het JSON-RPC-kanaal van de stdio-transport. Eén regel logging of één Console.WriteLine
// erop maakt de stroom onleesbaar en de client verbreekt de verbinding met een parsefout die niets
// over de oorzaak zegt. Alle logging gaat daarom naar stderr, ook Trace.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(static options => options.LogToStandardErrorThreshold = LogLevel.Trace);

UrenOptions options;
try
{
    options = UrenConfiguration.Resolve(builder.Configuration);
}
catch (InvalidOperationException exception)
{
    // Meteen omvallen met een leesbare regel op stderr. Een MCP-server die stil half werkt is
    // bijzonder onaangenaam: de aanroeper ziet alleen dat de tool er niet is, en niet waarom.
    await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
    return 1;
}

builder.Services.AddSingleton(Options.Create(options));
builder.Services.TryAddSingleton(TimeProvider.System);

// Een eigen public client met device-code, en géén DefaultAzureCredential — ook niet als
// terugvaloptie. Zie UrenCredentials: een terugval op de Azure CLI heropent de route die dat besluit
// juist sluit, stil, zodra iemand ooit de CLI-client op onze API autoriseert.
//
// In deze modus is de credential stil: hij gebruikt een bestaande aanmelding en vraagt nooit. Een
// device-code-prompt zou op stdout moeten en dat is het JSON-RPC-kanaal.
builder.Services.TryAddSingleton<TokenCredential>(_ => UrenCredentials.CreateSilent(options));
builder.Services.AddTransient<PortalTokenHandler>();

IHttpClientBuilder http = builder.Services
    .AddHttpClient<PortalUrenClient>(client =>
    {
        // Het pad wordt relatief aangeleverd, dus de basis-URL moet op een slash eindigen; anders
        // gooit Uri het laatste segment weg en komt het verzoek op de verkeerde plek uit.
        client.BaseAddress = new Uri(options.PortalBaseAddress!.ToString().TrimEnd('/') + "/");
        client.Timeout = options.Timeout;
    });

if (!options.DryRun)
{
    http.AddHttpMessageHandler<PortalTokenHandler>();
}

builder.Services
    .AddMcpServer(server => server.ServerInfo = new Implementation
    {
        Name = "soratus-uren",
        Version = typeof(UrenTools).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
    })
    .WithStdioServerTransport()
    .WithTools<UrenTools>();

await builder.Build().RunAsync().ConfigureAwait(false);

return 0;
