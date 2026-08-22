using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Soratus.Portal.Sprints;
using Soratus.Portal.Tests.Hulpmiddelen;
using Soratus.Portal.Views;

namespace Soratus.Portal.Tests.Contracten;

/// <summary>
/// De sprintcollector: wat hij ophaalt, wat hij wegschrijft, en vooral wat hij níet wegschrijft (§3.4).
/// </summary>
/// <remarks>
/// <para><strong>Elke test hieronder draait <c>RunAsync</c> en niet de dagelijkse lus.</strong> Die
/// methode is <c>internal</c> en levert een aantal op, precies zodat een test één ronde kan doen zonder
/// een kwartier te wachten. Dat is dezelfde afweging als bij elke klok in dit portaal: een drempel die
/// alleen door te wachten te bereiken is, wordt niet getest — en een test die het toch probeert met een
/// klok die niet wacht, hángt in plaats van rood te worden. Dat is gat 3 van punt 41.</para>
///
/// <para><strong>De scherpste regel van deze klasse is dat een mislukte lezing niets wegschrijft.</strong>
/// Punt 39 letterlijk: de vorige lezing blijft staan met haar eigen tijdstip erbij, want die is werkelijk
/// gelezen en de mislukte aanroep heeft niets gelezen. Zou hier een document met
/// <see cref="SprintState.Unknown"/> worden geschreven, dan wist één geweigerd verzoek een sprint die er
/// wél was — en dan staat er op het scherm van een klant "nog niet opgehaald" over werk dat er is.</para>
/// </remarks>
public class SprintcollectorTests
{
    /// <summary>De klantslug van de gezaaide klant.</summary>
    private const string Klant = "acme-logistiek";

    /// <summary>Het bord van die klant.</summary>
    private const string Bord = "soratus/Acme Logistiek/Acme Logistiek Team";

    /// <summary>De gezaaide sprint.</summary>
    private static DevOpsIteration Augustus() => new()
    {
        Id = "2de79897-d29b-47f9-b6d0-fff5493a6e1a",
        Name = "2026-08 Augustus",
        Path = @"Acme Logistiek\2026-08 Augustus",
        Start = new DateOnly(2026, 8, 1),
        Finish = new DateOnly(2026, 8, 31),
    };

    /// <summary>Bouwt een collector op deze bron en opslag.</summary>
    /// <param name="opslag">De opslag.</param>
    /// <param name="bron">De DevOps-naad.</param>
    /// <param name="opties">De instellingen, of <c>null</c> voor de standaard.</param>
    /// <param name="klok">De klok, of <c>null</c> voor de stilstaande klok van de tests.</param>
    /// <returns>De collector.</returns>
    private static SprintCollector Bouw(
        Vastesprintopslag opslag,
        Vastesprintbron bron,
        SprintOptions? opties = null,
        TimeProvider? klok = null) =>
        new(
            opslag,
            bron,
            Options.Create(opties ?? new SprintOptions()),
            klok ?? Weergavelaag.Klok,
            NullLogger<SprintCollector>.Instance);

    [Fact]
    public async Task EenGeslaagdeLezingWordtWeggeschrevenMetDeSprintErin()
    {
        var opslag = new Vastesprintopslag();
        opslag.Klanten.Add(new SprintTarget(Klant, Bord));

        var bron = new Vastesprintbron().Antwoordt(Vastesprintbron.Sprint(Augustus(), datedCount: 5));

        var geschreven = await Bouw(opslag, bron).RunAsync(CancellationToken.None);

        Assert.Equal(1, geschreven);

        var schrijfactie = Assert.Single(opslag.Schrijfacties);

        Assert.Equal(SprintState.Current, schrijfactie.State);
        Assert.Equal("2026-08 Augustus", schrijfactie.Sprint!.Name);
        Assert.Equal(5, schrijfactie.DatedCount);

        // De bevraagde scope gaat mee naar het document, en dat is het enige gereedschap op het scherm
        // tegen een tikfout die per ongeluk een ánder bestaand team raakt.
        Assert.Equal("/soratus/Acme Logistiek/Acme Logistiek Team", schrijfactie.Scope);
        Assert.Null(schrijfactie.Failure);
    }

