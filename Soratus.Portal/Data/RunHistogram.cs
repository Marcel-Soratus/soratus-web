namespace Soratus.Portal.Data;

/// <summary>
/// Wat er in één tijdblok is gedraaid.
/// </summary>
/// <param name="Runs">Het aantal runs dat in dit blok is gestart.</param>
/// <param name="Failed">Hoeveel daarvan mislukten.</param>
/// <remarks>
/// Bewust een eigen type en niet <c>SparkBlock</c> uit <c>Components.Shared</c>. De datalaag hoort
/// niet te weten dat er een sparkline bestaat; dat het toevallig dezelfde twee getallen zijn maakt
/// het nog geen presentatietype. De omzetting valt in <see cref="Views.PortalViews"/>, waar de
/// vertaling naar het scherm thuishoort.
/// </remarks>
public readonly record struct RunBucket(int Runs, int Failed);

/// <summary>
/// Het tijdvenster van een runhistogram: waar het begint, hoe breed een blok is en hoeveel blokken
/// er zijn.
/// </summary>
/// <param name="Start">Het begin van het eerste blok, altijd op een heel uur in UTC.</param>
/// <param name="BlockSize">De breedte van één blok.</param>
/// <param name="BlockCount">Het aantal blokken.</param>
public sealed record HistogramWindow(DateTimeOffset Start, TimeSpan BlockSize, int BlockCount)
{
    /// <summary>Het einde van het laatste blok.</summary>
    public DateTimeOffset End => Start + (BlockSize * BlockCount);

    /// <summary>
    /// Het venster van de sparkline: twaalf blokken van twee uur, samen 24 uur (§8).
    /// </summary>
    /// <param name="now">Het huidige moment.</param>
    /// <returns>Het venster, uitgelijnd op hele blokken.</returns>
    /// <remarks>
    /// Het venster wordt uitgelijnd op een even UTC-uur in plaats van simpelweg "nu min 24 uur" te
    /// nemen. Reden: de query telt per heel uur, en met een venster dat om 17:20 begint valt het uur
    /// 19:00–19:59 half in het ene blok en half in het andere. Er is dan geen manier om die runs
    /// eerlijk toe te wijzen. Met uitlijning valt elk uur in precies één blok.
    ///
    /// Het laatste blok is daarmee het blok waarin <paramref name="now"/> valt — meestal nog niet
    /// vol. Dat is voor een sparkline juist goed: het meest rechtse blokje groeit terwijl je kijkt.
    /// </remarks>
    public static HistogramWindow Last24Hours(DateTimeOffset now)
    {
        var utc = now.UtcDateTime;
        var blockStart = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour - (utc.Hour % 2), 0, 0, DateTimeKind.Utc);
        var end = new DateTimeOffset(blockStart, TimeSpan.Zero).AddHours(2);

        return new HistogramWindow(end.AddHours(-24), TimeSpan.FromHours(2), 12);
    }

    /// <summary>
    /// In welk blok dit moment valt.
    /// </summary>
    /// <param name="moment">Het moment.</param>
    /// <returns>De index, of <c>null</c> als het buiten het venster valt.</returns>
    public int? IndexOf(DateTimeOffset moment)
    {
        if (moment < Start || moment >= End)
        {
            return null;
        }

        var index = (int)((moment - Start).Ticks / BlockSize.Ticks);
        return index >= 0 && index < BlockCount ? index : null;
    }

    /// <summary>Een leeg histogram voor dit venster.</summary>
    public IReadOnlyList<RunBucket> Empty() => new RunBucket[BlockCount];
}
