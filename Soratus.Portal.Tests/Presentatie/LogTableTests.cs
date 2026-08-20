using Bunit;
using Soratus.Agents.Contracts;
using Soratus.Portal.Components.Shared;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Presentatie;

/// <summary>
/// De logtabel van de operator: een eerlijke voetregel, en een lange logregel die de rij niet
/// verbreedt.
/// </summary>
/// <remarks>
/// <para><strong>De voetregel is de vervanger van virtualisatie.</strong> Er staat geen
/// <c>Virtualize</c> in deze tabel, want een uitklapbare rij heeft geen vaste hoogte. In plaats
/// daarvan zegt de voet hoeveel je ziet van hoeveel er is. Dat getal is het enige dat een lezer
/// vertelt dat er méér is; staat er "50 van 50" terwijl er 900 zijn, dan concludeert iemand op een
/// dag dat er niets is gebeurd.</para>
///
/// <para><strong>De lange regel wordt op de markup getest en niet op de layout.</strong> In de data
/// zit een logregel van ruim 3400 tekens zonder spaties op de handige plekken. Dat die de tabel niet
/// verbreedt hangt aan drie dingen die alle drie nodig zijn: het kolomspoor is
/// <c>minmax(0, 1fr)</c> en geen kale <c>1fr</c>, de cel is afgekapt, en de uitklap staat búiten de
/// grid-rij. Alle drie zijn in de markup vast te stellen. Of het er in een browser ook goed
/// uitziet is een vraag die een unittest niet hoort te beantwoorden — en die drie eigenschappen zijn
/// precies wat er stilletjes wegvalt bij een herschrijving.</para>
/// </remarks>
public class LogTableTests : BunitContext
{
    private static readonly DateTimeOffset Basis = Testgegevens.Nu - TimeSpan.FromMinutes(5);

    /// <summary>Een logregel van ruim 3400 tekens zonder een enkele spatie.</summary>
    /// <remarks>
    /// Zonder spaties, want dat is het geval dat mis gaat: een browser breekt tekst op witruimte,
    /// en een ononderbroken tekenreeks — een token, een pad, een base64-payload — heeft die niet.
    /// </remarks>
    private const string LangBericht =
        "SGVsbG9Xb3JsZFBheWxvYWQ=/src/Mail/Rules/SenderDomainRule.cs:line34"
        + "0123456789abcdefABCDEF0123456789abcdefABCDEF0123456789abcdefABCDEF";

    private static LogRecord Regel(int nummer, LogLevel niveau, string bericht = "Voortgang.") =>
        Testlogregels.Regel(
            id: $"lt-{nummer:D4}",
            moment: Basis + TimeSpan.FromSeconds(nummer),
            niveau: niveau,
            bericht: bericht);

    private static LogRecord LangeRegel() =>
        Regel(9, LogLevel.Error, string.Concat(Enumerable.Repeat(LangBericht, 26)));

    [Fact]
    public void DeVoetregelZegtHoeveelErInBeeldStaanVanHoeveelErZijn()
    {
        var cut = Render<LogTable>(p => p
            .Add(c => c.Rows, [Regel(1, LogLevel.Info), Regel(2, LogLevel.Warn)])
            .Add(c => c.TotalCount, 917));

        var voet = cut.Find(".table-foot__count").TextContent;

        Assert.StartsWith("2 van 917 regels", voet, StringComparison.Ordinal);
    }

