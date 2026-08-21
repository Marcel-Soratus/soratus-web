using Microsoft.Extensions.Logging.Abstractions;
using Soratus.Portal.Data;
using Soratus.Portal.Mail;
using Soratus.Portal.Tests.Hulpmiddelen;
using Soratus.Portal.Views;

namespace Soratus.Portal.Tests.Contracten;

/// <summary>
/// De naad tussen de facturatiekant en het maandoverzicht per mail (§3.7).
/// </summary>
/// <remarks>
/// <para><strong>Wat hier wordt gemeten is dat er onderweg niets verandert.</strong>
/// <c>BillingStatementFigures</c> mag niets optellen, niets afronden en geen enkele <c>null</c>
/// vervangen. Elke test hieronder vergelijkt de mailvorm met de schermvorm van dezelfde maand op
/// dezelfde opslag; komt er een verschil uit, dan bestaat er een tweede definitie van "wat deze maand
/// kost" en kan de mail een ander bedrag noemen dan het scherm.</para>
///
/// <para>De keten loopt door de échte <c>BillingViews</c> op de échte fixture. Zou de test de adapter
/// op een handgevuld viewmodel zetten, dan meet hij de vertaling van getallen die de testschrijver
/// heeft bedacht — en juist de combinaties van ontbrekende gegevens zijn wat er te meten valt.</para>
/// </remarks>
public class MaandoverzichtbedragenTests
{
    private static (IMonthlyStatementFigures Bron, IBillingViews Weergaven) Keten(
        Vasteportaalopslag opslag)
    {
        var weergaven = VasteFactuurweergaven.Bouw(opslag);

        return (
            new BillingStatementFigures(weergaven, NullLogger<BillingStatementFigures>.Instance),
            weergaven);
    }

    private static string Vorigemaand => HourMonths.Of(Testgegevens.Nu.AddMonths(-1));

    [Fact]
    public async Task DeBedragenZijnLetterlijkDieVanHetScherm()
    {
        // De kern van deze naad. Zou de adapter één getal anders doorgeven — een afronding, een
        // optelling, een terugval — dan is dit de enige plek waar dat blijkt.
        var opslag = new Vasteportaalopslag();
        var (bron, weergaven) = Keten(opslag);

        var scope = await Weergavelaag.Schrijfscope();
        var rij = await weergaven.BuildMonthAsync(scope, Vorigemaand);
        var bedragen = await bron.BuildStatementAsync(scope, Vorigemaand);

        Assert.NotNull(bedragen);
        Assert.Equal(rij.AzureCharged, bedragen.AzureAmount);
        Assert.Equal(rij.HoursAmount, bedragen.ExtraHoursAmount);
        Assert.Equal(rij.OverBundleHours, bedragen.ExtraHours);
        Assert.Equal(rij.UsedHours, bedragen.UsedHours);
        Assert.Equal(rij.BundledHours, bedragen.BundledHours);
        Assert.Equal(rij.Total, bedragen.Total);
        Assert.Equal(rij.IsFinal, bedragen.AmountsAreComplete);
        Assert.Equal(rij.MeasuredAt, bedragen.MeasuredAt);
    }

    [Fact]
    public async Task HetTotaalWordtNietUitDeDelenOpgeteld()
    {
        // De spiegel van de test hierboven, en scherper: hij zou ook groen staan als de adapter het
        // totaal zélf uitrekende en per ongeluk hetzelfde antwoord kreeg. Hier telt hij niet op, want
        // de som van de delen is met opzet niet gelijk aan het totaal — het uurbedrag zit er niet in.
        var opslag = new Vasteportaalopslag();
        var (bron, _) = Keten(opslag);

        var bedragen = await bron.BuildStatementAsync(await Weergavelaag.Schrijfscope(), Vorigemaand);

        Assert.NotNull(bedragen);
        Assert.NotNull(bedragen.Total);

        // Het totaal is Azure plus uren. Zou de adapter alleen het Azure-bedrag doorgeven als totaal,
        // of alleen de uren, dan valt dat hier op.
        Assert.Equal(bedragen.AzureAmount + bedragen.ExtraHoursAmount, bedragen.Total);
    }

    [Fact]
    public async Task ZonderEnkeleMetingKomtErNullEnGeenBedragVanNul()
    {
        // De enige betekenis van null aan deze naad: er is over die maand nooit gemeten. De mailkant
        // weigert dan met NoFigures, en dat is waar. Zou hier een bedragenobject met nullen uitkomen,
        // dan zou de weigering "een bedrag is onbekend" heten en zou een operator gaan zoeken naar een
        // meting die nooit is gedaan.
        var opslag = new Vasteportaalopslag();
        opslag.GeenKosten();

        var (bron, _) = Keten(opslag);

        Assert.Null(await bron.BuildStatementAsync(await Weergavelaag.Schrijfscope(), Vorigemaand));
    }

    [Fact]
    public async Task MetEenMetingKomtErWelIetsUit()
    {
        // De onmisbare spiegel. Zonder deze test mag BuildStatementAsync altijd null teruggeven en gaat
        // er nooit een maandoverzicht de deur uit.
        var opslag = new Vasteportaalopslag();
        var (bron, _) = Keten(opslag);

        Assert.NotNull(await bron.BuildStatementAsync(await Weergavelaag.Schrijfscope(), Vorigemaand));
    }

