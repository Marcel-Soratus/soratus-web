using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Soratus.Portal.Data;
using Soratus.Portal.Mail;

namespace Soratus.Portal.Tests.Maandoverzicht;

/// <summary>
/// Dat het echte portaal met de mailkant erin nog start, en dat elk onderdeel ervan op te lossen is.
/// </summary>
/// <remarks>
/// <para><strong>Deze tests bestaan door een fout die zij hadden moeten vangen.</strong> De
/// registratie van <see cref="MonthlyStatementService"/> hing aan
/// <see cref="IMonthlyStatementFigures"/>, waarvoor nog geen implementatie bestond. Dat leek een
/// luie fout die pas bij de eerste aanroep zou opvallen. Het is er geen: in Development staat
/// <c>ValidateOnBuild</c> aan op de DI-container, dus <c>WebApplicationBuilder.Build()</c> werpt en
/// het portaal start niet. Gemeten gevolg: alle 26 tests van het urenendpoint vielen om, met een
/// melding die naar dát endpoint wees en niet naar de mailkant.</para>
///
/// <para><strong>Waarom de andere tests dat niet zagen.</strong> Ze bouwen
/// <see cref="MonthlyStatementService"/> met de hand op — met drie testdubbels, en dat is opzet,
/// want wat er te meten valt is de volgorde in die klasse. Maar daarmee zien ze de registratie
/// nooit. Een testverzameling die elk onderdeel los uitoefent en de samenstelling niet, is precies
/// blind voor deze klasse fout.</para>
///
/// <para><strong>Waarom <see cref="WebApplicationFactory{TEntryPoint}"/>.</strong> Om dezelfde reden
/// als bij het urenendpoint: wat hier gemeten wordt is een eigenschap van de registratie in
/// <c>Program.cs</c>, en een test die die registratie nabouwt meet zijn eigen kopie. De twee
/// achtergronddiensten worden gericht verwijderd — die zoeken Cosmos op bij het opstarten — en niet
/// met een <c>RemoveAll&lt;IHostedService&gt;</c>, want de webserver zelf is er ook een.</para>
/// </remarks>
public class RegistratieTests
{
    [Fact]
    public void HetEchtePortaalStartMetDeMailkantErin()
    {
        // Het bouwen van de host is de meting: ValidateOnBuild loopt elke registratie na. Zou er één
        // afhankelijkheid van de mailkant ontbreken, dan werpt deze regel — en dat is de fout die
        // eerder de testverzameling van een andere sessie plat legde.
        using var host = new Portaalhost();

        Assert.NotNull(host.Services);
    }

    [Fact]
    public void DeDrieOnderdelenDieAltijdMoetenStaanStaanEr()
    {
        using var host = new Portaalhost();
        using var scope = host.Services.CreateScope();

        // Deze drie hangen aan niets buiten de mailkant en horen er dus altijd te zijn, ook zolang de
        // naad naar de kostenkant ontbreekt.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMailOutbox>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IStatementStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IStatementViews>());
    }

    [Fact]
    public void DeNaadEnDeDienstZijnAllesOfNiets()
    {
        // De duurzame regel, en de enige die vóór én ná het aansluiten van de kostenkant waar blijft.
        //
        // Alleen de dienst en niet de naad: dan start het portaal niet — ValidateOnBuild werpt, en dat
        // nam eerder alle 26 tests van het urenendpoint mee met een melding die naar Program.cs wees.
        // Alleen de naad en niet de dienst: dan is er een bedragenbron waar niets langs komt, en de
        // mailkaart valt om op een ontbrekende dienst zodra iemand hem op een pagina zet.
        //
        // Deze test kan dus niet groen staan in de gebroken tussenstand, en hij staat groen in beide
        // eindstanden. Dat is wat hem bruikbaar houdt nadat de naad is geland.
        using var host = new Portaalhost();
        using var scope = host.Services.CreateScope();

        var naad = scope.ServiceProvider.GetService<IMonthlyStatementFigures>();
        var dienst = scope.ServiceProvider.GetService<MonthlyStatementService>();

        Assert.True(
            (naad is null) == (dienst is null),
            $"IMonthlyStatementFigures is {(naad is null ? "niet " : string.Empty)}geregistreerd en "
            + $"MonthlyStatementService is {(dienst is null ? "niet " : string.Empty)}geregistreerd. "
            + "Die twee horen in dezelfde wijziging te komen en te gaan. Alleen de dienst betekent dat "
            + "het portaal niet meer start; alleen de naad betekent een bedragenbron waar niets "
            + "langskomt. Zie het commentaar bij het maandoverzicht-blok in Program.cs.");
    }

