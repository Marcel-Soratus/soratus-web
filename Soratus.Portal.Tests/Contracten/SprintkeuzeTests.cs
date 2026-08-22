using Soratus.Portal.Sprints;

namespace Soratus.Portal.Tests.Contracten;

/// <summary>
/// Welke iteratie de huidige sprint is (§3.4).
/// </summary>
/// <remarks>
/// <para><strong>Dit is de harde regel van deze lane: de sprint komt uit de dátums van een iteratie en
/// nooit uit de naam.</strong> De naam is voor mensen — <c>2026-08 Augustus</c> hernoemen naar
/// <c>Augustus</c> mag niets verschuiven. Dat is dezelfde klasse fout als een resourcegroep die uit een
/// weergavetekst werd afgeleid, en de tests hieronder zijn zo gebouwd dat het zichtbaar wordt als iemand
/// die weg inbouwt: de iteraties heten met opzet dingen die niet bij hun datums passen.</para>
///
/// <para><strong>En de meting die het veld ontmaskert dat je hier zou willen gebruiken.</strong> Op 22
/// augustus 2026 gaf de teamiteratielijst per iteratie een <c>timeFrame</c> mee:
/// <c>2026-08 Augustus</c> stond op <c>1</c> (current) en de rest op <c>2</c>. Dat lijkt precies het
/// antwoord — tot je ziet wat er nog op <c>2</c> stond: de drie iteraties <em>zonder</em> datums. Dat veld
/// kan "ligt in de toekomst" niet van "heeft geen datums" onderscheiden, en dat is exact het onderscheid
/// waar deze klasse om draait. <see cref="DevOpsIteration"/> heeft het veld daarom niet.</para>
///
/// <para>Puur en zonder klok: de dag komt als parameter binnen. Dat is de voorwaarde om de invariant te
/// meten in plaats van zijn gevolg — een test die op de echte klok zou lopen, meet elke maand iets anders
/// en is over vier maanden groen om een reden die niemand nog kan navertellen.</para>
/// </remarks>
public class SprintkeuzeTests
{
    /// <summary>Een dag in augustus 2026, de maand van de gemeten huidige sprint.</summary>
    private static readonly DateOnly Augustusdag = new(2026, 8, 22);

    /// <summary>
    /// Een iteratie met datums.
    /// </summary>
    /// <param name="naam">De naam. Met opzet vrij te kiezen, want er wordt niets uit afgeleid.</param>
    /// <param name="start">De eerste dag.</param>
    /// <param name="finish">De laatste dag, inclusief.</param>
    /// <returns>De iteratie.</returns>
    private static DevOpsIteration Met(string naam, DateOnly start, DateOnly finish) => new()
    {
        Id = $"guid-{naam}",
        Name = naam,
        Path = $@"Project\{naam}",
        Start = start,
        Finish = finish,
    };

    /// <summary>Een iteratie zonder datums, zoals <c>Iteration 1</c> t/m <c>3</c> op het echte bord.</summary>
    /// <param name="naam">De naam.</param>
    /// <returns>De iteratie.</returns>
    private static DevOpsIteration Zonder(string naam) => new()
    {
        Id = $"guid-{naam}",
        Name = naam,
        Path = $@"Project\{naam}",
    };

