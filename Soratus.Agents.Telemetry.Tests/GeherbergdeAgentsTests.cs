using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry.HostedAgents;
using Soratus.Agents.Telemetry.Internal;
using Soratus.Agents.Telemetry.Logging;
using Soratus.Agents.Telemetry.Tests.Hulpmiddelen;
using ContractLogLevel = Soratus.Agents.Contracts.LogLevel;

namespace Soratus.Agents.Telemetry.Tests;

/// <summary>
/// De laag voor meerdere agents in één host, zonder webframework erbij.
/// </summary>
/// <remarks>
/// Deze tests staan hier en niet in <c>Soratus.Agents.AspNetCore.Tests</c>, en dat is het punt dat
/// ze bewijzen: "meerdere agents in één proces, een hartslag van de host, een run per aanroep" is
/// niet iets van ASP.NET Core. Alleen het antwoord op de vraag welke agents het zijn is
/// hostspecifiek. Zou deze laag ASP.NET nodig hebben, dan zou dit bestand niet compileren.
/// </remarks>
public sealed class GeherbergdeAgentsTests
{
    [Fact]
    public async Task Twee_aangekondigde_diensten_leveren_twee_registraties_op()
    {
        (OpvangendeRegistratieSink sink, _) = await DraaiAsync(static _ => Task.CompletedTask);

        Assert.Equal(
            ["voorraad-webhook", "wachtrij-verwerker"],
            sink.Registrations.Select(static registratie => registratie.AgentName)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));

        // Zonder werk is de levensfase 'wacht op werk'; bij het afsluiten wordt dat 'netjes
        // gestopt'. Deze host is inmiddels gestopt, dus per agent hoort het eerste document het
        // eerste te zeggen en het laatste het tweede.
        foreach (IGrouping<string, AgentRegistration> agent in
            sink.Registrations.GroupBy(static registratie => registratie.AgentName, StringComparer.Ordinal))
        {
            Assert.Equal(AgentLifecycle.IdleWaiting, agent.First().Lifecycle);
            Assert.Equal(AgentLifecycle.StoppedCleanly, agent.Last().Lifecycle);
        }

        foreach (AgentRegistration registratie in sink.Registrations)
        {
            Assert.Null(registratie.Schedule);
            Assert.Null(registratie.NextRunAt);
        }
    }

    [Fact]
    public async Task Een_run_op_naam_van_één_dienst_staat_niet_op_naam_van_de_andere()
    {
        (OpvangendeRegistratieSink sink, _) = await DraaiAsync(static async diensten =>
        {
            var agents = diensten.GetRequiredService<ISoratusHostedAgents>();
            ISoratusHostedAgent? verwerker = agents.Find("wachtrij-verwerker");
            Assert.NotNull(verwerker);

            await verwerker.RunAsync(TriggerKind.Queue, (run, _) =>
            {
                run.Processed(7);
                diensten.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Bakker.Voorraad")
                    .AgentEvent("bericht.verwerkt", "Zeven berichten verwerkt.");
                return Task.CompletedTask;
            });
        });

        RunRecord run = Assert.Single(sink.Runs, document => document.Result != RunResult.Running);
        Assert.Equal("wachtrij-verwerker", run.AgentName);
        Assert.Equal(TriggerKind.Queue, run.Trigger);
        Assert.Equal(7, run.ItemsProcessed);

        LogRecord regel = Assert.Single(sink.Logs, r => r.Event == "bericht.verwerkt");
        Assert.Equal("wachtrij-verwerker", regel.AgentName);
        Assert.Equal(run.Id, regel.RunId);
    }

    [Fact]
    public async Task Een_dienst_die_niet_is_aangekondigd_bestaat_niet()
    {
        _ = await DraaiAsync(static diensten =>
        {
            Assert.Null(diensten.GetRequiredService<ISoratusHostedAgents>().Find("verzonnen"));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Buiten_een_run_hoort_een_logregel_bij_geen_enkele_dienst()
    {
        (OpvangendeRegistratieSink sink, _) = await DraaiAsync(static diensten =>
        {
            diensten.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Bakker.Onderhoud")
                .AgentEvent("onderhoud.gedraaid", "Het onderhoud is gedraaid.");
            return Task.CompletedTask;
        });

        Assert.DoesNotContain(sink.Logs, regel => regel.Event == "onderhoud.gedraaid");
    }

    [Fact]
    public async Task Een_mededeling_van_de_host_kan_wel_op_naam_van_een_dienst()
    {
        (OpvangendeRegistratieSink sink, _) = await DraaiAsync(static diensten =>
        {
            ISoratusHostedAgent? webhook =
                diensten.GetRequiredService<ISoratusHostedAgents>().Find("voorraad-webhook");
            Assert.NotNull(webhook);
            webhook.ReportEvent(ContractLogLevel.Warn, "koppeling.traag", "De koppeling reageerde traag.");
            return Task.CompletedTask;
        });

        LogRecord regel = Assert.Single(sink.Logs, r => r.Event == "koppeling.traag");
        Assert.Equal("voorraad-webhook", regel.AgentName);
        Assert.Equal(ContractLogLevel.Warn, regel.Level);
        Assert.Null(regel.RunId);
    }

    /// <summary>
    /// Zet een gewone host op — geen web — met twee aangekondigde diensten en een opvangende sink.
    /// </summary>
    private static async Task<(OpvangendeRegistratieSink Sink, IServiceProvider Diensten)> DraaiAsync(
        Func<IServiceProvider, Task> werk)
    {
        var sink = new OpvangendeRegistratieSink();

        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SORATUS_CUSTOMER__ID"] = "bakker",
            ["SORATUS_AGENT__NAME"] = "bakker-host",
            ["SORATUS_TELEMETRY__ENDPOINT"] = "https://localhost:8081/",
        });

        builder.AddSoratusHostedAgents(opties => opties.FlushInterval = TimeSpan.FromMilliseconds(20));

        builder.Services.AddSoratusHostedAgent(new HostedAgentDeclaration
        {
            AgentName = "wachtrij-verwerker",
            DisplayType = "Wachtrij",
            Trigger = TriggerKind.Queue,
        });

        builder.Services.AddSoratusHostedAgent(new HostedAgentDeclaration
        {
            AgentName = "voorraad-webhook",
            DisplayType = "Koppeling",
            Trigger = TriggerKind.Webhook,
        });

        builder.Services.RemoveAll<ITelemetrySink>();
        builder.Services.AddSingleton<ITelemetrySink>(sink);

        using IHost host = builder.Build();
        await host.StartAsync();

        await werk(host.Services);

        await host.StopAsync();

        return (sink, host.Services);
    }
}
