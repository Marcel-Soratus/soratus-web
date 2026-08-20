using System.Reflection;
using Bunit;
using Soratus.Portal.Components.Pages;
using Soratus.Portal.Tests.Hulpmiddelen;
using Soratus.Portal.Views;

namespace Soratus.Portal.Tests.Zichtbaarheid;

/// <summary>
/// De tweede toestand van een toegang: onbekend, actief of ontbrekend — drie waarden en geen twee.
/// </summary>
/// <remarks>
/// <para>Een toegang bestaat uit twee helften. De ene legt dit portaal vast (het toegangsdocument);
/// de andere is de app-roltoewijzing in Entra ID, en die blijft handwerk — er is precies één
/// Graph-permissie waarmee een app app-rollen kan toekennen, die is niet tot één app te beperken, en
/// een gecompromitteerd portaal zou daarmee de tenant kunnen overnemen. Het portaal krijgt hem dus
/// niet, en heeft ook geen leesrecht om te controleren of iemand anders het heeft gedaan.</para>
///
/// <para><strong>Daarom is dit geen <c>bool</c>.</strong> Twee toestanden zouden "wij weten het
/// niet" en "die persoon kan niet naar binnen" op één waarde laten vallen, en dat zijn twee
/// verschillende mededelingen aan twee verschillende lezers. De eerste is een beperking van ons
/// portaal, de tweede een reden om iemand te bellen. Deze tests leggen vast dat het verschil
/// bestaat, dat het op het scherm te zien is, en dat "onbekend" nergens als "niet uitgenodigd"
/// wordt gerenderd — dat laatste is precies de stille onwaarheid die het ontwerp vermijdt.</para>
/// </remarks>
public class EntratoestandTests : Portaalrendertest
{
    private static Type Contractpagina =>
        Paginaverzameling.MetRoute("/klant/{Slug}/contract")
        ?? throw new InvalidOperationException(
            "Er staat geen pagina op route '/klant/{Slug}/contract'. Is de route hernoemd, dan " +
            "hoort deze test mee te verhuizen — niet te verdwijnen.");

    /// <summary>De woorden waarmee het portaal geen van de drie toestanden mag beschrijven.</summary>
    /// <remarks>
    /// Alle drie beweren iets wat het portaal niet weet: dat de uitnodiging nog moet komen, dat er
    /// op iets wordt gewacht, of dat iemand wel of niet kan aanmelden. Ze staan hier als lijst en
    /// niet als één zin, want de eerste opzet van dit scherm had er een veld
    /// <c>uitnodigingVerstuurd</c> voor — een toestand die niets in het portaal ooit zou vullen, dus
    /// zou het scherm "wacht op uitnodiging" blijven zeggen ook nadat iemand het had gedaan.
    /// </remarks>
    public static readonly string[] VerbodenBeweringen =
    [
        "niet uitgenodigd",
        "nog niet uitgenodigd",
        "wacht op uitnodiging",
        "uitnodiging verstuurd",
    ];

