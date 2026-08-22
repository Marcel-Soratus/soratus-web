using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry.HostedAgents;
using Soratus.Agents.Telemetry.Internal;
using Soratus.Agents.Telemetry.Tests.Hulpmiddelen;

namespace Soratus.Agents.Telemetry.Tests;

/// <summary>
/// Een geherbergde agent die op een klok draait in plaats van op een aanroep.
/// </summary>
/// <remarks>
/// <para>Dit is het geval dat de laag van punt 42 nog niet kon uitdrukken. Daar draait elke
/// geherbergde agent op een aanroep — een verzoek, een bericht — en dan is er geen volgende run om te
/// voorspellen. De beheeragents van het portaal draaien op een tik van een timer, en die hebben een
/// plan. Zonder gepubliceerd plan is "laatste run 26 uur geleden" niet te beoordelen: er staat
/// nergens hoe vaak deze agent hoort te draaien.</para>
///
/// <para><strong>De scherpste test hier is
/// <see cref="De_volgende_run_is_wat_de_host_meldt_en_geen_herberekening_uit_de_cron"/>.</strong> Die
/// meet de invariant en niet zijn gevolg: een <c>nextRunAt</c> die bij elke hartslag uit de cron wordt
/// herrekend ligt altijd in de toekomst, en dan is een gemiste run er per constructie niet aan te
/// zien. Alleen het moment waarop werkelijk wordt gewacht kan in het verleden komen te liggen.</para>
/// </remarks>
public sealed class GeherbergdeKlokagentTests
{
    private const string Collector = "kosten-collector";

    private static readonly SoratusSchedule Nachtelijk = SoratusSchedule.Parse("0 4 * * *");

    [Fact]
    public async Task Een_klokagent_publiceert_zijn_plan_en_zijn_volgende_run()
    {
        DateTimeOffset volgende = new(2026, 8, 22, 4, 0, 0, TimeSpan.Zero);

        (OpvangendeRegistratieSink sink, _) = await DraaiAsync(async agents =>
        {
            ISoratusHostedAgent agent = agents.GetOrAdd(Aankondiging());
            agent.ReportNextRun(volgende);

            // Eén tik van de klok is één run, en de trigger is een timer. Bij een dienst op aanvraag
            // zou dat http of queue zijn; hier is het de klok zelf.
            await agent.RunAsync(TriggerKind.Timer, static (run, _) =>
            {
                run.Processed(3);
                return Task.CompletedTask;
            });
        });

        AgentRegistration registratie = sink.Registrations.Last(r => r.AgentName == Collector);

        Assert.Equal("0 4 * * *", registratie.Schedule);
        Assert.Equal(TriggerKind.Timer, registratie.TriggerKind);
        Assert.Equal(volgende, registratie.NextRunAt);

        RunRecord run = Assert.Single(sink.Runs, document => document.Result != RunResult.Running);
        Assert.Equal(Collector, run.AgentName);
        Assert.Equal(TriggerKind.Timer, run.Trigger);
        Assert.Equal(RunResult.Ok, run.Result);
        Assert.Equal(3, run.ItemsProcessed);
    }

    [Fact]
    public async Task De_volgende_run_is_wat_de_host_meldt_en_geen_herberekening_uit_de_cron()
    {
        // Het moment ligt ruim in het verleden. Zou de bibliotheek nextRunAt uit de cron vanaf nu
        // uitrekenen — zoals het pad met één agent per proces doet — dan stond hier een tijdstip in de
        // toekomst en was deze test groen zonder dat de eigenschap bestond. Dit is de enige manier
        // waarop een stilgevallen planlus in een levend proces zichtbaar is.
        DateTimeOffset gemist = new(2020, 1, 1, 4, 0, 0, TimeSpan.Zero);

        (OpvangendeRegistratieSink sink, _) = await DraaiAsync(agents =>
        {
            agents.GetOrAdd(Aankondiging()).ReportNextRun(gemist);
            return Task.CompletedTask;
        });

        AgentRegistration registratie = sink.Registrations.Last(r => r.AgentName == Collector);

        Assert.Equal(gemist, registratie.NextRunAt);
        Assert.True(registratie.NextRunAt < registratie.LastHeartbeatAt);
    }

    [Fact]
    public async Task Zonder_gemeld_moment_blijft_de_volgende_run_leeg_ook_met_een_plan()
    {
        // Een plan zegt wanneer er hoort te worden gedraaid; het zegt niet dat er iemand op wacht. Zou
        // hier een tijdstip staan, dan was het uit de cron gerekend en daarmee altijd in de toekomst.
        (OpvangendeRegistratieSink sink, _) = await DraaiAsync(agents =>
        {
            agents.GetOrAdd(Aankondiging());
            return Task.CompletedTask;
        });

        AgentRegistration registratie = sink.Registrations.Last(r => r.AgentName == Collector);

        Assert.Equal("0 4 * * *", registratie.Schedule);
        Assert.Null(registratie.NextRunAt);
    }

