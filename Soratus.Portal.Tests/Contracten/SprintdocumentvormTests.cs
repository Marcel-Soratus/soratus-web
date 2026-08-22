using Soratus.Portal.Sprints;

namespace Soratus.Portal.Tests.Contracten;

/// <summary>
/// De omzetting van een sprintlezing naar het document dat in de opslag komt.
/// </summary>
/// <remarks>
/// <para><strong>Dit bestand bestaat door een mutatie die niets rood maakte.</strong> Het weghalen van de
/// iteraties zonder datums uit <c>CosmosSprintCollectorStore.ToDocument</c> bleef groen: de collectortests
/// meten wat er in de <see cref="SprintWrite"/> staat en de schermtests meten wat er uit een
/// <see cref="SprintDocument"/> komt, en niemand keek naar de stap ertussen. Dat is precies het gat dat
/// punt 41 als echt gat noteert voor de klantdocumentmapping — en het is hier goedkoop te dichten, want
/// <c>ToDocument</c> is met opzet <c>internal static</c> en puur gehouden zodat een test de
/// <em>productiemapping</em> kan aanroepen in plaats van hem na te bouwen.</para>
///
/// <para>Wat deze tests níet dekken is de aanroep aan Cosmos eromheen: de upsert, de partitiesleutel die
/// meegaat, en de puntlezing. Die praten met de opslag en hebben geen test — hetzelfde eerlijke gat als bij
/// <c>CosmosAzureCostCollectorStore</c>. Wat er wél is: de mapping die dáár tussen zit, en dat was de enige
/// helft die zonder Cosmos te meten viel.</para>
/// </remarks>
public class SprintdocumentvormTests
{
    /// <summary>De gezaaide sprint.</summary>
    private static DevOpsIteration Augustus() => new()
    {
        Id = "2de79897-d29b-47f9-b6d0-fff5493a6e1a",
        Name = "2026-08 Augustus",
        Path = @"Acme Logistiek\2026-08 Augustus",
        Start = new DateOnly(2026, 8, 1),
        Finish = new DateOnly(2026, 8, 31),
    };

    /// <summary>Een volledige lezing.</summary>
    private static SprintWrite Lezing() => new(
        "acme-logistiek",
        SprintState.Current,
        "/soratus/Acme Logistiek/Acme Logistiek Team",
        new DateTimeOffset(2026, 8, 22, 9, 14, 0, TimeSpan.Zero),
        Augustus(),
        [
            new SprintWorkItem
            {
                Id = 4571,
                Type = "Task",
                Title = "Declaratieregels valideren",
                State = "Active",
                Stage = WorkItemStage.InProgress,
                RemainingWork = 6.5m,
            },
        ],
        [new SprintIterationRef { Name = "Iteration 1", Path = @"Acme Logistiek\Iteration 1" }],
        [new SprintIterationRef { Name = "Sprint 42", Path = @"Acme Logistiek\Sprint 42" }],
        DatedCount: 5,
        Failure: null);

    [Fact]
    public void ElkVeldVanDeLezingKomtInHetDocumentTerecht()
    {
        var document = CosmosSprintCollectorStore.ToDocument(Lezing());

        Assert.Equal(SprintDocumentKeys.Id, document.Id);
        Assert.Equal(SprintDocumentKeys.Kind, document.Kind);
        Assert.Equal("acme-logistiek", document.PartitionKey);
        Assert.Equal("acme-logistiek", document.CustomerId);
        Assert.Equal(SprintState.Current, document.State);
        Assert.Equal("/soratus/Acme Logistiek/Acme Logistiek Team", document.Scope);
        Assert.Equal(new DateTimeOffset(2026, 8, 22, 9, 14, 0, TimeSpan.Zero), document.ReadAt);
        Assert.Equal("2de79897-d29b-47f9-b6d0-fff5493a6e1a", document.SprintId);
        Assert.Equal("2026-08 Augustus", document.SprintName);
        Assert.Equal(@"Acme Logistiek\2026-08 Augustus", document.BoardPath);
        Assert.Equal(5, document.DatedCount);
        Assert.Null(document.Failure);

        Assert.Equal(4571, Assert.Single(document.Items).Id);
        Assert.Equal("Iteration 1", Assert.Single(document.Undated).Name);
        Assert.Equal("Sprint 42", Assert.Single(document.Overlapping).Name);
    }

