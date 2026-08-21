using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Soratus.Portal.Alerts;
using Soratus.Portal.Data;
using Soratus.Portal.Mail;
using Soratus.Portal.Tests.Maandoverzicht;

namespace Soratus.Portal.Tests.Storingsmelder;

/// <summary>
/// Dat het echte portaal met de melder erin nog start, en dat zijn instellingen veilig uitkomen.
/// </summary>
/// <remarks>
/// <para><strong>Deze tests bestaan door een fout in een andere lane.</strong> Een
/// <c>AddHostedService</c> die aan een niet-geregistreerde afhankelijkheid hangt maakt
/// <c>WebApplicationBuilder.Build()</c> onmogelijk — <c>ValidateOnBuild</c> staat in Development aan —
/// en dat nam eerder alle 26 tests van het urenendpoint mee, met een melding die naar
/// <c>Program.cs</c> wees en niet naar de kant die hem had gemaakt. Zie §29.11.</para>
///
/// <para>De twee achtergronddiensten die Cosmos opzoeken worden gericht verwijderd, net als bij
/// <c>RegistratieTests</c>. De melder zelf staat niet in Development geregistreerd, dus die hoeft er
/// niet uit.</para>
/// </remarks>
public class MelderregistratieTests
{
    [Fact]
    public void HetEchtePortaalStartMetDeMelderErin()
    {
        using var host = new Portaalhost();

        Assert.NotNull(host.Services);
    }

    [Fact]
    public void DeOnderdelenVanDeMelderZijnOpTeLossen()
    {
        // De melder zelf staat in Development niet geregistreerd — dat is opzet, zie Program.cs — dus
        // wat hier wordt gemeten zijn zijn drie afhankelijkheden. Zonder deze test valt een ontbrekende
        // registratie pas op in productie, en dan legt hij het opstarten plat.
        using var host = new Portaalhost();
        using var scope = host.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAgentFaultSource>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAgentAlertStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMailOutbox>());
    }

    [Fact]
    public void DeMelderIsMetDeHandTeBouwenUitDeContainer()
    {
        // De tegenhanger van de vorige test, en de reden dat hij er apart staat: dat de drie
        // afhankelijkheden op te lossen zijn, is niet hetzelfde als dat de dienst zelf te bouwen is.
        // Komt er ooit een vierde afhankelijkheid bij die niet geregistreerd is, dan blijft de test
        // hierboven groen en wordt deze rood — en dat is precies het geval dat het opstarten in productie
        // onmogelijk maakt terwijl het lokaal werkt.
        using var host = new Portaalhost();

        Assert.NotNull(ActivatorUtilities.CreateInstance<AgentFaultAlerter>(host.Services));
    }

    [Fact]
    public void DeProefdraaimodusStaatAanInDeConfiguratieVanHetPortaal()
    {
        // Dezelfde meting als bij het maandoverzicht, en met opzet nog een keer: de melder leest dezelfde
        // vlag. Zou er ooit "DryRun": false in appsettings.json belanden, dan verstuurt een
        // ontwikkelmachine echte storingsmeldingen.
        using var host = new Portaalhost();

        Assert.True(
            host.Services.GetRequiredService<IOptions<PortalMailOptions>>().Value.DryRun,
            "PortalMail:DryRun staat in de configuratie van het portaal op false. Dan verstuurt elke "
            + "omgeving die deze appsettings gebruikt echte mail — ook de storingsmelder.");
    }

    [Fact]
    public void ZonderConfiguratieHeeftDeMelderGeenOntvangerEnMeldtHijNiets()
    {
        // Dit is de verwachte stand vandaag: PortalAlerts:Recipients staat nergens, dus er wordt niets
        // gemeld en de melder zegt dat bij elke ronde als error. Deze test legt vast dat het niet stil
        // gebeurt — en hij wordt rood zodra er adressen in appsettings.json belanden, want dan hoort dit
        // besluit opnieuw bekeken te worden.
        using var host = new Portaalhost();

        var opties = host.Services.GetRequiredService<IOptions<AgentAlertOptions>>().Value;

        Assert.Empty(opties.UsableRecipients());
        Assert.True(opties.Enabled);
        Assert.Equal(60, opties.IntervalSeconds);
        Assert.Equal(6, opties.RepeatAfterHours);
    }

    /// <summary>Het echte portaal, met alleen de twee achtergronddiensten eruit.</summary>
    private sealed class Portaalhost : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>(typeof(TelemetryWarmup));
                services.RemoveAll<IHostedService>(typeof(PortalDirectoryRefresh));
            });
        }
    }
}
