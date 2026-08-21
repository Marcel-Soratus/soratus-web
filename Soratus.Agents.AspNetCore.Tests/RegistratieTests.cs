using System.Text.Json;
using Soratus.Agents.AspNetCore.Tests.Hulpmiddelen;
using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry.Internal;

namespace Soratus.Agents.AspNetCore.Tests;

/// <summary>
/// Wat er over de drie diensten in de opslag terechtkomt zodra de webapplicatie start.
/// </summary>
public sealed class RegistratieTests
{
    [Fact]
    public async Task De_drie_endpoints_leveren_drie_registraties_en_het_endpoint_zonder_agent_geen()
    {
        await using var host = await Proefwebhost.StartAsync(DrieDiensten.Monteer);
        await host.LeegdraaienAsync(() => host.Sink.LaatsteRegistraties.Count >= 3);

        Assert.Equal(
            [DrieDiensten.Chat, DrieDiensten.Import, DrieDiensten.Overzicht],
            host.Sink.LaatsteRegistraties.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task De_webhost_zelf_krijgt_geen_eigen_rij_in_het_overzicht()
    {
        await using var host = await Proefwebhost.StartAsync(DrieDiensten.Monteer);
        await host.LeegdraaienAsync(() => host.Sink.LaatsteRegistraties.Count >= 3);

        // 'mbv-web' is de naam van het proces uit de configuratie. Hij levert klant, versie en
        // omgeving aan de drie diensten en wordt zelf niet gepubliceerd: zijn hartslag zou per
        // constructie dezelfde zijn als die van de drie, dus die rij zou een regel toevoegen
        // zonder een feit toe te voegen.
        Assert.DoesNotContain("mbv-web", host.Sink.LaatsteRegistraties.Keys);
    }

    [Fact]
    public async Task Een_dienst_op_aanvraag_meldt_geen_schema_en_geen_volgende_run()
    {
        await using var host = await Proefwebhost.StartAsync(DrieDiensten.Monteer);
        await host.LeegdraaienAsync(() => host.Sink.LaatsteRegistraties.Count >= 3);

        foreach (AgentRegistration registratie in host.Sink.LaatsteRegistraties.Values)
        {
            Assert.Equal(TriggerKind.Http, registratie.TriggerKind);
            Assert.Null(registratie.Schedule);
            Assert.Null(registratie.NextRunAt);
        }
    }

    [Fact]
    public async Task Zonder_lopende_aanroep_is_de_levensfase_wachten_en_de_status_idle()
    {
        await using var host = await Proefwebhost.StartAsync(DrieDiensten.Monteer);
        await host.LeegdraaienAsync(() => host.Sink.LaatsteRegistraties.Count >= 3);

        AgentRegistration registratie = host.Sink.LaatsteRegistraties[DrieDiensten.Import];

        Assert.Equal(AgentLifecycle.IdleWaiting, registratie.Lifecycle);

        // En dit is de stand die het portaal eruit haalt: geen alarm, rang 1, dus de klant komt
        // hier niet van bovenaan de lijst te staan.
        Assert.Equal(
            AgentStatus.Idle,
            AgentStatusCalculator.Calculate(registratie, lastCompletedRun: null, host.Klok.GetUtcNow()));
    }

    [Fact]
    public async Task Als_de_hartslag_stokt_wordt_dezelfde_dienst_alsnog_degraded()
    {
        await using var host = await Proefwebhost.StartAsync(DrieDiensten.Monteer);
        await host.LeegdraaienAsync(() => host.Sink.LaatsteRegistraties.Count >= 3);

        AgentRegistration registratie = host.Sink.LaatsteRegistraties[DrieDiensten.Import];
        DateTimeOffset later = registratie.LastHeartbeatAt + AgentStatusThresholds.Degraded + TimeSpan.FromSeconds(1);

        // Dit is de keerzijde van het ontwerp, en hij hoort te bestaan: 'wacht op werk' houdt een
        // dienst niet groen als het proces zwijgt. Precies dit gebeurt er als iemand Always On
        // uitzet — zie punt 42 van de afwijkingen.
        Assert.Equal(
            AgentStatus.Degraded,
            AgentStatusCalculator.Calculate(registratie, lastCompletedRun: null, later));
    }

    [Fact]
    public async Task Alle_drie_de_diensten_delen_de_processtart_van_de_host()
    {
        await using var host = await Proefwebhost.StartAsync(DrieDiensten.Monteer);
        await host.LeegdraaienAsync(() => host.Sink.LaatsteRegistraties.Count >= 3);

        // Eén uitrol, één proces, één 'draait sinds'. Dat dit veld op alle drie gelijk is, is het
        // aflezpaar voor een uitgeladen host: schuift het na een stilte op, dan is het proces
        // opnieuw gestart en niet de agent gevallen.
        DateTimeOffset[] starts =
            [.. host.Sink.LaatsteRegistraties.Values.Select(static registratie => registratie.StartedAt)];

        Assert.Single(starts.Distinct());
    }

    [Fact]
    public async Task De_gemelde_feiten_komen_van_de_host_en_de_naam_en_het_type_van_het_endpoint()
    {
        await using var host = await Proefwebhost.StartAsync(DrieDiensten.Monteer);
        await host.LeegdraaienAsync(() => host.Sink.LaatsteRegistraties.Count >= 3);

        AgentRegistration chat = host.Sink.LaatsteRegistraties[DrieDiensten.Chat];

        Assert.Equal("mbv", chat.CustomerId);
        Assert.Equal(AgentEnvironment.Production, chat.Environment);
        Assert.Equal("Chat", chat.DisplayType);
        Assert.Equal("POST /api/chat", chat.TriggerDetail);
        Assert.Equal(DrieDiensten.Chat, chat.Id);
        Assert.Equal(DrieDiensten.Chat, chat.PartitionKey);
    }

    [Fact]
    public async Task De_tijdstempels_gaan_in_UTC_de_deur_uit()
    {
        await using var host = await Proefwebhost.StartAsync(DrieDiensten.Monteer);
        await host.LeegdraaienAsync(() => host.Sink.LaatsteRegistraties.Count >= 3);

        AgentRegistration registratie = host.Sink.LaatsteRegistraties[DrieDiensten.Chat];

        // De klok levert UTC; een offset die hier niet nul is zou als tekst in Cosmos landen en
        // dan sorteert de lijst stil verkeerd. Zie punt 7 en TimestampNormalization.
        Assert.Equal(TimeSpan.Zero, registratie.LastHeartbeatAt.Offset);
        Assert.Equal(TimeSpan.Zero, registratie.StartedAt.Offset);
        Assert.Equal(
            TimestampNormalization.Width,
            TimestampNormalization.ToCanonical(registratie.LastHeartbeatAt).Length);
    }

    [Fact]
    public async Task Elke_dienst_krijgt_bij_het_starten_één_regel_die_zegt_waar_de_hartslag_vandaan_komt()
    {
        await using var host = await Proefwebhost.StartAsync(DrieDiensten.Monteer);
        await host.LeegdraaienAsync(
            () => host.Sink.Logs.Count(regel => regel.Event == HostedAgentsRegistrationService.StartEvent) >= 3);

        LogRecord[] start =
        [
            .. host.Sink.Logs.Where(regel => regel.Event == HostedAgentsRegistrationService.StartEvent),
        ];

        Assert.Equal(3, start.Length);
        Assert.Equal(3, start.Select(regel => regel.AgentName).Distinct(StringComparer.Ordinal).Count());
        Assert.All(start, regel => Assert.Equal(Contracts.LogLevel.Info, regel.Level));

        // De uitleg staat operator-only in extra mee, want de lezer die dit patroon aantreft zoekt
        // op dat moment de betekenis en niet de documentatie.
        JsonElement extra = Assert.NotNull(start[0].Extra);
        Assert.Equal(
            HostedAgentsRegistrationService.StartExplanation,
            extra.GetProperty("uitleg").GetString());

        // En niet in msg: dat leest de klant, en die heeft aan een uitleg over een Azure-instelling
        // niets.
        Assert.DoesNotContain("Always On", start[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task De_hartslag_loopt_op_het_interval_uit_het_contract()
    {
        await using var host = await Proefwebhost.StartAsync(DrieDiensten.Monteer);
        await host.LeegdraaienAsync(() => host.Klok.GevraagdeWachttijden.Count > 0);

        // Eén grens op één plek: het interval komt uit AgentStatusThresholds, zodat het scherm en
        // de bibliotheek per definitie dezelfde afspraak hanteren. Loopt de hartslag trager dan de
        // drempel, dan staat élke dienst permanent op degraded terwijl de documenten kloppen — een
        // fout die geen enkele test over documenten kan zien.
        Assert.Contains(AgentStatusThresholds.HeartbeatInterval, host.Klok.GevraagdeWachttijden);
        Assert.True(AgentStatusThresholds.HeartbeatInterval * 2 <= AgentStatusThresholds.Degraded);
    }

    [Fact]
    public async Task Bij_het_afsluiten_meldt_elke_dienst_dat_hij_netjes_is_gestopt()
    {
        var host = await Proefwebhost.StartAsync(DrieDiensten.Monteer);
        await host.LeegdraaienAsync(() => host.Sink.LaatsteRegistraties.Count >= 3);
        OpvangendeSink sink = host.Sink;

        await host.DisposeAsync();

        // Een geplande herstart laat elke dienst op idle achter en belt niemand wakker. Alleen een
        // proces dat hard verdwijnt laat zijn laatste hartslag staan en wordt degraded.
        Assert.Equal(3, sink.LaatsteRegistraties.Count);
        Assert.All(
            sink.LaatsteRegistraties.Values,
            registratie => Assert.Equal(AgentLifecycle.StoppedCleanly, registratie.Lifecycle));
    }
}