    [Fact]
    public void ZonderTotaalIsHetTotaalWatErInBeeldStaat()
    {
        // Niet "2 van 0" en niet een leeg getal: staat er geen totaal, dan is wat je ziet alles wat
        // er is. Dat is een waar antwoord en geen aanname.
        var cut = Render<LogTable>(p => p
            .Add(c => c.Rows, [Regel(1, LogLevel.Info), Regel(2, LogLevel.Warn)]));

        Assert.StartsWith(
            "2 van 2 regels",
            cut.Find(".table-foot__count").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DeVoetregelMeldtLiveTailAlsHijAanStaat()
    {
        // Onder de tabel hoort te staan waaróm er regels bij komen. Zonder die melding lijkt een
        // tabel die vanzelf verspringt op een storing.
        var cut = Render<LogTable>(p => p
            .Add(c => c.Rows, [Regel(1, LogLevel.Info)])
            .Add(c => c.TotalCount, 4)
            .Add(c => c.LiveTail, true));

        var voet = cut.Find(".table-foot__count").TextContent;

        Assert.Contains("1 van 4 regels", voet, StringComparison.Ordinal);
        Assert.Contains("live tail actief", voet, StringComparison.Ordinal);
    }

    [Fact]
    public void DeVoetregelZwijgtOverLiveTailAlsHijUitStaat()
    {
        var cut = Render<LogTable>(p => p
            .Add(c => c.Rows, [Regel(1, LogLevel.Info)])
            .Add(c => c.TotalCount, 4));

        Assert.DoesNotContain(
            "live tail",
            cut.Find(".table-foot__count").TextContent,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeLaadknopStaatErAlleenAlsErEchtMeerIs()
    {
        // Een knop die niets meer oplevert is erger dan geen knop: hij belooft dat er iets is.
        var zonder = Render<LogTable>(p => p
            .Add(c => c.Rows, [Regel(1, LogLevel.Info)])
            .Add(c => c.TotalCount, 1)
            .Add(c => c.OnLoadMore, () => { }));

        Assert.Empty(zonder.FindAll(".table-foot button"));

        var met = Render<LogTable>(p => p
            .Add(c => c.Rows, [Regel(1, LogLevel.Info)])
            .Add(c => c.TotalCount, 40)
            .Add(c => c.CanLoadMore, true)
            .Add(c => c.OnLoadMore, () => { }));

        Assert.Single(met.FindAll(".table-foot button"));
    }

    [Fact]
    public void EenLegeTabelZegtDatHetFilterNietsOverlaatEnRekentDeVoetregelOpNul()
    {
        var cut = Render<LogTable>(p => p
            .Add(c => c.Rows, [])
            .Add(c => c.TotalCount, 0));

        Assert.Contains("Geen logregels", cut.Find(".table-empty").TextContent, StringComparison.Ordinal);
        Assert.StartsWith(
            "0 van 0 regels",
            cut.Find(".table-foot__count").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HetKolomspoorVanDeBerichtkolomHeeftEenOndergrensVanNul()
    {
        // Een kale 1fr heeft min-width: auto en groeit mee met de langste ononderbroken tekenreeks
        // in de kolom. Met minmax(0, …) blijft de track binnen zijn deel van de breedte. Dit is de
        // eerste van de drie eigenschappen die samen de lange regel binnen de rij houden.
        Assert.Contains("minmax(0, 1fr)", LogTable.Columns.Template, StringComparison.Ordinal);

        var sporen = LogTable.Columns.Visible.Select(k => k.Track).ToArray();

        Assert.DoesNotContain("1fr", sporen);
    }

    [Fact]
    public void DeKolomindelingStaatOpDeKaartEnNietOpDeRij()
    {
        // De responsieve regel in layout.css overschrijft --row-cols op de rij. Een declaratie op
        // het element verslaat alleen een geërfde waarde en geen inline gezette, dus staat de
        // basiswaarde op de rij, dan klapt de tabel onder 768px niet in.
        var cut = Render<LogTable>(p => p
            .Add(c => c.Rows, [Regel(1, LogLevel.Info)]));

        var kaart = cut.Find("section.card");

        Assert.Contains("minmax(0, 1fr)", kaart.GetAttribute("style"), StringComparison.Ordinal);
        Assert.NotNull(kaart.GetAttribute("data-rowgrid"));

        foreach (var rij in cut.FindAll(".data-row"))
        {
            Assert.Null(rij.GetAttribute("style"));
        }
    }

    [Fact]
    public void EenLangeLogregelVerbreedtDeRijNiet()
    {
        // De tweede en derde eigenschap. De berichtcel is afgekapt, en de uitgeklapte JSON staat als
        // broer van de rij en niet erin: stond hij erin, dan werd het <pre> een grid-item en telde
        // het mee in de kolombreedte — en dan hebben de eerste twee geen zin meer.
        var lang = LangeRegel();

        var cut = Render<LogTable>(p => p
            .Add(c => c.Rows, [lang, Regel(1, LogLevel.Info)])
            .Add(c => c.ExpandedIds, new HashSet<string>(StringComparer.Ordinal) { lang.Id })
            .Add(c => c.OnToggle, (string _) => { }));

        var berichtcel = cut.Find(".log-cell__message");

        Assert.Contains("data-cell--truncate", berichtcel.ClassList);

        // De tooltip is afgekapt op 400 tekens plus de uitleg. Het hele bericht als title zou een
        // blok tekst opleveren dat het halve scherm bedekt en niet te scrollen is.
        var tooltip = berichtcel.GetAttribute("title");

        Assert.NotNull(tooltip);
        Assert.True(
            tooltip.Length < lang.Message.Length,
            $"De tooltip is {tooltip.Length} tekens en het bericht {lang.Message.Length}. Een " +
            "tooltip die het hele bericht draagt bedekt het scherm en is niet te scrollen; wie " +
            "meer wil, klapt de regel uit.");
        Assert.Contains("klik de regel open", tooltip, StringComparison.Ordinal);

        // De uitklap bestaat, en staat naast de rij in plaats van erin.
        var uitklap = cut.Find("pre.json-disclosure");

        Assert.Equal("log-row", uitklap.ParentElement?.ClassName);
        Assert.Empty(cut.FindAll(".data-row pre"));
        Assert.Empty(cut.FindAll(".data-row .json-disclosure"));
    }

    [Fact]
    public void EenIngeklapteRegelHeeftGeenUitklapEnGeenAriaControls()
    {
        var cut = Render<LogTable>(p => p
            .Add(c => c.Rows, [Regel(1, LogLevel.Error)])
            .Add(c => c.OnToggle, (string _) => { }));

        Assert.Empty(cut.FindAll("pre.json-disclosure"));

        var rij = cut.Find("button.data-row--log");

        Assert.Equal("false", rij.GetAttribute("aria-expanded"));
        Assert.Null(rij.GetAttribute("aria-controls"));
    }

    [Fact]
    public void DeTabelHeeftGeenAriaLiveWantLiveTailZouEenSchermlezerDoodlezen()
    {
        // Met live tail aan komen er elke paar seconden regels bij. Wat er te melden is — dat live
        // tail aan staat — meldt LiveTailToggle, één keer.
        var cut = Render<LogTable>(p => p
            .Add(c => c.Rows, [Regel(1, LogLevel.Info), Regel(2, LogLevel.Error)])
            .Add(c => c.LiveTail, true));

        Assert.Empty(cut.FindAll("[aria-live]"));
    }
}
