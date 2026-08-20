using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Soratus.Agents.HeartbeatDemo;
using Soratus.Agents.Telemetry;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<HeartbeatDemoOptions>(builder.Configuration.GetSection("HeartbeatDemo"));

// Dit is alles. Registratie, hartslag, levenscyclus, runs, logs en de planning op
// SORATUS_AGENT__SCHEDULE komen hieruit.
builder.AddSoratusAgent<HeartbeatDemoAgent>();

await builder.Build().RunAsync();