    [Fact]
    public async Task EenMislukteLezingSchrijftNiets()
    {
        // Punt 39, en het is de scherpste regel van deze klasse. Er is niets gelezen, dus er is niets om
        // weg te schrijven — en de vorige lezing blijft staan met haar eigen tijdstip erbij. Zou hier een
        // document met Unknown worden geschreven, dan wist één geweigerd verzoek een sprint die er wél was.
        var opslag = new Vastesprintopslag();
        opslag.Klanten.Add(new SprintTarget(Klant, Bord));

        var bron = new Vastesprintbron().Antwoordt(
            Vastesprintbron.Niets("Het portaal mag dit bord niet lezen."));

        var geschreven = await Bouw(opslag, bron).RunAsync(CancellationToken.None);

        Assert.Equal(0, geschreven);
        Assert.Empty(opslag.Schrijfacties);
    }

    [Fact]
    public async Task EenLezingDieOmvaltSchrijftOokNiets()
    {
        // Hetzelfde gevolg langs een ander pad: een uitzondering uit de client in plaats van een antwoord
        // met NotAvailable. Dat is een catch en geen if, en beide moeten "niets wegschrijven" opleveren —
        // twee paden naar dezelfde regel zijn twee plekken waar hij kan wegvallen.
        var opslag = new Vastesprintopslag();
        opslag.Klanten.Add(new SprintTarget(Klant, Bord));

        var bron = new Vastesprintbron { Werpt = new HttpRequestException("weg") };

        var geschreven = await Bouw(opslag, bron).RunAsync(CancellationToken.None);

        Assert.Equal(0, geschreven);
        Assert.Empty(opslag.Schrijfacties);
    }

    [Fact]
    public async Task EenGeslaagdeLezingGevolgdDoorEenWeigeringLaatDeEersteStaan()
    {
        // De invariant van punt 39 over twee ronden, en dit is de vorm waarin hij op het scherm te zien is:
        // er staat één lezing, en het tijdstip erbij is dat van de geslaagde. De tweede ronde schrijft
        // niets, dus de eerste blijft.
        var opslag = new Vastesprintopslag();
        opslag.Klanten.Add(new SprintTarget(Klant, Bord));

        var bron = new Vastesprintbron().AntwoordtAchtereenvolgens(
            Vastesprintbron.Sprint(Augustus()),
            Vastesprintbron.Niets("Azure DevOps liet ons niet door."));

        var collector = Bouw(opslag, bron, new SprintOptions { FreshnessFactor = 0.1 });

        await collector.RunAsync(CancellationToken.None);
        await collector.RunAsync(CancellationToken.None);

        var schrijfactie = Assert.Single(opslag.Schrijfacties);
        Assert.Equal(SprintState.Current, schrijfactie.State);
    }

    [Fact]
    public async Task EenOnleesbaarAntwoordWordtWelWeggeschrevenAlsOnbekend()
    {
        // De uitzondering op de regel hierboven, en de richting is met opzet deze kant op: het betekent dat
        // onze lezer niet meer bij de API past, en dat is een defect dat zichtbaar hoort te zijn. Van de
        // twee mogelijke fouten — geen sprint of een sprint met te weinig items — is alleen de eerste
        // zichtbaar. Punt 39, derde regel.
        var opslag = new Vastesprintopslag();
        opslag.Klanten.Add(new SprintTarget(Klant, Bord));

        const string reden = "Er is naar 16 work items gevraagd en er zijn er 12 teruggekomen.";
        var bron = new Vastesprintbron().Antwoordt(Vastesprintbron.Onleesbaar(reden));

        var geschreven = await Bouw(opslag, bron).RunAsync(CancellationToken.None);

        Assert.Equal(1, geschreven);

        var schrijfactie = Assert.Single(opslag.Schrijfacties);

        Assert.Equal(SprintState.Unknown, schrijfactie.State);
        Assert.Equal(reden, schrijfactie.Failure);
        Assert.Null(schrijfactie.Sprint);
        Assert.Empty(schrijfactie.Items);
    }

