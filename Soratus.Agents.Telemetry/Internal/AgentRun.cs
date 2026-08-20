using System.Diagnostics;
using System.Text.Json;
using Soratus.Agents.Contracts;

namespace Soratus.Agents.Telemetry.Internal;

/// <summary>De implementatie van <see cref="IAgentRun"/>.</summary>
/// <remarks>
/// <c>DisposeAsync</c> is met opzet volledig synchroon: hij zet alleen het definitieve document
/// in de buffer. Zou hij ergens op wachten, dan zou het herstellen van <see cref="RunScope"/>
/// bij de aanroeper niet meer aankomen en zouden logregels ná de run nog steeds de oude runId
/// dragen.
/// </remarks>
internal sealed class AgentRun : IAgentRun
{
    private readonly AgentIdentity _identity;
    private readonly TelemetryWriter _writer;
    private readonly LogRecordFactory _logs;
    private readonly AgentRun? _previous;
    private readonly Stopwatch _stopwatch;
    private readonly string _partitionKey;

    private int _itemsProcessed;
    private int _itemsFailed;
    private bool _rolledBack;
    private string? _errorType;
    private string? _errorMessage;
    private int _disposed;

    internal AgentRun(
        AgentIdentity identity,
        TelemetryWriter writer,
        LogRecordFactory logs,
        TriggerKind trigger)
    {
        _identity = identity;
        _writer = writer;
        _logs = logs;
        _previous = RunScope.Current;

        RunId = UlidGenerator.NewRunId();
        StartedAt = DateTimeOffset.UtcNow;
        Trigger = trigger;
        _partitionKey = RunRecord.BuildPartitionKey(identity.AgentName, StartedAt);
        _stopwatch = Stopwatch.StartNew();
    }

    public string RunId { get; }

    public DateTimeOffset StartedAt { get; }

    public TriggerKind Trigger { get; }

    public int ItemsProcessed => Volatile.Read(ref _itemsProcessed);

    public int ItemsFailed => Volatile.Read(ref _itemsFailed);

    public void Processed(int count = 1)
    {
        if (count > 0)
        {
            Interlocked.Add(ref _itemsProcessed, count);
        }
    }

    public void FailedItems(int count = 1)
    {
        if (count > 0)
        {
            Interlocked.Add(ref _itemsFailed, count);
        }
    }

    public void MarkRolledBack() => _rolledBack = true;

    public void Fail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        _errorType = exception.GetType().FullName;
        _errorMessage = exception.Message;

        // De stacktrace hoort in extra: de operator ziet in het portaal de foutregel van de
        // gefaalde run en moet die kunnen uitklappen zonder eerst Application Insights te openen.
        JsonElement? extra = ExtraJson.Build(
            state: null,
            payload: null,
            exception: exception,
            category: null,
            eventId: default,
            scopeProvider: null,
            maxLength: _logs.MaxExtraLength);

        _writer.Enqueue(_logs.Create(
            Contracts.LogLevel.Error,
            "run.failed",
            $"Run {RunId} is mislukt: {exception.Message}",
            extra,
            DateTimeOffset.UtcNow));
    }

    public void Fail(string errorType, string errorMessage)
    {
        _errorType = errorType;
        _errorMessage = errorMessage;

        _writer.Enqueue(_logs.Create(
            Contracts.LogLevel.Error,
            "run.failed",
            $"Run {RunId} is mislukt: {errorMessage}",
            extra: null,
            DateTimeOffset.UtcNow));
    }

    /// <summary>Schrijft het openingsdocument met <see cref="RunResult.Running"/>.</summary>
    internal void Begin()
    {
        RunScope.Current = this;
        _writer.Enqueue(BuildRecord(RunResult.Running, finishedAt: null));
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        RunScope.Current = _previous;
        _stopwatch.Stop();

        RunResult result = _errorType is not null
            ? RunResult.Failed
            : ItemsProcessed == 0 && ItemsFailed == 0
                ? RunResult.Skipped
                : RunResult.Ok;

        _writer.Enqueue(BuildRecord(result, DateTimeOffset.UtcNow));
        return ValueTask.CompletedTask;
    }

    private RunRecord BuildRecord(RunResult result, DateTimeOffset? finishedAt) => new()
    {
        Id = RunId,
        PartitionKey = _partitionKey,
        CustomerId = _identity.CustomerId,
        AgentName = _identity.AgentName,
        StartedAt = StartedAt,
        FinishedAt = finishedAt,
        DurationMs = finishedAt is null ? null : _stopwatch.ElapsedMilliseconds,
        Result = result,
        ItemsProcessed = ItemsProcessed,
        ItemsFailed = ItemsFailed,
        RolledBack = _rolledBack,
        Trigger = Trigger,
        ErrorType = _errorType,
        ErrorMessage = _errorMessage,
        Version = _identity.Version,
    };
}
