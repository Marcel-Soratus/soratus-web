using Soratus.Agents.Contracts;
using Soratus.Portal.Data;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Contracten;

/// <summary>
/// De telling op de chips is dezelfde telling als wat het filter oplevert, opgeteld.
/// </summary>
/// <remarks>
/// <para>De chips boven de logtabel tellen per niveau, en de voetregel onder de tabel zegt "n van
/// m regels". Die m is niet apart geteld maar de som van de chips die aan staan — zo kunnen de twee
/// getallen op hetzelfde scherm elkaar niet tegenspreken. Deze tests leggen die gelijkheid vast aan
/// beide kanten: elke chip is precies wat je krijgt als je alleen dat niveau aanzet, en de som over
/// de aangezette niveaus is precies wat het hele filter oplevert.</para>
///
/// <para>De telling gaat over de regels ná de zoekterm maar vóór het niveaufilter. Dat is een
/// besluit met een reden: een uitgezette chip die 0 toont vertelt je niet wat je mist door hem uit
/// te laten staan, en dan is er geen aanleiding om hem ooit weer aan te zetten.</para>
/// </remarks>
public class LogtellingTests
{
    private static readonly DateTimeOffset Basis = Testgegevens.Nu - TimeSpan.FromMinutes(30);

    /// <summary>
    /// Elf regels: zes info, drie warn, twee error, met "fout" in het bericht van vier ervan.
    /// </summary>
    /// <remarks>
    /// Bewust ongelijke aantallen per niveau. Waren ze gelijk, dan zou een verwisseling van twee
    /// niveaus in de telling niet opvallen.
    /// </remarks>
    private static IReadOnlyList<LogRecord> Regels() =>
    [
        Regel(1, LogLevel.Info, "batch gestart"),
        Regel(2, LogLevel.Info, "14 berichten opgehaald"),
        Regel(3, LogLevel.Info, "regel toegepast"),
        Regel(4, LogLevel.Info, "regel toegepast"),
        Regel(5, LogLevel.Info, "fout genegeerd, doorgegaan"),
        Regel(6, LogLevel.Info, "batch afgerond"),
        Regel(7, LogLevel.Warn, "afzender onbekend"),
        Regel(8, LogLevel.Warn, "fout in de bijlage overgeslagen"),
        Regel(9, LogLevel.Warn, "traag antwoord van de bron"),
        Regel(10, LogLevel.Error, "fout: bron antwoordde niet"),
        Regel(11, LogLevel.Error, "fout: run afgebroken"),
    ];

    [Theory]
    [InlineData(LogLevel.Info, 6)]
    [InlineData(LogLevel.Warn, 3)]
    [InlineData(LogLevel.Error, 2)]
    public async Task DeChipVanEenNiveauIsPreciesWatDatNiveauAlleenOplevert(LogLevel niveau, int verwacht)
    {
        var tabbladen = Weergavelaag.Tabbladen(new Vastetelemetriestore().MetLogregels(Regels()));
        var scope = await Weergavelaag.Operatorscope();

        var weergave = await tabbladen.BuildLogsAsync(
            scope,
            "factuur-intake",
            new LogQuery { Levels = [niveau] });

        Assert.NotNull(weergave);
        Assert.Equal(verwacht, weergave.Lines.Count);
        Assert.Equal(verwacht, weergave.Counts[niveau]);
        Assert.All(weergave.Lines, regel => Assert.Equal(niveau, regel.Level));
    }

    [Fact]
    public async Task DeSomVanDeAangezetteChipsIsPreciesWatHetFilterOplevert()
    {
        // Dit is het getal in de voetregel. Zou het apart geteld worden, dan kan het gaan afwijken
        // van de chips erboven — en dan staat er op één scherm twee keer een ander antwoord op
        // dezelfde vraag.
        var tabbladen = Weergavelaag.Tabbladen(new Vastetelemetriestore().MetLogregels(Regels()));
        var scope = await Weergavelaag.Operatorscope();

        LogLevel[] aan = [LogLevel.Warn, LogLevel.Error];

        var weergave = await tabbladen.BuildLogsAsync(
            scope,
            "factuur-intake",
            new LogQuery { Levels = aan });

        Assert.NotNull(weergave);
        Assert.Equal(5, weergave.Lines.Count);
        Assert.Equal(weergave.Lines.Count, aan.Sum(niveau => weergave.Counts[niveau]));
    }

