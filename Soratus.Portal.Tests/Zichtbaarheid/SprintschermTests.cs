using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Soratus.Portal.Components.Pages.Klant;
using Soratus.Portal.Sprints;
using Soratus.Portal.Tests.Hulpmiddelen;
using Soratus.Portal.Views;

namespace Soratus.Portal.Tests.Zichtbaarheid;

/// <summary>
/// Wat er werkelijk op het sprintscherm staat, per rol (§3.4).
/// </summary>
/// <remarks>
/// <para><strong>Deze tests renderen de échte pagina met de échte projectie.</strong> Dat is het punt: de
/// rolgrens is een typeverschil, en <see cref="Beveiliging.SprintZichtbaarheidTests"/> meet dat verschil
/// op de typen.
/// Hier wordt gemeten dat de projectie het ook wérkelijk zo doet — dat de klantweergave het adres van een
/// medewerker niet in zijn markup krijgt, ook niet in een tooltip of in een metaregel. Een fixture die de
/// viewmodellen zelf zou vullen, laat dat groen staan zonder het te meten.</para>
///
/// <para><strong>De registratie staat hier en niet in <see cref="Portaalrendertest"/>.</strong> Dat is een
/// lane-afspraak: de basisklasse en de vastgelegde paginalijsten zijn van de hoofdsessie, omdat drie
/// sessies daar tegelijk een pagina aan toevoegen en een verloren regel daar stil is — de pagina valt dan
/// gewoon niet meer onder het vangnet, en het vangnet blijft groen. Deze klasse zet de dienst dus zelf
/// neer, ná het aanmelden en vóór de eerste render.</para>
/// </remarks>
public class SprintschermTests : Portaalrendertest
{
    /// <summary>De slug van een klant waar de testklantgebruiker géén toegang tot heeft.</summary>
    /// <remarks>
    /// Met opzet een klant die wél bestáát in de klantenlijst en niet een verzonnen naam. Een onbekende slug
    /// lost nergens op en dan valt er ook niets te lekken; juist een bestaande klant levert een naam op die
    /// in een titel terecht zou kunnen komen.
    /// </remarks>
    private const string VreemdeKlant = "bakker-bv";

    /// <summary>De route van het sprintscherm.</summary>
    private const string Route = "/klant/{Slug}/sprint";

    /// <summary>Meldt een operator aan en zet de sprintweergave neer.</summary>
    private void MeldOperatorAanMetSprint()
    {
        MeldOperatorAan();
        Services.AddSingleton(VasteSprintweergaven.Bouw(Opslag));
    }

    /// <summary>Meldt een klant aan en zet de sprintweergave neer.</summary>
    private void MeldKlantAanMetSprint()
    {
        MeldKlantAan();
        Services.AddSingleton(VasteSprintweergaven.Bouw(Opslag));
    }

    /// <summary>Rendert het sprintscherm op de eigen slug.</summary>
    /// <returns>De gerenderde pagina.</returns>
    private IRenderedComponent<Bunit.Rendering.ContainerFragment> Scherm() =>
        RenderPagina(Pagina());

    /// <summary>Het paginatype van het sprintscherm.</summary>
    /// <returns>Het type.</returns>
    private static Type Pagina() =>
        Paginaverzameling.MetRoute(Route)
        ?? throw new InvalidOperationException(
            $"Er staat geen pagina op route '{Route}'. Deze tests meten het sprintscherm; is de route "
            + "hernoemd, dan hoort deze constante mee te veranderen — een test die niets vindt meet niets.");

