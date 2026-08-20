using Bunit;
using Soratus.Portal.Components.Shared;
using Soratus.Portal.Data;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Presentatie;

/// <summary>
/// De sparkline is altijd twaalf blokken breed, ook voor een agent die niets deed.
/// </summary>
/// <remarks>
/// <para>Twaalf blokken van twee uur zijn samen precies 24 uur (§8), en dat is de hele betekenis
/// van de kolom: het meest rechtse blokje is nu en het meest linkse is gisteren om deze tijd. Zou
/// een agent zonder runs een kortere of lege reeks krijgen, dan staan de rijen van een tabel niet
/// meer onder elkaar uitgelijnd en gaat een blokje op dezelfde plek in twee rijen over een ander
/// tijdvak. Dan is de kolom niet leesbaar meer, en juist een agent die stilstaat is de rij waar je
/// naar kijkt.</para>
///
/// <para>Het aantal komt uit <see cref="HistogramWindow.Last24Hours"/> en niet uit een twaalf in
/// deze test: het venster is de bron van dat getal, en <c>PortalViews</c> vult de reeks aan tot
/// <see cref="HistogramWindow.BlockCount"/> voor agents die de opslag niet eens teruggeeft. Het
/// moment gaat als parameter mee, want <c>Last24Hours</c> lijnt uit op een even UTC-uur en dat is
/// met de echte klok niet reproduceerbaar te testen.</para>
/// </remarks>
public class SparklineTests : BunitContext
{
    [Fact]
    public void EenAgentDieInVierentwintigUurNietsDeedKrijgtTochTwaalfBlokken()
    {
        var venster = HistogramWindow.Last24Hours(Testgegevens.Nu);
        var blokken = venster.Empty()
            .Select(bucket => new SparkBlock(bucket.Runs, bucket.Failed))
            .ToArray();

        var cut = Render<Sparkline>(p => p.Add(c => c.Blocks, blokken));

        var spans = cut.FindAll("span.spark");

        Assert.Equal(12, venster.BlockCount);
        Assert.Equal(venster.BlockCount, spans.Count);
        Assert.All(spans, span => Assert.Contains("spark--empty", span.ClassList));
    }

    [Fact]
    public void EenLegeSparklineZegtInWoordenDatErNietsIsGedraaid()
    {
        // Twaalf blokjes van 2px zijn voor een schermlezer niets en voor een ziende lezer een
        // streepje. De tekstbeschrijving is hier de enige drager, dus die moet er staan.
        var venster = HistogramWindow.Last24Hours(Testgegevens.Nu);
        var blokken = venster.Empty()
            .Select(bucket => new SparkBlock(bucket.Runs, bucket.Failed))
            .ToArray();

        var cut = Render<Sparkline>(p => p.Add(c => c.Blocks, blokken));

        var sparkline = cut.Find("span.sparkline");

        Assert.Equal("img", sparkline.GetAttribute("role"));
        Assert.Equal("0 runs in 24 u, 0 mislukt (2-uursblokken)", sparkline.GetAttribute("aria-label"));
    }
}
