namespace Soratus.Agents.Telemetry.Internal;

/// <summary>
/// Draagt de lopende run mee over de asynchrone stroom, zodat elke logregel binnen een run
/// automatisch de juiste runId krijgt.
/// </summary>
/// <remarks>
/// De waarde wordt uitsluitend synchroon gezet — in <c>StartRunAsync</c> vóór enige <c>await</c>
/// en in <c>DisposeAsync</c>, die niets afwacht. Dat is geen detail: een <c>AsyncLocal</c> die
/// ná een <c>await</c> in een aangeroepen methode wordt gezet, is bij de aanroeper niet
/// zichtbaar, en dan zouden logregels stil hun runId kwijtraken.
/// </remarks>
internal static class RunScope
{
    private static readonly AsyncLocal<AgentRun?> Value = new();

    internal static AgentRun? Current
    {
        get => Value.Value;
        set => Value.Value = value;
    }
}
