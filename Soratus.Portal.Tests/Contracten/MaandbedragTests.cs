using Soratus.Portal.Data;

namespace Soratus.Portal.Tests.Contracten;

/// <summary>
/// De berekening van het maandbedrag: Azure plus beheeropslag, plus de uren boven bundel, op één
/// totaal (§3.7).
/// </summary>
/// <remarks>
/// <para><strong>Dit is besluit 15 op de plek waar hij geld kost.</strong> Vier getallen komen hier
/// samen die elk om een eigen reden kunnen ontbreken, en een <c>?? 0m</c> op één ervan levert een
/// totaal op dat te laag is en er geloofwaardig uitziet. Elke test hieronder is een combinatie van
/// aanwezig en afwezig, en de assertie is bijna altijd <c>Assert.Null</c> — met de spiegel ernaast,
/// want een berekening die altijd <c>null</c> teruggeeft haalt elke <c>Assert.Null</c>.</para>
///
/// <para>De bedragen zijn de gemeten bedragen: <c>37,4563985414928</c> is wat Cost Management op
/// 21 augustus 2026 voor <c>Azure App Service</c> gaf. Ronde getallen zouden verbergen dat er ergens
/// één keer wordt afgerond.</para>
/// </remarks>
public class MaandbedragTests
{
    private const decimal Subtotaal = 37.4563985414928m;

    private const decimal Opslag = 8.75m;

    private const decimal Tarief = 137.5m;

    private static MonthlyCharge Bereken(
        decimal? subtotaal = Subtotaal,
        decimal? opslag = Opslag,
        decimal? bovenBundel = 4m,
        decimal? tarief = Tarief,
        AzureCostState toestand = AzureCostState.Measured,
        bool intern = false) =>
        MonthlyChargeCalculator.ForMonth(
            "2026-07",
            "juli 2026",
            toestand,
            subtotaal,
            opslag,
            bovenBundel,
            tarief,
            intern);

    // ── De volle berekening ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void MetAlleVierDeGetallenIsErEenTotaalEnTeltDeKolomOp()
    {
        // De spiegel van elke Assert.Null hieronder. Zonder deze test mag ForMonth altijd null
        // teruggeven en is er nooit iets te factureren.
        var bedrag = Bereken();

        Assert.Equal(37.46m, bedrag.AzureSubtotal);
        Assert.Equal(3.28m, bedrag.SurchargeAmount);
        Assert.Equal(40.74m, bedrag.AzureCharged);
        Assert.Equal(550.00m, bedrag.HoursAmount);
        Assert.Equal(590.74m, bedrag.Total);
        Assert.Equal(MonthlyChargeGap.None, bedrag.Gap);
        Assert.True(bedrag.HasTotal);

        // En de eigenschap waar dit hele scherm op leunt: wat er staat telt op. Zou het door te
        // belasten bedrag een eigen afronding van subtotaal × (1 + opslag) zijn, dan wijkt het af van
        // de twee bedragen erboven en maakt een kolom die niet optelt een lezer terecht wantrouwig
        // over het hele scherm.
        Assert.Equal(bedrag.AzureCharged, bedrag.AzureSubtotal + bedrag.SurchargeAmount);
        Assert.Equal(bedrag.Total, bedrag.AzureCharged + bedrag.HoursAmount);
    }

    [Fact]
    public void HetAfrondenGaatNaarBovenBijEenHalveCent()
    {
        // MidpointRounding.AwayFromZero en niet de standaard ToEven. Dat laatste is voor statistiek de
        // juiste keuze en voor een factuur de verkeerde: daar gaat een halve cent naar boven. Het
        // verschil is één cent en het valt op precies die facturen op waar iemand naneemt.
        //
        // 0,125 × 100 = 12,5 cent. ToEven maakt daar 0,12 van, AwayFromZero 0,13.
        var bedrag = Bereken(subtotaal: 1.25m, opslag: 10m, bovenBundel: 0m, tarief: null);

        Assert.Equal(0.13m, bedrag.SurchargeAmount);
    }

