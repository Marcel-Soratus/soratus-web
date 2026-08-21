using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Soratus.Portal.Data;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Zichtbaarheid;

/// <summary>
/// Het facturatiescherm (§3.7) per rol, en in beide richtingen.
/// </summary>
/// <remarks>
/// <para><strong>Elke test heeft zijn spiegel.</strong> Een test die alleen afwezigheid controleert
/// blijft groen als de pagina stukgaat of leeg blijft — dan is er niets, dus ook niet het verboden
/// gegeven.</para>
///
/// <para><strong>Dit is het vangnet en niet de grens.</strong> De echte grens is een typeverschil: een
/// klantscope levert een <c>CustomerBillingView</c> en dat type draagt de uitsplitsing en de marge
/// niet, en het klantcomponent kan ze dus niet renderen. Die kant staat in
/// <c>Presentatie.FactuurcomponentTests</c>, op typeniveau. Wat hier wordt gemeten is of de gegevens
/// die er wél op staan werkelijk op het scherm belanden, en of er niets in een tooltip is
/// geslopen.</para>
///
/// <para>De gegevens komen uit <see cref="Vasteportaalopslag"/> door de échte
/// <c>BillingViews</c>-projectie heen. Zou de fixture het klantpad zelf armer vullen, dan blijft elke
/// test hier groen omdat de fixture al filterde en niet omdat de scheiding werkt.</para>
/// </remarks>
public class FacturatieschermTests : Portaalrendertest
{
    private static Type Factuurpagina =>
        Paginaverzameling.MetRoute("/klant/{Slug}/facturatie")
        ?? throw new InvalidOperationException(
            "Er staat geen pagina op route '/klant/{Slug}/facturatie'. Is de route hernoemd, dan hoort "
            + "deze test mee te verhuizen — niet te verdwijnen.");

    // ── De uitsplitsing per dienst is operator-only (§2) ────────────────────────────────────────

