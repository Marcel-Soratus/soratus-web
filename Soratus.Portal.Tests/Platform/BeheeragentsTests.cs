using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Soratus.Agents.Contracts;
using Soratus.Portal.Alerts;
using Soratus.Portal.Data;
using Soratus.Portal.Platform;
using Soratus.Portal.Tests.Hulpmiddelen;
using Soratus.Portal.Tests.Storingsmelder;

namespace Soratus.Portal.Tests.Platform;

/// <summary>
/// De twee beheeragents van §4 melden zichzelf: een plan, een volgende run en een run per tik.
/// </summary>
/// <remarks>
/// <para><strong>Wat hier wordt gemeten is de naad tussen de lus en de bibliotheek, en de invariant
/// daarop is dat het plan waarop wordt gewácht hetzelfde plan is dat wordt aangekondigd.</strong>
/// Met twee bronnen — een timer hier en een cron-expressie daar — is dat een afspraak; met één object
/// is het een eigenschap. De tests kijken daarom naar beide kanten van dezelfde tik: de aankondiging
/// die de agent krijgt, en de wachttijd die de klok gevraagd wordt.</para>
///
/// <para>De klok gaat met opzet nooit af (<c>Snelleklok(..., meteenAf: false)</c>) waar het over de
/// aankondiging gaat. Een klok die meteen afgaat laat de lus rondtollen tussen starten en stoppen, en
/// dan meet een test die naar de eerste wachttijd kijkt zijn eigen ruis.</para>
/// </remarks>
public sealed class BeheeragentsTests
{
    /// <summary>Een donderdagmiddag: het eerstvolgende 04:00 UTC is de volgende dag.</summary>
    private static readonly DateTimeOffset Middag = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Dezelfde scope als in <c>KostencollectorTests</c>.</summary>
    private const string ScopeMbv =
        "/subscriptions/501a66d2-de54-4d4f-9f7c-1fbb55bec17f/resourceGroups/MBV";

    // ── De kostencollector ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeCollectorKondigtZichAanMetHetPlanWaaropHijWacht()
    {
        var (collector, host, klok, _) = Collector();

        var agent = await Gemeld(collector, host, PlatformAgentNames.Costs);

        // §4: kosten-collector, Cost Management, dagelijks 04:00. Precies de naam en het type waarmee
        // hij ook in de seed-data staat, want dat is de vorm die het scherm al kent.
        Assert.Equal(PlatformAgentNames.Costs, agent.Declaration.AgentName);
        Assert.Equal("Cost Management", agent.Declaration.DisplayType);
        Assert.Equal(TriggerKind.Timer, agent.Declaration.Trigger);
        Assert.Equal("0 4 * * *", agent.Declaration.Schedule!.Expression);

        // Dit is de invariant: het gemelde moment en de gevraagde wachttijd wijzen naar hetzelfde
        // tijdstip, en dat tijdstip volgt uit het aangekondigde plan.
        var verwacht = new DateTimeOffset(2026, 8, 22, 4, 0, 0, TimeSpan.Zero);

        Assert.Equal(verwacht, agent.GemeldeVolgendeRuns[0]);
        Assert.Equal(verwacht - Middag, klok.Wachttijden[0]);
    }

    [Fact]
    public async Task EenAnderDraaimomentSchuiftHetPlanEnDeWachttijdSamenOp()
    {
        // De mutatie waar deze test op let: een plan uit één bron en een wachttijd uit een andere. Met
        // een afwijkend uur valt dat door de mand; met het standaarduur zou beide 04:00 zijn geweest.
        var (collector, host, klok, _) = Collector(uur: 7);

        var agent = await Gemeld(collector, host, PlatformAgentNames.Costs);

        // Zeven uur UTC is op deze middag pas morgen: negentien uur wachten en niet zeven.
        var verwacht = new DateTimeOffset(2026, 8, 22, 7, 0, 0, TimeSpan.Zero);

        Assert.Equal("0 7 * * *", agent.Declaration.Schedule!.Expression);
        Assert.Equal(verwacht, agent.GemeldeVolgendeRuns[0]);
        Assert.Equal(verwacht - Middag, klok.Wachttijden[0]);

        // De toelichting op de trigger schuift mee. Een cron-expressie is voor een operator geen tekst
        // die je op een scherm wil lezen; deze regel is dat wel, en hij hoort dus niet uit de pas te
        // lopen met het plan.
        Assert.Equal("dagelijks 07:00 UTC", agent.Declaration.TriggerDetail);
    }