    // ── Onbekend wordt geen nul ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ZonderAzureBedragIsErGeenTotaalEnGeenNul()
    {
        var bedrag = Bereken(subtotaal: null, toestand: AzureCostState.Unknown);

        Assert.Null(bedrag.AzureSubtotal);
        Assert.Null(bedrag.SurchargeAmount);
        Assert.Null(bedrag.AzureCharged);
        Assert.Null(bedrag.Total);
        Assert.True(bedrag.Gap.HasFlag(MonthlyChargeGap.AzureUnknown));

        // Het uurbedrag blijft wél bekend. Dat is geen inconsistentie: de uren staan vast, alleen het
        // verbruik niet. Wat er niet mag is dat het uurbedrag als totaal doorgaat.
        Assert.Equal(550.00m, bedrag.HoursAmount);
        Assert.NotEqual(bedrag.HoursAmount, bedrag.Total);
    }

    [Fact]
    public void ZonderAfgesprokenOpslagIsHetDoorTeBelastenBedragOnbekend()
    {
        // Besluit 15, op het veld dat daar als het gevaarlijkste wordt aangewezen. Nul procent opslag is
        // een afspraak; geen opslag ingevuld is een afspraak die nog moet komen. Zou de tweede als de
        // eerste doorrekenen, dan is het door te belasten bedrag gelijk aan de inkoop — onze marge weg,
        // zonder dat er iets aan het getal te zien is.
        var bedrag = Bereken(opslag: null);

        Assert.Equal(37.46m, bedrag.AzureSubtotal);
        Assert.Null(bedrag.SurchargeAmount);
        Assert.Null(bedrag.AzureCharged);
        Assert.Null(bedrag.Total);
        Assert.True(bedrag.Gap.HasFlag(MonthlyChargeGap.NoSurchargeAgreed));
    }

    [Fact]
    public void NulProcentOpslagIsEenAfspraakEnGeenGat()
    {
        // De spiegel van de test hierboven, en de enige die het verschil tussen null en nul aantoont.
        // Zonder deze test mag ForMonth elk percentage als "niet afgesproken" behandelen.
        var bedrag = Bereken(opslag: 0m);

        Assert.Equal(0m, bedrag.SurchargeAmount);
        Assert.Equal(37.46m, bedrag.AzureCharged);
        Assert.Equal(587.46m, bedrag.Total);
        Assert.False(bedrag.Gap.HasFlag(MonthlyChargeGap.NoSurchargeAgreed));
    }

    [Fact]
    public void ZonderVastgelegdeBundelIsErGeenUurbedragEnGeenTotaal()
    {
        // Punt 19 die in de facturatie doorwerkt. Zonder bundel is er geen overschrijding, en "nul uur
        // boven bundel" zou zeggen dat een klant binnen een afspraak valt die niet bestaat. Dit is
        // vandaag geen randgeval: in platform/customers staan zeven klanten en geen enkel contract.
        var bedrag = Bereken(bovenBundel: null);

        Assert.Null(bedrag.HoursAmount);
        Assert.Null(bedrag.Total);
        Assert.True(bedrag.Gap.HasFlag(MonthlyChargeGap.NoBundleAgreed));
    }

    [Fact]
    public void NulUurBovenBundelKostNulEuroOokZonderAfgesprokenTarief()
    {
        // Het enige juiste antwoord, en het scheidt zich van de test hierboven: bij een klant die binnen
        // zijn bundel blijft valt er niets te factureren, en dan is het ontbreken van een tarief geen
        // belemmering. Zou hier null uitkomen, dan is een klant die netjes binnen zijn bundel blijft
        // niet te factureren zolang niemand een tarief heeft ingevuld dat toch niet gebruikt wordt.
        var bedrag = Bereken(bovenBundel: 0m, tarief: null);

        Assert.Equal(0m, bedrag.HoursAmount);
        Assert.Equal(40.74m, bedrag.Total);
        Assert.False(bedrag.Gap.HasFlag(MonthlyChargeGap.NoRateAgreed));
    }

