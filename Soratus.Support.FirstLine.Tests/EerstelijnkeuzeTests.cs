using System.Reflection;

namespace Soratus.Support.FirstLine.Tests;

/// <summary>
/// De opdracht aan het model en het teruglezen van zijn antwoord.
/// </summary>
/// <remarks>
/// <para><strong>Hier zit de nieuwe zwakke plek van dit ontwerp, en daarom staat hij vol randgevallen.
/// </strong> De vorm van §46 maakt een verzonnen feit onmogelijk; wat er in de plaats is gekomen is
/// een <em>nummer</em>, en een nummer kan er één naast zitten. Zo'n fout is stil: het antwoord heeft
/// een geldige vorm, er staat een bronregel onder, en niemand ziet dat de bron bij een ander feit
/// hoort. De nummering vanaf één bestaat alleen binnen <c>FirstLinePrompt</c>, en dit is de plek waar
/// dat gemeten wordt.</para>
///
/// <para>De feiten in deze tests zijn met opzet <em>onderscheidbaar</em> ("eerste", "tweede", …). Drie
/// feiten die op elkaar lijken meten niets — dat is de les van de twee streepjes die elkaars
/// afwezigheid dekten.</para>
/// </remarks>
public class EerstelijnkeuzeTests
{
    private static readonly FirstLineQuestion Drie = new()
    {
        Text = "Hoe staat mijn agent ervoor?",
        Facts = ["het eerste feit", "het tweede feit", "het derde feit"],
    };

    // ── De vorm van de keuze ────────────────────────────────────────────────────────────────────

    [Fact]
    public void EenKeuzeHeeftGeenEnkelTekstveld()
    {
        // Dezelfde meting als op SupportAnswer in het portaal, hier een laag dieper. Zodra er een
        // string-eigenschap op dit type staat, kan het model een zin sturen — en dan is "hij kan geen
        // bedrag verzinnen" niet meer waar aan deze kant van de naad.
        var tekstvelden = typeof(FirstLineChoice)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name)
            .ToArray();

