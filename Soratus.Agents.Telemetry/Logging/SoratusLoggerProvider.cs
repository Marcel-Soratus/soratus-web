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
///
/// Framework-categorieën komen pas vanaf <c>warn</c> door; zie <see cref="MinimumFor"/>.
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

        return new SoratusLogger(category, MinimumFor(category), _factory, _writer, () => _scopeProvider);
    }

    /// <summary>
    /// De ondergrens voor een categorie: <c>Warning</c> voor framework-categorieën,
    /// <c>Information</c> voor de rest.
    /// </summary>
    /// <remarks>
    /// <para>Op <c>info</c> vertelt het framework dingen over zichzelf, niet over het werk van de
    /// klant. Gemeten in de opslag: <c>Microsoft.Hosting.Lifetime</c> schreef
    /// <c>"Content root path: D:\SORATUS\Website\..."</c> in <c>msg</c>, en <c>msg</c> wordt door de
    /// klant gelezen. Dat is een absoluut bestandspad op één regel, dus de knip op de
    /// regelovergang helpt er niet tegen.</para>
    ///
    /// <para>Dat "Application started" hiermee verdwijnt kost niets: dat feit staat beter
    /// gemodelleerd in het registratiedocument, als <c>startedAt</c> en <c>lifecycle</c>. Het
    /// portaal toont "draait sinds" daaruit, en een herstart geeft een nieuwe <c>startedAt</c>. Een
    /// feit in een veld verslaat een regel die je moet zien langskomen.</para>
    ///
    /// <para><c>warn</c> en <c>error</c> komen wél door, want dan gaat een framework-melding over
    /// echt gedrag. <c>HttpsRedirectionMiddleware — Failed to determine the https port for
    /// redirect</c> is onschadelijk, maar het is een echte melding en een operator hoort hem te
    /// kunnen vinden.</para>
    ///
    /// <para>De toets is de <em>categorie</em> en niet de inhoud van het bericht. Een patroon in de
    /// tekst zou vandaag op een pad met <c>D:\</c> letten en morgen een pad met <c>/srv/</c> missen.
    /// Een voorvoegsel op de categorie is een structureel gegeven dat de logger al heeft.</para>
    /// </remarks>
    private static LogLevel MinimumFor(string category) =>
        IsFramework(category) ? LogLevel.Warning : LogLevel.Information;

    private static bool IsOwn(string category) => HasPrefix(category, OwnNamespace);

    private static bool IsFramework(string category) =>
        HasPrefix(category, "Microsoft") || HasPrefix(category, "System");

    /// <summary>
    /// Of <paramref name="category"/> de naamruimte <paramref name="prefix"/> is of erin valt.
    /// </summary>
    /// <remarks>
    /// Met het punt erbij, zodat <c>Microsoft</c> en <c>Microsoft.Hosting</c> matchen maar
    /// <c>MicrosoftKoppeling</c> van een agentbouwer niet.
    /// </remarks>
    private static bool HasPrefix(string category, string prefix) =>
        category.Equals(prefix, StringComparison.Ordinal)
        || category.StartsWith(prefix + ".", StringComparison.Ordinal);

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