    [Fact]
    public async Task EenTikVanDeKlokIsEenRunMetDeGemetenMaandenErin()
    {
        var (collector, host, _, opslag) = Collector(meteenAf: true, client: Metingen());
        opslag.Klant("mbv", ScopeMbv);

        var agent = await Gedraaid(collector, host, PlatformAgentNames.Costs);

        var run = agent.Runs[0];

        Assert.Equal(TriggerKind.Timer, run.Trigger);
        Assert.True(run.Afgerond);
        Assert.Null(run.Mislukking);

        // Twee maanden voor één klant: de vorige en de lopende. Dat getal komt uit de collector zelf
        // en niet uit deze test — het is het aantal maanden dat is weggeschreven.
        Assert.Equal(2, run.ItemsProcessed);
    }

    [Fact]
    public async Task ZonderIngerichteTelemetrieMeetDeCollectorGewoonDoor()
    {
        // De afhankelijkheidsrichting, en dit is de belangrijkste test van dit bestand. Telemetrie mag
        // het werk nooit omleggen, en werk dat zonder telemetrie helemaal niet meer gebeurt is de
        // scherpste vorm daarvan. Hier staat er geen ISoratusHostedAgents in de container.
        var opslag = new Vastekostenopslag();
        var client = Metingen();
        opslag.Klant("mbv", ScopeMbv);

        var collector = new AzureCostCollector(
            opslag,
            client,
            Options.Create(new AzureCostOptions()),
            new Snelleklok(Middag),
            NullLogger<AzureCostCollector>.Instance);

        var geschreven = await collector.RunAsync(CancellationToken.None);

        Assert.Equal(2, geschreven);
        Assert.NotEmpty(client.Vragen);
    }

    // ── De storingsmelder ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeMelderKondigtZichAanMetHetPlanWaaropHijWacht()
    {
        var bank = new Storingsmelderbank();
        var host = new Vasteagenthost();
        var klok = new Snelleklok(Middag, meteenAf: false);
        var melder = Melder(bank, host, klok);

        var agent = await Gemeld(melder, host, PlatformAgentNames.Alerts);

        // §4: storingsmelder, Monitoring, elke minuut.
        Assert.Equal(PlatformAgentNames.Alerts, agent.Declaration.AgentName);
        Assert.Equal("Monitoring", agent.Declaration.DisplayType);
        Assert.Equal(TriggerKind.Timer, agent.Declaration.Trigger);
        Assert.Equal("* * * * *", agent.Declaration.Schedule!.Expression);

        var verwacht = Middag.AddMinutes(1);

        Assert.Equal(verwacht, agent.GemeldeVolgendeRuns[0]);
        Assert.Equal(TimeSpan.FromMinutes(1), klok.Wachttijden[0]);
    }

    [Fact]
    public async Task EenMislukteRondeVanDeMelderIsEenMislukteRun()
    {
        // Dít is wat de opdracht "de storingsmelder kan niet melden dat de storingsmelder stuk is"
        // hier oplevert: hij kan het niet mailen op het moment zelf, maar de mislukking staat wél in de
        // opslag. De ronde daarna leest die en meldt erover — de kringloop uit de klassedocumentatie.
        var bank = new Storingsmelderbank();
        bank.Bron.Leesfout = new InvalidOperationException("de klantopslag antwoordt niet");

        var host = new Vasteagenthost();
        var melder = Melder(bank, host, new Snelleklok(Middag, meteenAf: true));

        var agent = await Gedraaid(melder, host, PlatformAgentNames.Alerts);

        var run = agent.Runs[0];

        Assert.NotNull(run.Mislukking);
        Assert.True(run.Afgerond);
    }

