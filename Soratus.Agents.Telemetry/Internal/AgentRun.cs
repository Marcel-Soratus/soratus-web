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

        // Het volledige typenaam blijft staan. Voor de operator is de naamruimte juist het nuttige
        // deel — Sync.ValidationException is een ander defect dan Mail.ValidationException — en hier
        // afkappen zou dat onherstelbaar weggooien. Of dit veld naar de klant geprojecteerd mag
        // worden is een vraag voor het portaal, niet voor de schrijfkant.
        _errorType = exception.GetType().FullName;

        // errorMessage is wél klantzichtbaar en krijgt daarom dezelfde knip als msg. Dit is geen
        // theoretisch geval: de boodschap van een CosmosException is een halve pagina diagnostiek
        // over meerdere regels, en die zou hier ongefilterd in een klantzichtbaar veld belanden.
        // Er gaat niets verloren — de volledige boodschap staat hieronder in
        // extra._exception.message van de run.failed-regel, en die is operator-only.
        _errorMessage = MessageTruncation.Cut(exception.Message).Message;

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
        // Ook een zelf opgegeven boodschap wordt geknipt. Dat de bouwer hem zelf schreef maakt hem
        // niet veiliger — hij kan er net zo goed een respons van een externe partij in doorgeven.
        //
        // Ook hier gaat niets verloren, maar langs een andere weg dan bij een uitzondering: er is
        // geen extra._exception, en de rest belandt in extra.msgOverflow van de run.failed-regel
        // hieronder. Dat werkt omdat die regel de onafgekapte boodschap in zijn tekst draagt en dus
        // zelf langs de knip op msg gaat.
        _errorType = errorType;
        _errorMessage = MessageTruncation.Cut(errorMessage).Message;

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
