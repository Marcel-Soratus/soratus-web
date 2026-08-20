using Soratus.Portal.Components.Pages.Klant;
using Soratus.Portal.Components.Shared;
using Soratus.Portal.Data;

namespace Soratus.Portal.Tests.Presentatie;

/// <summary>
/// De getalvormen en de woorden van het urenscherm: wat er staat als er niets is, en wat er staat
/// als er wél iets is.
/// </summary>
/// <remarks>
/// <para>De helft van deze tests gaat over <c>null</c>, en dat is geen overdaad. Punt 15 van de
/// afwijkingennotitie zegt dat een ontbrekend bedrag niet nul is, en punt 19 dat een maand zonder
/// bundel geen maand met een bundel van nul is. Deze laag is de plek waar dat verschil stil kan
/// sneuvelen: er wordt hier niets meer gerekend, dus een <c>?? 0</c> hier valt nergens anders meer
/// op — hij levert alleen een scherm op waar "0 u" staat waar niets is afgesproken.</para>
/// </remarks>
public class HourTextTests
{
    // ── Uren en saldi ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EenBundelDieNietIsVastgelegdIsEenStreepjeEnGeenNul()
    {
        // Punt 15, op het scherm. "0 u" zou zeggen dat er een bundel van nul uur is afgesproken —
        // alles gaat per uur — en dat is een andere afspraak dan geen afspraak.
        Assert.Equal("—", HourText.Hours((decimal?)null));
    }

    [Fact]
    public void EenBundelVanNulUurIsWelDegelijkNulUur()
    {
        // De spiegel, en zonder deze zou de test hierboven ook te halen zijn door elk getal een
        // streepje te maken. Nul is een afspraak en hoort als getal op het scherm te staan.
        Assert.Equal("0 u", HourText.Hours(0m));
    }

    [Theory]
    [InlineData(12, "12 u")]
    [InlineData(2.5, "2,5 u")]
    [InlineData(-0.5, "-0,5 u")]
    public void UrenStaanInNederlandseVormMetDeEenheidErachter(decimal uren, string verwacht) =>
        Assert.Equal(verwacht, HourText.Hours(uren));

    [Fact]
    public void EenSaldoZonderBundelIsEenStreepjeEnGeenNegatiefGetal()
    {
        // Punt 19: met een niet-nullable bundel zou het saldo van een klant zonder afspraak
        // 0 - geboekt zijn, dus negatief, dus "boven bundel". Dan staat er op het scherm dat een
        // klant zijn bundel overschrijdt die er nooit een had.
        Assert.Equal("—", HourText.Balance(null));
    }

    [Theory]
    [InlineData(2.5, "+2,5 u")]
    [InlineData(0, "+0 u")]
    [InlineData(-4, "-4 u")]
    public void EenSaldoDraagtAltijdEenTeken(decimal saldo, string verwacht) =>
        // Exact nul krijgt een plus: de bundel is precies op, en dat valt binnen de afspraak. Zonder
        // teken is "2,5 u" in een saldokolom bovendien niet te onderscheiden van "-2,5 u" op een
        // smal scherm.
        Assert.Equal(verwacht, HourText.Balance(saldo));

    // ── De tooltip van het maandtotaal ──────────────────────────────────────────────────────────

    [Fact]
    public void DeTooltipVanEenMaandZegtWaaruitHetTotaalIsOpgebouwd()
    {
        var maand = Maand(geboekt: 9.5m, regels: 4, correctie: 0m);

        Assert.Equal("9,5 u uit 4 regels", HourText.MonthTitle(maand));
    }

    [Fact]
    public void DeTooltipMeldtEenHandmatigeCorrectieAlsBijdrageEnNietAlsAfwijking()
    {
        // §3.6 vraagt om een melding dat er handmatig is gecorrigeerd. De mockup zet daar het
        // verschil tussen twee getallen in — een override tegenover de som van de specificatie — en
        // die twee getallen bestaan hier niet: een correctie ís een regel in de specificatie
        // (besluit 16), dus het totaal blijft de som. Wat er dan te melden valt is hoeveel van dat
        // totaal uit correcties komt.
        var maand = Maand(geboekt: 9.5m, regels: 4, correctie: -0.5m);

        Assert.Equal(
            "9,5 u uit 4 regels, waarvan -0,5 u handmatig gecorrigeerd",
            HourText.MonthTitle(maand));
    }