    [Fact]
    public async Task ZonderNiveaufilterIsDeSomVanAlleChipsHetTotaal()
    {
        var regels = Regels();
        var tabbladen = Weergavelaag.Tabbladen(new Vastetelemetriestore().MetLogregels(regels));
        var scope = await Weergavelaag.Operatorscope();

        var weergave = await tabbladen.BuildLogsAsync(scope, "factuur-intake", new LogQuery());

        Assert.NotNull(weergave);
        Assert.Equal(regels.Count, weergave.Lines.Count);
        Assert.Equal(regels.Count, weergave.Counts.Values.Sum());
    }

    [Fact]
    public async Task DeTellingHoudtDeZoektermAanMaarNietHetNiveaufilter()
    {
        // De kern van de afspraak. Met alleen error aan staat er nog steeds "1" op de warn-chip:
        // dat is wat je mist zolang die chip uit staat. Zou de telling het niveaufilter ook
        // toepassen, dan stond er 0 en had de chip niets meer te zeggen.
        var tabbladen = Weergavelaag.Tabbladen(new Vastetelemetriestore().MetLogregels(Regels()));
        var scope = await Weergavelaag.Operatorscope();

        var weergave = await tabbladen.BuildLogsAsync(
            scope,
            "factuur-intake",
            new LogQuery { Levels = [LogLevel.Error], Search = "fout" });

        Assert.NotNull(weergave);

        // Vier regels bevatten "fout": één info, één warn en twee error.
        Assert.Equal(1, weergave.Counts[LogLevel.Info]);
        Assert.Equal(1, weergave.Counts[LogLevel.Warn]);
        Assert.Equal(2, weergave.Counts[LogLevel.Error]);

        // In beeld komen alleen de error-regels, en dat is precies de chip die aan staat.
        Assert.Equal(2, weergave.Lines.Count);
        Assert.Equal(weergave.Lines.Count, weergave.Counts[LogLevel.Error]);
    }

    [Fact]
    public async Task ElkNiveauStaatInDeTellingOokAlsErGeenRegelVanIs()
    {
        // Drie chips, altijd drie. Verdwijnt een niveau uit de telling, dan is "er is geen
        // warn-chip" niet te onderscheiden van "er zijn geen warns" — en dan verspringt de rij ook
        // zodra er één waarschuwing bij komt.
        var alleenInfo = Regels().Where(r => r.Level == LogLevel.Info).ToArray();
        var tabbladen = Weergavelaag.Tabbladen(new Vastetelemetriestore().MetLogregels(alleenInfo));
        var scope = await Weergavelaag.Operatorscope();

        var weergave = await tabbladen.BuildLogsAsync(scope, "factuur-intake", new LogQuery());

        Assert.NotNull(weergave);

        foreach (var niveau in Enum.GetValues<LogLevel>())
        {
            Assert.True(
                weergave.Counts.ContainsKey(niveau),
                $"Het niveau {niveau} staat niet in de telling. Alle niveaus horen er altijd in " +
                "te staan, ook met nul regels: een ontbrekende chip en een chip met 0 zijn op het " +
                "scherm niet te onderscheiden.");
        }

        Assert.Equal(0, weergave.Counts[LogLevel.Warn]);
        Assert.Equal(0, weergave.Counts[LogLevel.Error]);
    }

    [Fact]
    public async Task DeKlantEnDeOperatorZienDezelfdeTellingen()
    {
        // De scheiding tussen de rollen gaat over de vrije context van een regel, niet over hoeveel
        // regels er zijn. Zou de klant een andere telling krijgen, dan zou een operator een
        // gesprek over "er staan er drie" niet kunnen volgen.
        var store = new Vastetelemetriestore().MetLogregels(Regels());
        var tabbladen = Weergavelaag.Tabbladen(store);

        var klant = await tabbladen.BuildLogsAsync(
            await Weergavelaag.Klantscope(),
            "factuur-intake",
            new LogQuery());

        var operatorweergave = await tabbladen.BuildLogsAsync(
            await Weergavelaag.Operatorscope(),
            "factuur-intake",
            new LogQuery());

        Assert.NotNull(klant);
        Assert.NotNull(operatorweergave);
        Assert.Equal(operatorweergave.Counts, klant.Counts);
        Assert.Equal(operatorweergave.Lines.Count, klant.Lines.Count);
    }

    private static LogRecord Regel(int nummer, LogLevel niveau, string bericht) =>
        Testlogregels.Regel(
            id: $"t-{nummer:D4}",
            moment: Basis + TimeSpan.FromSeconds(nummer),
            niveau: niveau,
            gebeurtenis: "run.voortgang",
            bericht: bericht);
}
