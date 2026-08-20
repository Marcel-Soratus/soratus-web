using System.Collections;

namespace Soratus.Agents.Telemetry.Logging;

/// <summary>
/// De state van een logregel die via <see cref="AgentLoggerExtensions"/> is geschreven.
/// </summary>
/// <remarks>
/// Implementeert de gebruikelijke KVP-lijst, zodat de console-provider en Application Insights
/// deze regel net zo goed kunnen renderen als de Soratus-provider. Zo hoeft een agentbouwer
/// niet te kiezen tussen leesbare lokale logs en het portaal.
/// </remarks>
internal sealed class AgentEventState(string eventName, string message, object? payload)
    : IReadOnlyList<KeyValuePair<string, object?>>
{
    internal string EventName { get; } = eventName;

    internal string Message { get; } = message;

    internal object? Payload { get; } = payload;

    public int Count => 2;

    public KeyValuePair<string, object?> this[int index] => index switch
    {
        0 => new KeyValuePair<string, object?>("event", EventName),
        1 => new KeyValuePair<string, object?>("{OriginalFormat}", Message),
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => Message;
}
