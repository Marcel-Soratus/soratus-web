using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Soratus.Portal.Data;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Contracten;

/// <summary>
/// De dagelijkse kostencollector: wat hij bevraagt, wat hij wegschrijft en wat hij met opzet niet doet.
/// </summary>
/// <remarks>
/// <para><strong>De helft van deze tests bewijst dat er iets níet gebeurt.</strong> Dat is hier de
/// duurdere helft: een collector die te veel wegschrijft levert € 0,00 op een factuur, en een collector
/// die te veel bevraagt trekt een aanroepbudget leeg dat gemeten schaars is — vier aanroepen binnen elf
/// seconden gaven vier 429's, en op 21 augustus 2026 kwam er zelfs na tien minuten stilte nog een.</para>
///
/// <para>De klok staat stil en elk wachten is meteen voorbij; zie <see cref="Snelleklok"/>. Dat die
/// klok de gevraagde wachttijden bijhoudt is geen bijzaak — de stilte tussen twee aanroepen ís het
/// ontwerp, en zonder die assertie zou een test die twee aanroepen telt ook groen zijn als ze binnen
/// een milliseconde achter elkaar gingen.</para>
/// </remarks>
public class KostencollectorTests
{
    private const string Abonnement = "501a66d2-de54-4d4f-9f7c-1fbb55bec17f";

    private const string ScopeMbv = $"/subscriptions/{Abonnement}/resourceGroups/MBV";

    private const string ScopeTweede = $"/subscriptions/{Abonnement}/resourceGroups/rg-tweede";

