using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Soratus.Portal.Data;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Zichtbaarheid;

/// <summary>
/// Het urenscherm (§3.6) per rol, en in beide richtingen.
/// </summary>
/// <remarks>
/// <para><strong>Elke test heeft zijn spiegel.</strong> Een test die alleen afwezigheid controleert
/// blijft groen als de pagina stukgaat of leeg blijft — dan is er niets, dus ook niet het verboden
/// gegeven. Waar de spiegel op het scherm bestaat staat hij hier als tweede test.</para>
///
/// <para><strong>Dit is het vangnet en niet de grens.</strong> De echte grens is een typeverschil:
/// een klantscope levert een <c>CustomerHoursView</c> en dat type draagt de fiatteringsstroom niet,
/// en het klantcomponent kan hem dus niet renderen. Die kant staat in
/// <c>Presentatie.UrencomponentTests</c>, op typeniveau. Wat hier wordt gemeten is of de gegevens
/// die er wél op staan werkelijk op het scherm belanden, en of er niets in een tooltip is
/// geslopen.</para>
///
/// <para>De gegevens komen uit <see cref="Vasteportaalopslag"/> door de échte
/// <c>HourViews</c>-projectie heen. Zou de fixture het klantpad zelf armer vullen, dan blijft elke
/// test hier groen omdat de fixture al filterde en niet omdat de scheiding werkt.</para>
/// </remarks>
public class UrenschermTests : Portaalrendertest
{
    private static Type Urenpagina =>
        Paginaverzameling.MetRoute("/klant/{Slug}/uren")
        ?? throw new InvalidOperationException(
            "Er staat geen pagina op route '/klant/{Slug}/uren'. Is de route hernoemd, dan hoort " +
            "deze test mee te verhuizen — niet te verdwijnen.");

