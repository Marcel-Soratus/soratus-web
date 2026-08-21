using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Soratus.Agents.AspNetCore.Internal;
using Soratus.Agents.AspNetCore.Tests.Hulpmiddelen;
using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry;
using Soratus.Agents.Telemetry.HostedAgents;

namespace Soratus.Agents.AspNetCore.Tests;

/// <summary>
/// De fouten die een aanroeper kan maken, en of ze zichtbaar worden.
/// </summary>
public sealed class InrichtingTests
{
    [Fact]
    public async Task De_aanroeplaag_vergeten_levert_een_rode_regel_op_en_geen_stille_leegte()
    {
        await using var host = await Proefwebhost.StartAsync(DrieDiensten.Monteer, aanroeplaag: false);

        await host.Client.PostAsync("/api/declaraties", content: null);
        await host.LeegdraaienAsync(
            () => host.Sink.Logs.Count(regel => regel.Event == EndpointWiringCheck.Event) >= 3);

        // Zonder deze melding zouden er drie diensten met een verse hartslag en nul runs staan, en
        // dat is in het portaal niet te onderscheiden van drie diensten die niemand aanroept.
        LogRecord[] meldingen =
            [.. host.Sink.Logs.Where(regel => regel.Event == EndpointWiringCheck.Event)];

        Assert.Equal(3, meldingen.Length);
        Assert.All(meldingen, regel => Assert.Equal(Contracts.LogLevel.Error, regel.Level));
        Assert.All(meldingen, regel => Assert.Equal(EndpointWiringCheck.Message, regel.Message));
        Assert.Empty(host.Sink.Runs);
    }

    [Fact]
    public async Task Met_de_aanroeplaag_erin_komt_die_melding_er_niet()
    {
        await using var host = await Proefwebhost.StartAsync(DrieDiensten.Monteer);

        await host.Client.PostAsync("/api/declaraties", content: null);
        await host.LeegdraaienAsync(() => host.Sink.AfgerondeRunsVan(DrieDiensten.Import).Count >= 1);
        await Task.Delay(100);

        Assert.DoesNotContain(host.Sink.Logs, regel => regel.Event == EndpointWiringCheck.Event);
    }

    [Fact]
    public async Task Zonder_endpoints_met_een_agent_klaagt_niemand()
    {
        await using var host = await Proefwebhost.StartAsync(
            static app => app.MapGet("/healthz", static () => Results.Ok()),
            aanroeplaag: false);

        await host.Client.GetAsync("/healthz");
        await Task.Delay(200);

        Assert.Empty(host.Sink.Logs);
        Assert.Empty(host.Sink.Registrations);
    }

