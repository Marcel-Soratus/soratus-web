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

    /// <summary>
    /// Bouwt één logregel.
    /// </summary>
    /// <remarks>
    /// Dit is de enige plek waar een <see cref="LogRecord"/> ontstaat, en daarom de enige plek waar
    /// de knip op <c>msg</c> wordt aangeroepen — de knip zelf staat in
    /// <see cref="MessageTruncation"/> in <c>Soratus.Agents.Contracts</c>, zodat het portaal bij het
    /// projecteren naar de klant dezelfde definitie gebruikt. Beide schrijfpaden komen hier langs: de
    /// <c>ILoggerProvider</c> — waarlangs een bestaande agent met gewone <c>ILogger</c>-aanroepen
    /// logt — en <see cref="AgentRun.Fail(Exception)"/>, dat de foutboodschap van een uitzondering
    /// in <c>msg</c> zet. Dat tweede pad is niet theoretisch: de boodschap van een
    /// <c>CosmosException</c> is een halve pagina met diagnostiek, en die zou zonder deze knip
    /// rechtstreeks in het veld belanden dat de klant leest.
    /// </remarks>
    internal LogRecord Create(
        Contracts.LogLevel level,
        string eventName,
        string message,
        JsonElement? extra,
        DateTimeOffset timestamp)
    {
        (string msg, string? overflow) = MessageTruncation.Cut(message, _options.MaxMessageLength);

        if (overflow is not null)
        {
            extra = ExtraJson.WithField(
                extra,
                MessageTruncation.OverflowKey,
                overflow.Length <= _options.MaxExtraLength
                    ? overflow
                    : overflow[.._options.MaxExtraLength]);
        }

        return new LogRecord
        {
            Id = UlidGenerator.NewUlid(timestamp),
            PartitionKey = LogRecord.BuildPartitionKey(identity.AgentName, timestamp),
            Timestamp = timestamp,
            Level = level,
            Event = string.IsNullOrWhiteSpace(eventName) ? "log" : eventName,
            Message = msg,
            RunId = RunScope.Current?.RunId,
            Extra = extra,
            CustomerId = identity.CustomerId,
            AgentName = identity.AgentName,
        };
    }
}