    // ── Te fiatteren regels ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void EenKlantZietDeTeFiatterenRegelsNietOpZijnSpecificatie()
    {
        // De kern van de acceptatie-eis. De regel bestaat, staat in dezelfde maand en heeft dezelfde
        // vorm als de regels eromheen; het enige verschil is zijn stand.
        MeldKlantAan();

        var markup = Render().Markup;

        Assert.DoesNotContain(
            Vasteportaalopslag.Tefiatterenomschrijving,
            markup,
            StringComparison.Ordinal);

        Assert.Contains(
            Vasteportaalopslag.Gefiatteerdeomschrijving,
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EenOperatorZietDeTeFiatterenRegelsWel()
    {
        // De spiegel. Zonder deze zegt de test hierboven niets: een scherm dat helemaal geen
        // specificatie meer heeft toont die regel ook niet.
        MeldOperatorAan();

        var markup = Render().Markup;

        Assert.Contains(
            Vasteportaalopslag.Tefiatterenomschrijving,
            markup,
            StringComparison.Ordinal);

        Assert.Contains("Te fiatteren", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenKlantZietGeenTellerMetWatErTeFiatterenLigt()
    {
        // §3.6 zet "+ x u te fiatteren" in de maandtabel en zegt erbij: operator-only. Dat getal
        // staat niet op HourBalance maar op OperatorMonthRow, dus er is geen veld waarin het bij de
        // klant kan belanden — en dan hoort het ook nergens in zijn markup te staan.
        MeldKlantAan();

        var markup = Render().Markup;

        Assert.DoesNotContain("fiatteren", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            $"+ {Getal(Vasteportaalopslag.Tefiatterenmaanduren)} u",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EenOperatorZietDieTellerWel()
    {
        MeldOperatorAan();

        var markup = Render().Markup;

        Assert.Contains(
            $"+ {Getal(Vasteportaalopslag.Tefiatterenmaanduren)} u",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EenKlantZietNietWieEenRegelHeeftGefiatteerd()
    {
        // De naam van een fiatteur is het gevaarlijkste veld van deze weergave: die verraadt niet
        // alleen dát er is gefiatteerd maar ook door wie. Zie CustomerHourRow — het veld staat er
        // niet op.
        MeldKlantAan();

        Assert.DoesNotContain(Vasteportaalopslag.Fiatteur, Render().Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenOperatorZietWelWieHeeftGefiatteerd()
    {
        MeldOperatorAan();

        Assert.Contains(Vasteportaalopslag.Fiatteur, Render().Markup, StringComparison.Ordinal);
    }

    // ── Afgewezen regels ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EenKlantZietDeAfgewezenRegelNietEnDeRedenOokNiet()
    {
        // Dubbel gedekt (punt 17): de regel is niet gefiatteerd, en de klantquery vraagt alleen om
        // gefiatteerde regels. De reden is bovendien een operatorafweging die de klant niets zegt.
        MeldKlantAan();

        var markup = Render().Markup;

        Assert.DoesNotContain(Vasteportaalopslag.Afgewezenomschrijving, markup, StringComparison.Ordinal);
        Assert.DoesNotContain(Vasteportaalopslag.Afwijsreden, markup, StringComparison.Ordinal);
        Assert.DoesNotContain("afgewezen", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EenOperatorZietDeAfgewezenRegelInEenEigenLijstMetDeRedenErbij()
    {
        // Punt 17: bewaren is het besluit, en het bezwaar — een specificatie die volloopt met regels
        // die niet meetellen — is opgelost in de weergave en niet in de opslag.
        MeldOperatorAan();

        var cut = Render();

        Assert.Contains(Vasteportaalopslag.Afwijsreden, cut.Markup, StringComparison.Ordinal);

        var afgewezen = Kaart(cut, "Afgewezen regels");
        var specificatie = Kaart(cut, "Specificatie");

        Assert.Contains(
            Vasteportaalopslag.Afgewezenomschrijving,
            afgewezen.TextContent,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            Vasteportaalopslag.Afgewezenomschrijving,
            specificatie.TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ZonderAfgewezenRegelsStaatErGeenLijstDieZegtDatErNietsIs()
    {
        // Leeg is de gewone toestand, en dan hoort er geen sectie te staan. Zie
        // OperatorHoursView.Rejected.
        Opslag.EenAndereOperatorBeoordeeltDeRegel(
            Opslag.Urenregels().Single(regel => regel.Status == HourEntryStatus.Rejected).Id,
            HourEntryStatus.Approved);

        MeldOperatorAan();

        Assert.Empty(Render().FindAll("section[aria-label='Afgewezen regels']"));
    }

    // ── Formulieren en knoppen ──────────────────────────────────────────────────────────────────

    [Fact]
    public void EenKlantHeeftGeenEnkelFormulierEnGeenEnkeleKnopOpHetUrenscherm()
    {
        // §2 geeft de klant op uren alleen de gefiatteerde regels, en niets om te doen. Er staat dus
        // geen uitgegrijsde knop maar helemaal niets: een knop die niets doet belooft dat het wél kan.
        MeldKlantAan();

        var cut = Render();

        Assert.Empty(cut.FindAll("form"));
        Assert.Empty(cut.FindAll("input"));
        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void EenOperatorKrijgtEenBoekformulierEenCorrectieformulierEnPerRegelTweeActies()
    {
        // De spiegel, en hij is hier het halve werk: een pagina die stukgaat rendert ook geen
        // formulier, en dan is de test hierboven groen om de verkeerde reden.
        MeldOperatorAan();

        var cut = Render();

        Assert.NotNull(Kaart(cut, "Uren boeken"));
        Assert.NotNull(Kaart(cut, "Correctie plaatsen"));

        // Twee te fiatteren regels, elk met Fiatteren en Afwijzen; en de afgewezen regel met alleen
        // Fiatteren. Drie regels, vijf acties.
        Assert.Equal(5, cut.FindAll(".row-actions a").Count);
    }

    [Fact]
    public void EenGefiatteerdeRegelKrijgtGeenEnkeleActie()
    {
        // Punt 18: gefiatteerd is definitief. De knop staat er niet omdat HourEntryTransitions zegt
        // dat het niet mag, en niet omdat de weergave een eigen vergelijking maakt — dan zou er een
        // knop staan die een melding oplevert.
        Opslag.GeenUren();
        Opslag.LegUrenregelVast(Regel(HourEntryStatus.Approved, "Alleen gefiatteerd"));

        MeldOperatorAan();

        Assert.Empty(Render().FindAll(".row-actions a"));
    }

    // ── Het maandtotaal ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeSpecificatieVanDeKlantTeltOpTotHetMaandtotaalDatErbovenStaat()
    {
        // De eerste acceptatie-eis, gemeten op het scherm en niet op het viewmodel: de uren in de
        // rijen worden bij elkaar opgeteld en vergeleken met de voetregel én met de kolom "Besteed"
        // in de maandtabel. Zou een van de drie ergens anders vandaan komen, dan zou dit verschillen.
        MeldKlantAan();

        var cut = Render();

        var regels = Urenperregel(Kaart(cut, "Specificatie"));

        Assert.Equal(Vasteportaalopslag.Gefiatteerdemaanduren, regels.Sum());
        Assert.Equal(
            Vasteportaalopslag.Gefiatteerdemaanduren,
            Voetregel(Kaart(cut, "Specificatie")));
        Assert.Equal(
            Vasteportaalopslag.Gefiatteerdemaanduren,
            Besteed(Kaart(cut, "Uren per maand")));
    }

    [Fact]
    public void DeSpecificatieVanDeOperatorTeltDeTeFiatterenUrenNietMeeInHetTotaal()
    {
        // De andere helft van dezelfde eis. De te fiatteren regels staan in de tabel — je moet kunnen
        // zien wat ze met de maand zouden doen — maar ze zitten niet in de som, en het maandtotaal
        // van de operator is exact dat van de klant.
        MeldOperatorAan();

        var cut = Render();

        Assert.Equal(
            Vasteportaalopslag.Gefiatteerdemaanduren,
            Voetregel(Kaart(cut, "Specificatie")));

        Assert.Equal(
            Vasteportaalopslag.Gefiatteerdemaanduren,
            Besteed(Kaart(cut, "Uren per maand")));

        // En de som van álle regels in de tabel is een ánder getal, anders zegt de test hierboven
        // niets: dan zouden de te fiatteren regels net zo goed meegeteld kunnen zijn.
        Assert.NotEqual(
            Vasteportaalopslag.Gefiatteerdemaanduren,
            Urenperregel(Kaart(cut, "Specificatie")).Sum());
    }

    [Fact]
    public async Task HetMaandtotaalIsHetzelfdeGetalVoorBeideRollen()
    {
        // Niet twee tellingen naast elkaar maar dezelfde berekening op dezelfde regels. Deze test
        // staat op de viewmodellen en niet op de markup, want dit is een eigenschap van de projectie
        // en niet van het scherm.
        var weergaven = VasteUrenweergaven.Bouw(Opslag);
        var maand = HoursQuery.ForMonth(Vasteportaalopslag.Dezemaand);

        var klant = await weergaven.BuildHoursAsync(await Weergavelaag.Klantscope(), maand);
        var beheer = await weergaven.BuildHoursAsync(await Weergavelaag.Schrijfscope(), maand);

        Assert.Equal(klant.Months[0].Booked, beheer.Months[0].Balance.Booked);
        Assert.Equal(Vasteportaalopslag.Gefiatteerdemaanduren, klant.Months[0].Booked);
    }

    [Fact]
    public void EenCorrectieStaatOpBeideSchermenInDeSpecificatie()
    {
        // Besluit 16, en de klant hoort hem te zien: zou hij de correctierij niet zien, dan telt zijn
        // specificatie niet op tot zijn maandtotaal — en dan is de eigenschap waarvoor dit hele
        // besluit bestaat weg op het enige scherm waar hij te controleren valt.
        MeldKlantAan();

        var klant = Render().Markup;

        Assert.Contains(Vasteportaalopslag.Correctieomschrijving, klant, StringComparison.Ordinal);
        Assert.Contains(HourCategories.Correction, klant, StringComparison.Ordinal);
    }

    [Fact]
    public void DeTooltipVanHetMaandtotaalMeldtDeHandmatigeCorrectie()
    {
        // §3.6 vraagt om die melding. Hij staat er als bijdrage — hoeveel van het totaal uit
        // correcties komt — en niet als verschil tussen twee getallen, want dat tweede getal bestaat
        // niet: het totaal ís de som.
        MeldKlantAan();

        Assert.Contains(
            "handmatig gecorrigeerd",
            Render().Markup,
            StringComparison.Ordinal);
    }

    // ── Schrijfvoorwaarden ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EenSchrijfvoorwaardeStaatInGeenEnkeleWeergaveVanHetUrenscherm(bool operatorrol)
    {
        // Een etag is een schrijfvoorwaarde en geen gegeven. De klant schrijft niet, en ook op het
        // operatorscherm hoort hij niet in de markup te staan — dan staat hij in de paginabron.
        // Dezelfde regel als op het contractscherm. Het gevolg is dat het fiatteren op dit scherm
        // zonder etag gebeurt; dat besluit staat in Uren.razor en wordt gemeten in
        // UrenschrijfactieTests.
        MeldAanAls(operatorrol);

        Assert.DoesNotContain(
            Vasteportaalopslag.Etagvingerafdruk,
            Render().Markup,
            StringComparison.Ordinal);
    }

    // ── De drie schermtoestanden van §3.6 ───────────────────────────────────────────────────────

    [Fact]
    public void StandaardStaatErAlleenDeHuidigeMaandEnGeenJaartotaal()
    {
        // §3.6: standaard alleen de huidige maand. Een jaartotaal over één maand is geen jaartotaal
        // maar een tweede plek waar hetzelfde getal staat.
        MeldKlantAan();

        var cut = Render();

        Assert.Single(Maandrijen(Kaart(cut, "Uren per maand")));
        Assert.DoesNotContain("Jaartotaal", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Alle maanden", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AlleMaandenKlaptDeHistorieEnHetJaartotaalOpen()
    {
        MeldKlantAan();

        var cut = Render("?alle=1");

        Assert.Contains("Jaartotaal", cut.Markup, StringComparison.Ordinal);
        Assert.True(
            Maandrijen(Kaart(cut, "Uren per maand")).Count > 1,
            "Met ?alle=1 staat er nog steeds één maand op het overzicht.");
    }

    [Fact]
    public void EenGekozenMaandFiltertDeSpecificatieEnLaatDeMaandtabelHeel()
    {
        // De derde toestand van §3.6: klik op een maand filtert de specificatie op die maand, en de
        // maandtabel blijft compleet.
        MeldKlantAan();

        var cut = Render($"?maand={Vasteportaalopslag.Vorigemaand}");

        Assert.True(Maandrijen(Kaart(cut, "Uren per maand")).Count > 1);
        Assert.DoesNotContain(
            Vasteportaalopslag.Gefiatteerdeomschrijving,
            Kaart(cut, "Specificatie").TextContent,
            StringComparison.Ordinal);
        Assert.Contains("gefilterd", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenOnleesbareMaandInDeUrlValtTerugOpDeStandaardweergave()
    {
        // HoursQuery.ForMonth werpt met opzet op een verzonnen maand — dat is een fout in de
        // aanroeper — en dit is de aanroeper die hem moet opvangen, want deze waarde komt uit de
        // adresbalk. Een 500 op een getypte URL is geen antwoord.
        MeldKlantAan();

        var cut = Render("?maand=augustus");

        Assert.Single(Maandrijen(Kaart(cut, "Uren per maand")));
    }

    // ── Geen bundel, geen contract, geen uren ───────────────────────────────────────────────────

    [Fact]
    public void EenMaandZonderAfgesprokenBundelKrijgtGeenSaldoEnGeenBovenBundel()
    {
        // Punt 19: de vierde stand. Zou hij als "Boven bundel" verschijnen — wat er gebeurt zodra
        // iemand ?? 0m schrijft — dan staat er dat een klant zijn bundel overschrijdt die er nooit
        // een had.
        Opslag = new Vasteportaalopslag(
            contract: Vasteportaalopslag.Volledigcontract() with { BundledHours = null });

        MeldKlantAan();

        var cut = Render();

        Assert.Contains("Geen bundel", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Boven bundel", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("geen urenbundel per maand", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EenKlantZonderGeboekteUrenLeestDatErNietsStaat()
    {
        Opslag.GeenUren();

        MeldKlantAan();

        var cut = Render();

        Assert.Contains("Niets geboekt", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("geen uren", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EenOperatorZonderContractLeestDatErNietsIsOmTegenAfTeZetten()
    {
        // HasContract op false betekent: er kunnen uren geboekt worden op een klant zonder contract.
        // Dat mag — onboarding gaat in die volgorde — maar het scherm hoort dat te melden in plaats
        // van overal streepjes te zetten.
        Opslag = new Vasteportaalopslag(zonderContract: true);

        MeldOperatorAan();

        var markup = Render().Markup;

        Assert.Contains("nog geen contract", markup, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(Kaart(Render(), "Uren boeken"));
    }

    [Fact]
    public void EenKlantLeestDatSoratusDeUrenBijhoudtEnNietsOverEenWachtrij()
    {
        // De mededeling die HoursNotice.CustomerReadOnly is, en het punt eraan: hij zegt niets over
        // fiatteren. Een uitleg als "uren worden na akkoord van Soratus toegevoegd" zou aan alle
        // eisen van eerlijkheid voldoen en precies de fout zijn — dan weet de klant dat er een
        // wachtrij is, en dan is de volgende vraag hoe lang die is.
        MeldKlantAan();

        var markup = Render().Markup;

        Assert.Contains("door Soratus bijgehouden", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("akkoord", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("in behandeling", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wachtrij", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HetScherpsteWoordVanDeOperatorZitInHetVangnetVanDeKlant()
    {
        // Bewijs dat de stilte bij de klant een keuze is en geen storing: op het operatorscherm
        // staan de woorden die KlantVangnetTests verbiedt. Zonder deze test blijft dat vangnet groen
        // zodra dit scherm de woorden nergens meer gebruikt — en dan is er geen fiatteerscherm meer.
        MeldOperatorAan();

        var markup = Render().Markup;

        Assert.Contains("Fiatteren", markup, StringComparison.Ordinal);
        Assert.Contains("Uren boeken", markup, StringComparison.Ordinal);
    }

    // ── Gereedschap ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Rendert het urenscherm, eventueel met een querystring erachter.</summary>
    private IRenderedComponent<Bunit.Rendering.ContainerFragment> Render(string? query = null)
    {
        Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/klant/{EigenKlant}/uren{query}");

        return RenderPagina(Urenpagina);
    }

    private void MeldAanAls(bool operatorrol)
    {
        if (operatorrol)
        {
            MeldOperatorAan();
        }
        else
        {
            MeldKlantAan();
        }
    }

    /// <summary>De kaart met deze kop.</summary>
    /// <remarks>
    /// Op de toegankelijke naam en niet op een classnaam: <c>DataCard</c> en <c>FormCard</c> zetten
    /// hun kop als <c>aria-label</c>, en dat is de enige aanduiding die niet met de opmaak
    /// meebeweegt.
    /// </remarks>
    private static IElement Kaart(
        IRenderedComponent<Bunit.Rendering.ContainerFragment> cut,
        string kop)
    {
        var kaarten = cut.FindAll($"section[aria-label='{kop}']");

        Assert.True(
            kaarten.Count == 1,
            $"Er staan {kaarten.Count} kaarten met de kop \"{kop}\" op dit scherm; er hoort er " +
            "precies één te staan.");

        return kaarten[0];
    }

    /// <summary>De gewone rijen van een tabel, zonder de kop en zonder de totaalrij.</summary>
    private static IReadOnlyList<IElement> Regels(IElement kaart) =>
    [
        .. kaart.QuerySelectorAll(".data-row")
            .Where(rij => !rij.ClassList.Contains("data-row--total")),
    ];

    private static IReadOnlyList<IElement> Maandrijen(IElement kaart) => Regels(kaart);

    /// <summary>De uren per regel, uit de laatste getalcel van elke rij.</summary>
    /// <remarks>
    /// Uit de markup en niet uit het viewmodel: de vraag is of de rijen op het scherm optellen tot
    /// het totaal op het scherm. Een test die het viewmodel optelt, telt dezelfde som nog een keer.
    /// </remarks>
    private static IReadOnlyList<decimal> Urenperregel(IElement kaart) =>
        [.. Regels(kaart).Select(rij => Urenwaarde(rij.QuerySelectorAll(".num").Last()))];

    /// <summary>Het getal in de totaalrij van een tabel.</summary>
    private static decimal Voetregel(IElement kaart) =>
        Urenwaarde(kaart.QuerySelector(".data-row--total")!.QuerySelectorAll(".num").First());

    /// <summary>De kolom "Besteed" van de eerste maandrij.</summary>
    /// <remarks>
    /// De tweede getalcel: bundel, besteed, saldo. Op index en niet op een classnaam, want de cellen
    /// krijgen hun kolom van <c>RowGrid</c> en dragen geen eigen naam.
    /// </remarks>
    private static decimal Besteed(IElement kaart) =>
        Urenwaarde(Regels(kaart)[0].QuerySelectorAll(".num")[1]);

    /// <summary>Het aantal uren uit een cel als "9,5 u".</summary>
    /// <param name="cel">De <c>DataCell</c> met het getal erin.</param>
    /// <returns>Het getal.</returns>
    /// <remarks>
    /// <para><strong>Heet niet <c>Uren</c>.</strong> Dat verbergt <see cref="Portaalrendertest.Uren"/>
    /// — de weergavelaag van het urenscherm — en dan roept de volgende lezer de verkeerde aan. De
    /// waarschuwing daarover (CS0108) is niet met <c>new</c> onderdrukt: er is geen enkel verband
    /// tussen die twee, dus er valt ook niets te overschrijven.</para>
    ///
    /// <para><strong>Het schermlezerlabel gaat er eerst af.</strong> <c>DataCell</c> zet de kolomkop
    /// als eerste kind in de cel (zie <c>GridColumn.Labelled</c>): normaal in <c>.sr-only</c>, en
    /// onder 768px — waar de kolomkop verdwijnt — wordt datzelfde element de zichtbare aanduiding
    /// boven de waarde. Dat hóórt daar, en juist bij een getalkolom: een kale "9,5" zonder kop zegt
    /// niets. Maar het staat wel in <c>TextContent</c>, en <c>"Uren9,5"</c> is geen getal.</para>
    ///
    /// <para>Eén element overslaan en niet letters wegpoetsen. Zou deze helper alles wat geen cijfer
    /// is weghalen, dan leest hij een cel met per ongeluk twee waarden erin als één lang getal, en
    /// dan telt de test iets op wat er niet staat. Wat er overblijft is precies wat een ziende lezer
    /// in de kolom ziet.</para>
    /// </remarks>
    private static decimal Urenwaarde(IElement cel) =>
        decimal.Parse(
            Celwaarde(cel).Replace("u", string.Empty, StringComparison.Ordinal).Trim(),
            NumberStyles.Number | NumberStyles.AllowLeadingSign,
            CultureInfo.GetCultureInfo("nl-NL"));

    /// <summary>De tekst van een cel zonder het schermlezerlabel dat <c>DataCell</c> erin zet.</summary>
    private static string Celwaarde(IElement cel) =>
        string.Concat(
            cel.ChildNodes
                .Where(knoop => knoop is not IElement element
                    || !element.ClassList.Contains("data-cell__label"))
                .Select(knoop => knoop.TextContent));

    private static string Getal(decimal waarde) =>
        waarde.ToString("0.##", CultureInfo.GetCultureInfo("nl-NL"));

    /// <summary>Een urenregel voor een stand die de standaardgegevens niet hebben.</summary>
    private static HourEntryDocument Regel(HourEntryStatus stand, string omschrijving) => new()
    {
        Id = PortalDocumentIds.HourEntry($"test-{omschrijving.GetHashCode(StringComparison.Ordinal):x8}"),
        PartitionKey = Vasteportaalopslag.Standaardklant,
        CustomerId = Vasteportaalopslag.Standaardklant,
        Month = Vasteportaalopslag.Dezemaand,
        Category = HourCategories.Development,
        Note = omschrijving,
        Hours = 2m,
        Source = HourEntrySource.Portal,
        By = "Sanne de Wit",
        Status = stand,
        CreatedAt = Testgegevens.Nu,
        CreatedBy = "Sanne de Wit",
        ApprovedAt = stand == HourEntryStatus.Approved ? Testgegevens.Nu : null,
        ApprovedBy = stand == HourEntryStatus.Approved ? "Sanne de Wit" : null,
    };
}