    [Fact]
    public async Task EenLezingZonderHuidigeSprintWordtWeggeschrevenMetZijnEigenToestand()
    {
        // Geen sprint is een geslaagde lezing en geen mislukking. De toestand gaat mee, want vijf van de
        // zes toestanden vragen iets anders van een mens — en op het scherm is dat het verschil tussen
        // "datums invullen" en "wachten".
        var opslag = new Vastesprintopslag();
        opslag.Klanten.Add(new SprintTarget(Klant, Bord));

        var bron = new Vastesprintbron().Antwoordt(
            Vastesprintbron.Geen(
                SprintState.NoDatedIterations,
                undated:
                [
                    new DevOpsIteration
                    {
                        Id = "a", Name = "Iteration 1", Path = @"Acme Logistiek\Iteration 1",
                    },
                ]));

        await Bouw(opslag, bron).RunAsync(CancellationToken.None);

        var schrijfactie = Assert.Single(opslag.Schrijfacties);

        Assert.Equal(SprintState.NoDatedIterations, schrijfactie.State);
        Assert.Equal("Iteration 1", Assert.Single(schrijfactie.Undated).Name);
        Assert.Null(schrijfactie.Failure);
    }

    [Fact]
    public async Task DeOverlappendeIteratiesGaanMeeNaarHetDocument()
    {
        // Zonder die namen is de melding "er lopen meerdere periodes" niet te gebruiken. Deze test is er
        // omdat het pad van keuze naar document twee lijsten draagt en er één van kan wegvallen zonder dat
        // de andere het merkt.
        var opslag = new Vastesprintopslag();
        opslag.Klanten.Add(new SprintTarget(Klant, Bord));

        var bron = new Vastesprintbron().Antwoordt(
            Vastesprintbron.Geen(
                SprintState.Ambiguous,
                overlapping:
                [
                    Augustus(),
                    new DevOpsIteration
                    {
                        Id = "b",
                        Name = "Sprint 42",
                        Path = @"Acme Logistiek\Sprint 42",
                        Start = new DateOnly(2026, 8, 15),
                        Finish = new DateOnly(2026, 9, 15),
                    },
                ],
                datedCount: 2));

        await Bouw(opslag, bron).RunAsync(CancellationToken.None);

        var schrijfactie = Assert.Single(opslag.Schrijfacties);

        Assert.Equal(SprintState.Ambiguous, schrijfactie.State);
        Assert.Equal(2, schrijfactie.Overlapping.Count);
        Assert.Null(schrijfactie.Sprint);
    }

    [Fact]
    public async Task EenKlantZonderBordWordtNietBevraagd()
    {
        // Punt 15 op de plek waar hij de aanroep raakt. Leeg is een geldige toestand: er wordt niet
        // gevraagd, er komt geen document, en het scherm meldt dat er niets is ingericht.
        var opslag = new Vastesprintopslag();
        opslag.Klanten.Add(new SprintTarget(Klant, null));
        opslag.Klanten.Add(new SprintTarget("bakker-bv", "   "));

        var bron = new Vastesprintbron();

        var geschreven = await Bouw(opslag, bron).RunAsync(CancellationToken.None);

        Assert.Equal(0, geschreven);
        Assert.Empty(bron.Aanroepen);
        Assert.Empty(opslag.Schrijfacties);
    }

    [Fact]
    public async Task EenOnbruikbaarBordWordtNietBevraagd()
    {
        // Kan alleen als iemand het klantdocument met de hand heeft aangepast — beide formulieren
        // valideren — en het is het enige geval waarin een klant een bord heeft en toch niet wordt
        // opgehaald. Er wordt niets bevraagd en niets weggeschreven; het scherm zegt dat het bord niet te
        // gebruiken is, en dat is een andere handeling dan invullen.
        var opslag = new Vastesprintopslag();
        opslag.Klanten.Add(new SprintTarget(Klant, "sub-soratus-acme · rg-acme-prod"));

        var bron = new Vastesprintbron();

        await Bouw(opslag, bron).RunAsync(CancellationToken.None);

        Assert.Empty(bron.Aanroepen);
        Assert.Empty(opslag.Schrijfacties);
    }