    [Fact]
    public void EenKlantZietDeUitsplitsingPerDienstNiet()
    {
        // §2: "Facturatie: Azure per dienst + beheeropslag — nee". De dienstnaam is het gegeven dat de
        // uitsplitsing verraadt, en hij staat op geen enkel klanttype.
        MeldKlantAan();

        Assert.DoesNotContain(
            Vasteportaalopslag.Grootstedienst,
            Render(Metmaand).Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EenOperatorZietDieUitsplitsingWel()
    {
        // De spiegel. Zonder deze test zegt de test hierboven niets: een scherm dat helemaal geen
        // uitsplitsing meer heeft toont die dienstnaam ook niet.
        MeldOperatorAan();

        var markup = Render(Metmaand).Markup;

        Assert.Contains(Vasteportaalopslag.Grootstedienst, markup, StringComparison.Ordinal);
        Assert.Contains(Vasteportaalopslag.Centendienst, markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenKlantZietHetOpslagpercentageNergens()
    {
        // Onze marge. Dit is het gevaarlijkste gegeven van dit scherm: het staat als 8,75 in de
        // fixture en dat getal komt in geen enkel ander veld voor.
        MeldKlantAan();

        Assert.DoesNotContain(
            Getal(Vasteportaalopslag.Opslagpercentage),
            Render().Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EenOperatorZietHetOpslagpercentageWel()
    {
        MeldOperatorAan();

        Assert.Contains(
            Getal(Vasteportaalopslag.Opslagpercentage),
            Render().Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EenKlantZietDeBevraagdeOmgevingNiet()
    {
        // De scope staat op het operatorscherm omdat een geslaagd, leeg antwoord niet van een verkeerde
        // omgeving te onderscheiden is. Voor de klant is het de volledige omgeving uit §2, en die is
        // daar dicht.
        MeldKlantAan();

        Assert.DoesNotContain(
            Vasteportaalopslag.Kostenscope,
            Render(Metmaand).Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EenOperatorZietDeBevraagdeOmgevingBijEenMaandZonderRegels()
    {
        // De spiegel, én de reden dat dit veld bestaat. Gemeten: een resource group die niet bestaat
        // geeft HTTP 200 met nul regels — hetzelfde antwoord als een bestaande omgeving over een
        // periode die nog niet is geboekt. De code kan die twee niet uit elkaar halen; een mens die de
        // bevraagde scope ziet staan wel.
        // Zónder ?maand=, dus zonder de uitsplitsingskaart. De eerste versie van deze test rendeerde
        // mét die kaart, en daar staat de scope ook onder als losse regel — een mutatie die de scope van
        // de maandrij haalde maakte daardoor niets rood. Hier kan hij alleen uit de rij zelf komen.
        MeldOperatorAan();

        var rij = Maandrijen(Render())
            .FirstOrDefault(kandidaat => string.Equals(
                Maandnaam(kandidaat),
                HourMonths.Label(Vasteportaalopslag.Maandzonderregels),
                StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                "De maand zonder regels staat niet op het overzicht; deze test meet dan niets.");

        Assert.Contains(Vasteportaalopslag.Kostenscope, rij.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void BijEenMaandMetBedragenStaatDeOmgevingErNietBij()
    {
        // De spiegel: de scope hoort alleen bij een maand zonder regels te staan, want daar is hij het
        // enige gereedschap tegen een tikfout. Bij een maand met bedragen zegt hij niets nieuws en zou
        // hij elke rij twee regels hoger maken.
        MeldOperatorAan();

        var rij = Maandrijen(Render())
            .First(kandidaat => string.Equals(
                Maandnaam(kandidaat),
                HourMonths.Label(HourMonths.Of(Testgegevens.Nu.AddMonths(-1))),
                StringComparison.Ordinal));

        Assert.DoesNotContain(Vasteportaalopslag.Kostenscope, rij.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void EenKlantZietDeRedenVanEenMislukteMetingNiet()
    {
        // De storingstekst van de collector is bedrijfsvoering en geen klantgegeven — dezelfde afweging
        // als bij errorType op een mislukte run (punt 14).
        MeldKlantAan();

        Assert.DoesNotContain(Vasteportaalopslag.Meetfout, Render().Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenOperatorZietDieRedenWel()
    {
        MeldOperatorAan();

        Assert.Contains(Vasteportaalopslag.Meetfout, Render().Markup, StringComparison.Ordinal);
    }

    // ── Een streepje is geen nul ────────────────────────────────────────────────────────────────

    [Fact]
    public void EenMaandMetEenGeslaagdeMetingZonderRegelsToontGeenNulEuro()
    {
        // De kernopdracht van dit onderdeel, gemeten op het scherm. De maand heeft een geslaagde meting
        // met nul rijen; achter dat antwoord zitten drie werkelijkheden en maar één ervan is nul. Er
        // hoort dus een streepje te staan en geen bedrag.
        MeldOperatorAan();

        var cel = Bedragcel(Vasteportaalopslag.Maandzonderregels, kolom: 1);

        Assert.Equal("—", cel);
    }

    [Fact]
    public void EenMaandZonderEnkeleMetingToontGeenNulEuro()
    {
        // Geen document. Dezelfde regel als "geen document betekent geen status": de afwezigheid van een
        // meting is geen meting van nul. Deze maand staat op het overzicht omdat het contract hem dekt.
        MeldOperatorAan();

        Assert.Equal("—", Bedragcel(Vasteportaalopslag.Maandzondermeting, kolom: 1));
    }

    [Fact]
    public void EenMaandMetEenVolledigeMetingToontWelEenBedrag()
    {
        // De onmisbare spiegel van de twee tests hierboven. Zonder deze test mag elk bedrag een
        // streepje worden en staan ze allebei groen op een scherm dat nooit een getal toont.
        MeldOperatorAan();

        var vorigemaand = HourMonths.Of(Testgegevens.Nu.AddMonths(-1));
        var cel = Bedragcel(vorigemaand, kolom: 1);

        Assert.NotEqual("—", cel);
        Assert.Contains("€", cel, StringComparison.Ordinal);
    }

    [Fact]
    public void EenDienstDieMinderDanEenCentKostStaatErNietAlsNulEuro()
    {
        // Gemeten: Key Vault kostte over de hele maand € 0,000242498791899135. Als € 0,00 tonen zou
        // zeggen dat die dienst niets kost, en dat is dezelfde onwaarheid als € 0,00 voor een onbekend
        // bedrag — alleen kleiner.
        MeldOperatorAan();

        var markup = Render(Metmaand).Markup;

        Assert.Contains("&lt; € 0,01", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenDienstDieExactNulKostStaatErWelAlsNulEuro()
    {
        // De spiegel, en de enige test die aantoont dat "een streepje is geen nul" niet hetzelfde is als
        // "er staat nooit nul". Bandwidth stond gemeten op exact € 0,0000 en dat is een bedrag.
        MeldOperatorAan();

        var markup = Render(Metmaand).Markup;

        Assert.Contains(Vasteportaalopslag.Nuldienst, markup, StringComparison.Ordinal);
        Assert.Contains("€ 0,00", markup, StringComparison.Ordinal);
    }

    // ── De lopende maand staat bovenaan als concept (§3.7) ──────────────────────────────────────

    [Fact]
    public void DeLopendeMaandStaatBovenaanEnHeetLopend()
    {
        MeldOperatorAan();

        var rijen = Maandrijen(Render());

        Assert.Equal(HourMonths.Label(HourMonths.Of(Testgegevens.Nu)), Maandnaam(rijen[0]));
        Assert.Contains("loopt nog", rijen[0].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void EenAfgeslotenMaandHeetNietLopend()
    {
        // De spiegel: zonder deze test mag elke maand "loopt nog" heten, en dan is er geen maand meer
        // te factureren zonder dat er iets rood staat.
        MeldOperatorAan();

        var rijen = Maandrijen(Render());

        Assert.DoesNotContain("loopt nog", rijen[1].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void OokDeKlantZietDeLopendeMaandBovenaan()
    {
        MeldKlantAan();

        var rijen = Maandrijen(Render());

        Assert.Equal(HourMonths.Label(HourMonths.Of(Testgegevens.Nu)), Maandnaam(rijen[0]));
        Assert.Contains("loopt nog", rijen[0].TextContent, StringComparison.Ordinal);
    }

    // ── Azure en uren op één totaal (§3.7) ─────────────────────────────────────────────────────

    [Fact]
    public void ZonderJaartotaalStaatErEenStreepjeEnGeenDeelsom()
    {
        // De standaardgegevens hebben maanden zonder bedrag, dus er is geen jaartotaal. Een deelsom zou
        // niet van een compleet jaartotaal te onderscheiden zijn en hij is lager; van die twee fouten is
        // alleen "geen getal" zichtbaar.
        MeldOperatorAan();

        var totaal = Render().FindAll(".data-row--total .num").Last();

        Assert.Equal("—", Celwaarde(totaal));
    }

    [Fact]
    public void MetEenMetingOpElkeMaandIsErWelEenJaartotaal()
    {
        // De onmisbare spiegel. Zonder deze test mag YearTotal altijd null zijn en staat de test
        // hierboven groen over een scherm dat nooit een jaartotaal toont.
        //
        // Elke maand die op het overzicht staat krijgt een volledige meting. Dat is niet
        // omslachtigheid: de regel is dat één ontbrekende maand het hele jaartotaal wegneemt, dus de
        // spiegel kán alleen bestaan als er geen enkele maand ontbreekt.
        Opslag.GeenKosten();

        foreach (var maand in Maandenvanhetjaar())
        {
            Opslag.LegMetingVast(VolledigeMeting(maand));
        }

        MeldOperatorAan();

        var totaal = Celwaarde(Render().FindAll(".data-row--total .num").Last());

        Assert.NotEqual("—", totaal);
        Assert.Contains("€", totaal, StringComparison.Ordinal);
    }

    // ── Wat een streepje betekent, staat op het scherm ──────────────────────────────────────────

    [Fact]
    public void DeKlantLeestWatEenStreepjeBetekentZonderDeTechniekErachter()
    {
        MeldKlantAan();

        var markup = Render().Markup;

        Assert.Contains("Een streepje betekent", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Cost Management", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("404", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DeOperatorLeestDieTechniekWel()
    {
        // De spiegel, en de reden dat de twee teksten aparte constanten zijn: de operatortekst noemt
        // Cost Management en de 404, en dat is bedrijfsvoering. Een gedeelde tekst met een if erin is
        // één verschrijving verwijderd van onze bedrijfsvoering op het scherm van de klant.
        MeldOperatorAan();

        var markup = Render().Markup;

        Assert.Contains("Cost Management", markup, StringComparison.Ordinal);
        Assert.Contains("404", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenKlantLeestBijEenOnbekendVerbruikDatHetNogNietIsVastgesteld()
    {
        // Dit gat is met een mutatietest gevonden: de tak die de verbruikszin toevoegt uitzetten maakte
        // niets rood. Er stond wél een test op de contractzin, en die is een andere zin om een andere
        // reden — een klant met een compleet contract en een mislukte meting leest alleen deze.
        MeldKlantAan();

        var markup = Render().Markup;

        Assert.Contains(
            "Het verbruik van deze maand is nog niet vastgesteld",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EenKlantLeestDieZinNietBijEenMaandDieWelIsVastgesteld()
    {
        // De spiegel. Zonder deze test mag die zin bij élke maand staan, en dan zegt hij niets meer.
        // De vorige maand is volledig gemeten en heeft een totaal, dus daar hoort geen uitleg te staan.
        MeldKlantAan();

        var rij = Maandrijen(Render())
            .First(kandidaat => string.Equals(
                Maandnaam(kandidaat),
                HourMonths.Label(HourMonths.Of(Testgegevens.Nu.AddMonths(-1))),
                StringComparison.Ordinal));

        Assert.DoesNotContain("nog niet vastgesteld", rij.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void DeInterneBeheerklantLeestDatErNietsWordtDoorbelast()
    {
        // §4: de interne klant loopt op een beheercontract, intern en niet gefactureerd. Ook dit gat is
        // met een mutatietest gevonden — de tak die de interne klant vóór alle andere redenen afvangt
        // uitzetten maakte niets rood, omdat er geen enkele test met een interne klant was.
        //
        // Het verbruik hoort gemeten te blijven (de beheeragents draaien ergens en dat kost geld) en er
        // hoort géén bedrag van nul te staan: dat zou zeggen dat we een factuur van nul sturen.
        var intern = Autorisatiebron.Standaard();
        intern[0].IsInternal = true;

        MeldKlantAan(intern);

        var markup = Render().Markup;

        Assert.Contains("intern beheer van Soratus", markup, StringComparison.Ordinal);
        Assert.Contains("niet doorbelast", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenGewoneKlantLeestDatNiet()
    {
        // De spiegel: zonder deze test mag élke klant "niet doorbelast" te lezen krijgen.
        MeldKlantAan();

        Assert.DoesNotContain("niet doorbelast", Render().Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenMaandDieAlleenEenMetingHeeftValtNietVanHetOverzicht()
    {
        // Verbruik is geld, dus een maand met kosten buiten de contractperiode hoort zichtbaar te zijn.
        // Ook dit gat is met een mutatietest gevonden: de vereniging met de gemeten maanden weghalen
        // maakte niets rood, omdat in de standaardgegevens élke gemeten maand toch al binnen de
        // contractperiode viel.
        //
        // Het contract van de fixture gaat in op 2025-11-01, dus juni 2025 valt erbuiten: hij heeft geen
        // urenstand en zou zonder die vereniging nergens staan.
        Opslag.LegMetingVast(VolledigeMeting("2025-06"));

        MeldOperatorAan();

        var maanden = Maandrijen(Render("?jaar=2025")).Select(Maandnaam).ToArray();

        Assert.Contains(HourMonths.Label("2025-06"), maanden);
    }

    [Fact]
    public void EenKlantZonderContractLeestDatErNogAfsprakenOpenstaan()
    {
        // De klantvariant van de drie contractgaten: één zin, en die noemt de beheeropslag niet.
        Opslag = new Vasteportaalopslag(zonderContract: true);

        MeldKlantAan();

        var markup = Render().Markup;

        Assert.Contains("contractafspraken open", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("opslag", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("urenbundel", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EenOperatorZonderContractLeestWelkeDrieAfsprakenOntbreken()
    {
        // De spiegel. Een operator die er één ziet gaat die oplossen en houdt dan een totaal dat nog
        // steeds ontbreekt — daarom staan alle gaten er, en niet alleen de eerste.
        Opslag = new Vasteportaalopslag(zonderContract: true);

        MeldOperatorAan();

        var markup = Render().Markup;

        Assert.Contains("geen beheeropslag afgesproken", markup, StringComparison.Ordinal);
        Assert.Contains("geen urenbundel vastgelegd", markup, StringComparison.Ordinal);
    }

    // ── Gereedschap ─────────────────────────────────────────────────────────────────────────────

    /// <summary>De querystring die de uitsplitsing van de lopende maand openklapt.</summary>
    private static string Metmaand => Bijmaand(HourMonths.Of(Testgegevens.Nu));

    private static string Bijmaand(string maand) =>
        $"?jaar={HourMonths.YearOf(maand)}&maand={maand}";

    /// <summary>
    /// Rendert het facturatiescherm, eventueel met een querystring erachter.
    /// </summary>
    /// <remarks>
    /// Heet niet <c>Facturatie</c>: dat verbergt <see cref="Portaalrendertest.Facturatie"/> — de
    /// weergavelaag — en dan roept de volgende lezer de verkeerde aan.
    /// </remarks>
    private IRenderedComponent<Bunit.Rendering.ContainerFragment> Render(string? query = null)
    {
        Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/klant/{EigenKlant}/facturatie{query}");

        return RenderPagina(Factuurpagina);
    }

    /// <summary>De maandrijen van de eerste tabel, zonder de totaalrij.</summary>
    private IReadOnlyList<AngleSharp.Dom.IElement> Maandrijen(
        IRenderedComponent<Bunit.Rendering.ContainerFragment> cut) =>
    [
        .. cut.FindAll(".card .data-row")
            .Where(rij => !rij.ClassList.Contains("data-row--total")),
    ];

    /// <summary>
    /// De maandnaam van een rij: alleen het label, zonder de meta-regels eromheen.
    /// </summary>
    /// <remarks>
    /// <para>Alleen de <em>tekstknopen</em> van het eerste span in de maandcel, en niet zijn
    /// <c>TextContent</c>. Die laatste levert <c>"augustus 2026· loopt nog"</c> op, want "loopt nog"
    /// staat als genest span in dezelfde cel — gemeten, en het is de eerste vorm die deze helper
    /// had.</para>
    ///
    /// <para>Eén knooptype overslaan en niet met tekstbewerking de meta-regels wegpoetsen. Zou deze
    /// helper op "·" afkappen, dan leest hij een maandlabel dat om een andere reden een punt bevat
    /// half — en dan vindt <see cref="Bedragcel"/> de rij niet en meet de test stil niets.</para>
    /// </remarks>
    private static string Maandnaam(AngleSharp.Dom.IElement rij) =>
        string.Concat(
            rij.QuerySelectorAll(".data-cell")
                .First()
                .QuerySelectorAll("span")
                .First()
                .ChildNodes
                .Where(knoop => knoop.NodeType == AngleSharp.Dom.NodeType.Text)
                .Select(knoop => knoop.TextContent))
            .Trim();

    /// <summary>
    /// De waarde van een bedragkolom op de rij van deze maand.
    /// </summary>
    /// <param name="maand">De maand als <c>yyyy-MM</c>.</param>
    /// <param name="kolom">De nulgebaseerde kolomindex binnen de rij.</param>
    /// <returns>De celtekst zonder het schermlezerlabel.</returns>
    /// <remarks>
    /// Op de cel en niet op de hele rij, want een rij bevat ook de maandnaam en de toestand — en een
    /// test die "—" in de rijtekst zoekt vindt hem in elke rij zodra er één kolom leeg is.
    /// </remarks>
    private string Bedragcel(string maand, int kolom)
    {
        var rij = Maandrijen(Render())
            .FirstOrDefault(kandidaat =>
                string.Equals(Maandnaam(kandidaat), HourMonths.Label(maand), StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"De maand {maand} staat niet op het facturatieoverzicht. Zonder die rij meet deze "
                + "test niets; controleer Vasteportaalopslag.Kosten.");

        return Celwaarde(rij.QuerySelectorAll(".data-cell").ElementAt(kolom));
    }

    /// <summary>De tekst van een cel zonder het schermlezerlabel dat <c>DataCell</c> erin zet.</summary>
    /// <remarks>
    /// Dezelfde reden als bij het urenscherm: <c>DataCell</c> zet de kolomkop als eerste kind in de cel,
    /// en <c>"Subtotaal—"</c> is geen bedrag.
    /// </remarks>
    private static string Celwaarde(AngleSharp.Dom.IElement cel) =>
        string.Concat(
            cel.ChildNodes
                .Where(knoop => knoop is not AngleSharp.Dom.IElement element
                    || !element.ClassList.Contains("data-cell__label"))
                .Select(knoop => knoop.TextContent))
            .Trim();

    private static string Getal(decimal waarde) =>
        waarde.ToString("0.##", CultureInfo.GetCultureInfo("nl-NL"));

    /// <summary>
    /// De maanden van dit jaar die op het facturatieoverzicht horen te staan.
    /// </summary>
    /// <remarks>
    /// Uit dezelfde functie die het overzicht gebruikt en niet uit een eigen lus over twaalf maanden.
    /// Zou de test twaalf maanden zaaien en het scherm er acht tonen, dan is de test groen om de
    /// verkeerde reden; zou hij er acht zaaien en het scherm er negen tonen, dan is hij rood zonder
    /// dat er iets mis is.
    /// </remarks>
    private IReadOnlyList<string> Maandenvanhetjaar() =>
        HourBalanceCalculator.MonthsInScope(
            Testgegevens.Nu.Year,
            new DateOnly(2025, 11, 1),
            DateOnly.FromDateTime(Testgegevens.Nu.DateTime),
            Opslag.Urenregels());

    /// <summary>Een volledig gemeten maand, voor de standen die de standaardgegevens niet hebben.</summary>
    private static AzureCostDocument VolledigeMeting(string maand) => new()
    {
        Id = AzureCostDocumentKeys.ForMonth(maand),
        PartitionKey = Vasteportaalopslag.Standaardklant,
        CustomerId = Vasteportaalopslag.Standaardklant,
        Month = maand,
        State = AzureCostState.Measured,
        Lines = [new AzureCostLine { Service = "Azure App Service", Amount = 12.34m }],
        Currency = "EUR",
        Scope = Vasteportaalopslag.Kostenscope,
        MeasuredAt = Testgegevens.Nu,
        CoversThrough = AzureCostCompleteness.Bounds(maand).Last.ToString("yyyy-MM-dd"),
    };
}
