namespace Soratus.Agents.AspNetCore.Tests.Hulpmiddelen;

/// <summary>
/// Een klok die stilstaat tot een test hem vooruitzet.
/// </summary>
/// <remarks>
/// <para>Hij bestaat omdat de drempels van dit contract in minuten zijn en een test in
/// milliseconden. Een hartslag van dertig seconden en een degraded-grens van twee minuten zijn
/// niet te meten door te wachten; ze zijn alleen te meten door de klok in de hand te houden. Dat
/// is ook waarom geen enkele methode in <c>Soratus.Agents.Contracts</c> zelf de klok leest.</para>
///
/// <para><see cref="GetTimestamp"/> loopt mee met <see cref="GetUtcNow"/> en niet met een
/// <c>Stopwatch</c>. Anders zou de duur van een run uit een andere bron komen dan zijn begin- en
/// eindtijd, en dan kan een run drie milliseconden duren tussen twee tijdstempels die een uur
/// uiteen liggen.</para>
///
/// <para><c>CreateTimer</c> geeft een <em>echte</em> timer terug en houdt alleen bij welke
/// wachttijd er is aangevraagd. Regisseren van de tik is hier niet nodig — de tests wachten op
/// echte dingen, een verzoek en het afsluiten van een host — maar de aangevraagde wachttijd wél
/// weten is dat wel: dat is de enige manier om te zien dat de hartslag op het interval van het
/// contract loopt.</para>
/// </remarks>
internal sealed class StuurbareKlok : TimeProvider
{
    private readonly List<TimeSpan> _wachttijden = [];
    private long _ticks;

    internal StuurbareKlok(DateTimeOffset start)
    {
        Start = start;
        _ticks = start.UtcTicks;
    }

    /// <summary>Het moment waarop deze klok begon, voor een test die het beginpunt nodig heeft.</summary>
    internal DateTimeOffset Start { get; }

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _ticks);

    /// <summary>
    /// Elke wachttijd die iemand via deze klok heeft aangevraagd.
    /// </summary>
    /// <remarks>
    /// Hiermee is het hartslaginterval te meten zonder erop te wachten. Dat is nodig, want een
    /// hartslag die vijf keer te traag loopt maakt élke dienst permanent degraded, en dat is aan
    /// een test over documenten niet te zien: die documenten zijn gewoon goed, ze komen alleen te
    /// laat. Gemeten met een mutatie: zonder deze meting bleef die fout groen.
    /// </remarks>
    internal IReadOnlyList<TimeSpan> GevraagdeWachttijden
    {
        get
        {
            lock (_wachttijden)
            {
                return [.. _wachttijden];
            }
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_wachttijden)
        {
            _wachttijden.Add(dueTime);
        }

        // Wél een echte timer: de tests wachten op echte dingen (een verzoek, het afsluiten van een
        // host) en hebben geen geregisseerde tik nodig.
        return base.CreateTimer(callback, state, dueTime, period);
    }

    /// <summary>Zet de klok vooruit.</summary>
    internal void Verzet(TimeSpan hoeveel) => Interlocked.Add(ref _ticks, hoeveel.Ticks);
}
