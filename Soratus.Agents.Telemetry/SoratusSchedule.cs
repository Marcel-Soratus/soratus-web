using Cronos;

namespace Soratus.Agents.Telemetry;

/// <summary>
/// Eén cron-expressie, geparseerd, met de tijdzone waarin hij wordt uitgelegd.
/// </summary>
/// <remarks>
/// <para><strong>Dit type bestaat zodat de expressie die het portaal <em>publiceert</em> en het moment
/// waarop de host <em>wacht</em> uit hetzelfde object komen.</strong>
/// <see cref="Soratus.Agents.Contracts.AgentRegistration.Schedule"/> belooft dat de cron-expressie in
/// het document de expressie is waarop werkelijk wordt gepland — "niet een losse beschrijving die uit
/// de pas kan lopen met de werkelijkheid". Bij een agent met een eigen lus houdt de bibliotheek die
/// belofte zelf, want zij plant. Bij een <em>geherbergde</em> agent op een klok doet de host dat, en
/// dan is de enige manier om die belofte te houden: de host laten wachten op precies het object dat
/// hij aankondigt. Vandaar dat <see cref="HostedAgents.HostedAgentDeclaration.Schedule"/> geen
/// <c>string</c> is maar dit type.</para>
///
/// <para>Waarom de bibliotheek de klok hier <em>niet</em> overneemt, terwijl ze dat bij
/// <c>IScheduledAgent</c> wel doet: dan zou het werk van de kostencollector en de storingsmelder pas
/// gebeuren als de telemetrie is ingericht. Dat is de verkeerde afhankelijkheidsrichting — telemetrie
/// mag een agent nooit omleggen, en een agent die zonder telemetrie helemaal niet meer draait is de
/// scherpste vorm daarvan. De klok blijft dus van de host; alleen de expressie is gedeeld.</para>
///
/// <para>Er zit geen toestand in en er wordt nergens een klok gelezen: <see cref="NextAfter"/> krijgt
/// het moment als parameter. Dezelfde afspraak als in <c>Soratus.Agents.Contracts</c>, en om dezelfde
/// reden — een plan van eens per dag is anders niet te testen zonder een dag te wachten.</para>
/// </remarks>
public sealed class SoratusSchedule : IEquatable<SoratusSchedule>
{
    private readonly CronExpression _expression;

    private SoratusSchedule(string expression, CronExpression parsed, TimeZoneInfo timeZone)
    {
        Expression = expression;
        _expression = parsed;
        TimeZone = timeZone;
    }

    /// <summary>De expressie zoals hij is opgegeven. Dit is wat er in het document komt te staan.</summary>
    public string Expression { get; }

    /// <summary>De tijdzone waarin <see cref="Expression"/> wordt uitgelegd.</summary>
    public TimeZoneInfo TimeZone { get; }

    /// <summary>
    /// Parseert een cron-expressie.
    /// </summary>
    /// <param name="expression">
    /// De expressie, met vijf velden (minuutprecisie) of zes (met seconden). Welke van de twee het is
    /// volgt uit de expressie zelf.
    /// </param>
    /// <param name="timeZone">
    /// De zone waarin hij wordt uitgelegd, of <c>null</c> voor UTC. <c>0 6 1 * *</c> hoort in
    /// Nederland om zes uur te lopen en niet om acht; wat er uit <see cref="NextAfter"/> komt is
    /// altijd UTC.
    /// </param>
    /// <returns>De geparseerde expressie.</returns>
    /// <exception cref="ArgumentException">Als <paramref name="expression"/> leeg is.</exception>
    /// <exception cref="InvalidOperationException">Als de expressie geen geldige cron is.</exception>
    /// <remarks>
    /// Werpt bij een fout, en dat is opzet: een cron-expressie die niet parseert plant niets, en
    /// zulke expressies worden geloofd. Wie hem uit configuratie bouwt hoort dat op een plek te doen
    /// waar een fout niet de host meeneemt — zie het gebruik in <c>Soratus.Portal</c>.
    /// </remarks>
    public static SoratusSchedule Parse(string expression, TimeZoneInfo? timeZone = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        string trimmed = expression.Trim();

        // Cronos kent vijf velden (minuut-precisie) en zes (met seconden). Welke van de twee het is
        // volgt uit de expressie zelf; de aanroeper hoeft geen formaat mee te geven.
        int fields = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        CronFormat format = fields >= 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;

        try
        {
            return new SoratusSchedule(trimmed, CronExpression.Parse(trimmed, format), timeZone ?? TimeZoneInfo.Utc);
        }
        catch (CronFormatException exception)
        {
            throw new InvalidOperationException(
                $"'{trimmed}' is geen geldige cron-expressie. {exception.Message}",
                exception);
        }
    }

    /// <summary>
    /// Het eerstvolgende moment ná <paramref name="moment"/>, altijd in UTC.
    /// </summary>
    /// <param name="moment">Waarvandaan wordt gerekend.</param>
    /// <returns>Het moment, of <c>null</c> als de expressie geen volgend moment meer oplevert.</returns>
    /// <remarks>
    /// De tijdzone doet mee bij het <em>uitrekenen</em>, maar wat er uit komt is UTC. Die twee moeten
    /// gescheiden blijven: zodra de offset van de zone mee naar buiten gaat staan er gemengde offsets
    /// in de opslag en sorteert Cosmos ze lexicografisch verkeerd.
    /// </remarks>
    public DateTimeOffset? NextAfter(DateTimeOffset moment) =>
        _expression.GetNextOccurrence(moment, TimeZone)?.ToUniversalTime();

    /// <inheritdoc />
    /// <remarks>
    /// Waardegelijkheid op de expressie en de zone, en niet op het geparseerde object: twee keer
    /// dezelfde expressie parseren hoort hetzelfde plan op te leveren. Dit type hangt onder een
    /// <c>record</c> (<see cref="HostedAgents.HostedAgentDeclaration"/>), en zonder deze
    /// implementatie zou twee keer dezelfde aankondiging bouwen daar als een <em>conflict</em> worden
    /// gelezen — een waarschuwing over een verschil dat er niet is.
    /// </remarks>
    public bool Equals(SoratusSchedule? other) =>
        other is not null
        && string.Equals(Expression, other.Expression, StringComparison.Ordinal)
        && TimeZone.Equals(other.TimeZone);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SoratusSchedule);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Expression, TimeZone);

    /// <inheritdoc />
    public override string ToString() => Expression;
}
