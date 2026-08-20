using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Soratus.Agents.Telemetry.Internal;
using ContractLogLevel = Soratus.Agents.Contracts.LogLevel;

namespace Soratus.Agents.Telemetry.Logging;

/// <summary>
/// Zet een gewone <c>ILogger</c>-aanroep om naar een <c>LogRecord</c>.
/// </summary>
/// <remarks>
/// Dit is het stuk dat de wrijving wegneemt. Een bestaande agent die netjes met
/// <c>ILogger</c> werkt, verschijnt zonder één regel wijziging in het portaal: de
/// structured-logging-state komt in <c>extra</c>, de uitzondering levert een stacktrace, en de
/// runId wordt uit de asynchrone stroom gehaald.
/// </remarks>
internal sealed class SoratusLogger(
    string category,
    LogLevel minimum,
    LogRecordFactory factory,
    TelemetryWriter writer,
    Func<IExternalScopeProvider?> scopeProvider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
        scopeProvider()?.Push(state);

    /// <summary>
    /// De ondergrens van deze categorie. Debug en trace vallen er altijd buiten; voor
    /// framework-categorieën ligt de grens een stap hoger. Zie
    /// <see cref="SoratusLoggerProvider"/> voor waarom.
    /// </summary>
    /// <remarks>
    /// Dit wordt hier afgedwongen en niet met een <c>AddFilter</c>-regel, omdat een filter door de
    /// host overschreven kan worden. Een contractregel die een agent kan uitzetten is geen regel.
    /// </remarks>
    public bool IsEnabled(LogLevel logLevel) => logLevel >= minimum && logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        ContractLogLevel level = Map(logLevel);
        var agentEvent = state as AgentEventState;

        string message = agentEvent?.Message ?? formatter(state, exception) ?? string.Empty;
        string eventName = agentEvent?.EventName ?? DeriveEventName(category, eventId);

        JsonElement? extra = ExtraJson.Build(
            state: agentEvent is null ? state as IEnumerable<KeyValuePair<string, object?>> : null,
            payload: agentEvent?.Payload,
            exception: exception,
            category: category,
            eventId: eventId,
            scopeProvider: scopeProvider(),
            maxLength: factory.MaxExtraLength);

        writer.Enqueue(factory.Create(level, eventName, message, extra, DateTimeOffset.UtcNow));
    }

    private static ContractLogLevel Map(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Warning => ContractLogLevel.Warn,
        LogLevel.Error or LogLevel.Critical => ContractLogLevel.Error,
        _ => ContractLogLevel.Info,
    };

    /// <summary>
    /// Leidt een puntgescheiden gebeurtenisnaam af als de bouwer er geen heeft gegeven.
    /// </summary>
    /// <remarks>
    /// Bij voorkeur uit de naam van de <c>EventId</c>, anders uit het laatste deel van de
    /// categorie: <c>Facturen.FactuurIntakeAgent</c> wordt <c>factuur.intake.agent</c>. Geraden,
    /// dus, maar wel stabiel en herkenbaar — en wie een betere naam wil, gebruikt
    /// <see cref="AgentLoggerExtensions.AgentEvent"/>.
    /// </remarks>
    private static string DeriveEventName(string category, EventId eventId)
    {
        string source = !string.IsNullOrEmpty(eventId.Name)
            ? eventId.Name
            : category[(category.LastIndexOf('.') + 1)..];

        if (string.IsNullOrEmpty(source))
        {
            return "log";
        }

        if (source.Contains('.'))
        {
            return source.ToLowerInvariant();
        }

        var builder = new StringBuilder(source.Length + 8);
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (!char.IsLetterOrDigit(c))
            {
                continue;
            }

            if (char.IsUpper(c) && builder.Length > 0)
            {
                builder.Append('.');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.Length == 0 ? "log" : builder.ToString();
    }
}
