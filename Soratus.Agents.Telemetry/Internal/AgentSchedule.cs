namespace Soratus.Agents.Telemetry.Internal;

/// <summary>
/// De cron-expressie van deze agent, één keer geparseerd.
/// </summary>
/// <remarks>
/// <para>Zowel de planner als het registratiedocument vragen hier de volgende run op. Daarom is dit
/// één object en geen twee berekeningen: het contract belooft dat <c>nextRunAt</c> de échte
/// volgende run is, en dat is alleen waar als het scherm en de planner uit dezelfde bron putten.</para>
///
/// <para>Het parseren en uitrekenen zelf staat in <see cref="SoratusSchedule"/> en niet hier. Dat is
/// geen laagje om een laagje: die klasse is publiek, omdat een host die zijn geherbergde klok-agents
/// zelf plant dezelfde expressie moet kunnen aankondigen en erop wachten. Eén implementatie van "wat
/// is het volgende moment", twee aanroepers.</para>
///
/// <para>Wat híer overblijft is wat alleen op het pad met één agent per proces bestaat: dat de
/// expressie <em>mag</em> ontbreken, en dat een fout erin naar <c>SORATUS_AGENT__SCHEDULE</c>
/// verwijst.</para>
/// </remarks>
internal sealed class AgentSchedule
{
    private readonly SoratusSchedule? _schedule;

    internal AgentSchedule(string? expression, TimeZoneInfo timeZone)
    {
        Raw = string.IsNullOrWhiteSpace(expression) ? null : expression.Trim();

        if (Raw is null)
        {
            return;
        }

        try
        {
            _schedule = SoratusSchedule.Parse(Raw, timeZone);
        }
        catch (InvalidOperationException exception)
        {
            // De binnenste melding en niet die van SoratusSchedule zelf: die zegt al "'x' is geen
            // geldige cron-expressie", en dat twee keer in één regel zetten maakt de melding langer
            // zonder hem duidelijker te maken. Wat hier wél bij hoort is de sleutel waar de expressie
            // uit komt, want dat is de plek die de lezer moet aanpassen.
            throw new InvalidOperationException(
                $"SORATUS_AGENT__SCHEDULE bevat geen geldige cron-expressie: '{Raw}'. "
                + (exception.InnerException?.Message ?? exception.Message),
                exception);
        }
    }

    /// <summary>De expressie zoals die is opgegeven, of <c>null</c> bij een agent zonder schema.</summary>
    internal string? Raw { get; }

    /// <summary>Of deze agent op een schema draait.</summary>
    internal bool HasSchedule => _schedule is not null;

    /// <summary>
    /// Het eerstvolgende moment na <paramref name="from"/>, altijd in UTC, of <c>null</c> zonder
    /// schema.
    /// </summary>
    internal DateTimeOffset? GetNextOccurrence(DateTimeOffset from) => _schedule?.NextAfter(from);
}