    [Fact]
    public void DeDrieToestandenHebbenDrieVerschillendeWoorden()
    {
        // Als twee toestanden hetzelfde woord krijgen is het onderscheid weg zonder dat het type
        // verandert — en dan is de enum een bool met een omweg.
        var woorden = Enum.GetValues<AccessEntraState>()
            .Select(ContractText.AccessState)
            .ToArray();

        Assert.Equal(3, woorden.Length);
        Assert.Equal(woorden.Length, woorden.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(woorden, string.IsNullOrWhiteSpace);
    }

    [Fact]
    public void DeDrieToestandenHebbenDrieVerschillendeUitlegregels()
    {
        // Het woord staat in een kolom van 96px; wat het betekent staat in de tooltip. Zijn die
        // tooltips gelijk, dan is het woord het enige onderscheid en is de uitleg geen uitleg.
        var uitleg = Enum.GetValues<AccessEntraState>()
            .Select(ContractText.AccessStateTitle)
            .ToArray();

        Assert.Equal(uitleg.Length, uitleg.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(uitleg, string.IsNullOrWhiteSpace);
    }

    [Fact]
    public void OnbekendZegtDatHetPortaalHetNietWeetEnNietDatIemandNietIsUitgenodigd()
    {
        // De kern van deze hele enum. "Onbekend" is een uitspraak over ons en niet over de persoon.
        var woord = ContractText.AccessState(AccessEntraState.Unknown);
        var uitleg = ContractText.AccessStateTitle(AccessEntraState.Unknown);

        foreach (var bewering in VerbodenBeweringen)
        {
            Assert.DoesNotContain(bewering, woord, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(bewering, uitleg, StringComparison.OrdinalIgnoreCase);
        }

        // En het zegt wél wat er aan de hand is: het portaal kan het niet zien. Een "onbekend"
        // zonder die uitleg laat de lezer denken dat er iets stuk is.
        Assert.Contains("niet zien", uitleg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnbekendEnOntbrekendZijnNietDezelfdeMededeling()
    {
        // Zou iemand de enum ooit tot een bool terugbrengen, dan vallen deze twee samen. Dat is de
        // wijziging die deze test hoort tegen te houden: "wij weten het niet" tegenover "deze
        // persoon kan nog niet naar binnen".
        Assert.NotEqual(
            ContractText.AccessState(AccessEntraState.Unknown),
            ContractText.AccessState(AccessEntraState.Missing));

        Assert.NotEqual(
            ContractText.AccessStateTitle(AccessEntraState.Unknown),
            ContractText.AccessStateTitle(AccessEntraState.Missing));

        // Alleen bij "actief" mag er staan dat iemand kan aanmelden. Bij de andere twee weet het
        // portaal dat niet.
        Assert.Contains(
            "kan aanmelden",
            ContractText.AccessStateTitle(AccessEntraState.Active),
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "kan aanmelden",
            ContractText.AccessStateTitle(AccessEntraState.Unknown),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeOnbekendeToestandIsDeStandaardwaarde()
    {
        // Een toegangsrij die om welke reden dan ook zonder toestand wordt gemaakt, hoort op
        // "onbekend" uit te komen en niet op "actief". De veilige kant is hier de eerlijke kant.
        Assert.Equal(AccessEntraState.Unknown, default(AccessEntraState));
    }

    [Theory]
    [InlineData(typeof(CustomerAccessRow))]
    [InlineData(typeof(OperatorAccessRow))]
    public void GeenEnkeleToegangsrijDraagtEenTweewaardigeUitnodiging(Type rij)
    {
        // De structurele kant, en de enige die niet van een gerenderde pagina afhangt: er hoort geen
        // veld te bestaan dat de tweede helft van een toegang als ja/nee draagt, en ook geen moment
        // waarop de uitnodiging zou zijn verstuurd. Zo'n veld zou door niets in het portaal worden
        // gevuld en dus voor altijd het verkeerde zeggen.
        string[] verboden = ["IsInvited", "Invited", "InvitedAt", "CanSignIn", "HasAccess"];

        var gevonden = rij
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name)
            .Intersect(verboden, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(
            gevonden.Length == 0,
            $"{rij.Name} draagt {string.Join(", ", gevonden)}. De tweede helft van een toegang — de " +
            "app-rol in Entra ID — is geen gegeven van dit portaal maar een lezing op het moment van " +
            "renderen, en zolang het portaal geen leesrecht op Entra heeft is hij onbekend. Een " +
            "gekopieerde toestand die niemand bijwerkt zegt op een dag het tegenovergestelde van de " +
            "waarheid. Zie AccessEntraState.");
    }

    [Fact]
    public async Task DeWeergavelaagLevertVandaagVoorElkeRegelOnbekend()
    {
        // Wat de productiecode werkelijk doet, voor beide rollen. Het portaal heeft geen leesrecht
        // op Entra, dus er is niets te weten — en dan hoort er ook niets anders te staan. Zou hier
        // ooit "actief" uitkomen, dan komt dat uit een veld in een document en niet uit een lezing,
        // en dat is precies de constructie die dit ontwerp weigert.
        var weergaven = new VasteContractweergaven(Opslag);

        var klant = await weergaven.BuildContractAsync(await Weergavelaag.Klantscope());
        var operatorweergave = await weergaven.BuildContractAsync(await Weergavelaag.Schrijfscope());

        Assert.NotEmpty(klant.Access);
        Assert.NotEmpty(operatorweergave.Access);

        Assert.All(klant.Access, rij => Assert.Equal(AccessEntraState.Unknown, rij.EntraState));
        Assert.All(operatorweergave.Access, rij => Assert.Equal(AccessEntraState.Unknown, rij.EntraState));
    }

    [Fact]
    public void EenKlantLeestBijElkeRegelDatHetPortaalNietInEntraKanKijken()
    {
        // De productiegetrouwe stand op het scherm: drie regels, drie keer "onbekend", en eronder de
        // uitleg waarom. Zwijgen zou de klant naar ons laten bellen met een vraag waarop het scherm
        // het antwoord had.
        MeldKlantAan();

        var cut = RenderPagina(Contractpagina);

        Assert.Equal(
            3,
            Voorkomens(cut.Markup, ContractText.AccessState(AccessEntraState.Unknown)));

        Assert.Contains("uitgenodigd", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kan niet zien", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ElkeToestandHeeftEenEigenWoordOpHetScherm(bool operatorrol)
    {
        // Het pad dat de productiecode vandaag niet oplevert: zodra het portaal leesrecht op Entra
        // heeft, komen "actief" en "ontbreekt" in dezelfde kolom te staan. Zonder deze test is er
        // geen manier om te zien wat het scherm er dan mee doet — en dan blijkt het pas als het
        // leesrecht er is en er twee regels hetzelfde woord krijgen.
        Contracten = new VasteContractweergaven(
            Opslag,
            Autorisatiebron.Standaard(),
            metAlleEntratoestanden: true);

        if (operatorrol)
        {
            MeldOperatorAan();
        }
        else
        {
            MeldKlantAan();
        }

        var markup = RenderPagina(Contractpagina).Markup;

        foreach (var toestand in Enum.GetValues<AccessEntraState>())
        {
            var woord = ContractText.AccessState(toestand);

            Assert.True(
                Voorkomens(markup, woord) == 1,
                $"De toestand {toestand} hoort met het woord \"{woord}\" precies één keer op het " +
                "scherm te staan — de fixture zet de drie regels op de drie toestanden. Staat hij " +
                "er niet, dan valt die toestand op het scherm samen met een andere, en dan is het " +
                "onderscheid weg dat de hele reden is dat dit geen bool is.");
        }
    }

    /// <summary>Hoe vaak deze tekst in de markup staat.</summary>
    /// <param name="markup">De gerenderde markup.</param>
    /// <param name="tekst">De tekst.</param>
    /// <returns>Het aantal keren.</returns>
    /// <remarks>
    /// Op de tekst en niet op een selector: het woord staat in een <c>span</c> met een tooltip, en
    /// juist de tooltip is de plek waar in dit portaal eerder iets is gelekt. Wat er staat is wat
    /// een lezer ziet, waar het ook staat.
    /// </remarks>
    private static int Voorkomens(string markup, string tekst)
    {
        var aantal = 0;
        var index = markup.IndexOf(tekst, StringComparison.Ordinal);

        while (index >= 0)
        {
            aantal++;
            index = markup.IndexOf(tekst, index + tekst.Length, StringComparison.Ordinal);
        }

        return aantal;
    }
}
