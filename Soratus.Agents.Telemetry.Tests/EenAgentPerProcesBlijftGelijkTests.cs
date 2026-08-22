using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry.Internal;
using Soratus.Agents.Telemetry.Tests.Hulpmiddelen;

namespace Soratus.Agents.Telemetry.Tests;

/// <summary>
/// Het pad met één agent per proces, ongewijzigd — en dat is hier het punt van de tests.
/// </summary>
/// <remarks>
/// <para><strong>Deze tests bestaan omdat het parseren en uitrekenen van een cron-expressie is
/// verhuisd.</strong> Dat werk stond in <see cref="AgentSchedule"/> en staat nu in
/// <see cref="SoratusSchedule"/>, omdat een host die zijn geherbergde klok-agents zelf plant dezelfde
/// expressie moet kunnen aankondigen én erop wachten. Eén implementatie, twee aanroepers.</para>
///
/// <para>Wat er dus gemeten moet worden is dat de <em>oude</em> aanroeper niets merkt. Gemeten: er was
/// geen enkele test die van dit pad de <c>schedule</c> of de <c>nextRunAt</c> in het document
/// vastpinde — de verhuizing had daar dus stil iets kunnen breken. Deze drie tests sluiten dat gat, en
/// ze horen groen te blijven zolang <c>AddSoratusAgent</c> bestaat.</para>
///
/// <para><strong>En let op het verschil met de geherbergde klok-agent</strong>
/// (<see cref="GeherbergdeKlokagentTests"/>): hier wordt <c>nextRunAt</c> bij elke hartslag opnieuw uit
/// de cron gerekend vanaf nu, dus hij ligt per constructie in de toekomst. Dat is bestaand gedrag en
/// het is hier niet veranderd; het is wél de reden dat een geherbergde klok-agent zijn volgende run
/// meldt in plaats van hem te laten uitrekenen.</para>
/// </remarks>
public sealed class EenAgentPerProcesBlijftGelijkTests
{
    [Fact]
    public async Task Een_agent_met_een_schema_publiceert_zijn_cron_en_een_volgende_run()
    {
        (OpvangendeRegistratieSink sink, DateTimeOffset voor) = await DraaiAsync("0 4 * * *");

        AgentRegistration registratie = sink.Registrations[0];

        Assert.Equal("0 4 * * *", registratie.Schedule);
        Assert.Equal(TriggerKind.Timer, registratie.TriggerKind);
        Assert.NotNull(registratie.NextRunAt);

        // Uit de cron gerekend vanaf nu, dus in de toekomst. Dat is bestaand gedrag en geen keuze van
        // deze test; het staat er zodat een verhuizing van het rekenwerk niet stil iets anders oplevert.
        Assert.True(registratie.NextRunAt > voor);
        Assert.Equal(4, registratie.NextRunAt!.Value.Hour);
        Assert.Equal(0, registratie.NextRunAt!.Value.Minute);
        Assert.Equal(TimeSpan.Zero, registratie.NextRunAt!.Value.Offset);
    }

    [Fact]
    public async Task Een_agent_zonder_schema_publiceert_geen_cron_en_geen_volgende_run()
    {
        (OpvangendeRegistratieSink sink, _) = await DraaiAsync(schema: null);

        AgentRegistration registratie = sink.Registrations[0];

        Assert.Null(registratie.Schedule);
        Assert.Null(registratie.NextRunAt);
        Assert.Equal(TriggerKind.Manual, registratie.TriggerKind);
    }

    [Fact]
    public void Een_onleesbare_cron_wijst_naar_de_sleutel_waar_hij_uit_komt()
    {
        // De melding hoort de configuratiesleutel te noemen en niet alleen "dit is geen cron": de lezer
        // moet weten welke waarde hij moet aanpassen. Dat de expressie zelf erin staat is het tweede
        // deel daarvan.
        InvalidOperationException fout = Assert.Throws<InvalidOperationException>(
            () => Bouw("elke nacht om vier uur"));

        Assert.Contains("SORATUS_AGENT__SCHEDULE", fout.Message, StringComparison.Ordinal);
        Assert.Contains("elke nacht om vier uur", fout.Message, StringComparison.Ordinal);
    }

    /// <summary>Draait een host met één agent en levert wat er is weggeschreven.</summary>
    /// <param name="schema">De cron-expressie, of <c>null</c> voor geen schema.</param>
    /// <returns>De sink en het moment vlak vóór het opstarten.</returns>
    private static async Task<(OpvangendeRegistratieSink Sink, DateTimeOffset Voor)> DraaiAsync(string? schema)
    {
        var sink = new OpvangendeRegistratieSink();
        HostApplicationBuilder builder = Bouw(schema);

        builder.Services.RemoveAll<ITelemetrySink>();
        builder.Services.AddSingleton<ITelemetrySink>(sink);

        DateTimeOffset voor = DateTimeOffset.UtcNow;

        using IHost host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        Assert.NotEmpty(sink.Registrations);

        return (sink, voor);
    }

    /// <summary>Zet een host met één agent op; werpt als het schema niet klopt.</summary>
    private static HostApplicationBuilder Bouw(string? schema)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
        });

        var configuratie = new Dictionary<string, string?>
        {
            ["SORATUS_CUSTOMER__ID"] = "bakker",
            ["SORATUS_AGENT__NAME"] = "voorraad-sync",
            ["SORATUS_TELEMETRY__ENDPOINT"] = "https://localhost:8081/",
        };

        if (schema is not null)
        {
            configuratie["SORATUS_AGENT__SCHEDULE"] = schema;
        }

        builder.Configuration.AddInMemoryCollection(configuratie);
        builder.AddSoratusAgent(opties => opties.FlushInterval = TimeSpan.FromMilliseconds(20));

        return builder;
    }
}