    [Fact]
    public void DeTweeIteratielijstenWordenNietVerwisseldEnNietWeggelaten()
    {
        // De mutatie die dit bestand heeft doen ontstaan: `Undated = []` in de mapping maakte niets rood.
        // Ze staan naast elkaar, hebben hetzelfde type en dezelfde twee velden — precies de vorm waarin een
        // copy-paste er één laat vallen of ze verwisselt, zonder dat de collectortests of de schermtests er
        // iets van merken.
        var document = CosmosSprintCollectorStore.ToDocument(Lezing());

        Assert.Equal(@"Acme Logistiek\Iteration 1", Assert.Single(document.Undated).Path);
        Assert.Equal(@"Acme Logistiek\Sprint 42", Assert.Single(document.Overlapping).Path);
    }

    [Fact]
    public void DeDatumsGaanAlsDagNaarDeOpslagEnNietAlsMoment()
    {
        // Een dag is geen moment, en een dag die als moment wordt opgeslagen krijgt een tijdzone die er niet
        // bij hoort. Gemeten: DevOps laat de tijd van een iteratiedatum vallen.
        var document = CosmosSprintCollectorStore.ToDocument(Lezing());

        Assert.Equal("2026-08-01", document.Start);
        Assert.Equal("2026-08-31", document.Finish);
    }

    [Fact]
    public void ZonderSprintStaanDeSprintveldenLeegEnNietOpEenVerzonnenWaarde()
    {
        // Vijf van de zes toestanden hebben geen sprint. Dan hoort er geen naam, geen pad en geen periode te
        // staan — en zeker geen datum van vandaag, want dat is een periode die niemand heeft ingevuld.
        var document = CosmosSprintCollectorStore.ToDocument(
            Lezing() with
            {
                State = SprintState.NoDatedIterations,
                Sprint = null,
                Items = [],
                Overlapping = [],
                DatedCount = 0,
            });

        Assert.Equal(SprintState.NoDatedIterations, document.State);
        Assert.Null(document.SprintId);
        Assert.Null(document.SprintName);
        Assert.Null(document.BoardPath);
        Assert.Null(document.Start);
        Assert.Null(document.Finish);
        Assert.Empty(document.Items);

        // En de iteraties zonder datums blijven wél staan: dat is juist de mededeling die bij deze toestand
        // hoort.
        Assert.Single(document.Undated);
    }

    [Fact]
    public void EenOnleesbareLezingDraagtDeRedenEnGeenSprint()
    {
        var document = CosmosSprintCollectorStore.ToDocument(
            new SprintWrite(
                "acme-logistiek",
                SprintState.Unknown,
                "/soratus/Acme Logistiek/Acme Logistiek Team",
                new DateTimeOffset(2026, 8, 22, 9, 14, 0, TimeSpan.Zero),
                Sprint: null,
                Items: [],
                Undated: [],
                Overlapping: [],
                DatedCount: 0,
                "Het portaal mag dit bord niet lezen."));

        Assert.Equal(SprintState.Unknown, document.State);
        Assert.Equal("Het portaal mag dit bord niet lezen.", document.Failure);
        Assert.Null(document.SprintName);

        // De bevraagde scope gaat óók bij een mislukking mee. Dat is het enige gereedschap waarmee een
        // operator kan uitsluiten dat we het verkeerde bord bevragen.
        Assert.Equal("/soratus/Acme Logistiek/Acme Logistiek Team", document.Scope);
    }

    [Fact]
    public void HetDocumentHeeftGeenStatistieken()
    {
        // Geen vergeten veld maar een invariant: de statistieken zijn de som over de items en bestaan alleen
        // als afgeleide. Een opgeslagen aantal dat de lijst tegenspreekt is een tweede waarheid, en de
        // verkeerde van de twee zou degene zijn die niemand bijwerkt.
        //
        // Reflectie en geen assertie op één naam: zo valt élk statistiekveld op dat iemand er later bij zet.
        string[] verboden = ["Items Count", "Completed", "Blocked", "OpenHours", "DoneHours", "StoryPoints", "Tally"];

        var namen = typeof(SprintDocument)
            .GetProperties()
            .Select(eigenschap => eigenschap.Name)
            .ToArray();

        foreach (var naam in verboden)
        {
            Assert.DoesNotContain(naam, namen);
        }
    }
}