    /// <summary>De vijf maandsprints van het echte bord, met hun gemeten datums.</summary>
    /// <remarks>
    /// De namen zijn de echte namen en de datums de echte datums. Dat is geen versiering: als iemand ooit
    /// de maand uit de naam zou afleiden, blijven déze tests groen — daarom staat er verderop een test met
    /// namen die met opzet niet bij hun datums passen. Twee soorten gegevens, twee soorten bewijs.
    /// </remarks>
    private static DevOpsIteration[] Maandsprints() =>
    [
        Met("2026-08 Augustus", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)),
        Met("2026-09 September", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)),
        Met("2026-10 Oktober", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)),
        Met("2026-11 November", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)),
        Met("2026-12 December", new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31)),
    ];

    [Fact]
    public void OpHetEchteBordIsAugustusDeHuidigeSprint()
    {
        // De gemeten stand van 22 augustus 2026: vijf maandsprints met datums, drie oude iteraties zonder,
        // en augustus is de huidige.
        var keuze = SprintSelection.Choose(
            [.. Maandsprints(), Zonder("Iteration 1"), Zonder("Iteration 2"), Zonder("Iteration 3")],
            Augustusdag);

        Assert.Equal(SprintState.Current, keuze.State);
        Assert.Equal("2026-08 Augustus", keuze.Current!.Name);
        Assert.Equal(5, keuze.DatedCount);
        Assert.Equal(3, keuze.Undated.Count);
        Assert.Empty(keuze.Overlapping);
    }

    [Fact]
    public void DeNaamDoetNietMeeAanDeKeuze()
    {
        // Dít is de test die de harde regel meet. De namen zijn met opzet verkeerd: de iteratie die
        // "2026-12 December" heet loopt in augustus, en de iteratie die "2026-08 Augustus" heet loopt in
        // december. Wie de maand uit de naam afleidt, kiest hier de verkeerde — en dat is de fout die op
        // het scherm niet van een juiste te onderscheiden is.
        //
        // Dat dit kan gebeuren is geen theorie: het hernoemen van een iteratie is een gewone handeling op
        // een bord, en DevOps werkt het pad van elk work item dan bij zonder dat er iets aan de datums
        // verandert.
        DevOpsIteration[] iteraties =
        [
            Met("2026-12 December", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)),
            Met("2026-08 Augustus", new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31)),
        ];

        var keuze = SprintSelection.Choose(iteraties, Augustusdag);

        Assert.Equal(SprintState.Current, keuze.State);
        Assert.Equal("2026-12 December", keuze.Current!.Name);
    }

    [Fact]
    public void EenHernoemingVerschuiftDeSprintNiet()
    {
        // De spiegel van de test hierboven, en hij zegt hetzelfde vanuit de andere kant: dezelfde datums
        // met een andere naam leveren dezelfde keuze op. "2026-08 Augustus hernoemen naar Augustus mag de
        // maand niet verschuiven" — dat is de regel, en dit is hoe hij te meten valt.
        var voor = SprintSelection.Choose(Maandsprints(), Augustusdag);

        var hernoemd = Maandsprints();
        hernoemd[0] = hernoemd[0] with { Name = "Augustus" };

        var na = SprintSelection.Choose(hernoemd, Augustusdag);

        Assert.Equal(voor.State, na.State);
        Assert.Equal(voor.Current!.Start, na.Current!.Start);
        Assert.Equal(voor.Current.Finish, na.Current.Finish);
        Assert.Equal(voor.Current.Id, na.Current.Id);
    }

    [Fact]
    public void DeEersteDagVanDeSprintValtErin()
    {
        var keuze = SprintSelection.Choose(Maandsprints(), new DateOnly(2026, 8, 1));

        Assert.Equal(SprintState.Current, keuze.State);
        Assert.Equal("2026-08 Augustus", keuze.Current!.Name);
    }

    [Fact]
    public void DeLaatsteDagVanDeSprintValtErin()
    {
        // De belangrijkste grens van deze klasse, en hij komt uit een meting: er is
        // "31 augustus 23:59:59" naar DevOps verstuurd en "2026-08-31T00:00:00Z" teruggekomen. Het zijn
        // datums en geen momenten. Zou de einddatum als moment worden gelezen, dan eindigt augustus op 31
        // augustus om middernacht en is de laatste dag van élke maand geen sprintdag — één dag per maand
        // waarop het portaal "er loopt vandaag geen sprint" meldt op een bord waar niets aan de hand is.
        var keuze = SprintSelection.Choose(Maandsprints(), new DateOnly(2026, 8, 31));

        Assert.Equal(SprintState.Current, keuze.State);
        Assert.Equal("2026-08 Augustus", keuze.Current!.Name);
    }

    [Fact]
    public void DeDagNaDeLaatsteValtInDeVolgendeSprint()
    {
        // De aansluiting. Zonder deze test is "inclusief aan beide kanten" ook waar als de grenzen
        // overlappen, en dan zou 1 september in twee sprints vallen — wat Ambiguous zou opleveren op een
        // bord waar niets aan de hand is.
        var keuze = SprintSelection.Choose(Maandsprints(), new DateOnly(2026, 9, 1));

        Assert.Equal(SprintState.Current, keuze.State);
        Assert.Equal("2026-09 September", keuze.Current!.Name);
    }

    [Fact]
    public void GeenEnkeleIteratieIsEenEigenToestand()
    {
        var keuze = SprintSelection.Choose([], Augustusdag);

        Assert.Equal(SprintState.NoIterations, keuze.State);
        Assert.Null(keuze.Current);
        Assert.Empty(keuze.Undated);
        Assert.Equal(0, keuze.DatedCount);
    }

    [Fact]
    public void IteratiesZonderDatumsZijnEenAndereToestandDanGeenIteraties()
    {
        // Dit was de werkelijke stand van het echte bord tot 21 augustus 2026, en hij was stil kapot: de
        // teaminstelling stond op @currentIteration, die macro wordt door datums bepaald, en er was dus
        // helemaal geen huidige sprint — terwijl er wél werk op het bord stond.
        //
        // Een eigen toestand en niet dezelfde als "geen iteraties", want de handeling is een andere:
        // datums invullen tegenover iteraties aanmaken. Zouden ze samenvallen, dan zou het scherm iemand
        // laten zoeken naar iteraties die er al zijn.
        var keuze = SprintSelection.Choose(
            [Zonder("Iteration 1"), Zonder("Iteration 2"), Zonder("Iteration 3")],
            Augustusdag);

        Assert.Equal(SprintState.NoDatedIterations, keuze.State);
        Assert.Null(keuze.Current);
        Assert.Equal(3, keuze.Undated.Count);
        Assert.Equal(0, keuze.DatedCount);
    }

    [Fact]
    public void EenIteratieMetMaarEenDatumIsNietGedateerd()
    {
        // Eén van de twee is niet genoeg, en dat is geen strengheid. Een iteratie met alleen een
        // begindatum heeft geen einde, dus "vandaag valt erin" is voor élke dag na het begin waar — ook
        // over drie jaar. Zo'n iteratie is in de gebruikersinterface van DevOps niet te maken, maar de API
        // kan hem leveren, en dan is het antwoord op "welke sprint loopt nu" onzin in plaats van leeg.
        DevOpsIteration[] iteraties =
        [
            Zonder("Alleen begin") with { Start = new DateOnly(2026, 1, 1) },
            Zonder("Alleen eind") with { Finish = new DateOnly(2026, 12, 31) },
        ];

        var keuze = SprintSelection.Choose(iteraties, Augustusdag);

        Assert.Equal(SprintState.NoDatedIterations, keuze.State);
        Assert.Equal(2, keuze.Undated.Count);
    }

    [Fact]
    public void EenGatInDeKalenderIsGeenStoring()
    {
        // Er zijn periodes en vandaag valt in geen ervan: tussen twee sprints, of een sprint die morgen
        // begint. Dat is een geldige stand van een gezond project en het valt met opzet niet samen met
        // Unknown — "wij hebben het niet kunnen ophalen" en "wij hebben het opgehaald en er loopt nu
        // niets" zijn twee verschillende uitspraken. Zouden ze samenvallen, dan ziet een echte weigering
        // uit als een rustige maand.
        var keuze = SprintSelection.Choose(Maandsprints(), new DateOnly(2026, 7, 15));

        Assert.Equal(SprintState.NoCurrentSprint, keuze.State);
        Assert.Null(keuze.Current);
        Assert.Equal(5, keuze.DatedCount);
    }

    [Fact]
    public void TweeOverlappendePeriodesLeverenGeenSprintOp()
    {
        // Er wordt géén sprint gekozen, en dat is de hele reden dat Ambiguous bestaat. Twee overlappende
        // periodes zijn twee antwoorden op "welke sprint loopt nu", en stil de eerste of de kortste kiezen
        // is een verzonnen antwoord dat op het scherm niet van een juist antwoord te onderscheiden is.
        // Dezelfde soort keuze als bij een geslaagd leeg antwoord van Cost Management: een ambiguïteit die
        // niet op te lossen is hoort zichtbaar te zijn in plaats van weggerekend.
        DevOpsIteration[] iteraties =
        [
            Met("Augustus", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)),
            Met("Sprint 42", new DateOnly(2026, 8, 15), new DateOnly(2026, 9, 15)),
        ];

        var keuze = SprintSelection.Choose(iteraties, Augustusdag);

        Assert.Equal(SprintState.Ambiguous, keuze.State);
        Assert.Null(keuze.Current);
        Assert.Equal(2, keuze.Overlapping.Count);
        Assert.Equal(2, keuze.DatedCount);
    }

    [Fact]
    public void DeOverlappendeIteratiesKomenMetNaamTerug()
    {
        // Zonder die namen is de melding "er lopen meerdere periodes" niet te gebruiken: de handeling
        // erachter is de periodes corrigeren, en dan moet je weten welke. Alleen de overlappende komen
        // terug en niet alle gedateerde — een lijst met alles erin zou de operator laten zoeken.
        DevOpsIteration[] iteraties =
        [
            Met("Juli", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)),
            Met("Augustus", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)),
            Met("Sprint 42", new DateOnly(2026, 8, 15), new DateOnly(2026, 9, 15)),
        ];

        var keuze = SprintSelection.Choose(iteraties, Augustusdag);

        Assert.Equal(
            ["Augustus", "Sprint 42"],
            keuze.Overlapping.Select(iteratie => iteratie.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EenIteratieDieOpEenDagBeginEnEindigtIsEenGeldigeSprint()
    {
        // Een sprint van één dag. Klinkt als een randgeval en is het niet: een iteratie die per ongeluk
        // dezelfde begin- en einddatum heeft gekregen is precies het geval waarop een exclusieve
        // vergelijking ("start < vandaag < finish") niets vindt — en dan meldt het portaal "geen sprint"
        // op een bord dat er wél een heeft.
        var keuze = SprintSelection.Choose(
            [Met("Eén dag", Augustusdag, Augustusdag)],
            Augustusdag);

        Assert.Equal(SprintState.Current, keuze.State);
        Assert.Equal("Eén dag", keuze.Current!.Name);
    }

    [Fact]
    public void DeIteratiesZonderDatumsKomenBijElkeToestandTerug()
    {
        // Ook bij een gezonde huidige sprint, en dat is het punt: juist dán is "er valt werk buiten elke
        // periode" iets wat niemand anders zegt. Een weergave die die lijst alleen bij een lege sprint zou
        // tonen, biedt bij een volle sprint een onvolledig beeld aan als volledig.
        DevOpsIteration[] iteraties =
        [
            Met("Augustus", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)),
            Zonder("Iteration 1"),
        ];

        var lopend = SprintSelection.Choose(iteraties, Augustusdag);
        var gat = SprintSelection.Choose(iteraties, new DateOnly(2026, 7, 1));

        Assert.Equal(SprintState.Current, lopend.State);
        Assert.Single(lopend.Undated);

        Assert.Equal(SprintState.NoCurrentSprint, gat.State);
        Assert.Single(gat.Undated);
    }

    [Fact]
    public void ErIsPreciesEenToestandMetEenSprint()
    {
        // De invariant en niet zijn gevolg: Current dan en slechts dan als er een sprint is. Zonder deze
        // test kan een toekomstige tak een sprint teruggeven bij een toestand die zegt dat er geen is —
        // en dan rendert het scherm de statistieken van een sprint onder de mededeling dat er geen is.
        DateOnly[] dagen =
        [
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            new DateOnly(2027, 1, 1),
        ];

        DevOpsIteration[][] borden =
        [
            [],
            [Zonder("Iteration 1")],
            Maandsprints(),
            [
                Met("A", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)),
                Met("B", new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 20)),
            ],
        ];

        foreach (var bord in borden)
        {
            foreach (var dag in dagen)
            {
                var keuze = SprintSelection.Choose(bord, dag);

                Assert.Equal(keuze.State == SprintState.Current, keuze.Current is not null);
            }
        }
    }
}
