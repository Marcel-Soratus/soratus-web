using Cronos;

namespace Soratus.Agents.Telemetry.Internal;

/// <summary>
/// De cron-expressie van deze agent, één keer geparseerd.
/// </summary>
/// <remarks>
/// Zowel de planner als het registratiedocument vragen hier de volgende run op. Daarom is dit
/// één object en geen twee berekeningen: het contract belooft dat <c>nextRunAt</c> de échte
/// volgende run is, en dat is alleen waar als het scherm en de planner uit dezelfde bron putten.
/// </remarks>
internal sealed class AgentSchedule
{
    private readonly CronExpression? _expression;
    private readonly TimeZoneInfo _timeZone;

    internal AgentSchedule(string? expression, TimeZoneInfo timeZone)
    {
        _timeZone = timeZone;
        Raw = string.IsNullOrWhiteSpace(expression) ? null : expression.Trim();

        if (Raw is null)
        {
            return;
        }

        // Cronos kent vijf velden (minuut-precisie) en zes (met seconden). Welke van de twee het
        // is volgt uit de expressie zelf; de bouwer hoeft geen formaat mee te geven.
        int fields = Raw.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        CronFormat format = fields >= 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;

        try
        {
            _expression = CronExpression.Parse(Raw, format);
        }
        catch (CronFormatException exception)
        {
            throw new InvalidOperationException(
                $"SORATUS_AGENT__SCHEDULE bevat geen geldige cron-expressie: '{Raw}'. {exception.Message}",
                exception);
        }
    }

    /// <summary>De expressie zoals die is opgegeven, of <c>null</c> bij een agent zonder schema.</summary>
    internal string? Raw { get; }

    /// <summary>Of deze agent op een schema draait.</summary>
    internal bool HasSchedule => _expression is not null;

    /// <summary>
    /// Het eerstvolgende moment na <paramref name="from"/>, altijd in UTC, of <c>null</c> zonder
    /// schema.
    /// </summary>
    /// <remarks>
    /// De tijdzone doet mee bij het <em>uitrekenen</em> — <c>0 6 1 * *</c> hoort in Nederland om
    /// zes uur te lopen en niet om acht — maar wat er uit komt is UTC. Dat zijn twee dingen die
    /// gescheiden moeten blijven: zodra de offset van de zone mee naar buiten gaat, staan er
    /// gemengde offsets in de opslag en sorteert Cosmos ze lexicografisch verkeerd.
    /// </remarks>
    internal DateTimeOffset? GetNextOccurrence(DateTimeOffset from) =>
        _expression?.GetNextOccurrence(from, _timeZone)?.ToUniversalTime();
}