    [Fact]
    public async Task Een_gemeld_moment_op_een_dienst_zonder_plan_wordt_niet_gepubliceerd()
    {
        // Anders staat er een 'volgende run' naast een triggerKind die zegt dat deze dienst op een
        // aanroep draait — precies de tegenspraak die Validate weigert, nu via een omweg.
        (OpvangendeRegistratieSink sink, _) = await DraaiAsync(agents =>
        {
            agents.Find("wachtrij-verwerker")!.ReportNextRun(new DateTimeOffset(2026, 8, 22, 4, 0, 0, TimeSpan.Zero));
            return Task.CompletedTask;
        });

        foreach (AgentRegistration registratie in sink.Registrations.Where(r => r.AgentName == "wachtrij-verwerker"))
        {
            Assert.Null(registratie.Schedule);
            Assert.Null(registratie.NextRunAt);
        }
    }

    [Fact]
    public void Een_timer_zonder_plan_en_een_plan_zonder_timer_zijn_beide_een_fout()
    {
        InvalidOperationException zonderPlan = Assert.Throws<InvalidOperationException>(
            () => new HostedAgentDeclaration { AgentName = Collector, Trigger = TriggerKind.Timer }.Validate());

        Assert.Contains("Schedule", zonderPlan.Message, StringComparison.Ordinal);

        InvalidOperationException zonderTimer = Assert.Throws<InvalidOperationException>(
            () => new HostedAgentDeclaration
            {
                AgentName = "voorraad-webhook",
                Trigger = TriggerKind.Webhook,
                Schedule = Nachtelijk,
            }.Validate());

        Assert.Contains("0 4 * * *", zonderTimer.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Twee_keer_hetzelfde_plan_is_dezelfde_aankondiging()
    {
        // Waardegelijkheid op het plan, en dat is geen nettigheid. De aanroepkant vraagt zijn agent op
        // met GetOrAdd(aankondiging) — twee keer opgebouwd, want de lus en de bron bouwen hem allebei —
        // en zonder deze gelijkheid leest de registry dat als twee verschillende aankondigingen en
        // waarschuwt hij over een verschil dat er niet is.
        Assert.Equal(Aankondiging(), Aankondiging());
        Assert.Equal(SoratusSchedule.Parse("0 4 * * *"), SoratusSchedule.Parse(" 0 4 * * * "));
        Assert.NotEqual(SoratusSchedule.Parse("0 4 * * *"), SoratusSchedule.Parse("0 5 * * *"));
    }

    [Fact]
    public void Een_plan_rekent_in_zijn_eigen_zone_en_levert_UTC()
    {
        // Zes uur in Nederland is vier uur UTC in de zomer. Dat de uitkomst UTC is, is geen detail:
        // gemengde offsets in de opslag sorteren lexicografisch verkeerd.
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        SoratusSchedule plan = SoratusSchedule.Parse("0 6 1 * *", zone);

        DateTimeOffset? volgende = plan.NextAfter(new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 9, 1, 4, 0, 0, TimeSpan.Zero), volgende);
        Assert.Equal(TimeSpan.Zero, volgende!.Value.Offset);
    }

    [Fact]
    public void Een_onzinnig_plan_wordt_geweigerd_en_niet_gecorrigeerd()
    {
        Assert.Throws<InvalidOperationException>(() => SoratusSchedule.Parse("elke nacht"));
        Assert.Throws<ArgumentException>(() => SoratusSchedule.Parse("   "));
    }

    private static HostedAgentDeclaration Aankondiging() => new()
    {
        AgentName = Collector,
        DisplayType = "Cost Management",
        Trigger = TriggerKind.Timer,
        Schedule = SoratusSchedule.Parse("0 4 * * *"),
        TriggerDetail = "dagelijks 04:00 UTC",
    };

    /// <summary>
    /// Zet een gewone host op — geen web — met één dienst op aanvraag en een opvangende sink, en laat
    /// de test daar een klokagent bij zetten.
    /// </summary>
    /// <param name="werk">Wat er met de agents gebeurt.</param>
    /// <returns>De sink en de container.</returns>
    private static async Task<(OpvangendeRegistratieSink Sink, IServiceProvider Diensten)> DraaiAsync(
        Func<ISoratusHostedAgents, Task> werk)
    {
        var sink = new OpvangendeRegistratieSink();

        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SORATUS_CUSTOMER__ID"] = "soratus",
            ["SORATUS_AGENT__NAME"] = "soratus-portal",
            ["SORATUS_TELEMETRY__ENDPOINT"] = "https://localhost:8081/",
        });

        builder.AddSoratusHostedAgents(opties => opties.FlushInterval = TimeSpan.FromMilliseconds(20));

        // Eén dienst op aanvraag ernaast, zodat de twee soorten in dezelfde host staan: dat is de
        // werkelijkheid van een webapplicatie die ook een klok heeft.
        builder.Services.AddSoratusHostedAgent(new HostedAgentDeclaration
        {
            AgentName = "wachtrij-verwerker",
            DisplayType = "Wachtrij",
            Trigger = TriggerKind.Queue,
        });

        builder.Services.RemoveAll<ITelemetrySink>();
        builder.Services.AddSingleton<ITelemetrySink>(sink);

        using IHost host = builder.Build();
        await host.StartAsync();

        await werk(host.Services.GetRequiredService<ISoratusHostedAgents>());

        await host.StopAsync();

        return (sink, host.Services);
    }
}
