using Microsoft.Extensions.Logging.Abstractions;
using Soratus.Portal.Support;
using Soratus.Portal.Tests.Hulpmiddelen;
using Soratus.Support.FirstLine;

namespace Soratus.Portal.Tests.Eerstelijn;

/// <summary>
/// Een kiezer die teruggeeft wat een test hem opdraagt, en bewaart wat hij kreeg.
/// </summary>
/// <remarks>
/// Wat hij kreeg bewaren is de helft van de meting: de vraag hoort de vraag van de klant te zijn, en
/// de feiten horen in de volgorde van het portaal te staan. Een kiezer die alleen antwoordt, meet de
/// heenweg niet.
/// </remarks>
internal sealed class Vastekiezer(Func<FirstLineQuestion, FirstLineChoice?> keuze) : IFirstLineChooser
{
    /// <summary>De laatste vraag die is voorgelegd.</summary>
    internal FirstLineQuestion? Gekregen { get; private set; }

    public Task<FirstLineChoice?> ChooseAsync(
        FirstLineQuestion question,
        CancellationToken cancellationToken = default)
    {
        Gekregen = question;

        return Task.FromResult(keuze(question));
    }
}

/// <summary>
/// De brug tussen de naad van het portaal en de kiezer buiten deze assembly.
/// </summary>
/// <remarks>
/// <para><strong>Dit bestand meet de fout die dit ontwerp erbij heeft gekregen.</strong> §46 maakte
/// een verzonnen feit onmogelijk; wat er in de plaats is gekomen is een <em>plaats in een lijst</em>,
/// en een plaats kan er één naast zitten. Zo'n fout is stil: het antwoord heeft een geldige vorm, er
/// staat een bronregel onder, en niemand ziet dat die bron bij een ander feit hoort. En het is óns
/// fout en niet die van het model.</para>
///
/// <para><strong>De grondslagen in deze tests zijn onderscheidbaar, met opzet.</strong> Drie
/// agentstatussen die op elkaar lijken meten niets — dat is de les van de twee streepjes die elkaars
/// afwezigheid dekten. Hier zijn het drie verschillende agents met drie verschillende statussen, dus
/// een herordening en een af-één-fout leveren beide een ander feit op dan het feit dat er hoort te
/// staan.</para>
/// </remarks>
public class EerstelijnbrugTests
{
    /// <summary>
    /// Drie grondslagen, in de volgorde waarin het portaal ze aanbiedt.
    /// </summary>
    /// <remarks>
    /// <para>Drie onderscheidbare feiten, en dat is de voorwaarde waaronder deze tests iets meten:
    /// bij drie feiten die op elkaar lijken blijft een af-één-fout groen.</para>
    ///
    /// <para><strong>En ze staan met opzet niet in alfabetische volgorde.</strong> Dat is door de
    /// mutatieronde gevonden en niet bedacht: de eerste opzet had drie feiten die begonnen met "De
    /// agent", "In juli" en "Over juni", en die stonden per ongeluk al gesorteerd. Een mutatie die de
    /// lijst sorteerde vóórdat de feiten werden opgebouwd, maakte daardoor niets rood — de sortering
    /// was een no-op op precies deze gegevens. Dat is dezelfde klasse fout als de twee streepjes die
    /// elkaars afwezigheid dekten, en hij zat in mijn eigen fixture. Zie
    /// <see cref="DeFeitenInDezeTestsStaanNietInAlfabetischeVolgorde"/>, die dat nu vasthoudt.</para>
    /// </remarks>
    private static SupportEnquiry Drie() => new()
    {
        Question = "Draait mijn factuur-import nog?",
        Grounds =
        [
            VasteSupportweergaven.Grondslag(
                SupportGroundKind.AgentStatus,
                "voorraad-sync",
                "De agent voorraad-sync heeft de status Live. Laatste run 2 minuten geleden."),
            VasteSupportweergaven.Grondslag(
                SupportGroundKind.AgentStatus,
                "factuur-import",
                "De agent factuur-import heeft de status Mislukt. Laatste run 3 minuten geleden."),
            VasteSupportweergaven.Grondslag(
                SupportGroundKind.Hours,
                "2026-07",
                "In juli 2026 staan 12 gefiatteerde uren op een bundel van 20."),
        ],
    };

    [Fact]
    public void DeFeitenInDezeTestsStaanNietInAlfabetischeVolgorde()
    {
        // Een test op de fixture, en die staat hier omdat de mutatieronde hem heeft afgedwongen. De
        // volgorde van de aangeboden grondslagen is de volgorde van het portaal (agents, dan uren, dan
        // facturatie — zie SupportDesk.GroundsAsync) en die is nooit alfabetisch. Zijn deze drie
        // feiten dat per ongeluk wél, dan is een mutatie die de lijst sorteert onzichtbaar, en dan
        // meten de tests eronder minder dan ze lijken te meten.
        var feiten = Drie().Grounds.Select(grondslag => grondslag.Fact).ToArray();

        Assert.NotEqual(
            feiten.OrderBy(feit => feit, StringComparer.Ordinal).ToArray(),
            feiten);
    }

