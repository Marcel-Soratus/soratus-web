using Soratus.Portal.Components.Pages.Klant;
using Soratus.Portal.Sprints;

namespace Soratus.Portal.Tests.Presentatie;

/// <summary>
/// De opmaak van het sprintscherm (§3.4).
/// </summary>
/// <remarks>
/// <para><strong>Dit bestand bestaat door een mutatie die niets rood maakte, en het gat was het
/// belangrijkste van de hele ronde.</strong> Het omzetten van
/// <c>SprintText.Hours(null)</c> van een streepje naar <c>"0 u"</c> bleef groen, terwijl dat precies de
/// invariant is waar dit scherm om draait: van de zestien work items die op 22 augustus 2026 uit het echte
/// bord kwamen had géén enkel item een waarde in <c>RemainingWork</c>, <c>CompletedWork</c> of
/// <c>StoryPoints</c>.</para>
///
/// <para><strong>Waarom de schermtest hem niet zag:</strong> die controleert of er érgens een streepje in de
/// markup staat, en dat is ook waar als één kolom er geen meer heeft — de story points en de aanmakerkolom
/// hebben er ook een. Een assertie op "staat er een streepje in deze pagina" meet dus iets zwakkers dan ze
/// belooft. Wat er nodig was is de opmaakfunctie zelf, zonder pagina eromheen, en dat is dit bestand.</para>
///
/// <para>Dat is dezelfde soort les als gat 2 van punt 41: twee dingen die op elkaar lijken dekken elkaars
/// afwezigheid. Hier waren dat twee streepjes uit twee verschillende kolommen.</para>
/// </remarks>
public class SprintTextTests
{
    [Fact]
    public void EenNietIngevuldUrenveldWordtEenStreepjeEnNooitNul()
    {
        // De invariant van dit scherm in één regel. Nul betekent dat iemand nul heeft ingevuld; een
        // streepje betekent dat er niets staat om op te tellen. Zonder deze test is dat verschil alleen
        // via de pagina te meten, en daar is het niet te onderscheiden van een streepje uit een andere
        // kolom.
        Assert.Equal(SprintText.Dash, SprintText.Hours(null));
        Assert.Equal(SprintText.Dash, SprintText.Points(null));
    }

    [Fact]
    public void EenEchteNulWordtNul()
    {
        // De keerzijde, en zonder haar is "altijd een streepje" ook groen — en dan verdwijnt een uur dat
        // werkelijk op nul staat achter dezelfde tekst als een uur dat niemand heeft ingevuld.
        Assert.Equal("0 u", SprintText.Hours(0m));
        Assert.Equal("0", SprintText.Points(0m));
    }

    [Theory]
    [InlineData(6.5, "6,5 u")]
    [InlineData(8, "8 u")]
    [InlineData(0.25, "0,25 u")]
    public void UrenStaanInDeNederlandseVormMetEenEenheid(double uren, string verwacht)
    {
        // Komma en geen punt (§8: getallen tabulair, Nederlandse opmaak), en zonder overbodige nullen: "8 u"
        // en niet "8,00 u". Een vast aantal decimalen zou van elk heel getal een getal met komma maken, en
        // dat leest als precisie die er niet is.
        Assert.Equal(verwacht, SprintText.Hours((decimal)uren));
    }

    [Fact]
    public void EenAantalKrijgtNooitEenStreepje()
    {
        // Het spiegelbeeld van de eerste test, en het is de andere helft van dezelfde regel: "hoeveel van
        // deze items zijn afgerond" heeft altijd een antwoord zodra we de items hebben gelezen, en dat
        // antwoord kan nul zijn. Of we hebben gelezen staat in SprintState en niet in dit getal.
        Assert.Equal("0", SprintText.Count(0));
        Assert.Equal("7", SprintText.Count(7));
    }

    [Fact]
    public void EenPeriodeBinnenEenMaandNoemtDeMaandEenKeer()
    {
        // "1 t/m 31 augustus 2026" is wat een mens schrijft, en op een bord met maandsprints is dat elke
        // sprint.
        Assert.Equal(
            "1 t/m 31 augustus 2026",
            SprintText.Period(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));
    }

    [Fact]
    public void EenPeriodeOverTweeMaandenNoemtBeideMaanden()
    {
        Assert.Equal(
            "15 augustus t/m 15 september 2026",
            SprintText.Period(new DateOnly(2026, 8, 15), new DateOnly(2026, 9, 15)));
    }

    [Fact]
    public void EenPeriodeOverTweeJarenNoemtBeideJaren()
    {
        Assert.Equal(
            "15 december 2026 t/m 15 januari 2027",
            SprintText.Period(new DateOnly(2026, 12, 15), new DateOnly(2027, 1, 15)));
    }