    [Fact]
    public async Task ElkeKlantWordtMetZijnEigenBordBevraagd()
    {
        // Klinkt vanzelfsprekend en is het niet: een lus die de scope buiten zich opbouwt, bevraagt voor
        // elke klant het bord van de laatste. Dat is niet aan het resultaat te zien — er komt netjes een
        // sprint terug — en op het scherm staat dan de sprint van een ánder project.
        var opslag = new Vastesprintopslag();
        opslag.Klanten.Add(new SprintTarget(Klant, Bord));
        opslag.Klanten.Add(new SprintTarget("bakker-bv", "soratus/Bakker/Bakker Team"));

        var bron = new Vastesprintbron().Antwoordt(Vastesprintbron.Sprint(Augustus()));

        await Bouw(opslag, bron).RunAsync(CancellationToken.None);

        Assert.Equal(
            ["/soratus/Acme Logistiek/Acme Logistiek Team", "/soratus/Bakker/Bakker Team"],
            bron.Aanroepen.Select(scope => scope.Path));

        Assert.Equal(
            [Klant, "bakker-bv"],
            opslag.Schrijfacties.Select(actie => actie.CustomerId));
    }

    [Fact]
    public async Task EenVerseLezingWordtNietOpnieuwOpgehaald()
    {
        // De wederzijdse uitsluiting tussen twee portaalinstanties, en hij is te meten als "hij is niet
        // opnieuw aangeroepen" en op geen andere manier. Zonder hem doen twee instanties elk kwartier
        // dezelfde vijf aanroepen per klant.
        var opslag = new Vastesprintopslag();
        opslag.Klanten.Add(new SprintTarget(Klant, Bord));
        opslag.Gelezen(Klant, Weergavelaag.Klok.GetUtcNow() - TimeSpan.FromMinutes(2));

        var bron = new Vastesprintbron();

        var geschreven = await Bouw(opslag, bron).RunAsync(CancellationToken.None);

        Assert.Equal(0, geschreven);
        Assert.Empty(bron.Aanroepen);
        Assert.Equal(1, opslag.Puntlezingen);
    }

    [Fact]
    public async Task EenOudeLezingWordtWelOpnieuwOpgehaald()
    {
        // De spiegel. Zonder deze test is "sla over als hij vers is" ook waar bij een implementatie die
        // altijd overslaat — en dan wordt er nooit meer iets opgehaald, stil, met een lezing op het scherm
        // die langzaam veroudert onder een tijdstip dat wél klopt.
        var opslag = new Vastesprintopslag();
        opslag.Klanten.Add(new SprintTarget(Klant, Bord));
        opslag.Gelezen(Klant, Weergavelaag.Klok.GetUtcNow() - TimeSpan.FromMinutes(30));

        var bron = new Vastesprintbron().Antwoordt(Vastesprintbron.Sprint(Augustus()));

        var geschreven = await Bouw(opslag, bron).RunAsync(CancellationToken.None);

        Assert.Equal(1, geschreven);
        Assert.Single(bron.Aanroepen);
    }

    [Fact]
    public async Task EenMislukteneenPuntlezingSlaatDeKlantNietOver()
    {
        // De goede kant op: een aanroep te veel is goedkoop, en een klant die nooit meer wordt opgehaald
        // omdat de puntlezing struikelt is dat niet. Dezelfde afweging als bij de besparing in de
        // kostencollector, waar een onleesbare toestand ertoe leidt dat die maand gewoon opnieuw wordt
        // opgevraagd.
        var opslag = new Vastesprintopslag { PuntlezingWerpt = new InvalidOperationException("stuk") };
        opslag.Klanten.Add(new SprintTarget(Klant, Bord));

        var bron = new Vastesprintbron().Antwoordt(Vastesprintbron.Sprint(Augustus()));

        var geschreven = await Bouw(opslag, bron).RunAsync(CancellationToken.None);

        Assert.Equal(1, geschreven);
        Assert.Single(bron.Aanroepen);
    }