    [Fact]
    public void EenEnkeleRegelIsEenRegelEnGeenRegels() =>
        Assert.Equal("1 regel", HourText.Rows(1));

    // ── Wat de uren boven bundel kosten ─────────────────────────────────────────────────────────

    [Fact]
    public void DeKostenBovenBundelStaanMetDeRekensomErbij()
    {
        // Dit bedrag komt op een factuur. Wie het niet kan navertellen kan het niet controleren, dus
        // staat de som er zichtbaar bij en niet alleen de uitkomst.
        Assert.Equal(
            "4 u × € 137,50 = € 550,00",
            HourText.OverBundleCost(4m, 137.5m, isInternal: false));
    }

    [Theory]
    // Geen bundel, dus geen overschrijding te berekenen.
    [InlineData(null, 137.5d, false)]
    // Wel een bundel, maar niets erboven.
    [InlineData(0d, 137.5d, false)]
    // Wel een overschrijding, maar geen tarief afgesproken.
    [InlineData(4d, null, false)]
    // De interne beheerklant: er wordt niets doorbelast.
    [InlineData(4d, 137.5d, true)]
    public void ErStaatGeenBedragAlsErGeenBedragIs(double? uren, double? tarief, bool intern) =>
        // Vier gevallen, vier keer null, en elke keer om een andere reden die niet als € 0,00 mag
        // verschijnen. Een bedrag van nul zou in alle vier de gevallen liegen.
        Assert.Null(HourText.OverBundleCost(
            (decimal?)uren,
            (decimal?)tarief,
            intern));

    // ── De meta-regel en de datum ───────────────────────────────────────────────────────────────

    [Fact]
    public void DeMaandStaatAlleenBijEenRegelAlsDeSpecificatieMeerMaandenKanBevatten()
    {
        Assert.Equal("Ontwikkeling", HourText.EntryMeta("Ontwikkeling", "augustus 2026", withMonth: false));
        Assert.Equal(
            "Ontwikkeling · augustus 2026",
            HourText.EntryMeta("Ontwikkeling", "augustus 2026", withMonth: true));
    }

    [Fact]
    public void DeDagVanEenRegelStaatInDeVormVanDeContractkaart()
    {
        // Dezelfde vorm als de ingangsdatum van een contract: die twee staan op hetzelfde scherm van
        // dezelfde klant, en twee datumvormen naast elkaar is een verschil zonder betekenis.
        Assert.Equal("12-08-2026", HourText.RecordedOn(new DateOnly(2026, 8, 12)));
    }

    [Fact]
    public void HetDatetimeAttribuutVanEenDagDraagtGeenTijdEnGeenZone()
    {
        // Punt 7 houdt het datetime-attribuut van een moment in UTC. Een dag is geen moment: er valt
        // niets om te rekenen, en een "T00:00:00Z" erachter zou een tijdstip beweren dat er niet is.
        Assert.Equal("2026-08-12", HourText.Iso(new DateOnly(2026, 8, 12)));
    }

    // ── Paden ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HetPadVanEenMaandDraagtGeenTweedeJaartal()
    {
        // Het jaartal zit al in de maand. Twee plekken waar hetzelfde jaartal staat is één te veel:
        // bij ?maand=2026-07&jaar=2025 is er geen goed antwoord, en dan moet iemand kiezen welke
        // van de twee wint.
        var pad = HourText.MonthPath("acme-logistiek", "2026-07");

        Assert.Equal("/klant/acme-logistiek/uren?maand=2026-07", pad);
        Assert.DoesNotContain("jaar=", pad, StringComparison.Ordinal);
    }

    [Fact]
    public void HetPadVanEenJaarKlaptDeHistorieOpen() =>
        Assert.Equal(
            "/klant/acme-logistiek/uren?alle=1&jaar=2026",
            HourText.YearPath("acme-logistiek", 2026));

    [Fact]
    public void HetBeoordelingspadWijstNaarDeMaandVanDeRegelEnNietNaarDieVanHetScherm() =>
        // Zo landt de operator na zijn besluit op de maand waarin hij iets heeft veranderd, en ziet
        // hij het maandtotaal dat hij net heeft beïnvloed.
        Assert.Equal(
            "/klant/acme-logistiek/uren?maand=2026-07&beoordeel=hourEntry-abc&actie=fiatteren",
            HourText.JudgePath("acme-logistiek", "2026-07", "hourEntry-abc", HourText.ApproveAction));

