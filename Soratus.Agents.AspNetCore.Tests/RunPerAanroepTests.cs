using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Soratus.Agents.AspNetCore.Tests.Hulpmiddelen;
using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry;
using Soratus.Agents.Telemetry.HostedAgents;
using Soratus.Agents.Telemetry.Internal;
using Soratus.Agents.Telemetry.Logging;

namespace Soratus.Agents.AspNetCore.Tests;

/// <summary>
/// Eén aanroep is één run: wat er van een verzoek in de opslag terechtkomt.
/// </summary>
public sealed class RunPerAanroepTests
{
    [Fact]
    public async Task Eén_aanroep_levert_één_run_met_begin_einde_en_uitkomst()
    {
        await using var host = await Proefwebhost.StartAsync(DrieDiensten.Monteer);

        HttpResponseMessage antwoord = await host.Client.PostAsync("/api/declaraties", content: null);
        Assert.Equal(HttpStatusCode.OK, antwoord.StatusCode);

        await host.LeegdraaienAsync(() => host.Sink.AfgerondeRunsVan(DrieDiensten.Import).Count >= 1);

        RunRecord run = Assert.Single(host.Sink.AfgerondeRunsVan(DrieDiensten.Import));

        Assert.Equal(RunResult.Ok, run.Result);
        Assert.Equal(TriggerKind.Http, run.Trigger);
        Assert.Equal(3, run.ItemsProcessed);
        Assert.Equal("mbv", run.CustomerId);
        Assert.NotNull(run.FinishedAt);
        Assert.Null(run.ErrorType);

        // Ook het openingsdocument hoort er te zijn: zolang een chat loopt moet het portaal hem
        // als lopende run kunnen laten zien en niet als niets.
        Assert.Contains(
            host.Sink.Runs,
            document => document.Id == run.Id && document.Result == RunResult.Running);
        Assert.Equal(RunRecord.BuildPartitionKey(DrieDiensten.Import, run.StartedAt), run.PartitionKey);
    }

    [Fact]
    public async Task Een_aanroep_van_de_ene_dienst_komt_niet_op_naam_van_de_andere()
    {
        await using var host = await Proefwebhost.StartAsync(DrieDiensten.Monteer);

        await host.Client.PostAsync("/api/declaraties", content: null);
        await host.Client.GetAsync("/api/financieel");
        await host.Client.GetAsync("/healthz");

        await host.LeegdraaienAsync(() => host.Sink.Runs.Count(run => run.Result != RunResult.Running) >= 2);

        Assert.Single(host.Sink.AfgerondeRunsVan(DrieDiensten.Import));
        Assert.Single(host.Sink.AfgerondeRunsVan(DrieDiensten.Overzicht));

        // En het endpoint zonder metadata levert geen run op. Dat is de andere helft van de belofte:
        // een controlepunt van het platform is geen dienst van de klant.
        Assert.Empty(host.Sink.AfgerondeRunsVan(DrieDiensten.Chat));
    }

    [Fact]
    public async Task Een_mislukte_inlezing_wordt_failed_en_weegt_zwaarder_dan_idle()
    {
        await using var host = await Proefwebhost.StartAsync(static app =>
        {
            DrieDiensten.Monteer(app);
            app.MapPost("/api/stuk", static void () => throw new InvalidDataException("Bedrag ontbreekt op regel 4."))
                .WithSoratusAgent("stukke-import", "Document-intake");
        });

        await Assert.ThrowsAnyAsync<Exception>(() => host.Client.PostAsync("/api/stuk", content: null));
        await host.LeegdraaienAsync(() => host.Sink.AfgerondeRunsVan("stukke-import").Count >= 1);

        RunRecord run = Assert.Single(host.Sink.AfgerondeRunsVan("stukke-import"));

        Assert.Equal(RunResult.Failed, run.Result);
        Assert.Equal(typeof(InvalidDataException).FullName, run.ErrorType);
        Assert.Equal("Bedrag ontbreekt op regel 4.", run.ErrorMessage);

        // Dit is de reden dat het opvalt: de hartslag is vers en de levensfase is 'wacht op werk',
        // en tóch komt de mislukte run erbovenuit. Een dienst kan zich niet met 'ik wacht even'
        // uit een mislukte aanroep praten.
        AgentRegistration registratie = host.Sink.LaatsteRegistraties["stukke-import"];
        Assert.Equal(AgentLifecycle.IdleWaiting, registratie.Lifecycle);
        Assert.Equal(
            AgentStatus.Failed,
            AgentStatusCalculator.Calculate(registratie, run, host.Klok.GetUtcNow()));
    }