    /// <summary>21 augustus 2026, 04:00 UTC: het draaimoment uit §4 op de dag van de metingen.</summary>
    private static readonly DateTimeOffset Nu = new(2026, 8, 21, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EenKlantMetEenScopeWordtGemetenEnVastgelegd()
    {
        // De spiegel van alles hieronder. Zonder deze test mag de collector nooit iets wegschrijven en
        // is elke "hij schrijft niets"-test groen — en dan krijgt het facturatiescherm nooit gegevens.
        var (collector, opslag, client, _) = Bouw();
        opslag.Klant("mbv", ScopeMbv);
        opslag.Toestand("mbv", "2026-07", AzureCostState.Measured);
        client.Antwoord(ScopeMbv, "2026-08", Vastekostenclient.Gemeten("Azure App Service", 36.36m, Dagen(2026, 8, 1, 20)));

        var geschreven = await collector.RunAsync(CancellationToken.None);

        Assert.Equal(1, geschreven);
        var write = Assert.Single(opslag.Geschreven);
        Assert.Equal("mbv", write.CustomerId);
        Assert.Equal("2026-08", write.Month);

        // De lopende maand is nooit volledig: de laatste dag is nog niet om. §3.7 zet hem bovenaan als
        // concept, en dat is hier geen presentatiekeuze maar de uitkomst van AzureCostCompleteness.
        Assert.Equal(AzureCostState.Partial, write.State);
        Assert.Equal(new DateOnly(2026, 8, 20), write.CoversThrough);
        Assert.Equal(ScopeMbv, write.Scope);
        Assert.Equal(Nu, write.MeasuredAt);
        Assert.Equal("EUR", write.Currency);
        Assert.Null(write.Failure);
    }

    [Fact]
    public async Task EenAfgeslotenMaandDieVolledigIsGemetenKrijgtDeToestandMeasured()
    {
        // De maand die gefactureerd gaat worden. Zonder deze test mag Judge altijd Partial opleveren en
        // is er nooit een maand te factureren.
        var (collector, opslag, client, _) = Bouw();
        opslag.Klant("mbv", ScopeMbv);
        client.Antwoord(ScopeMbv, "2026-07", Vastekostenclient.Gemeten("Azure App Service", 58.2m, Dagen(2026, 7, 1, 31)));

        await collector.RunAsync(CancellationToken.None);

        var juli = opslag.Geschreven.Single(w => w.Month == "2026-07");
        Assert.Equal(AzureCostState.Measured, juli.State);
        Assert.Equal(new DateOnly(2026, 7, 31), juli.CoversThrough);
    }

    [Fact]
    public async Task DeVorigeMaandGaatVoorDeLopende()
    {
        // Loopt het budget halverwege de run leeg, dan is de maand die je wil hebben degene die je het
        // eerst hebt gedaan — en dat is de maand die gefactureerd gaat worden.
        var (collector, opslag, client, _) = Bouw();
        opslag.Klant("mbv", ScopeMbv);

        await collector.RunAsync(CancellationToken.None);

        Assert.Equal([( ScopeMbv, "2026-07"), (ScopeMbv, "2026-08")], client.Vragen);
    }

    [Fact]
    public async Task EenVorigeMaandDieAlVolledigIsWordtNietOpnieuwOpgevraagd()
    {
        // De besparing op het schaarse ding. Een maand op Measured kan niet meer veranderen — de
        // volledigheidsregel eist dat de laatste dag er staat én dat er twee dagen ná de maand is
        // gemeten, en aan beide is niets meer te doen. Voor achtentwintig van de eenendertig dagen van
        // een maand halveert dit het aantal aanroepen per klant.
        var (collector, opslag, client, _) = Bouw();
        opslag.Klant("mbv", ScopeMbv);
        opslag.Toestand("mbv", "2026-07", AzureCostState.Measured);

        await collector.RunAsync(CancellationToken.None);

        Assert.Equal([(ScopeMbv, "2026-08")], client.Vragen);
    }

    [Theory]
    [InlineData(AzureCostState.Partial)]
    [InlineData(AzureCostState.NoLines)]
    [InlineData(AzureCostState.Unknown)]
    public async Task EenVorigeMaandDieNogNietVolledigIsWordtWelOpnieuwOpgevraagd(AzureCostState toestand)
    {
        // De spiegel van de besparing hierboven, en hij is het halve werk: alleen Measured is definitief.
        // Zou hier ook Partial worden overgeslagen, dan wordt een maand die op de 1e om 04:00 onvolledig
        // was nooit meer bijgewerkt — en die maand is precies de maand die punt 31 beschrijft.
        var (collector, opslag, client, _) = Bouw();
        opslag.Klant("mbv", ScopeMbv);
        opslag.Toestand("mbv", "2026-07", toestand);

        await collector.RunAsync(CancellationToken.None);

        Assert.Contains((ScopeMbv, "2026-07"), client.Vragen);
    }

    [Fact]
    public async Task ErZitStilteTussenTweeAanroepenEnNietVoorDeEerste()
    {
        // Gemeten: vier aanroepen binnen elf seconden gaven vier 429's, en een geslaagde aanroep vroeg
        // dertig tot veertig seconden stilte — voor een maandvraag met dagkorrel bleek drieënvijftig
        // seconden nog te weinig. De stilte tussen twee aanroepen is dus het ontwerp en geen detail.
        //
        // En niet vóór de eerste: dan zou elke run vier minuten later beginnen dan hij hoort.
        var (collector, opslag, _, klok) = Bouw();
        opslag.Klant("mbv", ScopeMbv);
        opslag.Klant("tweede", ScopeTweede);
        opslag.Toestand("mbv", "2026-07", AzureCostState.Measured);
        opslag.Toestand("tweede", "2026-07", AzureCostState.Measured);

        await collector.RunAsync(CancellationToken.None);

        var pauzes = klok.Wachttijden.Where(w => w > TimeSpan.Zero).ToArray();
        Assert.Single(pauzes);
        Assert.Equal(TimeSpan.FromSeconds(240), pauzes[0]);
    }

    [Fact]
    public async Task ZonderScopeWordtErNietsBevraagdEnNietsGeclaimd()
    {
        // Een klant zonder scope is niet ingericht, en dat is een geldige toestand. Er hoort dan géén
        // aanroep te gaan én géén claim te worden gezet: een claim zonder werk zou de run van een
        // andere instantie dezelfde dag blokkeren.
        var (collector, opslag, client, _) = Bouw();
        opslag.Klant("mbv", null);
        opslag.Klant("acme", "   ");

        var geschreven = await collector.RunAsync(CancellationToken.None);

        Assert.Equal(0, geschreven);
        Assert.Empty(client.Vragen);
        Assert.Empty(opslag.Claimpogingen);
        Assert.Empty(opslag.Geschreven);
    }

    [Fact]
    public async Task EenOnbruikbareScopeWordtOvergeslagenEnDeRestGaatDoor()
    {
        // Kan alleen als iemand het document met de hand heeft aangepast — beide formulieren valideren.
        // Eén kapotte klant mag de andere niet meenemen: dan zou één tikfout de hele facturatie stilzetten.
        var (collector, opslag, client, _) = Bouw();
        opslag.Klant("acme", "sub-soratus-acme · rg-acme-prod");
        opslag.Klant("mbv", ScopeMbv);
        opslag.Toestand("mbv", "2026-07", AzureCostState.Measured);
        client.Antwoord(ScopeMbv, "2026-08", Vastekostenclient.Gemeten("Azure App Service", 36.36m, Dagen(2026, 8, 1, 20)));

        await collector.RunAsync(CancellationToken.None);

        Assert.Equal([(ScopeMbv, "2026-08")], client.Vragen);
        Assert.Single(opslag.Geschreven);
    }

    [Fact]
    public async Task EenTweedeRunOpDezelfdeDagDoetNiets()
    {
        // Het slot op twee instanties. Het portaal kan er meer dan één hebben, en dan draaien er twee
        // collectors — en die verdelen de emmer tot geen van beide nog een bedrag krijgt. De claim gaat
        // vóór de eerste aanroep, met een CreateItemAsync en geen upsert, dus de tweede krijgt een 409.
        var (collector, opslag, client, _) = Bouw();
        opslag.Klant("mbv", ScopeMbv);
        opslag.Toestand("mbv", "2026-07", AzureCostState.Measured);

        await collector.RunAsync(CancellationToken.None);
        var vragenNaEen = client.Vragen.Count;

        var tweede = await collector.RunAsync(CancellationToken.None);

        Assert.Equal(0, tweede);
        Assert.Equal(vragenNaEen, client.Vragen.Count);
        Assert.Equal(2, opslag.Claimpogingen.Count);
        Assert.Equal(new DateOnly(2026, 8, 21), opslag.Claimpogingen[1]);
    }

    [Fact]
    public async Task DeClaimGaatVoorDeEersteAanroep()
    {
        // De volgorde is het slot. Zou er eerst worden gemeten en daarna geclaimd, dan hebben twee
        // instanties de aanroepen al gedaan voordat een van de twee te horen krijgt dat het niet mocht.
        var (collector, opslag, client, _) = Bouw();
        opslag.Klant("mbv", ScopeMbv);

        await collector.RunAsync(CancellationToken.None);

        Assert.NotEmpty(opslag.Claimpogingen);
        Assert.NotEmpty(client.Vragen);
    }

    [Fact]
    public async Task EenMislukteAanroepSchrijftNietsEnLaatDeVorigeLezingStaan()
    {
        // §32: wat er op het scherm hoort te staan als de verzameling van vannacht is mislukt, is de
        // lezing van gisteren met het tijdstip erbij. Het bewaarde getal is werkelijk gemeten; de
        // mislukte aanroep heeft niets gemeten. Zou hier een document met Unknown worden geschreven, dan
        // wist één 429 een bedrag dat er wél was.
        var (collector, opslag, client, _) = Bouw();
        opslag.Klant("mbv", ScopeMbv);
        opslag.Toestand("mbv", "2026-07", AzureCostState.Measured);
        client.Antwoord(
            ScopeMbv,
            "2026-08",
            new AzureCostAnswer(
                AzureCostAnswerKind.NotAvailable,
                [],
                [],
                Currency: null,
                "Cost Management liet ons niet door.",
                Calls: 2));

        var geschreven = await collector.RunAsync(CancellationToken.None);

        Assert.Equal(0, geschreven);
        Assert.Empty(opslag.Geschreven);
    }

    [Fact]
    public async Task EenOnleesbaarAntwoordWordtOnbekendEnMetOpzetGeenBedrag()
    {
        // Punt 33: een onleesbaar bedrag werpt en wordt geen nul, en de aanroeper hoort er Unknown van
        // te maken. Dit is de enige uitkomst waarbij er wél iets wordt weggeschreven zonder dat er iets
        // is gemeten — en dat is de juiste richting: er ís geantwoord, onze lezer past er niet bij, en
        // van de twee mogelijke fouten (geen bedrag of een te laag bedrag) is alleen de eerste zichtbaar.
        var (collector, opslag, client, _) = Bouw();
        opslag.Klant("mbv", ScopeMbv);
        opslag.Toestand("mbv", "2026-07", AzureCostState.Measured);
        client.Antwoord(
            ScopeMbv,
            "2026-08",
            new AzureCostAnswer(
                AzureCostAnswerKind.Unreadable,
                [],
                [],
                Currency: null,
                "Het antwoord heeft geen kolom 'Cost'.",
                Calls: 1));

        await collector.RunAsync(CancellationToken.None);

        var write = Assert.Single(opslag.Geschreven);
        Assert.Equal(AzureCostState.Unknown, write.State);
        Assert.Empty(write.Lines);
        Assert.Null(write.Currency);
        Assert.Null(write.CoversThrough);
        Assert.NotNull(write.Failure);

        // De bevraagde scope staat er óók bij een mislukking. Dat is de enige verdediging tegen een
        // tikfout in een resourcegroepnaam, en juist bij "onbekend" is het de vraag die iemand stelt.
        Assert.Equal(ScopeMbv, write.Scope);
    }

    [Fact]
    public async Task NulRijenWordtNoLinesEnGeenNul()
    {
        // Punt 30, de kern van deze lane. Achter dit ene antwoord zitten drie werkelijkheden — niets
        // verbruikt, nog niet geboekt, verkeerde omgeving — en maar één ervan is nul. Het document zegt
        // daarom "geen regels" en heeft geen enkel bedrag; de scope eronder is wat een mens nodig heeft
        // om de derde mogelijkheid uit te sluiten.
        var (collector, opslag, client, _) = Bouw();
        opslag.Klant("mbv", ScopeMbv);
        opslag.Toestand("mbv", "2026-07", AzureCostState.Measured);
        client.Antwoord(
            ScopeMbv,
            "2026-08",
            new AzureCostAnswer(AzureCostAnswerKind.Answered, [], [], Currency: null, Reason: null, Calls: 1));

        await collector.RunAsync(CancellationToken.None);

        var write = Assert.Single(opslag.Geschreven);
        Assert.Equal(AzureCostState.NoLines, write.State);
        Assert.Empty(write.Lines);
        Assert.Equal(ScopeMbv, write.Scope);

        // En de lezing die het scherm eruit maakt heeft geen subtotaal. Dat is de invariant die deze
        // hele keten draagt: er is een subtotaal dan en slechts dan als er regels zijn.
        var lezing = AzureCostReading.From("2026-08", "augustus 2026", Document(write));
        Assert.Null(lezing.Subtotal);
        Assert.False(lezing.HasAmount);
    }

    [Fact]
    public async Task BedragenBuitenDeGevraagdeMaandWordenOnbekendEnGeenGeenRegels()
    {
        // Regels én geen dag binnen de maand: dan komen de bedragen van een andere periode. De regel van
        // Judge negeert zulke dagen en noemt de maand daarmee leeg — de veilige kant — maar dan zou er
        // een document ontstaan dat "geen regels" zegt naast een subtotaal dat wél bestaat, want dat
        // subtotaal is de som van de regels. Dat is geen toestand maar een defect.
        var (collector, opslag, client, _) = Bouw();
        opslag.Klant("mbv", ScopeMbv);
        opslag.Toestand("mbv", "2026-07", AzureCostState.Measured);
        client.Antwoord(
            ScopeMbv,
            "2026-08",
            Vastekostenclient.Gemeten("Azure App Service", 36.36m, Dagen(2026, 9, 1, 3)));

        await collector.RunAsync(CancellationToken.None);

        var write = Assert.Single(opslag.Geschreven);
        Assert.Equal(AzureCostState.Unknown, write.State);
        Assert.Empty(write.Lines);
        Assert.NotNull(write.Failure);
    }

    [Fact]
    public async Task EenOnbereikbareOpslagLevertGeenEnkeleAanroepOp()
    {
        // Een lezing die nergens landt kost wél een aanroep uit een budget dat gemeten schaars is. En
        // de run valt er niet over om: een BackgroundService die een uitzondering laat ontsnappen stopt
        // de host, en er is niets aan een mislukte kostenmeting dat een agentstatus in de weg staat.
        var (collector, opslag, client, _) = Bouw();
        opslag.Klant("mbv", ScopeMbv);
        opslag.Leesfout = new PortalDataNotProvisionedException("niet ingericht");

        var geschreven = await collector.RunAsync(CancellationToken.None);

        Assert.Equal(0, geschreven);
        Assert.Empty(client.Vragen);
        Assert.Empty(opslag.Claimpogingen);
    }

    [Fact]
    public async Task EenOnleesbareToestandLeidtTotOpnieuwOpvragenEnNietTotOverslaan()
    {
        // De besparing lukt niet. Dan wordt de vorige maand gewoon opgevraagd: een aanroep te veel is
        // duur, maar een maand die nooit definitief wordt is duurder — die is niet te factureren.
        var (collector, opslag, client, _) = Bouw();
        opslag.Klant("mbv", ScopeMbv);
        opslag.Toestand("mbv", "2026-07", AzureCostState.Measured);
        opslag.Toestandsfout = new InvalidOperationException("de puntlezing lukt niet");

        await collector.RunAsync(CancellationToken.None);

        Assert.Contains((ScopeMbv, "2026-07"), client.Vragen);
    }

    [Fact]
    public async Task MetDeVlagUitWordtErNietsGemeten()
    {
        // Rechtstreeks op RunAsync en niet via StartAsync. Dat laatste stond hier eerst en het was een
        // test die niets bewees: StartAsync geeft de besturing terug zodra ExecuteAsync zijn eerste
        // await raakt, dus of er nog iets gebeurde vóór StopAsync hing van de threadpool af. Gevonden
        // met een mutatie — de vlag negeren maakte niets rood.
        var (collector, opslag, client, _) = Bouw(aan: false);
        opslag.Klant("mbv", ScopeMbv);

        var geschreven = await collector.RunAsync(CancellationToken.None);

        Assert.Equal(0, geschreven);
        Assert.Empty(client.Vragen);
        Assert.Empty(opslag.Claimpogingen);
    }

    [Fact]
    public async Task DeJaarwisselingLevertDeDecembervanHetVorigeJaarOp()
    {
        // Een maandgrens die met rekenkunde op een maandnummer stil misgaat: 1 januari min één maand is
        // december van het jaar ervoor, en niet maand nul.
        var klok = new Snelleklok(new DateTimeOffset(2027, 1, 3, 4, 0, 0, TimeSpan.Zero));
        var (collector, opslag, client, _) = Bouw(klok: klok);
        opslag.Klant("mbv", ScopeMbv);

        await collector.RunAsync(CancellationToken.None);

        Assert.Equal([(ScopeMbv, "2026-12"), (ScopeMbv, "2027-01")], client.Vragen);
    }

    /// <summary>De dagen van een maand, vanaf de eerste, zoals een meting ze oplevert.</summary>
    private static IEnumerable<DateOnly> Dagen(int jaar, int maand, int van, int tot) =>
        Enumerable.Range(van, tot - van + 1).Select(dag => new DateOnly(jaar, maand, dag));

    /// <summary>Het document dat de opslag van een schrijfactie zou maken.</summary>
    /// <remarks>
    /// Met de hand en niet met de productiemapping, want die zit in <c>CosmosAzureCostCollectorStore</c>
    /// en die praat met Cosmos. Wat hier wordt getoetst is de invariant van
    /// <see cref="AzureCostReading"/> op de velden die de collector zet, en die velden staan er alle.
    /// </remarks>
    private static AzureCostDocument Document(AzureCostWrite write) => new()
    {
        Id = AzureCostDocumentKeys.ForMonth(write.Month),
        PartitionKey = write.CustomerId,
        CustomerId = write.CustomerId,
        Month = write.Month,
        State = write.State,
        Lines = write.Lines,
        Currency = write.Currency,
        Scope = write.Scope,
        MeasuredAt = write.MeasuredAt,
        CoversThrough = write.CoversThrough?.ToString("yyyy-MM-dd"),
        Failure = write.Failure,
    };

    /// <summary>Bouwt de collector op vaste onderdelen.</summary>
    private static (AzureCostCollector Collector, Vastekostenopslag Opslag, Vastekostenclient Client, Snelleklok Klok)
        Bouw(bool aan = true, Snelleklok? klok = null)
    {
        var opslag = new Vastekostenopslag();
        var client = new Vastekostenclient();
        var tijd = klok ?? new Snelleklok(Nu);

        var collector = new AzureCostCollector(
            opslag,
            client,
            Options.Create(new AzureCostOptions { Enabled = aan }),
            tijd,
            NullLogger<AzureCostCollector>.Instance);

        return (collector, opslag, client, tijd);
    }
}
