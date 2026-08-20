using Soratus.Agents.Contracts;
using Soratus.Portal.Data;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Contracten;

/// <summary>
/// De cursor van de live tail: geen regel dubbel, geen regel overgeslagen — ook niet als tientallen
/// regels dezelfde tijdstempel dragen.
/// </summary>
/// <remarks>
/// <para>Gelijke tijdstempels zijn geen randgeval. Een agent die zijn logregels in één batch
/// wegschrijft geeft ze allemaal dezelfde <c>ts</c> mee, en dan is de tijd alleen niet genoeg om
/// een cursor op te zetten. De opslaglaag lost dat met twee dingen op die bij elkaar horen: de
/// gelijkspelclausule op de id (<c>ts &gt; @since OR (ts = @since AND id &gt; @sinceId)</c>) en de
/// grensregel die een groep met dezelfde tijdstempel laat liggen zolang hij niet compleet is.</para>
///
/// <para>Wat hieronder draait is <see cref="Vastetelemetriestore"/> — een lijst in het geheugen die
/// die twee regels nadoet en voor de grensregel <em>de productiecode zelf</em> aanroept. De query
/// bewijzen deze tests niet; dat de clausule in de query staat, controleert
/// <see cref="DeTailqueryDraagtDeGelijkspelclausuleOpDeId"/>.</para>
/// </remarks>
public class LogtailcursorTests
{
    private static readonly DateTimeOffset T0 = Testgegevens.Nu - TimeSpan.FromSeconds(30);
    private static readonly DateTimeOffset T1 = Testgegevens.Nu - TimeSpan.FromSeconds(20);
    private static readonly DateTimeOffset T2 = Testgegevens.Nu - TimeSpan.FromSeconds(10);

    /// <summary>
    /// Zeventien regels over drie tijdstempels, met twaalf regels op precies dezelfde.
    /// </summary>
    /// <remarks>
    /// Twaalf op één tijdstempel is meer dan de paginagrens in de tests hieronder. Dat is het punt:
    /// de grens valt dan gegarandeerd midden in die groep, en dat is het geval waarin een cursor
    /// die alleen de tijd kent een regel kwijtraakt.
    /// </remarks>
    private static IReadOnlyList<LogRecord> Batchregels() =>
    [
        .. Testlogregels.GelijkeTijdstempels(T0, 3, "a"),
        .. Testlogregels.GelijkeTijdstempels(T1, 12, "b"),
        .. Testlogregels.GelijkeTijdstempels(T2, 2, "c"),
    ];