        Assert.True(
            tekstvelden.Length == 0,
            "FirstLineChoice heeft een tekstveld: " + string.Join(", ", tekstvelden) + ". Daarmee kan "
            + "een model een feit teruggeven in plaats van er een aan te wijzen, en dan is de "
            + "acceptatie-eis van fase 5 een instructie aan een model geworden in plaats van een vorm.");
    }

    [Fact]
    public void EenKeuzeIsAlleenViaDeTweeFabriekenTeMaken()
    {
        Assert.Empty(typeof(FirstLineChoice).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        var fabrieken = typeof(FirstLineChoice)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(FirstLineChoice))
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Fact", "ToAHuman"], fabrieken);
    }

    [Fact]
    public void DeOverdrachtHeeftDrieRedenenEnGeenVierde()
    {
        // Het portaal kent er vier; de vierde (AnswerNotUsable) is het oordeel van het portaal en niet
        // van het model. Deze test is het vangnet onder de mapping in ChoosingFirstLine: die valt bij
        // een onbekende waarde terug op NotSure, en zonder deze meting zou een vierde waarde daar stil
        // op landen in plaats van iemand langs die mapping te sturen.
        Assert.Equal(3, Enum.GetValues<FirstLineHandoff>().Length);
        Assert.Equal(FirstLineHandoff.NotSure, default);
    }

    // ── De opdracht ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeFeitenWordenGenummerdVanafEenInDeVolgordeVanHetPortaal()
    {
        var tekst = FirstLinePrompt.User(Drie);

        Assert.Contains("1. het eerste feit", tekst, StringComparison.Ordinal);
        Assert.Contains("2. het tweede feit", tekst, StringComparison.Ordinal);
        Assert.Contains("3. het derde feit", tekst, StringComparison.Ordinal);
        Assert.DoesNotContain("0. ", tekst, StringComparison.Ordinal);

        // De volgorde is de betekenis: een keuze is een plaats in deze lijst.
        Assert.True(
            tekst.IndexOf("1. het eerste", StringComparison.Ordinal)
            < tekst.IndexOf("2. het tweede", StringComparison.Ordinal),
            "De feiten staan niet in de volgorde van het portaal in de prompt. Dan wijst een nummer "
            + "naar een ander feit dan het portaal bij dat nummer terugleest.");
    }

    [Fact]
    public void DeVraagVanDeKlantStaatErzelfInEnWordtNietOmschreven()
    {
        Assert.Contains(Drie.Text, FirstLinePrompt.User(Drie), StringComparison.Ordinal);
    }

    [Fact]
    public void DeOpdrachtNoemtJsonWantDatEistDeDienst()
    {
        // Zonder het woord JSON in de prompt weigert response_format: json_object het verzoek. Dat is
        // een eis van de dienst en geen stijl, dus hij hoort gemeten te worden en niet onthouden.
        Assert.Contains("JSON", FirstLinePrompt.System, StringComparison.Ordinal);
    }

    [Fact]
    public void DeOpdrachtNoemtGeenModelEnGeenKlant()
    {
        // §46.9: in deze code staat geen modelnaam, want dat is een configuratiewaarde. En er staat
        // geen klantgegeven in de systeemopdracht: die is voor elke klant dezelfde tekst.
        Assert.DoesNotContain("gpt", FirstLinePrompt.System, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("soratus-prod", FirstLinePrompt.System, StringComparison.OrdinalIgnoreCase);
    }

    // ── Het teruglezen ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("{\"kies\": 1}", 0)]
    [InlineData("{\"kies\": 2}", 1)]
    [InlineData("{\"kies\": 3}", 2)]
    [InlineData("{\"kies\": \"3\"}", 2)]
    public void EenNummerWordtEenNulgebaseerdePlaats(string json, int plaats)
    {
        // De enige plek waar de nummering vanaf één en de index vanaf nul elkaar raken. Vier gevallen,
        // waarvan de eerste en de laatste de randen zijn: nummer 1 hoort het eerste feit te zijn en
        // nummer 3 het derde, en niet het tweede of het vierde.
        var keuze = FirstLinePrompt.Read(json);

        Assert.Equal(plaats, keuze?.Index);
        Assert.Null(keuze?.Handoff);
    }

    [Theory]
    [InlineData("{\"kies\": 0}")]
    [InlineData("{\"kies\": -1}")]
    public void EenNummerOnderEenIsEenOverdrachtEnGeenFout(string json)
    {
        // Een model dat "0" antwoordt bedoelt "geen van deze". Dat is precies NotSure, en het is geen
        // leesfout: null zou zeggen dat wij het antwoord niet begrepen.
        Assert.Equal(FirstLineHandoff.NotSure, FirstLinePrompt.Read(json)?.Handoff);
    }

    [Fact]
    public void EenNummerBovenHetAantalFeitenKomtHierWelDoor()
    {
        // Met opzet: het bereik hoort bij de lijst, en de lijst is van het portaal. Zou deze klasse
        // het aantal feiten kennen en er hier op toetsen, dan lag hetzelfde oordeel op twee plekken en
        // dekten die twee elkaars afwezigheid (punt 41). ChoosingFirstLine maakt hier een escalatie
        // van; zie EerstelijnbrugTests in Soratus.Portal.Tests.
        Assert.Equal(98, FirstLinePrompt.Read("{\"kies\": 99}")?.Index);
    }

    [Theory]
    [InlineData("buitenDeGegevens", FirstLineHandoff.OutsideTheData)]
    [InlineData("geenFeit", FirstLineHandoff.NeedsAHuman)]
    [InlineData("nietZeker", FirstLineHandoff.NotSure)]
    [InlineData("iets wat wij niet kennen", FirstLineHandoff.NotSure)]
    public void EenOverdrachtWordtGelezenEnEenOnbekendWoordIsNietZeker(string woord, FirstLineHandoff reden)
    {
        // Het onbekende woord is het interessante geval: het model wilde overdragen — dat deel is
        // duidelijk — en van de drie redenen is NotSure de enige die niets beweert.
        Assert.Equal(reden, FirstLinePrompt.Read($"{{\"overdracht\": \"{woord}\"}}")?.Handoff);
    }

    [Fact]
    public void EenAntwoordMetBeideVormenLeestAlsOverdracht()
    {
        // Bij twee waarheden wint de terughoudende, dezelfde regel als "niet ingericht gaat vóór
        // proefdraai" bij de mail. Een antwoord met beide is een antwoord dat we niet begrijpen, en
        // dan is niet-antwoorden de goede uitkomst.
        var keuze = FirstLinePrompt.Read("{\"kies\": 2, \"overdracht\": \"nietZeker\"}");

        Assert.Null(keuze?.Index);
        Assert.Equal(FirstLineHandoff.NotSure, keuze?.Handoff);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("dit is geen json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{\"iets anders\": 3}")]
    [InlineData("{\"kies\": \"twee\"}")]
    [InlineData("{\"kies\": null}")]
    [InlineData("{\"overdracht\": \"\"}")]
    public void WatNietTeLezenIsLevertNietsOp(string json)
    {
        // null is iets anders dan een overdracht: een overdracht is een besluit van de eerstelijn,
        // null is een storing bij ons. De klant leest in beide gevallen dezelfde zin; de operator ziet
        // het verschil in de logregel, en daar heeft hij het nodig.
        Assert.Null(FirstLinePrompt.Read(json));
    }
}
