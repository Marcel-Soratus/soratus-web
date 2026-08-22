namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// Een klok die stilstaat en waarvan elk wachten meteen voorbij is.
/// </summary>
/// <remarks>
/// <para><strong>Dit bestaat omdat de kostencollector met opzet traag is.</strong> De stilte tussen
/// twee aanroepen aan Cost Management staat standaard op vierhonderd seconden — gemeten, zie
/// <c>AzureCostOptions.PauseSeconds</c> — en een test die daar op wacht wordt niet gedraaid. Een
/// <c>TimeProvider</c> die alleen <c>GetUtcNow</c> overschrijft is hier niet genoeg:
/// <c>Task.Delay(span, provider, token)</c> vraagt de provider om een <c>ITimer</c>, en de
/// standaardimplementatie daarvan loopt op de échte klok. Vandaar dat ook <see cref="CreateTimer"/>
/// wordt overschreven.</para>
///
/// <para><strong>En hij houdt bij waaróp er is gewacht.</strong> Dat is geen bijzaak maar de kern van
/// wat er over deze collector te bewijzen valt: de stilte tussen twee aanroepen ís het ontwerp. Zonder
/// <see cref="Wachttijden"/> zou een test die zegt "er zijn twee aanroepen gedaan" ook groen zijn als
/// die twee binnen een milliseconde achter elkaar gingen — en dat is precies de fout die de emmer
/// leegtrekt.</para>
///
/// <para>De callback loopt op de threadpool en niet vanuit <see cref="CreateTimer"/> zelf. Dat tweede
/// zou de callback aanroepen vóórdat <c>Task.Delay</c> zijn timer heeft opgeborgen, en dat is een race
/// met de interne implementatie van de basisbibliotheek.</para>
/// </remarks>
internal sealed class Snelleklok(DateTimeOffset moment, bool meteenAf = true) : TimeProvider
{
    /// <summary>
    /// Of een gevraagde wachttijd meteen afgaat, of nooit.
    /// </summary>
    /// <remarks>
    /// <para><strong>"Nooit" is er voor de lus van een achtergronddienst.</strong> Een klok die meteen
    /// afgaat laat zo'n lus rondtollen tussen het starten en het stoppen van de dienst, en dan meet een
    /// test die naar de eerste wachttijd kijkt zijn eigen ruis. Met een wachttijd die nooit afgaat parkeert
    /// de lus precies één keer, en is wat er dán is vastgelegd — de gevraagde wachttijd, het gemelde
    /// moment van de volgende run — deterministisch. Het stoppen van de dienst breekt het wachten af
    /// via het annuleringstoken en niet via de klok.</para>
    /// </remarks>
    public bool MeteenAf { get; } = meteenAf;

    /// <summary>Het moment dat deze klok teruggeeft. Te verzetten binnen een test.</summary>
    public DateTimeOffset Nu { get; set; } = moment;

    /// <summary>
    /// Elke wachttijd waarom is gevraagd, in de volgorde waarin dat gebeurde.
    /// </summary>
    /// <remarks>
    /// Inclusief de wachttijden die nul of oneindig zijn; filteren doet de test, want wat er wél in
    /// hoort te staan verschilt per test.
    /// </remarks>
    public List<TimeSpan> Wachttijden { get; } = [];

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => Nu;

    /// <inheritdoc />
    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        Wachttijden.Add(dueTime);

        return MeteenAf ? new Nulwachttijd(callback, state) : new Nooitwachttijd();
    }

    /// <summary>Een timer die meteen afgaat.</summary>
    private sealed class Nulwachttijd : ITimer
    {
        internal Nulwachttijd(TimerCallback callback, object? state) =>
            ThreadPool.QueueUserWorkItem(_ => callback(state));

        /// <inheritdoc />
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        /// <inheritdoc />
        public void Dispose()
        {
            // Er is niets af te breken: de callback is al in de wachtrij gezet.
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Een timer die nooit afgaat; het wachten eindigt alleen door annulering.</summary>
    private sealed class Nooitwachttijd : ITimer
    {
        /// <inheritdoc />
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        /// <inheritdoc />
        public void Dispose()
        {
            // Er is niets af te breken: deze timer gaat nooit af.
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