    [Fact]
    public void EenPeriodeGebruiktTotEnMetEnGeenStreepje()
    {
        // De einddatum is inclusief: gemeten geeft DevOps 2026-08-31T00:00:00Z terug op een verzoek waarin
        // 31 augustus 23:59:59 stond, dus het zijn datums en de laatste dag hoort bij de sprint. Een
        // streepje laat open of die dag meedoet; "t/m" niet. Dat is geen opmaakvoorkeur maar een uitspraak
        // over de periode.
        var periode = SprintText.Period(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        Assert.Contains("t/m", periode, StringComparison.Ordinal);
        Assert.DoesNotContain("–", periode, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("2026-08-01", null)]
    [InlineData(null, "2026-08-31")]
    public void EenHalvePeriodeIsEenStreepjeEnGeenHalveTekst(string? van, string? tot)
    {
        // Een sprint met alleen een begindatum is geen sprint — zie DevOpsIteration.IsDated. Er staat dan
        // geen halve periode maar een streepje: "vanaf 1 augustus" zou suggereren dat de sprint doorloopt.
        Assert.Equal(SprintText.Dash, SprintText.Period(Dag(van), Dag(tot)));
    }

    [Theory]
    [InlineData(WorkItemOrigin.Agent, "agent")]
    [InlineData(WorkItemOrigin.Manual, "mens")]
    [InlineData(WorkItemOrigin.Unknown, "onbekend")]
    public void DeHerkomstStaatInWoordenEnOnbekendIsNietHandmatig(WorkItemOrigin herkomst, string woord)
    {
        // "Onbekend" en niet "handmatig", en dat is de hele reden dat die waarde bestaat: er staat in DevOps
        // vandaag niets dat het onderscheid draagt, dus "handmatig" zou een bewering zijn die niemand heeft
        // gemeten.
        Assert.Equal(woord, SprintText.Origin(herkomst));
    }

    [Theory]
    [InlineData(WorkItemStage.Proposed, "badge badge--idle")]
    [InlineData(WorkItemStage.InProgress, "badge badge--brand")]
    [InlineData(WorkItemStage.Resolved, "badge badge--live")]
    [InlineData(WorkItemStage.Completed, "badge badge--live")]
    [InlineData(WorkItemStage.Removed, "badge badge--idle")]
    public void DeBadgeVolgtDeKleurenVanSectieAcht(WorkItemStage fase, string klassen)
    {
        // §8: New = idle-grijs, Active = merkvlak, Resolved/Closed = live-groen. Die vlakken bestaan al in
        // patterns.css, dus er komt geen kleur bij — §8 is uitdrukkelijk: verzin geen nieuwe kleuren.
        Assert.Equal(klassen, SprintText.StageBadgeClass(fase, isBlocked: false));
    }

    [Fact]
    public void EenGeblokkeerdItemKrijgtAmberOngeacthZijnFase()
    {
        // Blokkade wint van de fase, en dat is een besluit: op één badge past één vlak, en van die twee is
        // de blokkade wat een mens moet zien. De statenaam blijft als label staan, dus er verdwijnt geen
        // informatie — §8: nooit kleur zonder label.
        Assert.Equal(
            "badge badge--degraded",
            SprintText.StageBadgeClass(WorkItemStage.InProgress, isBlocked: true));

        Assert.Equal(
            "badge badge--degraded",
            SprintText.StageBadgeClass(WorkItemStage.Completed, isBlocked: true));
    }

    [Fact]
    public void ElkeFaseHeeftEenEigenGlyphNaastZijnKleur()
    {
        // §1: status nooit alleen door kleur. Zet het scherm in grijstinten en de informatie hoort compleet
        // te zijn — dus een glyph die per fase verschilt, en niet één bolletje in vier kleuren.
        var glyphs = new[]
        {
            WorkItemStage.Proposed,
            WorkItemStage.InProgress,
            WorkItemStage.Completed,
            WorkItemStage.Removed,
        }.Select(fase => SprintText.StageGlyph(fase, isBlocked: false)).ToArray();

        Assert.Equal(glyphs.Length, glyphs.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(string.Empty, glyphs);
    }

    [Fact]
    public void HetPadNaarHetSprintschermEscapetDeSlug()
    {
        Assert.Equal("/klant/acme-logistiek/sprint", SprintText.Path("acme-logistiek"));
        Assert.Equal("/klant/acme%20bv/sprint", SprintText.Path("acme bv"));
    }

    [Fact]
    public void HetNummerVanEenWorkItemKrijgtEenHekje()
    {
        // Een los getal in een kolom naast andere getallen leest als een waarde. En er wordt niet naar
        // DevOps gelinkt: een link naar een bord waar de klant geen toegang tot heeft is een link naar een
        // inlogscherm.
        Assert.Equal("#4566", SprintText.Number(4566));
    }

    [Fact]
    public void ZonderLezingZegtDeOuderdomDatErNooitIsOpgehaald()
    {
        // Een woord en geen streepje: dit staat in de kop en niet in een getalkolom, en daar is "nooit
        // opgehaald" leesbaarder dan een liggend streepje.
        Assert.Equal("nooit opgehaald", SprintText.Age(null, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void MetEenLezingZegtDeOuderdomHoeOudHijIs()
    {
        var nu = new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

        var tekst = SprintText.Age(nu - TimeSpan.FromMinutes(8), nu);

        Assert.StartsWith("opgehaald ", tekst, StringComparison.Ordinal);
        Assert.DoesNotContain("nooit", tekst, StringComparison.Ordinal);
    }

    /// <summary>Een dag uit een tekst, of <c>null</c>.</summary>
    /// <param name="tekst">De dag als <c>jjjj-MM-dd</c>, of <c>null</c>.</param>
    /// <returns>De dag, of <c>null</c>.</returns>
    private static DateOnly? Dag(string? tekst) =>
        tekst is null ? null : DateOnly.Parse(tekst, System.Globalization.CultureInfo.InvariantCulture);
}