    [Fact]
    public void DeKlantZietDeSprintnaamDePeriodeEnHetBoardpad()
    {
        // §3.4 vraagt vier kopgegevens: sprintnaam, periode, boardpad en het tijdstip van laatste ophalen.
        // Het boardpad staat er met opzet ook voor de klant — §3.4 noemt hem bij naam en het is het pad
        // binnen het project van deze klant zelf. Wat §2 dichtzet is de kóppeling (organisatie, team,
        // rechten), en niet waar het werk van de klant op zijn eigen bord staat.
        MeldKlantAanMetSprint();

        var markup = Scherm().Markup;

        Assert.Contains(Vasteportaalopslag.Sprintnaam, markup, StringComparison.Ordinal);
        Assert.Contains(Vasteportaalopslag.Boardpad, markup, StringComparison.Ordinal);
        Assert.Contains("1 t/m 31 augustus 2026", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DeKlantZietHoeOudDeLezingIs()
    {
        // §3.4 vraagt het tijdstip van laatste ophalen, en §1 vraagt relatieve tijden in beeld. Dit veld is
        // de reden dat er wordt verzameld in plaats van bij elke paginaweergave op te halen: bij een
        // ophaling per weergave zou hier altijd "nu" staan en zou het niets zeggen.
        MeldKlantAanMetSprint();

        Assert.Contains("opgehaald", Scherm().Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DeKlantZietDeTitelsDeSoortEnDeHerkomstVanDeWorkItems()
    {
        // §3.4 vraagt per item "titel + tags + herkomst". De herkomst staat er altijd, ook als hij onbekend
        // is: een lege plek zou als "handmatig" gelezen worden, en dat is precies de bewering die niemand
        // heeft gemeten.
        MeldKlantAanMetSprint();

        var markup = Scherm().Markup;

        Assert.Contains("Declaratieregels valideren", markup, StringComparison.Ordinal);
        Assert.Contains("herkomst: agent", markup, StringComparison.Ordinal);
        Assert.Contains("herkomst: mens", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DeKlantZietDeNaamVanDeToegewezenPersoonMaarNietZijnAdres()
    {
        // De grens van dit scherm in één test, en het is een afweging die expliciet is gemaakt: §3.4 vraagt
        // "toegewezen" als kolom en een sprint zonder te zien wie waaraan werkt is geen sprintweergave. Wat
        // er niet doorheen komt is het adres — een naam staat op het bord waar deze klant werk in heeft, een
        // adres is een contactgegeven dat niemand hier heeft gevraagd.
        MeldKlantAanMetSprint();

        var markup = Scherm().Markup;

        Assert.Contains(Vasteportaalopslag.Toegewezenaam, markup, StringComparison.Ordinal);
        Assert.DoesNotContain(Vasteportaalopslag.Toegewezenadres, markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Vasteportaalopslag.Aanmakeradres)]
    [InlineData(Vasteportaalopslag.Aanmakernaam)]
    [InlineData(Vasteportaalopslag.Toegewezenadres)]
    [InlineData(Vasteportaalopslag.Standaardbord)]
    [InlineData(Vasteportaalopslag.Ongedateerdpad)]
    public void DeKlantZietGeenEnkelKoppelingsdetail(string verboden)
    {
        // Vijf gegevens die §2 dichtzet, en ze zitten geen van vijf op een klanttype — dit is de meting dat
        // de projectie dat ook werkelijk zo doet, inclusief tooltips en metaregels. Een @if zou hier kunnen
        // lekken; een ontbrekende property niet.
        MeldKlantAanMetSprint();

        Assert.DoesNotContain(verboden, Scherm().Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DeOperatorZietDeKoppelingEnDeAdressen()
    {
        // De spiegel, en hij is het halve werk: zonder hem is elke test hierboven ook groen als de
        // operatorweergave die gegevens óók niet toont — en dan is het gereedschap tegen een tikfout in een
        // teamnaam er niet, terwijl de zichtbaarheid klopt.
        MeldOperatorAanMetSprint();

        var markup = Scherm().Markup;

        Assert.Contains(Vasteportaalopslag.Standaardbord, markup, StringComparison.Ordinal);
        Assert.Contains(Vasteportaalopslag.Aanmakernaam, markup, StringComparison.Ordinal);
        Assert.Contains(Vasteportaalopslag.Aanmakeradres, markup, StringComparison.Ordinal);
        Assert.Contains(Vasteportaalopslag.Toegewezenadres, markup, StringComparison.Ordinal);
        Assert.Contains(Vasteportaalopslag.Ongedateerdpad, markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenNietIngevuldeUrenkolomIsEenStreepjeEnGeenNul()
    {
        // De gemeten kern van dit scherm: van de zestien work items op het echte bord had géén enkel item
        // een waarde in RemainingWork, CompletedWork of StoryPoints. De gezaaide sprint heeft één zo'n item
        // (#4566) en één met nul resterende uren (#4572), en die twee horen verschillend op het scherm te
        // staan.
        MeldKlantAanMetSprint();

        var markup = Scherm().Markup;

        Assert.Contains(SprintText.Dash, markup, StringComparison.Ordinal);

        // En de échte nul staat er als nul. Zonder deze helft zou "altijd een streepje" ook groen zijn.
        Assert.Contains("0 u", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DeUitlegDatEenStreepjeGeenNulIsStaatOpHetScherm()
    {
        // Zonder deze tekst leest een streepje als een storing, en dan gaat iemand het "oplossen" door in
        // DevOps een nul in te vullen — en dan is de informatie werkelijk weg. §1: eerlijke
        // systeemeigenschappen benoemen.
        MeldKlantAanMetSprint();

        Assert.Contains("geen nul", Scherm().Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeMededelingDatHetPortaalNietsTerugschrijftStaatOpHetScherm()
    {
        // §3.4: het portaal schrijft niets terug. §1 vraagt dat als tekst en niet als afwezigheid van
        // knoppen: laat de beperking zien in plaats van hem weg te poetsen.
        MeldKlantAanMetSprint();

        var markup = Scherm().Markup;

        Assert.Contains("alleen te lezen", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<button", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeIteratiesZonderDatumsWordenGemeldMetDeAantekeningDatDeItemsNietZijnGeteld()
    {
        // Staat er óók bij een gezonde sprint, en dat is het punt: juist dan is "er valt werk buiten elke
        // periode" iets wat niemand anders zegt. En de tekst zegt uitdrukkelijk dat de items niet zijn
        // geteld, want een ontbrekend aantal leest als nul.
        MeldKlantAanMetSprint();

        var markup = Scherm().Markup;

        Assert.Contains("zonder datums", markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("niet geteld", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ZonderLezingStaatErEenMededelingEnGeenLeegSprintoverzicht()
    {
        // De gewone beginstand in productie: het portaal staat er, de sprintcollector heeft nog nooit
        // gedraaid. Een leeg overzicht leest als "er is geen werk", en dat is een andere mededeling dan "wij
        // hebben hier nog niets gelezen".
        Opslag.GeenSprint();
        MeldKlantAanMetSprint();

        var markup = Scherm().Markup;

        Assert.Contains("nog niet opgehaald", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Declaratieregels valideren", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DeKlantZietBijEenMislukteLezingNietWaaromHetMisging()
    {
        // De reden noemt onze rolverlening en dat is een koppelingsdetail (§2). Wat de klant nodig heeft is
        // dat dit geen "geen werk" betekent en dat er iemand naar kijkt.
        Opslag.LegSprintVast(MislukteLezing());
        MeldKlantAanMetSprint();

        var markup = Scherm().Markup;

        Assert.DoesNotContain(Vasteportaalopslag.Leesfout, markup, StringComparison.Ordinal);
        Assert.Contains("Soratus ziet waarom", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DeOperatorZietBijEenMislukteLezingWelWaaromHetMisging()
    {
        Opslag.LegSprintVast(MislukteLezing());
        MeldOperatorAanMetSprint();

        Assert.Contains(Vasteportaalopslag.Leesfout, Scherm().Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenBordTussenTweeSprintsIsGeenStoringEnZegtDat()
    {
        // Een geldige stand van een gezond project. Hij valt met opzet niet samen met "nog niet opgehaald":
        // zouden die twee samenvallen, dan gaat een klant bellen over een storing die er niet is — en, erger,
        // dan ziet een echte weigering uit als een rustige maand.
        Opslag.LegSprintVast(Lezing(SprintState.NoCurrentSprint));
        MeldKlantAanMetSprint();

        var markup = Scherm().Markup;

        Assert.Contains("geen sprint", markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gewone stand", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nog niet opgehaald", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EenBordZonderDatumsZegtDatErDatumsMoetenKomen()
    {
        // Dit was de werkelijke stand van het echte bord tot 21 augustus 2026, en hij was stil kapot: er
        // stond werk op het bord en er was geen huidige sprint, omdat @currentIteration door datums wordt
        // bepaald. Een weergave die deze toestand niet kan uitdrukken toont een leeg scherm.
        Opslag.LegSprintVast(Lezing(SprintState.NoDatedIterations));
        MeldOperatorAanMetSprint();

        var markup = Scherm().Markup;

        Assert.Contains("begin- en einddatum", markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("staat wél werk op het bord", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ZonderVastgelegdBordZegtHetOperatorschermDatErNietsIsIngericht()
    {
        // Het onderscheid dat de lege pagina zelf niet kan maken: "niet ingericht" tegenover "nog niet
        // opgehaald". Die twee vragen een volstrekt verschillende handeling — een veld invullen tegenover
        // wachten — en zonder deze regel wacht een operator op een ophaling die nooit komt. Precies de vorm
        // van BillingNotice.NoScopeConfigured.
        Opslag.GeenSprint();
        Opslag.EenAndereOperatorWijzigtDeKlant(klant => klant with { DevOpsScope = null });
        MeldOperatorAanMetSprint();

        var markup = Scherm().Markup;

        Assert.Contains("geen DevOps-bord vastgelegd", markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("blok Omgeving", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenOnbruikbaarBordZegtDatHetGecorrigeerdMoetWorden()
    {
        // Kan alleen als iemand het klantdocument met de hand heeft aangepast — beide formulieren valideren.
        // En juist daarom hoort het hier te staan: een bord dat er wél is en niet werkt is niet van een bord
        // te onderscheiden dat er niet is, en de handeling is een andere.
        Opslag.GeenSprint();
        Opslag.EenAndereOperatorWijzigtDeKlant(
            klant => klant with { DevOpsScope = "sub-soratus-acme · rg-acme-prod" });
        MeldOperatorAanMetSprint();

        Assert.Contains("niet te gebruiken", Scherm().Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpDeSlugVanEenVreemdeKlantRendertHetSprintschermNietsEnZetHetGeenTitel()
    {
        // Dezelfde theorie als in PaginatitelTests, hier als eigen uitspraak over dit scherm. Een PageTitle
        // rendert in de HeadOutlet en niet in de markup, dus het vangnet op verboden woorden ziet hem niet —
        // de gebruiker wel, in zijn tabblad, zijn geschiedenis en zijn bladwijzers. Op dit scherm weegt dat
        // extra: de titel draagt de klantnaam, en de gegevens erachter komen uit een DevOps-project waarvan
        // een andere klant niets hoort te weten.
        MeldKlantAanMetSprint();

        var cut = RenderPagina(Pagina(), VreemdeKlant);

        Assert.True(
            string.IsNullOrWhiteSpace(cut.Markup),
            "Het sprintscherm rendert inhoud op de slug van een klant waar deze gebruiker geen toegang "
            + "tot heeft.");

        Assert.Empty(cut.FindComponents<PageTitle>());
    }

    [Fact]
    public void OpDeEigenSlugZetHetSprintschermWelEenTitelMetDeKlantnaam()
    {
        // De onmisbare tegenhanger: de test hierboven is alleen iets waard als er een titel te zetten valt.
        // Zou de pagina nooit een titel zetten, dan is die test groen zonder iets te meten.
        MeldKlantAanMetSprint();

        var cut = Scherm();
        var titel = Assert.Single(cut.FindComponents<PageTitle>());
        var tekst = Render(titel.Instance.ChildContent!).Markup;

        Assert.Contains("Sprint", tekst, StringComparison.Ordinal);
        Assert.Contains("Acme Logistiek", tekst, StringComparison.Ordinal);
        Assert.Contains("Agent Portal", tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void DeStatistiekenStaanOpHetSchermMetDeAantallenUitDeGezaaideSprint()
    {
        // §3.4 vraagt vijf statistieken. De gezaaide sprint heeft zeven items waarvan één verwijderd, dus
        // zes work items, één afgerond en één geblokkeerd — en de openstaande uren zijn 6,5 + 0 + 2 = 8,5.
        // Deze test staat er om de rekenkant op het scherm vast te leggen: een tegel die het verkeerde getal
        // toont is niet aan de vorm te zien.
        MeldKlantAanMetSprint();

        var markup = Scherm().Markup;

        Assert.Contains("Work items", markup, StringComparison.Ordinal);
        Assert.Contains("Geblokkeerd", markup, StringComparison.Ordinal);
        Assert.Contains("8,5 u", markup, StringComparison.Ordinal);
        Assert.Contains("verwijderd, niet meegeteld", markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// De woorden die een klant nergens mag zien, uit <see cref="KlantVangnetTests.VerbodenWoorden"/>.
    /// </summary>
    /// <remarks>
    /// De lijst van dat vangnet en geen eigen kopie: een tweede lijst zou op een dag een woord missen dat
    /// er daar bij is gekomen, en dan is deze theorie groen over een woord dat wél verboden is. De
    /// omzetting naar <see cref="TheoryData{T}"/> is nodig omdat <c>MemberData</c> geen <c>string[]</c>
    /// aanneemt.
    /// </remarks>
    public static TheoryData<string> Operatorwoorden
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var woord in KlantVangnetTests.VerbodenWoorden)
            {
                data.Add(woord);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Operatorwoorden))]
    public void HetSprintschermToontEenKlantGeenEnkelOperatorwoord(string woord)
    {
        // Hetzelfde vangnet als KlantVangnetTests, hier vooruit gemeten. Dat vangnet rendert élke pagina en
        // valt daarom om zolang ISprintViews niet in Portaalrendertest.MeldAan staat — en dat bestand is van
        // de hoofdsessie. Deze theorie doet dezelfde controle met de dienst er wél in, zodat de uitkomst
        // vast te stellen is vóór die regel er staat in plaats van erna.
        //
        // Hij vervangt dat vangnet niet: daar loopt hij over alle pagina's en hier over één. Zodra de
        // registratie er staat meten ze hetzelfde, en dan is deze theorie de goedkopere van de twee om te
        // laten staan — hij zegt namelijk wélke pagina hij meet.
        MeldKlantAanMetSprint();

        Assert.DoesNotContain(woord, Scherm().Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("08/31/2026")]
    [InlineData("31-08-2026")]
    [InlineData("2026/08/31")]
    public void EenPeriodeDieNietInDeOpslagvormStaatWordtGeenVerzonnenDatum(string datum)
    {
        // Een gat dat een mutatie vond: het omzetten van TryParseExact naar TryParse maakte niets rood,
        // want geen enkele test gaf een datum in een andere vorm. Dat is geen theoretisch geval — een
        // document uit een oudere vorm of een handmatige wijziging kan hem leveren, en dan hangt het
        // antwoord af van de cultuur van de server: "08/31/2026" is in de ene lezing 31 augustus en in de
        // andere onzin.
        //
        // Een periode die we niet kennen hoort als streepje op het scherm te komen en niet als een datum
        // die niemand heeft ingevuld. Dat is dezelfde regel als bij een bedrag: onleesbaar wordt onbekend
        // en nooit een waarde die geloofwaardig oogt.
        Opslag.LegSprintVast(Lezing(SprintState.Current) with { Start = datum, Finish = datum });
        MeldKlantAanMetSprint();

        var markup = Scherm().Markup;

        // Op "t/m" en niet op het jaartal: dat staat ook in de absolute tijd van de laatste ophaling, en
        // een assertie die daarop afgaat meet iets anders dan hij zegt. "t/m" komt uitsluitend uit
        // SprintText.Period, en die zet het alleen neer als béide datums te lezen waren.
        Assert.DoesNotContain("t/m", markup, StringComparison.Ordinal);
        Assert.Contains(SprintText.Dash, markup, StringComparison.Ordinal);
    }

    [Fact]
    public void HetSprintschermRendertVoorEenKlantInhoudEnZetEenTitel()
    {
        // Dit is de meting die zegt in wélke van de drie vastgelegde lijsten van PaginatitelTests deze
        // pagina valt, en het is een rendering en geen mening: inhoud én een titel, dus de groep
        // "DePaginasWaarvanEenKlantDeTitelTeZienKrijgt" en niet de groep die voor een klant niets rendert.
        //
        // Hij staat er als eigen test omdat die lijsten van de hoofdsessie zijn en er drie sessies aan
        // dezelfde lijst toevoegen. Zo is de uitkomst hier te lezen zonder die lijst aan te raken.
        MeldKlantAanMetSprint();

        var cut = Scherm();

        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
        Assert.Single(cut.FindComponents<PageTitle>());
    }

    /// <summary>Een lezing zonder sprint, in de gevraagde toestand.</summary>
    /// <param name="state">De toestand.</param>
    /// <returns>Het document.</returns>
    private static SprintDocument Lezing(SprintState state) => new()
    {
        Id = SprintDocumentKeys.Id,
        PartitionKey = Vasteportaalopslag.Standaardklant,
        CustomerId = Vasteportaalopslag.Standaardklant,
        State = state,
        Scope = Vasteportaalopslag.Bevraagdbord,
        ReadAt = Testgegevens.Nu - TimeSpan.FromMinutes(4),
        DatedCount = state == SprintState.NoCurrentSprint ? 5 : 0,
        Undated =
        [
            new SprintIterationRef
            {
                Name = Vasteportaalopslag.Ongedateerdeiteratie,
                Path = Vasteportaalopslag.Ongedateerdpad,
            },
        ],
    };

    /// <summary>Een lezing die is mislukt, met de reden erbij.</summary>
    /// <returns>Het document.</returns>
    private static SprintDocument MislukteLezing() =>
        Lezing(SprintState.Unknown) with { Failure = Vasteportaalopslag.Leesfout };
}