    [Fact]
    public void ZolangDeNaadOntbreektIsDeMailkantNietAangesloten()
    {
        // Deze test staat rood zolang de kostenkant niet is aangesloten, en dat is opzet — hij is de
        // tripwire, niet de storing. Hij is er omdat het alternatief erger is: een plaatshouder die
        // "niets gemeten" antwoordt, is niet te onderscheiden van een echte "niets gemeten", en dan
        // staat een test die alleen de volledigheid van de container toetst groen terwijl er stil
        // nooit wordt gemaild. Zie §29.11 van fase-0-afwijkingen.md.
        //
        // Hij gaat groen zodra IMonthlyStatementFigures en MonthlyStatementService samen in Program.cs
        // staan. Faalt hij daarná weer, dan is er werkelijk iets weg.
        using var host = new Portaalhost();
        using var scope = host.Services.CreateScope();

        var dienst = scope.ServiceProvider.GetService<MonthlyStatementService>();

        Assert.True(
            dienst is not null,
            "MonthlyStatementService is niet geregistreerd, dus er kan geen maandoverzicht worden "
            + "verstuurd en de mailkaart is niet te renderen. Dat is de verwachte stand zolang de "
            + "naad IMonthlyStatementFigures geen implementatie heeft; die twee regels komen samen "
            + "terug in Program.cs. Deze test is de enige plek waar die onafheid zichtbaar is.");
    }

    [Fact]
    public void DeProefdraaimodusStaatAanInDeConfiguratieVanHetPortaal()
    {
        using var host = new Portaalhost();

        var opties = host.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<PortalMailOptions>>()
            .Value;

        // De onveilige stand hoort iets te zijn dat iemand aanzet. Deze test kijkt niet naar de
        // standaardwaarde van de klasse — dat doet MailbroncodeTests — maar naar wat er na het binden
        // van appsettings.json werkelijk uitkomt. Zou daar ooit "DryRun": false in belanden, dan
        // verstuurt een ontwikkelmachine echte mail naar een echte klant.
        Assert.True(
            opties.DryRun,
            "PortalMail:DryRun staat in de configuratie van het portaal op false. Dan verstuurt elke "
            + "omgeving die deze appsettings gebruikt echte mail. De proefdraaimodus hoort per "
            + "omgeving te worden uitgezet en niet in het bestand dat overal meereist.");
    }

    [Fact]
    public void ZonderAangeslotenKostenkantWordtErNietGemaild()
    {
        // De vangnetklasse faalt naar de veilige kant. Zolang de kostensessie haar implementatie niet
        // heeft geregistreerd, levert de naad null en is de uitkomst een weigering — geen bedrag van
        // € 0,00 en geen mail. Deze test staat er zodat het weghalen van die eigenschap opvalt, ook
        // nadat de echte implementatie de plaatsvervanger heeft verdrongen.
        var bank = new Maandoverzichtbank(bedragen: null);
        bank.Bedragen = null;

        Assert.Equal(StatementRefusal.NoFigures, Weigering(bank).Refusal);
    }

    private static StatementResult Weigering(Maandoverzichtbank bank) =>
        bank.Dienst
            .SendAsync(bank.SchrijfrechtAsync().GetAwaiter().GetResult(), Maandoverzichtbank.AfgeslotenMaand)
            .GetAwaiter()
            .GetResult();

    /// <summary>
    /// Het echte portaal, met alleen de twee achtergronddiensten eruit.
    /// </summary>
    /// <remarks>
    /// <para>Een eigen host en niet die van het urenendpoint: die staat in de map van een andere
    /// sessie en vervangt drie dingen die voor deze meting niets doen (de urenschrijver en de
    /// tokenvalidatie). Wat hier nodig is, is minder: het portaal moet gebouwd kunnen worden.</para>
    ///
    /// <para>De twee diensten worden gericht verwijderd. <c>TelemetryWarmup</c> en
    /// <c>PortalDirectoryRefresh</c> zoeken Cosmos op bij het opstarten, en de tweede zou de
    /// klantenlijst naar de opslag migreren — dat mag een test niet doen. Een
    /// <c>RemoveAll&lt;IHostedService&gt;</c> zou de webserver meenemen.</para>
    /// </remarks>
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

/// <summary>Hulp om één achtergronddienst te verwijderen zonder de webserver mee te nemen.</summary>
internal static class Dienstverwijdering
{
    /// <summary>
    /// Verwijdert de registratie van <paramref name="implementatie"/> onder
    /// <typeparamref name="TDienst"/>.
    /// </summary>
    /// <typeparam name="TDienst">Het servicetype, hier <c>IHostedService</c>.</typeparam>
    /// <param name="services">De verzameling.</param>
    /// <param name="implementatie">Het implementatietype dat eruit moet.</param>
    /// <remarks>
    /// Op implementatietype en niet op servicetype: er staan meer <c>IHostedService</c>-registraties
    /// en één ervan is de webserver. Verwijder je die, dan start de host wel en antwoordt hij niet —
    /// een storing die eruitziet als een testfout.
    /// </remarks>
    internal static void RemoveAll<TDienst>(
        this IServiceCollection services,
        Type implementatie)
    {
        ArgumentNullException.ThrowIfNull(services);

        for (var index = services.Count - 1; index >= 0; index--)
        {
            if (services[index].ServiceType == typeof(TDienst)
                && services[index].ImplementationType == implementatie)
            {
                services.RemoveAt(index);
            }
        }
    }
}