    private static ChoosingFirstLine Brug(Vastekiezer kiezer) =>
        new(kiezer, NullLogger<ChoosingFirstLine>.Instance);

    // ── De heenweg ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeKiezerKrijgtDeVraagEnDeFeitenInDeVolgordeVanHetPortaal()
    {
        var vraag = Drie();
        var kiezer = new Vastekiezer(_ => FirstLineChoice.ToAHuman(FirstLineHandoff.NotSure));

        await Brug(kiezer).AnswerAsync(vraag);

        Assert.Equal(vraag.Question, kiezer.Gekregen?.Text);
        Assert.Equal(
            vraag.Grounds.Select(grondslag => grondslag.Fact).ToArray(),
            kiezer.Gekregen?.Facts.ToArray());
    }

    [Fact]
    public async Task DeKiezerKrijgtGeenGrondslagEnGeenLabelMaarAlleenHetFeit()
    {
        // De heenweg is platte tekst. Wie een sleutel meestuurt, stuurt iets mee waarmee de andere kant
        // kan gaan zoeken — en de andere kant hóórt niets te kunnen opvragen.
        var eigenschappen = typeof(FirstLineQuestion)
            .GetProperties()
            .Select(eigenschap => eigenschap.PropertyType)
            .ToArray();

        Assert.All(eigenschappen, type => Assert.NotEqual(typeof(SupportGround), type));

        Assert.DoesNotContain(
            typeof(FirstLineQuestion).GetProperties().Select(p => p.Name),
            naam => naam is "Key" or "Label" or "CustomerId" or "Kind");
    }

    // ── De terugweg: de plaats wordt in dezelfde lijst teruggelezen ─────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task EenGekozenPlaatsWijstNaarPreciesDatFeit(int plaats)
    {
        // De randen zitten erin: de eerste en de laatste plaats. Een af-één-fout of een omgekeerde
        // lijst levert hier een ánder feit op, en de feiten zijn met opzet onderscheidbaar.
        var vraag = Drie();
        var kiezer = new Vastekiezer(_ => FirstLineChoice.Fact(plaats));

        var antwoord = await Brug(kiezer).AnswerAsync(vraag);

        Assert.NotNull(antwoord);
        Assert.Equal(vraag.Grounds[plaats], antwoord.Ground);
        Assert.Equal(vraag.Grounds[plaats].Fact, antwoord.Ground?.Fact);
        Assert.Null(antwoord.Escalation);
    }