    [Fact]
    public void MetUrenBovenBundelMaarZonderTariefIsHetUurbedragOnbekend()
    {
        // De spiegel van de test hierboven. Nu is het tarief wél nodig, en het ontbreken ervan is een
        // blokkade en geen nul: die uren zijn werkelijk gemaakt en gefiatteerd.
        var bedrag = Bereken(tarief: null);

        Assert.Null(bedrag.HoursAmount);
        Assert.Null(bedrag.Total);
        Assert.True(bedrag.Gap.HasFlag(MonthlyChargeGap.NoRateAgreed));
        Assert.False(bedrag.Gap.HasFlag(MonthlyChargeGap.NoBundleAgreed));
    }

    [Fact]
    public void EenKlantZonderContractMistDrieAfsprakenTegelijk()
    {
        // Waarom de gaten vlaggen zijn en geen enkele waarde: een operator die er één ziet gaat die
        // oplossen en houdt dan een totaal dat nog steeds ontbreekt.
        var bedrag = Bereken(opslag: null, bovenBundel: null, tarief: null);

        Assert.True(bedrag.Gap.HasFlag(MonthlyChargeGap.NoSurchargeAgreed));
        Assert.True(bedrag.Gap.HasFlag(MonthlyChargeGap.NoBundleAgreed));
        Assert.Null(bedrag.Total);
    }

    [Fact]
    public void EenGemetenNulIsEenBedragEnGeenGat()
    {
        // De andere helft van de kernregel. In de echte uitvoer staan Bandwidth en Microsoft Entra op
        // exact € 0,0000; een maand die alleen zulke regels heeft, heeft een subtotaal van nul, en dát
        // is een bedrag. Zonder deze test is "een streepje is geen nul" niet te onderscheiden van "er
        // staat nooit nul".
        var bedrag = Bereken(subtotaal: 0m);

        Assert.Equal(0m, bedrag.AzureSubtotal);
        Assert.Equal(0m, bedrag.AzureCharged);
        Assert.Equal(550.00m, bedrag.Total);
        Assert.Equal(MonthlyChargeGap.None, bedrag.Gap);
    }

    // ── De interne klant ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeInterneKlantHeeftEenGemetenVerbruikEnGeenDoorTeBelastenBedrag()
    {
        // §4: de interne klant loopt op een beheercontract, intern en niet gefactureerd. Het verbruik
        // is een feit — de beheeragents draaien ergens en dat kost geld — maar er valt niets door te
        // belasten, en € 0,00 zou zeggen dat we een factuur van nul sturen.
        var bedrag = Bereken(intern: true);

        Assert.Equal(37.46m, bedrag.AzureSubtotal);
        Assert.Null(bedrag.SurchargeAmount);
        Assert.Null(bedrag.AzureCharged);
        Assert.Null(bedrag.HoursAmount);
        Assert.Null(bedrag.Total);
        Assert.True(bedrag.IsInternal);
        Assert.False(bedrag.IsFinal);
    }

    // ── Definitief ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AzureCostState.Measured, true)]
    [InlineData(AzureCostState.Partial, false)]
    [InlineData(AzureCostState.NoLines, false)]
    [InlineData(AzureCostState.Unknown, false)]
    public void AlleenEenVolledigGemetenMaandMetEenTotaalIsDefinitief(
        AzureCostState toestand,
        bool definitief)
    {
        // De vraag die de facturatie-agent stelt. Let op het verschil met HasTotal: bij Partial staat er
        // wél een getal — dat is het concept van de lopende maand uit §3.7 — en dat getal mag niet op
        // een factuur.
        var bedrag = Bereken(
            subtotaal: toestand is AzureCostState.Measured or AzureCostState.Partial ? Subtotaal : null,
            toestand: toestand);

        Assert.Equal(definitief, bedrag.IsFinal);
    }

    [Fact]
    public void EenLopendeMaandHeeftWelEenBedragMaarIsNietDefinitief()
    {
        // §3.7 vraagt dit met zoveel woorden: de lopende maand staat bovenaan als concept met live
        // berekende bedragen. Er staat dus een getal, en het is niet te factureren.
        var bedrag = Bereken(toestand: AzureCostState.Partial);

        Assert.True(bedrag.HasTotal);
        Assert.False(bedrag.IsFinal);
    }
}
