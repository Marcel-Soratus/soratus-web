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
    private readonly TimeProvider _clock;
    private readonly Action? _onCompleted;
    private readonly long _startTimestamp;
    private readonly string _partitionKey;

    private int _itemsProcessed;
    private int _itemsFailed;
    private bool _rolledBack;
    private string? _errorType;
    private string? _errorMessage;
    private int _disposed;
    private TimeSpan _elapsed;

    /// <summary>
    /// Opent een run. De klok komt als <see cref="TimeProvider"/> binnen en wordt nooit
    /// rechtstreeks gelezen, zodat de duur en de tijdstempels van een run te meten zijn zonder
    /// te wachten.
    /// </summary>
    /// <param name="identity">Wie deze run draait. Bij een geherbergde agent is dat niet het proces.</param>
    /// <param name="writer">De bufferlaag naar de opslag.</param>
    /// <param name="logs">De logfabriek van dezelfde agent, zodat een <c>run.failed</c>-regel bij hem hoort.</param>
    /// <param name="trigger">Waardoor deze run startte.</param>
    /// <param name="clock">De klok.</param>
    /// <param name="onCompleted">
    /// Wordt precies één keer aangeroepen zodra de run is afgesloten, ná het wegschrijven van het
    /// definitieve document. Hiermee houdt een geherbergde agent bij hoeveel aanroepen er lopen;
    /// die telling bepaalt zijn gemelde levensfase.
    /// </param>
    internal AgentRun(
        AgentIdentity identity,
        TelemetryWriter writer,
        LogRecordFactory logs,
        TriggerKind trigger,
        TimeProvider clock,
        Action? onCompleted = null)
    {
        _identity = identity;
        _writer = writer;
        _logs = logs;
        _previous = RunScope.Current;
        _clock = clock;
        _onCompleted = onCompleted;

        RunId = UlidGenerator.NewRunId();
        StartedAt = clock.GetUtcNow();
        Trigger = trigger;
        _partitionKey = RunRecord.BuildPartitionKey(identity.AgentName, StartedAt);
        _startTimestamp = clock.GetTimestamp();
    }

    public string RunId { get; }

    /// <summary>
    /// De logfabriek van de agent die deze run draait.
    /// </summary>
    /// <remarks>
    /// Hierlangs vindt een gewone <c>ILogger</c>-aanroep binnen een run de juiste agentnaam. In
    /// een host met één agent is dat dezelfde fabriek als die in de container staat; in een host
    /// met meerdere agents is dit de enige manier om te weten voor wie er gelogd wordt, want de
    /// <c>ILogger</c> van de aanroeper weet daar niets van.
    /// </remarks>
    internal LogRecordFactory Logs => _logs;

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
            _clock.GetUtcNow()));
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
            _clock.GetUtcNow()));
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
        _elapsed = _clock.GetElapsedTime(_startTimestamp);

        RunResult result = _errorType is not null
            ? RunResult.Failed
            : ItemsProcessed == 0 && ItemsFailed == 0
                ? RunResult.Skipped
                : RunResult.Ok;

        _writer.Enqueue(BuildRecord(result, _clock.GetUtcNow()));

        // Ná het wegschrijven, zodat een teller die op nul springt niet vóór het document van
        // deze run kan aankomen: dan zou de hartslag 'wacht op werk' melden terwijl de laatst
        // afgeronde run nog niet bestaat, en dat is precies één tel lang de verkeerde waarheid.
        _onCompleted?.Invoke();
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
        DurationMs = finishedAt is null ? null : (long)_elapsed.TotalMilliseconds,
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