    [Fact]
    public void EenSlugMetEenSchuineStreepKanGeenAnderPadWorden()
    {
        // De slug komt uit een viewmodel en hoort volgens PortalSlug altijd veilig te zijn. Een pad
        // dat op het formaat van zijn invoer vertrouwt, breekt stil zodra dat formaat verandert.
        Assert.DoesNotContain("../", HourText.Path("a/../b"), StringComparison.Ordinal);
    }

    // ── De standen en hun kleuren (§8) ──────────────────────────────────────────────────────────

    [Fact]
    public void DeVierMaandstandenHebbenElkEenEigenWoordEnEigenGlyph()
    {
        var woorden = HourStatusVisuals.AllMonths.Select(HourStatusVisuals.Label).ToArray();
        var glyphs = HourStatusVisuals.AllMonths.Select(HourStatusVisuals.Glyph).ToArray();

        Assert.Equal(4, woorden.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(4, glyphs.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EenMaandZonderBundelHergebruiktRangNulEnKrijgtGeenNieuweKleur()
    {
        // Punt 19 volgt punt 3: geen nieuwe kleur en geen nieuwe rang, maar de kale badge — die
        // draagt in patterns.css de neutrale rang-0-kleuren uit §8. Er is niets mis; er is alleen
        // niets om aan te toetsen.
        Assert.Equal("badge", HourStatusVisuals.BadgeClass(HourMonthStatus.NoBundleAgreed));
        Assert.Equal("–", HourStatusVisuals.Glyph(HourMonthStatus.NoBundleAgreed));
    }

    [Fact]
    public void BovenBundelIsAmberEnNietRood()
    {
        // Er is niets stuk: uren boven de bundel zijn een afspraak (§3.5) en worden achteraf
        // gefactureerd (§3.7). Rood zou zeggen dat er iets fout is gegaan.
        Assert.Equal("badge badge--degraded", HourStatusVisuals.BadgeClass(HourMonthStatus.OverBundle));
        Assert.Equal("badge badge--live", HourStatusVisuals.BadgeClass(HourMonthStatus.WithinBundle));
    }

    [Fact]
    public void EenPortaalregelIsNeutraalGrijsEnEenKoppelingKrijgtHetMerkvlak()
    {
        // §8, laatste regel over uren: Portaal = neutraal grijs, MCP/Claude Code en Azure DevOps =
        // merkvlak. Dat is precies .badge tegenover .badge--brand; er komt geen kleur bij.
        Assert.Equal("badge", HourStatusVisuals.SourceClass(HourEntrySource.Portal));
        Assert.Equal("badge badge--brand", HourStatusVisuals.SourceClass(HourEntrySource.Mcp));
        Assert.Equal("badge badge--brand", HourStatusVisuals.SourceClass(HourEntrySource.DevOps));
    }

    [Fact]
    public void EenAfgewezenRegelKrijgtGeenFailedVlak()
    {
        // Rood is in §8 voor een storing, en een afgewezen regel is geen storing maar een besluit
        // van een mens (punt 17). Amber voor "te fiatteren" is wél op zijn plek: daar moet iemand
        // iets mee.
        Assert.Equal("badge", HourStatusVisuals.BadgeClass(HourEntryStatus.Rejected));
        Assert.Equal("badge badge--degraded", HourStatusVisuals.BadgeClass(HourEntryStatus.Pending));
        Assert.Equal("badge badge--live", HourStatusVisuals.BadgeClass(HourEntryStatus.Approved));
    }

    /// <summary>Een maandstand met de getallen die de test nodig heeft.</summary>
    /// <remarks>
    /// Met de hand samengesteld en niet uit <c>HourBalanceCalculator</c>: deze tests gaan over de
    /// opmaak, en een berekening ertussen zou een falende assertie op twee plekken kunnen betekenen.
    /// De berekening zelf staat bij de datalaag.
    /// </remarks>
    private static HourBalance Maand(decimal geboekt, int regels, decimal correctie) => new()
    {
        Month = "2026-08",
        MonthLabel = "augustus 2026",
        BundledHours = 12m,
        Booked = geboekt,
        Balance = 12m - geboekt,
        OverBundleHours = Math.Max(0m, geboekt - 12m),
        Status = HourMonthStatus.WithinBundle,
        CorrectionHours = correctie,
        EntryCount = regels,
    };
}
