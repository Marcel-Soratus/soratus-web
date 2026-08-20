using System.Text.Json;
using Microsoft.Extensions.Options;
using Soratus.Agents.Contracts;

namespace Soratus.Agents.Telemetry.Internal;

/// <summary>
/// Maakt <see cref="LogRecord"/>-documenten. Op één plek, zodat de sleutels, de partitiesleutel
/// en het afkappen van te lange berichten overal gelijk zijn.
/// </summary>
internal sealed class LogRecordFactory(AgentIdentity identity, IOptions<SoratusTelemetryOptions> options)
{
    private readonly SoratusTelemetryOptions _options = options.Value;

    /// <summary>De maximale lengte van de <c>extra</c>-JSON.</summary>
    internal int MaxExtraLength => _options.MaxExtraLength;

    internal LogRecord Create(
        Contracts.LogLevel level,
        string eventName,
        string message,
        JsonElement? extra,
        DateTimeOffset timestamp)
    {
        return new LogRecord
        {
            Id = UlidGenerator.NewUlid(timestamp),
            PartitionKey = LogRecord.BuildPartitionKey(identity.AgentName, timestamp),
            Timestamp = timestamp,
            Level = level,
            Event = string.IsNullOrWhiteSpace(eventName) ? "log" : eventName,
            Message = Truncate(message),
            RunId = RunScope.Current?.RunId,
            Extra = extra,
            CustomerId = identity.CustomerId,
            AgentName = identity.AgentName,
        };
    }

    private string Truncate(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return "(geen bericht)";
        }

        return message.Length <= _options.MaxMessageLength
            ? message
            : string.Concat(message.AsSpan(0, _options.MaxMessageLength), " … (afgekapt)");
    }
}