    [Fact]
    public async Task EenOnbereikbareKlantenlijstLeestNietsEnWerptNiet()
    {
        // Een ronde die omvalt mag het portaal niet meenemen: een BackgroundService die een uitzondering
        // laat ontsnappen stopt de host. En er wordt niets bevraagd — een lezing die nergens kan landen
        // kost aanroepen en levert niets op het scherm.
        var opslag = new Vastesprintopslag { KlantenlijstWerpt = new InvalidOperationException("weg") };
        var bron = new Vastesprintbron();

        var geschreven = await Bouw(opslag, bron).RunAsync(CancellationToken.None);

        Assert.Equal(0, geschreven);
        Assert.Empty(bron.Aanroepen);
    }

    [Fact]
    public async Task DeVlagUitLeestNietsOpEnSchrijftNiets()
    {
        // Gat 3 van punt 41, hier meteen dichtgezet. De vlag staat óók bovenaan RunAsync en niet alleen in
        // ExecuteAsync: dit is de enige methode die werk doet en ze is internal, dus een tweede aanroeper
        // is mogelijk. Zonder deze controle zou een test op de vlag de dagelijkse lus moeten starten, en
        // met een klok die niet wacht draait die lus eindeloos — dan levert het negeren van de vlag geen
        // rode test op maar een test die hangt.
        var opslag = new Vastesprintopslag();
        opslag.Klanten.Add(new SprintTarget(Klant, Bord));

        var bron = new Vastesprintbron();

        var collector = Bouw(opslag, bron, new SprintOptions { Enabled = false });
        var geschreven = await collector.RunAsync(CancellationToken.None);

        Assert.Equal(0, geschreven);
        Assert.Empty(bron.Aanroepen);
        Assert.Empty(opslag.Schrijfacties);
        Assert.Equal(0, opslag.Puntlezingen);
    }

    [Fact]
    public async Task DeDagDieDeCollectorDoorgeeftIsDeNederlandseDagEnNietDeUtcDag()
    {
        // De invariant en niet zijn gevolg, want het gevolg is maar één keer per maand zichtbaar: op 1
        // september om 00:30 Nederlandse tijd is het in UTC nog 31 augustus. Zou UTC de dag bepalen, dan
        // wijst het portaal in dat halfuur de sprint van augustus aan als de huidige terwijl het bord
        // september zegt — de grens tussen twee maandsprints zou dan twee uur na middernacht liggen, en dat
        // is een grens die niemand heeft afgesproken.
        //
        // Dit is precies omgekeerd aan de kostencollector, en met een reden: daar gaat UTC naar de
        // volledigheidscontrole omdat Azure in UTC boekt. Een iteratie is een kalenderperiode die een mens
        // op een bord heeft ingevuld.
        var opslag = new Vastesprintopslag();
        opslag.Klanten.Add(new SprintTarget(Klant, Bord));

        var bron = new Vastesprintbron().Antwoordt(Vastesprintbron.Sprint(Augustus()));

        // 31 augustus 22:30 UTC is 1 september 00:30 in Nederland (zomertijd, UTC+2).
        var klok = new Vasteklok(new DateTimeOffset(2026, 8, 31, 22, 30, 0, TimeSpan.Zero));

        await Bouw(opslag, bron, klok: klok).RunAsync(CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 9, 1), Assert.Single(bron.Dagen));
    }

    /// <summary>Een klok die op één moment stilstaat.</summary>
    /// <param name="moment">Het moment.</param>
    /// <remarks>
    /// Een eigen klok en niet <see cref="Weergavelaag.Klok"/>: die staat op de vaste testtijd, en de test
    /// hierboven heeft juist een moment nodig dat vlak vóór middernacht in UTC ligt. Eén klasse van vier
    /// regels is goedkoper dan een tweede vaste tijd in de gedeelde hulpmiddelen, die dan in élke andere
    /// test meekijkt.
    /// </remarks>
    private sealed class Vasteklok(DateTimeOffset moment) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => moment;
    }
}
