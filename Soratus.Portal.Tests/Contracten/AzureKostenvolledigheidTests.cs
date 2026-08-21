using Soratus.Portal.Data;

namespace Soratus.Portal.Tests.Contracten;

/// <summary>
/// De volledigheidscontrole op een maand Azure-verbruik (§3.7).
/// </summary>
/// <remarks>
/// <para><strong>Wat hier wordt gemeten is het verschil tussen een cron-tijd kiezen en weten
/// waarom.</strong> Het onderzoek naar fase 4 adviseerde de volledigheid te controleren in plaats van
/// de facturatie-agent later te laten draaien, en <see cref="AzureCostCompleteness"/> is dat advies.
/// De getallen eronder zijn gemeten op 21 augustus 2026 tegen <c>resourceGroups/MBV</c>: negentien
/// volle dagen lagen tussen € 1,87731 en € 1,87967, de onvolledige dag stond op 95,97% van de
/// mediaan, en de dag van vandaag ontbrak nog helemaal.</para>
///
/// <para>Elke test heeft zijn spiegel. Een test die alleen "onvolledig" bewijst blijft groen als de
/// methode nooit iets anders teruggeeft, en dan is er geen maand meer te factureren.</para>
/// </remarks>
public class AzureKostenvolledigheidTests
{
    private static readonly DateOnly Juli1 = new(2026, 7, 1);

    private static readonly DateOnly Juli31 = new(2026, 7, 31);

    /// <summary>Alle dagen van juli 2026, zoals een volledige meting ze oplevert.</summary>
    private static IEnumerable<DateOnly> HeleJuli() =>
        Enumerable.Range(0, 31).Select(dag => Juli1.AddDays(dag));

    [Fact]
    public void EenMaandZonderDagenIsGeenNulMaarGeenRegels()
    {
        // De kern van de opdracht. Gemeten: een resource group die niet bestaat én een bestaande
        // resource group over een periode die nog niet geboekt is geven bééide HTTP 200 met nul rijen.
        // Dat mag nooit "volledig gemeten, nul euro" worden.
        var uitkomst = AzureCostCompleteness.Judge("2026-07", [], new DateOnly(2026, 9, 1));

        Assert.Equal(AzureCostState.NoLines, uitkomst.State);
        Assert.Null(uitkomst.CoversThrough);
    }

    [Fact]
    public void EenVolleMaandDieRuimDaarnaIsGemetenIsVolledig()
    {
        // De spiegel van de test hierboven en van elke test die "onvolledig" verwacht. Zonder deze
        // test mag Judge altijd Partial teruggeven en is er geen enkele maand te factureren.
        var uitkomst = AzureCostCompleteness.Judge("2026-07", HeleJuli(), new DateOnly(2026, 8, 15));

        Assert.Equal(AzureCostState.Measured, uitkomst.State);
        Assert.Equal(Juli31, uitkomst.CoversThrough);
    }

    [Fact]
    public void DeLopendeMaandIsNooitVolledig()
    {
        // §3.7: de lopende maand staat bovenaan als concept. Dat is hier geen presentatiekeuze maar
        // een uitkomst: de laatste dag van de maand kan nog niet geboekt zijn, want hij is nog niet om.
        var tot20 = Enumerable.Range(0, 20).Select(dag => new DateOnly(2026, 8, 1).AddDays(dag));

        var uitkomst = AzureCostCompleteness.Judge("2026-08", tot20, new DateOnly(2026, 8, 21));

        Assert.Equal(AzureCostState.Partial, uitkomst.State);
        Assert.Equal(new DateOnly(2026, 8, 20), uitkomst.CoversThrough);
    }

    [Fact]
    public void EenMetingOpDeEersteVanDeVolgendeMaandIsNogNietVolledig()
    {
        // Dit is het gemeten antwoord op openstaande vraag 9 van het haalbaarheidsonderzoek: staat de
        // laatste dag van een maand om 06:00 op de 1e volledig in Cost Management? Nee — en een meting
        // op dat moment kan niet vaststellen dát hij er staat.
        //
        // De onderbouwing: op 21 augustus 06:55 UTC stond de 20e op 95,97% van een volle dag. De
        // boeking loopt dus ongeveer acht uur achter, en om 06:00 op de 1e is het laatste uur van de
        // vorige maand nog niet binnen. De cron `0 6 1 * *` uit §4 van de spec factureert daarmee een
        // maand met een halve dag Azure erin, en dat is aan het bedrag niet te zien.
        var uitkomst = AzureCostCompleteness.Judge("2026-07", HeleJuli(), new DateOnly(2026, 8, 1));

        Assert.Equal(AzureCostState.Partial, uitkomst.State);
    }

