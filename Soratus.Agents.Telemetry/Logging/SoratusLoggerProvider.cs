using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Soratus.Agents.Telemetry.Internal;

namespace Soratus.Agents.Telemetry.Logging;

/// <summary>
/// De <c>ILoggerProvider</c> die gewone <c>ILogger</c>-aanroepen in het portaal laat verschijnen.
/// </summary>
/// <remarks>
/// De afhankelijkheden worden lui opgehaald. Zou deze provider ze in zijn constructor vragen,
/// dan ontstaat er een kring: de logfabriek maakt providers, deze provider vraagt de schrijver,
/// en de schrijver vraagt een logger van diezelfde fabriek.
///
/// Regels uit de bibliotheek zelf worden overgeslagen. Anders zou een waarschuwing over een
/// volle buffer een nieuwe regel in diezelfde buffer zetten. Interne problemen gaan naar de
/// gewone <c>ILogger</c> van de host, en dus naar console of Application Insights.
/// </remarks>
internal sealed class SoratusLoggerProvider(IServiceProvider services) : ILoggerProvider, ISupportExternalScope
{
    private const string OwnNamespace = "Soratus.Agents.Telemetry";

    private readonly ConcurrentDictionary<string, ILogger> _loggers = new(StringComparer.Ordinal);

    private IExternalScopeProvider? _scopeProvider;
    private LogRecordFactory? _factory;
    private TelemetryWriter? _writer;

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, static (category, provider) => provider.Build(category), this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopeProvider = scopeProvider;

    public void Dispose() => _loggers.Clear();

    private ILogger Build(string category)
    {
        if (IsOwn(category))
        {
            return NullLogger.Instance;
        }

        _factory ??= services.GetRequiredService<LogRecordFactory>();
        _writer ??= services.GetRequiredService<TelemetryWriter>();

        return new SoratusLogger(category, _factory, _writer, () => _scopeProvider);
    }

    private static bool IsOwn(string category) =>
        category.Equals(OwnNamespace, StringComparison.Ordinal)
        || category.StartsWith(OwnNamespace + ".", StringComparison.Ordinal);

    /// <summary>Slikt alles. Voor categorieën van de bibliotheek zelf.</summary>
    private sealed class NullLogger : ILogger
    {
        internal static readonly NullLogger Instance = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}