    [Fact]
    public async Task DeTailLevertElkeRegelPreciesEenKeerOokBijGelijkeTijdstempels()
    {
        var regels = Batchregels();
        var store = new Vastetelemetriestore().MetLogregels(regels);
        var scope = await Weergavelaag.Klantscope();

        var geleverd = new List<LogRecord>();
        var cursor = LogCursor.From(T0 - TimeSpan.FromSeconds(1));

        // Vijf per tik, zodat de grens midden in de groep van twaalf valt. Twintig tikken is ruim:
        // zeventien regels bij minstens één regel per tik is altijd binnen twintig klaar. Loopt de
        // lus vol, dan schuift de cursor niet op en is dat het echte probleem.
        for (var tik = 0; tik < 20; tik++)
        {
            var staart = await store.TailLogsAsync(
                scope,
                "factuur-intake",
                new LogTailQuery { Since = cursor, MaxLines = 5 });

            if (staart.Lines.Count == 0)
            {
                break;
            }

            geleverd.AddRange(staart.Lines);
            cursor = staart.Cursor;
        }

        Assert.Equal(regels.Count, geleverd.Count);
        Assert.Equal(
            regels.Select(r => r.Id).Order(StringComparer.Ordinal),
            geleverd.Select(r => r.Id).Order(StringComparer.Ordinal));

        // Geen enkele id twee keer: dat is de "geen dubbele" helft van de belofte.
        Assert.Equal(geleverd.Count, geleverd.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task DeTailLevertDeRegelsInOplopendeVolgordeVanTijdEnId()
    {
        // De volgorde is niet cosmetisch: het scherm voegt de nieuwe regels vooraan in, omgedraaid.
        // Komt de tail door de war, dan staat de tabel door de war.
        var store = new Vastetelemetriestore().MetLogregels(Batchregels());
        var scope = await Weergavelaag.Klantscope();

        var geleverd = new List<LogRecord>();
        var cursor = LogCursor.From(T0 - TimeSpan.FromSeconds(1));

        for (var tik = 0; tik < 20; tik++)
        {
            var staart = await store.TailLogsAsync(
                scope,
                "factuur-intake",
                new LogTailQuery { Since = cursor, MaxLines = 5 });

            if (staart.Lines.Count == 0)
            {
                break;
            }

            geleverd.AddRange(staart.Lines);
            cursor = staart.Cursor;
        }

        var verwacht = geleverd
            .OrderBy(r => r.Timestamp)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => r.Id);

        Assert.Equal(verwacht, geleverd.Select(r => r.Id));
    }

    [Fact]
    public async Task EenRegelOpDeCursortijdMetEenLagereIdKomtNietTerug()
    {
        // Zonder de gelijkspelclausule zou "ts >= @since" deze regel opnieuw leveren en
        // "ts > @since" zou hem overslaan. Precies één van de twee mag gebeuren, en het antwoord is
        // "geen van beide": hij is al geleverd, dus hij komt niet terug.
        var regels = Testlogregels.GelijkeTijdstempels(T1, 4, "b");
        var store = new Vastetelemetriestore().MetLogregels(regels);
        var scope = await Weergavelaag.Klantscope();

        var staart = await store.TailLogsAsync(
            scope,
            "factuur-intake",
            new LogTailQuery { Since = new LogCursor(T1, regels[1].Id), MaxLines = 10 });

        Assert.Equal([regels[2].Id, regels[3].Id], staart.Lines.Select(r => r.Id));
    }

    [Fact]
    public void DeGelijkspelclausuleKijktNaarDeTijdEnDaarnaNaarDeId()
    {
        var cursor = new LogCursor(T1, "b-0002");

        // Ouder: nooit.
        Assert.False(Vastetelemetriestore.NaCursor(Regel(T0, "b-9999"), cursor));

        // Gelijk, lagere of gelijke id: al geleverd.
        Assert.False(Vastetelemetriestore.NaCursor(Regel(T1, "b-0001"), cursor));
        Assert.False(Vastetelemetriestore.NaCursor(Regel(T1, "b-0002"), cursor));

        // Gelijk, hogere id: nog niet geleverd.
        Assert.True(Vastetelemetriestore.NaCursor(Regel(T1, "b-0003"), cursor));

        // Jonger: altijd, ook met een lagere id.
        Assert.True(Vastetelemetriestore.NaCursor(Regel(T2, "b-0001"), cursor));
    }

    [Fact]
    public async Task EenTailZonderNieuweRegelsLaatDeCursorStaan()
    {
        var regels = Testlogregels.GelijkeTijdstempels(T1, 3, "b");
        var store = new Vastetelemetriestore().MetLogregels(regels);
        var scope = await Weergavelaag.Klantscope();

        var cursor = new LogCursor(T1, regels[^1].Id);

        var staart = await store.TailLogsAsync(
            scope,
            "factuur-intake",
            new LogTailQuery { Since = cursor, MaxLines = 10 });

        Assert.True(staart.Lines.Count == 0);
        Assert.Equal(cursor, staart.Cursor);
    }

    [Fact]
    public void DeTailqueryDraagtDeGelijkspelclausuleOpDeId()
    {
        // De clausule zelf is SQL en draait alleen tegen Cosmos. Dat hij er staat is wél zonder
        // Cosmos vast te stellen, en dat is de moeite waard: valt hij weg, dan raakt de live tail
        // stil regels kwijt zodra twee regels dezelfde tijdstempel dragen. Dat is precies het soort
        // storing dat niemand als storing herkent.
        var gevonden = Broncode.Portaalbestanden()
            .Where(bestand => File.ReadAllText(bestand.FullName)
                .Contains("c.ts = @since AND c.id > @sinceId", StringComparison.Ordinal))
            .Select(Broncode.RelatiefPad)
            .ToArray();

        Assert.True(
            gevonden.Length > 0,
            "Geen enkel bestand in Soratus.Portal bevat de gelijkspelclausule " +
            "\"c.ts = @since AND c.id > @sinceId\".\n\n" +
            "Die clausule is wat de live tail bij gelijke tijdstempels bij elkaar houdt: met " +
            "alleen \"c.ts > @since\" slaat hij elke regel op de cursortijd over, en met " +
            "\"c.ts >= @since\" levert hij ze allemaal opnieuw. Is de query herschreven, dan hoort " +
            "deze test mee te veranderen — maar niet stilzwijgend te verdwijnen.");
    }

    [Fact]
    public void EenCultuurgevoeligeVergelijkingKiestEenAndereJongsteIdDanEenOrdinale()
    {
        // Geen test op onze code maar op een eigenschap van .NET die de test hieronder nodig heeft.
        // Enumerable.Max op strings gebruikt Comparer<string>.Default, en die is cultuurgevoelig:
        // bij nl-NL komt "ab1" ná "AB1", ordinaal komt hij ervoor. Zonder deze regel is de bevinding
        // hieronder een bewering; met deze regel is hij aantoonbaar.
        string[] ids = ["AB1", "ab1"];

        Assert.Equal("AB1", ids.Max());
        Assert.Equal("ab1", ids.OrderBy(id => id, StringComparer.Ordinal).Last());
        Assert.NotEqual(ids.Max(), ids.OrderBy(id => id, StringComparer.Ordinal).Last());
    }

    [Fact]
    public void DeOpslaglaagKiestDeTailcursorMetDezelfdeVergelijkingAlsWaarmeeHijSorteert()
    {
        // Deze test faalde toen hij werd geschreven. TailLogsAsync koos de cursor met
        // response.Max(line => line.Id) in de tak waar de hele pagina uit een tijdstempel bestaat,
        // terwijl hij overal elders ordinaal sorteert. Verschillen die twee vergelijkingen, dan
        // wijst de cursor niet naar de laatst geleverde regel en raakt de live tail stil een regel
        // kwijt. Die tak sorteert nu eerst ordinaal en neemt daarna de laatste regel, dus de cursor
        // is per definitie wat er als laatste uitging.
        var verdacht = Broncode.Portaalbestanden()
            .Select(bestand => (Pad: Broncode.RelatiefPad(bestand), Tekst: File.ReadAllText(bestand.FullName)))
            .Where(b => b.Tekst.Contains(".Max(line => line.Id)", StringComparison.Ordinal))
            .Select(b => b.Pad)
            .ToArray();

        Assert.True(
            verdacht.Length == 0,
            "De opslaglaag kiest een log-id met Enumerable.Max, en die vergelijkt cultuurgevoelig:\n" +
            $"  {string.Join("\n  ", verdacht)}\n\n" +
            "Overal elders in de tailquery wordt op StringComparer.Ordinal gesorteerd. Verschillen " +
            "die twee vergelijkingen op de id's die er staan, dan wijst de cursor niet naar de " +
            "laatst geleverde regel en raakt de live tail stil een regel kwijt. Kies de cursor " +
            "ordinaal — MaxBy met StringComparer.Ordinal, of gewoon de laatste regel van de al " +
            "gesorteerde lijst.");
    }

    private static LogRecord Regel(DateTimeOffset moment, string id) =>
        Testlogregels.Regel(id, moment, LogLevel.Info);
}
