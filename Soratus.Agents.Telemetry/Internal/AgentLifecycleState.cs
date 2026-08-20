using Soratus.Agents.Contracts;

namespace Soratus.Agents.Telemetry.Internal;

/// <summary>
/// Wat de agent op dit moment over zijn eigen levenscyclus meldt.
/// </summary>
/// <remarks>
/// Los object omdat twee kanten eraan zitten: de agent zet <c>IdleWaiting</c>, en de
/// hartslagdienst leest de waarde bij elke upsert. Er staat bewust geen status in — die leidt
/// het portaal af.
/// </remarks>
internal sealed class AgentLifecycleState
{
    private int _current = (int)AgentLifecycle.Running;

    internal AgentLifecycle Current
    {
        get => (AgentLifecycle)Volatile.Read(ref _current);
        set => Volatile.Write(ref _current, (int)value);
    }
}