    [Fact]
    public async Task ZonderIngerichteTelemetrieKijktDeMelderGewoonDoor()
    {
        var bank = new Storingsmelderbank();
        bank.Bron.Klanten.Add(
            Storingsmelderbank.Klant([Storingsmelderbank.Mislukt("voorraad-sync")], "bakker", "Bakker Logistiek"));

        var verstuurd = await bank.RondeAsync();

        Assert.Equal(1, verstuurd);
    }

    // ── Het plan van de melder is een cron en die kent geen halve minuten ───────────────────────

    [Theory]
    [InlineData(60, "* * * * *", 60)]
    [InlineData(300, "*/5 * * * *", 300)]
    [InlineData(3600, "0 * * * *", 3600)]
    [InlineData(15, "* * * * *", 60)]
    [InlineData(90, "*/2 * * * *", 120)]
    public void HetPlanVanDeMelderRondtAfOpHeleMinutenEnZegtDat(int seconden, string cron, int gepland)
    {
        // Een cron-expressie kan "elke negentig seconden" niet zeggen. Van de twee mogelijke
        // antwoorden — een plan publiceren dat niet klopt, of afronden op wat een cron kán zeggen en op
        // dat afgeronde plan draaien — is het tweede het eerlijke. De melder logt dat hij afrondt.
        Assert.Equal(cron, PlatformAgentPlans.Alerts(seconden).Expression);
        Assert.Equal(TimeSpan.FromSeconds(gepland), PlatformAgentPlans.PlannedInterval(seconden));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(int.MaxValue)]
    public void GeenEnkeleOnzinnigeInstellingLevertEenUitzonderingOp(int waarde)
    {
        // De Range-annotaties op deze opties hebben geen ValidateOnStart, dus de eerste keer dat ze
        // worden gelezen is binnen een achtergronddienst — en dat heeft het portaal vandaag al één keer
        // platgelegd. Vandaar dat het plan klemt in plaats van valideert.
        Assert.NotNull(PlatformAgentPlans.Costs(waarde));
        Assert.NotNull(PlatformAgentPlans.Alerts(waarde));
    }

    // ── Hulpmiddelen ────────────────────────────────────────────────────────────────────────────

    /// <summary>Hoe lang een test op de lus van een achtergronddienst wacht.</summary>
    /// <remarks>
    /// Twee seconden, en dat is ruim: het echte pad is in microseconden klaar. Deze grens bestaat
    /// alleen om een mutatie die de melding weghaalt rood te laten worden in plaats van te laten
    /// hangen.
    /// </remarks>
    private static readonly TimeSpan Wachtgrens = TimeSpan.FromSeconds(2);

    /// <summary>Wacht tot de agent een volgende run heeft gemeld.</summary>
    private static Task<Vasteagent> Gemeld(BackgroundService dienst, Vasteagenthost host, string agentName) =>
        GestartTot(dienst, host, agentName, agent => agent.EersteMelding);

    /// <summary>Wacht tot de agent één run heeft afgerond.</summary>
    private static Task<Vasteagent> Gedraaid(BackgroundService dienst, Vasteagenthost host, string agentName) =>
        GestartTot(dienst, host, agentName, agent => agent.EersteRun);

