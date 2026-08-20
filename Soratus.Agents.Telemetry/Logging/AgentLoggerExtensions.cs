using Microsoft.Extensions.Logging;

namespace Soratus.Agents.Telemetry.Logging;

/// <summary>
/// Schrijft een domeingebeurtenis weg: één gebeurtenisnaam, één zin, en vrije context.
/// </summary>
/// <remarks>
/// De gebeurtenisnaam is het enige veld waar de bouwer echt over moet nadenken — alleen hij
/// weet wat er gebeurde. De rest (runId, klant, agentnaam, tijdstip, sleutel, partitie) vult de
/// bibliotheek.
///
/// <para>
/// <strong>Let op het verschil tussen de twee velden.</strong> Het bericht wordt door de klant
/// gelezen: schrijf daar één Nederlandse zin over wat er met zijn werk is gebeurd, en geen
/// bestandspaden, klasse- of methodenamen, endpoints, scopes of resource groups. <c>extra</c> is
/// operator-only — de klantweergave van een logregel draagt dat veld niet — en is dus juist de
/// plek voor die context. Verwijs in het bericht ook niet naar <c>extra</c>: de klant kan er niet
/// bij, en een verwijzing naar iets onzichtbaars is geen mededeling maar een raadsel.
/// </para>
/// <para>
/// Zet je er toch meer dan één regel in, dan houdt de bibliotheek alleen de eerste regel over en
/// verhuist de rest naar <c>msgOverflow</c> in <c>extra</c>. Dat is geen suggestie maar gedrag.
/// </para>
/// <para>
/// Deze regels lopen gewoon door de <c>ILogger</c>-keten, dus ze verschijnen ook in de console
/// en in Application Insights. Wie liever plain <c>logger.LogInformation(...)</c> schrijft, komt
/// er net zo goed in; dan raadt de bibliotheek de gebeurtenisnaam uit de categorie — en geldt
/// dezelfde knip.
/// </para>
/// </remarks>
public static class AgentLoggerExtensions
{
    /// <summary>Schrijft een gebeurtenis op niveau <c>info</c>.</summary>
    /// <param name="logger">De logger van de agent.</param>
    /// <param name="eventName">Puntgescheiden gebeurtenisnaam, bijvoorbeeld <c>document.processed</c>.</param>
    /// <param name="message">Eén zin, in het Nederlands, leesbaar voor wie de code niet kent.</param>
    /// <param name="extra">Operator-only context, uitklapbaar voor de operator. Mag een anoniem object zijn.</param>
    public static void AgentEvent(this ILogger logger, string eventName, string message, object? extra = null) =>
        Write(logger, LogLevel.Information, eventName, message, extra, exception: null);

    /// <summary>Schrijft een gebeurtenis op niveau <c>warn</c>.</summary>
    /// <param name="logger">De logger van de agent.</param>
    /// <param name="eventName">Puntgescheiden gebeurtenisnaam, bijvoorbeeld <c>api.retry</c>.</param>
    /// <param name="message">Eén zin, in het Nederlands.</param>
    /// <param name="extra">Operator-only context, uitklapbaar voor de operator.</param>
    public static void AgentWarning(this ILogger logger, string eventName, string message, object? extra = null) =>
        Write(logger, LogLevel.Warning, eventName, message, extra, exception: null);

    /// <summary>Schrijft een gebeurtenis op niveau <c>error</c>.</summary>
    /// <param name="logger">De logger van de agent.</param>
    /// <param name="eventName">Puntgescheiden gebeurtenisnaam, bijvoorbeeld <c>document.rejected</c>.</param>
    /// <param name="message">Eén zin, in het Nederlands.</param>
    /// <param name="exception">De uitzondering; de stacktrace komt in <c>extra</c> terecht.</param>
    /// <param name="extra">Operator-only context, uitklapbaar voor de operator.</param>
    public static void AgentError(
        this ILogger logger,
        string eventName,
        string message,
        Exception? exception = null,
        object? extra = null) =>
        Write(logger, LogLevel.Error, eventName, message, extra, exception);

    private static void Write(
        ILogger logger,
        LogLevel level,
        string eventName,
        string message,
        object? extra,
        Exception? exception)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        if (!logger.IsEnabled(level))
        {
            return;
        }

        logger.Log(
            level,
            new EventId(0, eventName),
            new AgentEventState(eventName, message, extra),
            exception,
            static (state, _) => state.Message);
    }
}