    [Fact]
    public void EenMetingEenDagLaterIsWelVolledig()
    {
        // De spiegel: de grens ligt bij twee dagen ná het einde van de maand, niet verder. Zonder deze
        // test mag SettlementDays onbeperkt oplopen en wordt er nooit meer gefactureerd.
        var uitkomst = AzureCostCompleteness.Judge("2026-07", HeleJuli(), new DateOnly(2026, 8, 2));

        Assert.Equal(AzureCostState.Measured, uitkomst.State);
    }

    [Fact]
    public void EenGatMiddenInDeMaandMaaktDeMaandNietOnvolledig()
    {
        // Cost Management geeft voor een dag zonder kosten géén rij. Een klant wiens omgeving een dag
        // uit stond heeft dus een echt gat, en dat gat is niet te onderscheiden van een dag die nog
        // niet is geboekt. Zou een gat tot "onvolledig" leiden, dan is die klant nooit te factureren.
        var metGat = HeleJuli().Where(dag => dag.Day != 15);

        var uitkomst = AzureCostCompleteness.Judge("2026-07", metGat, new DateOnly(2026, 8, 10));

        Assert.Equal(AzureCostState.Measured, uitkomst.State);
        Assert.Equal(Juli31, uitkomst.CoversThrough);
    }

    [Fact]
    public void EenDagBuitenDeMaandTeltNietMee()
    {
        // Zo'n dag betekent dat de bevraagde periode niet de maand was. Negeren is de veilige kant:
        // liever een maand die niet gefactureerd wordt dan een maand met de kosten van een andere
        // periode erin. Hier zou meetellen de maand volledig laten lijken op de dagen van augustus.
        var uitkomst = AzureCostCompleteness.Judge(
            "2026-07",
            [new DateOnly(2026, 6, 30), new DateOnly(2026, 7, 5), new DateOnly(2026, 8, 3)],
            new DateOnly(2026, 9, 1));

        Assert.Equal(AzureCostState.Partial, uitkomst.State);
        Assert.Equal(new DateOnly(2026, 7, 5), uitkomst.CoversThrough);
    }

    [Fact]
    public void AlleenDagenBuitenDeMaandIsGeenRegels()
    {
        // De uiterste vorm van de test hierboven: als er na het filteren niets overblijft, is dat
        // "geen regels" en niet "nul euro". Anders zou een verkeerd bevraagde periode een bedrag
        // opleveren dat nergens over gaat.
        var uitkomst = AzureCostCompleteness.Judge(
            "2026-07",
            [new DateOnly(2026, 6, 30), new DateOnly(2026, 8, 3)],
            new DateOnly(2026, 9, 1));

        Assert.Equal(AzureCostState.NoLines, uitkomst.State);
        Assert.Null(uitkomst.CoversThrough);
    }

    [Fact]
    public void DubbeleDagenVeranderenNiets()
    {
        // Met dagkorrel én groepering op dienst staat elke dag er zo vaak in als er diensten zijn:
        // gemeten waren dat vijfenzestig rijen over twintig dagen. De aanroeper hoeft die dus niet te
        // ontdubbelen.
        var uitkomst = AzureCostCompleteness.Judge(
            "2026-07",
            HeleJuli().Concat(HeleJuli()).Concat(HeleJuli()),
            new DateOnly(2026, 8, 10));

        Assert.Equal(AzureCostState.Measured, uitkomst.State);
        Assert.Equal(Juli31, uitkomst.CoversThrough);
    }

    [Theory]
    [InlineData("2026-02", 28)]
    [InlineData("2028-02", 29)]
    [InlineData("2026-04", 30)]
    [InlineData("2026-12", 31)]
    public void DeLaatsteDagVanDeMaandKomtUitDeKalender(string maand, int dagen)
    {
        // Een tabel met maandlengtes zou februari 2028 op 28 dagen zetten, en dan valt de 29e stil
        // buiten de facturatie: de maand heet dan volledig terwijl er een dag ontbreekt.
        var (eerste, laatste) = AzureCostCompleteness.Bounds(maand);

        Assert.Equal(1, eerste.Day);
        Assert.Equal(dagen, laatste.Day);
        Assert.Equal(eerste.Month, laatste.Month);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("2026-13")]
    [InlineData("08-2026")]
    [InlineData("2026")]
    public void EenOnleesbareMaandWerptEnLevertGeenStilleUitkomst(string maand)
    {
        // De maand wordt door de collector samengesteld en niet ingetypt, dus dit is een fout in de
        // aanroeper. Zou hij stil "geen regels" opleveren, dan is een verkeerd samengestelde
        // maandsleutel niet van een klant zonder verbruik te onderscheiden — en dat is precies de
        // verwarring die dit hele onderdeel opheft.
        Assert.ThrowsAny<ArgumentException>(
            () => AzureCostCompleteness.Judge(maand, [], new DateOnly(2026, 9, 1)));
    }
}