    [Fact]
    public void Een_timer_als_trigger_is_een_fout_en_geen_stille_correctie()
    {
        // Een geherbergde agent heeft geen schema. Zou 'timer' hier mogen, dan staat er in het
        // portaal een agent op schema zonder schema en met een leeg 'volgende run' — een
        // tegenspraak die de lezer moet oplossen in plaats van de bouwer.
        InvalidOperationException fout = Assert.Throws<InvalidOperationException>(
            () => new SoratusAgentMetadata("verzonnen-timer", trigger: TriggerKind.Timer));

        Assert.Contains("geen schema", fout.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Een_agent_zonder_naam_bestaat_niet()
    {
        Assert.Throws<ArgumentException>(() => new SoratusAgentMetadata("  "));

        Assert.Throws<InvalidOperationException>(
            () => new HostedAgentDeclaration { AgentName = " ", Trigger = TriggerKind.Http }.Validate());
    }

    [Fact]
    public void De_twee_vormen_van_de_bibliotheek_sluiten_elkaar_uit()
    {
        InvalidOperationException heen = Assert.Throws<InvalidOperationException>(() =>
        {
            HostApplicationBuilder builder = Bouwer();
            builder.AddSoratusAgent();
            builder.AddSoratusHostedAgents();
        });

        InvalidOperationException terug = Assert.Throws<InvalidOperationException>(() =>
        {
            HostApplicationBuilder builder = Bouwer();
            builder.AddSoratusHostedAgents();
            builder.AddSoratusAgent();
        });

        Assert.Contains("sluiten elkaar uit", heen.Message, StringComparison.Ordinal);
        Assert.Contains("sluiten elkaar uit", terug.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Een_schema_op_een_host_met_diensten_op_aanvraag_is_een_fout()
    {
        HostApplicationBuilder builder = Bouwer(("SORATUS_AGENT__SCHEDULE", "0 6 * * *"));

        InvalidOperationException fout =
            Assert.Throws<InvalidOperationException>(() => builder.AddSoratusHostedAgents());

        Assert.Contains("SORATUS_AGENT__SCHEDULE", fout.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Twee_keer_aansluiten_verandert_niets()
    {
        HostApplicationBuilder builder = Bouwer();
        builder.AddSoratusHostedAgents();
        int na = builder.Services.Count;
        builder.AddSoratusHostedAgents();

        Assert.Equal(na, builder.Services.Count);
    }

    [Fact]
    public async Task Twee_endpoints_mogen_dezelfde_dienst_zijn()
    {
        await using var host = await Proefwebhost.StartAsync(static app =>
        {
            app.MapGet("/api/uren", static () => Results.Ok())
                .WithSoratusAgent("uren-koppeling", "Koppeling");
            app.MapPost("/api/uren", static () => Results.Ok())
                .WithSoratusAgent("uren-koppeling", "Koppeling");
        });

        await host.LeegdraaienAsync(() => host.Sink.LaatsteRegistraties.Count >= 1);

        // Eén dienst met een lees- en een schrijfroute is één agent, niet twee.
        Assert.Single(host.Sink.LaatsteRegistraties);

        await host.Client.GetAsync("/api/uren");
        await host.Client.PostAsync("/api/uren", content: null);
        await host.LeegdraaienAsync(() => host.Sink.AfgerondeRunsVan("uren-koppeling").Count >= 2);

        Assert.Equal(2, host.Sink.AfgerondeRunsVan("uren-koppeling").Count);
    }

    [Fact]
    public async Task Bij_twee_verschillende_aankondigingen_van_dezelfde_naam_blijft_de_eerste_staan()
    {
        await using var host = await Proefwebhost.StartAsync(static app =>
        {
            app.MapGet("/api/een", static () => Results.Ok())
                .WithSoratusAgent("dubbel", "Koppeling");
            app.MapGet("/api/twee", static () => Results.Ok())
                .WithSoratusAgent("dubbel", "Iets anders");
        });

        await host.LeegdraaienAsync(() => host.Sink.LaatsteRegistraties.Count >= 1);

        // Eén agent, en het type van de eerste aankondiging. Twee endpoints die dezelfde naam met
        // een ander type aankondigen is een inrichtingsfout, maar niet één die een lopende host mag
        // omleggen: de melding gaat naar de gewone logger van de host.
        AgentRegistration registratie = Assert.Single(host.Sink.LaatsteRegistraties.Values);
        Assert.Equal("Koppeling", registratie.DisplayType);
    }

    [Fact]
    public async Task Een_naam_zonder_type_krijgt_een_leesbaar_type()
    {
        await using var host = await Proefwebhost.StartAsync(static app =>
            app.MapGet("/api/kaal", static () => Results.Ok()).WithSoratusAgent("declaraties-import"));

        await host.LeegdraaienAsync(() => host.Sink.LaatsteRegistraties.Count >= 1);

        Assert.Equal("Declaraties import", Assert.Single(host.Sink.LaatsteRegistraties.Values).DisplayType);
    }

    private static HostApplicationBuilder Bouwer(params (string Sleutel, string Waarde)[] extra)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
        });

        var configuratie = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["SORATUS_CUSTOMER__ID"] = "mbv",
            ["SORATUS_AGENT__NAME"] = "mbv-web",
            ["SORATUS_TELEMETRY__ENDPOINT"] = "https://localhost:8081/",
        };

        foreach ((string sleutel, string waarde) in extra)
        {
            configuratie[sleutel] = waarde;
        }

        builder.Configuration.AddInMemoryCollection(configuratie);
        return builder;
    }
}
