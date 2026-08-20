using Soratus.Agents.Contracts;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Contracten;

/// <summary>
/// <c>CosmosAgentTelemetryStore.TrimYoungestGroup</c> op de grens: hij laat precies de jongste
/// groep regels met dezelfde tijdstempel vallen, niet meer en niet minder.
/// </summary>
/// <remarks>
/// <para>Deze methode is drie regels code en het hele verschil tussen een live tail die klopt en
/// een die stil regels kwijtraakt. Valt de paginagrens midden in een groep met dezelfde
/// tijdstempel, dan weet de opslaglaag niet of hij die groep compleet heeft; zou hij de cursor er
/// tóch op zetten, dan is een regel met dezelfde tijdstempel en een lagere id voorgoed
/// overgeslagen. Dus blijft die groep liggen tot hij compleet meekomt.</para>
///
/// <para>De tests roepen de productiecode aan via <see cref="Opslaglaag"/> en niet een nagebouwde
/// versie ervan. Een tweede implementatie van dezelfde drie regels zou vooral bewijzen dat ik ze
/// twee keer op dezelfde manier heb opgeschreven.</para>
/// </remarks>
public class TrimJongsteGroepTests
{
    private static readonly DateTimeOffset T0 = Testgegevens.Nu - TimeSpan.FromSeconds(30);
    private static readonly DateTimeOffset T1 = Testgegevens.Nu - TimeSpan.FromSeconds(20);
    private static readonly DateTimeOffset T2 = Testgegevens.Nu - TimeSpan.FromSeconds(10);

    [Fact]
    public void EenGroepVanEenRegelVerdwijntHelemaalEnDeRestBlijftStaan()
    {
        var regels = Lijst(
            (T0, "a-1"),
            (T0, "a-2"),
            (T1, "b-1"));

        Opslaglaag.TrimJongsteGroep(regels);

        Assert.Equal(["a-1", "a-2"], regels.Select(r => r.Id));
    }

    [Fact]
    public void EenGroepVanMeerdereRegelsVerdwijntInZijnGeheel()
    {
        // Niet één regel eraf maar de hele groep: half een groep leveren is precies de fout die
        // deze methode moet voorkomen.
        var regels = Lijst(
            (T0, "a-1"),
            (T1, "b-1"),
            (T1, "b-2"),
            (T1, "b-3"));

        Opslaglaag.TrimJongsteGroep(regels);

        Assert.Equal(["a-1"], regels.Select(r => r.Id));
    }

    [Fact]
    public void EenLijstDieHelemaalUitEenTijdstempelBestaatBlijftLeegAchter()
    {
        // De grens precies op nul. De opslaglaag vangt dit geval daarna zelf op — hij levert de
        // groep dan alsnog uit, want anders komt de tail nooit vooruit — maar deze methode hoort
        // hier gewoon te doen wat hij belooft en niet ineens iets te bewaren.
        var regels = Lijst(
            (T1, "b-1"),
            (T1, "b-2"),
            (T1, "b-3"));

        Opslaglaag.TrimJongsteGroep(regels);

        Assert.Empty(regels);
    }

    [Fact]
    public void EenLijstVanEenRegelBlijftLeegAchter()
    {
        var regels = Lijst((T1, "b-1"));

        Opslaglaag.TrimJongsteGroep(regels);

        Assert.Empty(regels);
    }

    [Fact]
    public void AlleenDeJongsteGroepGaatEraf()
    {
        // Drie groepen, en er hoort er precies één weg te gaan. Zou de methode op tijd sorteren of
        // op iets anders kijken dan de laatste tijdstempel, dan valt dat hier op.
        var regels = Lijst(
            (T0, "a-1"),
            (T0, "a-2"),
            (T1, "b-1"),
            (T1, "b-2"),
            (T2, "c-1"),
            (T2, "c-2"));

        Opslaglaag.TrimJongsteGroep(regels);

        Assert.Equal(["a-1", "a-2", "b-1", "b-2"], regels.Select(r => r.Id));
    }

    [Fact]
    public void HetVergelijkGaatOpDeTijdstempelEnNietOpDeDagOfDeMinuut()
    {
        // Twee regels binnen dezelfde seconde maar niet op hetzelfde moment zijn twee groepen. Zou
        // het vergelijk op de minuut of de partitiesleutel gaan, dan zou de tail bij elke tik een
        // hele seconde laten liggen.
        var bijna = T1.AddTicks(1);

        var regels = Lijst(
            (T1, "b-1"),
            (bijna, "b-2"));

        Opslaglaag.TrimJongsteGroep(regels);

        Assert.Equal(["b-1"], regels.Select(r => r.Id));
    }

    private static List<LogRecord> Lijst(params (DateTimeOffset Moment, string Id)[] regels) =>
        [.. regels.Select(r => Testlogregels.Regel(r.Id, r.Moment, LogLevel.Info))];
}