    [Fact]
    public async Task DeAangewezenGrondslagIsErEenDieIsAangebodenEnWordtDusAangenomen()
    {
        // De meting op de invariant en niet op het gevolg: CosmosSupportStore.Accept is de plek waar de
        // acceptatie-eis van fase 5 wordt afgedwongen, en die vergelijkt op waarde. Een brug die een
        // grondslag zou opbouwen in plaats van er een aan te wijzen, valt hier om.
        var vraag = Drie();
        var kiezer = new Vastekiezer(_ => FirstLineChoice.Fact(1));

        var antwoord = await Brug(kiezer).AnswerAsync(vraag);

        Assert.Equal(vraag.Grounds[1], CosmosSupportStore.Accept(vraag, antwoord));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(60)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public async Task EenPlaatsBuitenDeLijstIsEenEscalatieEnGeenAfkapping(int plaats)
    {
        // Geen clamp. Afkappen naar de dichtstbijzijnde geldige plaats zou een plausibel verkeerd feit
        // kiezen — een antwoord met een bronregel eronder die er niet bij hoort — en dat is precies de
        // fout die dit hele ontwerp onmogelijk wil maken.
        //
        // En geen uitzondering: SupportDesk vangt alles op en maakt er hetzelfde van, maar dan met een
        // stacktrace die suggereert dat er iets stuk is in plaats van dat het model buiten de lijst wees.
        var vraag = Drie();
        var kiezer = new Vastekiezer(_ => FirstLineChoice.Fact(plaats));

        var antwoord = await Brug(kiezer).AnswerAsync(vraag);

        Assert.Equal(SupportEscalation.AnswerNotUsable, antwoord?.Escalation);
        Assert.Null(antwoord?.Ground);
    }

    [Fact]
    public async Task ZonderAangebodenFeitenLevertElkePlaatsEenEscalatie()
    {
        var leeg = new SupportEnquiry { Question = "Hoe staat het ervoor?", Grounds = [] };
        var kiezer = new Vastekiezer(_ => FirstLineChoice.Fact(0));

        var antwoord = await Brug(kiezer).AnswerAsync(leeg);

        Assert.Equal(SupportEscalation.AnswerNotUsable, antwoord?.Escalation);
    }

    // ── De overdracht ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(FirstLineHandoff.NotSure, SupportEscalation.NotSure)]
    [InlineData(FirstLineHandoff.OutsideTheData, SupportEscalation.OutsideTheData)]
    [InlineData(FirstLineHandoff.NeedsAHuman, SupportEscalation.NeedsAHuman)]
    public async Task ElkeOverdrachtHeeftZijnEigenEscalatiereden(
        FirstLineHandoff overdracht,
        SupportEscalation reden)
    {
        // Alle drie, zodat een mapping die twee waarden op dezelfde reden legt hier omvalt. De reden
        // komt nooit als woord bij de klant — de zin is voor alle vier dezelfde — maar de operator
        // leest hem, en een reden die op een andere reden uitkomt is een verkeerd verslag.
        var kiezer = new Vastekiezer(_ => FirstLineChoice.ToAHuman(overdracht));

        var antwoord = await Brug(kiezer).AnswerAsync(Drie());

        Assert.Equal(reden, antwoord?.Escalation);
        Assert.Null(antwoord?.Ground);
    }

    [Fact]
    public async Task DeVierdeEscalatieredenIsNietVanDeKiezer()
    {
        // AnswerNotUsable is het oordeel van het portaal en niet van de eerstelijn (§46.9). Er is dus
        // geen overdracht die hem kan zetten — de enum aan de andere kant heeft er drie — en dat is met
        // een test vast te leggen in plaats van met een opmerking.
        Assert.Equal(3, Enum.GetValues<FirstLineHandoff>().Length);
        Assert.Equal(4, Enum.GetValues<SupportEscalation>().Length);

        var redenen = new List<SupportEscalation?>();

        foreach (var overdracht in Enum.GetValues<FirstLineHandoff>())
        {
            var antwoord = await Brug(new Vastekiezer(_ => FirstLineChoice.ToAHuman(overdracht)))
                .AnswerAsync(Drie());

            redenen.Add(antwoord?.Escalation);
        }

        Assert.DoesNotContain(SupportEscalation.AnswerNotUsable, redenen);
        Assert.Equal(3, redenen.Distinct().Count());
    }

    [Fact]
    public async Task GeenKeuzeLevertGeenAntwoordEnGeenEigenReden()
    {
        // null betekent op deze naad hetzelfde als AnswerNotUsable, en de opslag maakt daar één ding
        // van. De brug verzint er dus geen reden bij: waarom het niet lukte staat in de logregel van de
        // kiezer, waar het thuishoort.
        var antwoord = await Brug(new Vastekiezer(_ => null)).AnswerAsync(Drie());

        Assert.Null(antwoord);
    }

    // ── Wat de brug niet kan ────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeBrugHeeftGeenModelGeenEndpointEnGeenPrompt()
    {
        // Het enige stuk van de eerstelijn dat binnen Soratus.Portal staat, weet niets van een model.
        // Deze meting is een broncodecontrole, want "hij gebruikt het niet" is niet met gedrag te meten.
        // Zonder de opmerkingen, en dat is niet om de test te laten slagen: de eerste versie viel om op
        // haar eigen documentatie ("geen prompt, geen HTTP"), en dat is precies de klasse fout waar dit
        // project een naam voor heeft — een assertie die iets meet dat er ook staat als het klopt. Wat
        // hier gemeten wordt is de code, en de code hoort deze woorden niet te kennen.
        var bron = string.Join(
            '\n',
            File.ReadAllLines(
                    Path.Combine(Broncode.Portaalproject.FullName, "Support", "ChoosingFirstLine.cs"))
                .Where(regel => !regel.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        foreach (var verboden in new[]
        {
            "HttpClient", "openai", "api-version", "gpt-", "temperature", "TokenCredential", "prompt",
            "Endpoint", "Deployment",
        })
        {
            Assert.DoesNotContain(verboden, bron, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DeKiezerskantKanGeenGrondslagMaken()
    {
        // De kern van §47.1: het project buiten deze assembly verwijst niet naar Soratus.Portal en kent
        // SupportGround dus niet eens. Een verzonnen feit is daarmee niet afgeschermd maar niet uit te
        // drukken. Gemeten op de verwijzingen van de assembly zelf en niet op de csproj, want dat is wat
        // er werkelijk in de container staat.
        var verwijzingen = typeof(IFirstLineChooser).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToArray();

        Assert.DoesNotContain("Soratus.Portal", verwijzingen);
    }
}