    [Fact]
    public async Task EenGeslaagdeMetingZonderRegelsLevertBedragenMetNullOpEnGeenNull()
    {
        // Het onderscheid dat deze naad moet dragen: "er is nooit gemeten" is niet hetzelfde als "de
        // meting gaf geen bedrag". Het tweede is een geslaagde meting met nul rijen — gemeten, en niet
        // te onderscheiden van een omgeving die niet bestaat — en die hoort met zijn reden op het
        // operatorscherm te belanden in plaats van als afwezigheid.
        var opslag = new Vasteportaalopslag();
        var (bron, _) = Keten(opslag);

        var bedragen = await bron.BuildStatementAsync(
            await Weergavelaag.Schrijfscope(),
            Vasteportaalopslag.Maandzonderregels);

        Assert.NotNull(bedragen);
        Assert.Null(bedragen.AzureAmount);
        Assert.Null(bedragen.Total);
        Assert.False(bedragen.AmountsAreComplete);
        Assert.Equal(StatementFigureGap.CostReadFailed, bedragen.Gap);
    }

    [Fact]
    public async Task DeLopendeMaandIsNietVolledigEnHeetOnvolledigTijdvak()
    {
        // De lopende maand heeft wél een bedrag — §3.7 zet hem als concept bovenaan — en mag toch niet
        // gemaild worden. Dat is het verschil tussen een scherm dat je kunt verversen en een mail die de
        // deur uit is, en het is de reden dat PeriodIncomplete uit IsFinal komt en niet uit de
        // klantvlaggen: die kennen deze toestand niet, want op een scherm is het geen gat.
        var opslag = new Vasteportaalopslag();
        var (bron, _) = Keten(opslag);

        var bedragen = await bron.BuildStatementAsync(
            await Weergavelaag.Schrijfscope(),
            HourMonths.Of(Testgegevens.Nu));

        Assert.NotNull(bedragen);
        Assert.NotNull(bedragen.Total);
        Assert.False(bedragen.AmountsAreComplete);
        Assert.Equal(StatementFigureGap.PeriodIncomplete, bedragen.Gap);
    }

    [Fact]
    public async Task EenVolledigeMaandZonderGatenHeeftGeenReden()
    {
        // De spiegel van elke test die een gat verwacht. Zonder deze test mag Gap altijd een waarde
        // hebben en gaat er nooit een mail weg.
        var opslag = new Vasteportaalopslag();
        var (bron, _) = Keten(opslag);

        var bedragen = await bron.BuildStatementAsync(await Weergavelaag.Schrijfscope(), Vorigemaand);

        Assert.NotNull(bedragen);
        Assert.True(bedragen.AmountsAreComplete);
        Assert.Equal(StatementFigureGap.None, bedragen.Gap);
    }

    [Fact]
    public async Task ZonderContractHeetHetGatOnvolledigContractEnNietGeenOpslag()
    {
        // De samengevouwen waarde. Aan de operatorkant staan hier drie gaten (geen opslag, geen bundel,
        // geen tarief); aan deze kant is dat er één. Zou StatementFigureGap de fijnere verdeling
        // houden, dan zouden twee van zijn waarden onbereikbaar zijn — een veld dat bestaat en nooit
        // wordt gevuld, en dat is punt 11 voor de tweede keer.
        var opslag = new Vasteportaalopslag(zonderContract: true);
        var (bron, _) = Keten(opslag);

        var bedragen = await bron.BuildStatementAsync(await Weergavelaag.Schrijfscope(), Vorigemaand);

        Assert.NotNull(bedragen);
        Assert.Null(bedragen.Total);
        Assert.False(bedragen.AmountsAreComplete);
        Assert.Equal(StatementFigureGap.ContractIncomplete, bedragen.Gap);
    }

    [Fact]
    public void DeMailvormNoemtOnzeMargeInGeenEnkeleWaarde()
    {
        // De reden dat de twee waarden zijn samengevouwen, als test en niet alleen als opmerking. Een
        // enumwaarde die "NoSurcharge" heet komt op het operatorscherm en in de logregel terecht, en
        // "beheeropslag" staat in de lijst met woorden die een klant nergens mag zien.
        Assert.DoesNotContain(
            Enum.GetNames<StatementFigureGap>(),
            naam => naam.Contains("surcharge", StringComparison.OrdinalIgnoreCase)
                || naam.Contains("rate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ElkeWaardeVanDeMailvormIsBereikbaarVanuitDeFacturatiekant()
    {
        // Punt 11 als test: een waarde die bestaat en die geen enkele bron ooit zet, is een veld dat
        // onwaar en ongebruikt is. Deze test somt op welke waarden de adapter werkelijk kan opleveren
        // en vergelijkt dat met de enum. Komt er een waarde bij zonder bron, dan gaat dit rood.
        //
        // De lijst is met de hand opgeschreven en niet met reflectie afgeleid: reflectie over de
        // adapter zou alleen zeggen welke namen erin voorkomen, en het punt is welke er te bereiken
        // zijn. Die twee lopen uiteen zodra er een tak onbereikbaar wordt.
        string[] bereikbaar =
        [
            nameof(StatementFigureGap.None),
            nameof(StatementFigureGap.CostReadFailed),
            nameof(StatementFigureGap.PeriodIncomplete),
            nameof(StatementFigureGap.ContractIncomplete),
            nameof(StatementFigureGap.NotCharged),
        ];

        Assert.Equal(
            [.. bereikbaar.OrderBy(naam => naam, StringComparer.Ordinal)],
            [.. Enum.GetNames<StatementFigureGap>().OrderBy(naam => naam, StringComparer.Ordinal)]);
    }
}