    /// <summary>
    /// Start de dienst, wacht tot er iets is gebeurd, en stopt hem altijd weer.
    /// </summary>
    /// <param name="dienst">De achtergronddienst.</param>
    /// <param name="host">De opvangende agent-host.</param>
    /// <param name="agentName">De agent die de dienst hoort aan te kondigen.</param>
    /// <param name="waarop">Waarop deze test wacht.</param>
    /// <returns>De agent.</returns>
    /// <remarks>
    /// <para><strong>Eerst wachten op de aankondiging en dán opzoeken</strong>, en dat is geen
    /// omslachtigheid maar een gemeten eigenschap: het lijf van <c>ExecuteAsync</c> van een
    /// <see cref="BackgroundService"/> is niet gelopen als <c>StartAsync</c> terugkomt. Direct na
    /// <c>StartAsync</c> geteld: nul aankondigingen; een fractie later één. Dezelfde vorm die deze
    /// bibliotheek al drie keer heeft opgeleverd.</para>
    ///
    /// <para><strong>En het stoppen staat in een <c>finally</c>, en dat is met een mutatie
    /// gevonden.</strong> Zonder dat blijft er bij een mislukte test een dienst met een geparkeerde
    /// lus achter, en dan vielen in dezelfde run achttien tests van het urenendpoint om met een
    /// <c>401</c> — een failliet dat niets met de mutatie te maken had en de meting onleesbaar maakte.
    /// Eén rode test hoort er precies één te zijn.</para>
    /// </remarks>
    private static async Task<Vasteagent> GestartTot(
        BackgroundService dienst,
        Vasteagenthost host,
        string agentName,
        Func<Vasteagent, Task> waarop)
    {
        await dienst.StartAsync(CancellationToken.None);

        try
        {
            await host.EersteAankondiging.WaitAsync(Wachtgrens);

            var agent = Assert.IsType<Vasteagent>(host.Find(agentName));
            await waarop(agent).WaitAsync(Wachtgrens);
            return agent;
        }
        finally
        {
            await dienst.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Een client die de twee maanden van deze middag kan antwoorden: juli en augustus 2026.
    /// </summary>
    /// <remarks>
    /// Zonder afgesproken antwoord levert de dubbel <c>NotAvailable</c>, en dan schrijft de collector
    /// met opzet níets weg (punt 39). Dat is precies goed gedrag en het maakt het aantal verwerkte
    /// items nul — en dan zou een test op dat aantal niets meten.
    /// </remarks>
    private static Vastekostenclient Metingen()
    {
        var client = new Vastekostenclient();

        client.Antwoord(ScopeMbv, "2026-07", Vastekostenclient.Gemeten("Azure App Service", 36.36m, Dagen(7, 31)));
        client.Antwoord(ScopeMbv, "2026-08", Vastekostenclient.Gemeten("Azure App Service", 22.10m, Dagen(8, 20)));

        return client;
    }

    private static IEnumerable<DateOnly> Dagen(int maand, int tot) =>
        Enumerable.Range(1, tot).Select(dag => new DateOnly(2026, maand, dag));

    private static (AzureCostCollector Collector, Vasteagenthost Host, Snelleklok Klok, Vastekostenopslag Opslag)
        Collector(int uur = 4, bool meteenAf = false, Vastekostenclient? client = null)
    {
        var opslag = new Vastekostenopslag();
        var host = new Vasteagenthost();
        var klok = new Snelleklok(Middag, meteenAf);

        var collector = new AzureCostCollector(
            opslag,
            client ?? new Vastekostenclient(),
            Options.Create(new AzureCostOptions { RunHourUtc = uur }),
            klok,
            NullLogger<AzureCostCollector>.Instance,
            host);

        return (collector, host, klok, opslag);
    }

    private static AgentFaultAlerter Melder(Storingsmelderbank bank, Vasteagenthost host, Snelleklok klok) =>
        new(
            bank.Bron,
            bank.Markeringen,
            bank.Verzender,
            Options.Create(bank.Opties),
            Options.Create(bank.Mailopties),
            klok,
            NullLogger<AgentFaultAlerter>.Instance,
            host);
}