    [Fact]
    public async Task Een_antwoord_met_serverfout_wordt_failed_ook_zonder_uitzondering()
    {
        await using var host = await Proefwebhost.StartAsync(static app =>
            app.MapGet("/api/stil", static () => Results.StatusCode(503))
                .WithSoratusAgent("stille-fout", "Koppeling"));

        await host.Client.GetAsync("/api/stil");
        await host.LeegdraaienAsync(() => host.Sink.AfgerondeRunsVan("stille-fout").Count >= 1);

        RunRecord run = Assert.Single(host.Sink.AfgerondeRunsVan("stille-fout"));

        Assert.Equal(RunResult.Failed, run.Result);
        Assert.Equal("Http503", run.ErrorType);
        Assert.Contains("503", run.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Een_geweigerd_verzoek_van_de_aanroeper_is_geen_storing_van_de_dienst()
    {
        await using var host = await Proefwebhost.StartAsync(static app =>
            app.MapPost("/api/streng", static () => Results.BadRequest("Kolom 'bedrag' ontbreekt."))
                .WithSoratusAgent("strenge-import", "Document-intake"));

        await host.Client.PostAsync("/api/streng", content: null);
        await host.LeegdraaienAsync(() => host.Sink.AfgerondeRunsVan("strenge-import").Count >= 1);

        RunRecord run = Assert.Single(host.Sink.AfgerondeRunsVan("strenge-import"));

        // 4xx betekent dat de aanroeper iets verkeerd meestuurde en de dienst dat juist goed zag.
        // Zou dit failed worden, dan kleurt het portaal rood zodra een gebruiker een verkeerd
        // bestand aanbiedt, en dan is rood binnen een week niets meer waard.
        Assert.NotEqual(RunResult.Failed, run.Result);
        Assert.Null(run.ErrorType);
    }

    [Fact]
    public async Task Tijdens_een_lopende_aanroep_meldt_de_dienst_dat_hij_draait()
    {
        AgentLifecycle? tijdens = null;
        int lopendTijdens = 0;

        await using var host = await Proefwebhost.StartAsync(app =>
            app.MapPost("/api/traag", (HttpContext context) =>
                {
                    var agents = context.RequestServices.GetRequiredService<ISoratusHostedAgents>();
                    var dienst = context.RequestServices.GetRequiredService<HostedAgentsRegistrationService>();

                    lopendTijdens = agents.All.Single().RunsInFlight;

                    // De hartslag die op dit moment de deur uit gaat, gebouwd door de dienst die
                    // hem uitstuurt — en niet nagerekend in de test. Ook echt weggeschreven, zodat
                    // hieronder blijkt dat het document er ook aankomt.
                    tijdens = dienst.BuildRegistration((HostedAgent)agents.All.Single()).Lifecycle;
                    dienst.Publish();

                    return Results.Ok();
                })
                .WithSoratusAgent("trage-dienst", "Koppeling"));

        await host.Client.PostAsync("/api/traag", content: null);
        await host.LeegdraaienAsync(() => host.Sink.AfgerondeRunsVan("trage-dienst").Count >= 1);

        Assert.Equal(1, lopendTijdens);
        Assert.Equal(AgentLifecycle.Running, tijdens);

        // En het document met 'running' komt ook echt in de opslag aan. Dat kan alleen het document
        // van tijdens de aanroep zijn: buiten een aanroep is de levensfase 'wacht op werk'.
        await host.LeegdraaienAsync(
            () => host.Sink.Registrations.Any(r => r.Lifecycle == AgentLifecycle.Running));
        Assert.Contains(host.Sink.Registrations, r => r.Lifecycle == AgentLifecycle.Running);

        // En daarna weer wachten, zonder dat iemand dat hoeft te melden.
        Assert.Equal(0, host.Diensten.GetRequiredService<ISoratusHostedAgents>().All.Single().RunsInFlight);

        host.Diensten.GetRequiredService<HostedAgentsRegistrationService>().Publish();
        await host.LeegdraaienAsync(() =>
            host.Sink.LaatsteRegistraties["trage-dienst"].Lifecycle == AgentLifecycle.IdleWaiting);
        Assert.Equal(
            AgentLifecycle.IdleWaiting,
            host.Sink.LaatsteRegistraties["trage-dienst"].Lifecycle);
    }

    [Fact]
    public async Task De_duur_van_een_run_komt_van_de_klok_en_niet_van_een_stopwatch()
    {
        await using var host = await Proefwebhost.StartAsync(app =>
            app.MapPost("/api/lang", (HttpContext context) =>
                {
                    // Vijf seconden werk, zonder vijf seconden te wachten. Dat kan alleen als de
                    // hele keten de klok uit TimeProvider haalt.
                    ((StuurbareKlok)context.RequestServices.GetRequiredService<TimeProvider>())
                        .Verzet(TimeSpan.FromSeconds(5));
                    context.SoratusAgentRun()?.Processed();
                    return Results.Ok();
                })
                .WithSoratusAgent("lange-dienst", "Koppeling"));

        await host.Client.PostAsync("/api/lang", content: null);
        await host.LeegdraaienAsync(() => host.Sink.AfgerondeRunsVan("lange-dienst").Count >= 1);

        RunRecord run = Assert.Single(host.Sink.AfgerondeRunsVan("lange-dienst"));

        Assert.Equal(5000, run.DurationMs);
        Assert.Equal(TimeSpan.FromSeconds(5), run.FinishedAt - run.StartedAt);
    }

    [Fact]
    public async Task Een_logregel_uit_een_aanroep_draagt_de_naam_van_die_dienst_en_de_runId()
    {
        await using var host = await Proefwebhost.StartAsync(static app =>
        {
            app.MapPost("/api/een", static (HttpContext context) =>
                {
                    context.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Mbv.Declaraties")
                        .AgentEvent("regel.verwerkt", "Er zijn drie declaratieregels ingelezen.");
                    return Results.Ok();
                })
                .WithSoratusAgent("dienst-een", "Document-intake");

            app.MapPost("/api/twee", static (HttpContext context) =>
                {
                    context.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Mbv.Chat")
                        .AgentEvent("antwoord.gegeven", "De vraag is beantwoord.");
                    return Results.Ok();
                })
                .WithSoratusAgent("dienst-twee", "Chat");
        });

        await host.Client.PostAsync("/api/een", content: null);
        await host.Client.PostAsync("/api/twee", content: null);
        await host.LeegdraaienAsync(() => host.Sink.Logs.Count(regel => regel.RunId is not null) >= 2);

        LogRecord een = Assert.Single(host.Sink.Logs, regel => regel.Event == "regel.verwerkt");
        LogRecord twee = Assert.Single(host.Sink.Logs, regel => regel.Event == "antwoord.gegeven");

        Assert.Equal("dienst-een", een.AgentName);
        Assert.Equal("dienst-twee", twee.AgentName);
        Assert.NotNull(een.RunId);
        Assert.Equal(
            host.Sink.AfgerondeRunsVan("dienst-een").Single().Id,
            een.RunId);
        Assert.Equal(LogRecord.BuildPartitionKey("dienst-een", een.Timestamp), een.PartitionKey);
    }

    [Fact]
    public async Task Een_logregel_buiten_een_aanroep_wordt_niet_aan_een_wíllekeurige_dienst_toegeschreven()
    {
        await using var host = await Proefwebhost.StartAsync(static app =>
        {
            DrieDiensten.Monteer(app);
            app.MapGet("/api/naamloos", static (HttpContext context) =>
            {
                context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Mbv.Onderhoud")
                    .AgentEvent("onderhoud.gedraaid", "Het onderhoud is gedraaid.");
                return Results.Ok();
            });
        });

        await host.Client.GetAsync("/api/naamloos");
        await host.LeegdraaienAsync(() => host.Sink.LaatsteRegistraties.Count >= 3);
        await Task.Delay(100);

        // Deze regel hoort bij geen enkele agent, dus hij gaat niet naar het portaal. Hem aan een
        // van de drie diensten toeschrijven zou een verkeerd antwoord zijn op de vraag "wat deed
        // deze dienst"; hij blijft wél in de gewone log van de host staan.
        Assert.DoesNotContain(host.Sink.Logs, regel => regel.Event == "onderhoud.gedraaid");
    }
}
