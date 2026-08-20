using System.Globalization;

namespace Soratus.Portal.Components.Shared;

/// <summary>
/// Tijd in tekst: relatief in beeld, absoluut in de tooltip (§1 en §8 van de spec).
/// </summary>
/// <remarks>
/// Elke methode krijgt het referentiemoment mee. Er staat hier bewust nergens
/// <c>DateTimeOffset.Now</c>: dan is de uitkomst niet te testen en verschuift hij bij elke
/// render, ook als er niets is gebeurd. De pagina bepaalt het "nu" en geeft het door.
///
/// <strong>Opslag is UTC, weergave is Nederlandse tijd.</strong> Dat zijn twee verschillende
/// dingen. In Cosmos staat alles in UTC, want gemengde offsets breken de sortering. Maar een
/// operator die om 17:13 naar het scherm kijkt en "15:13" leest, denkt dat er iets twee uur
/// geleden is gebeurd. Zichtbare tijden en tooltips gaan daarom door
/// <see cref="DefaultZone"/>; alleen <see cref="Iso"/> blijft UTC, want dat is machineleesbaar
/// en hoort onveranderlijk te zijn.
///
/// De zone is overal een optionele parameter en geen constante binnenin: zo is zomertijd te
/// testen zonder de machineklok te verzetten, en zit een klant in een andere zone niet vast.
/// </remarks>
public static class TimeFormat
{
    /// <summary>De IANA-id van de zone waarin het portaal zijn tijden toont.</summary>
    public const string DefaultZoneId = "Europe/Amsterdam";

    /// <summary>
    /// De zone waarin tijden verschijnen als er geen andere wordt meegegeven: Europe/Amsterdam.
    /// </summary>
    /// <remarks>
    /// Een zone-id en geen vaste offset van +1 of +2. Daarmee gaat de zomertijd vanzelf goed,
    /// inclusief de twee zondagen per jaar waarop hij verspringt.
    /// </remarks>
    public static TimeZoneInfo DefaultZone { get; } = Resolve(DefaultZoneId);

    /// <summary>Relatieve tijd in gewone taal, zoals "11 min geleden".</summary>
    /// <param name="value">Het moment.</param>
    /// <param name="now">Het referentiemoment.</param>
    /// <returns>
    /// Het verschil in de grofste eenheid die nog iets zegt. Ligt <paramref name="value"/> in de
    /// toekomst, dan "over …" in dezelfde eenheden.
    /// </returns>
    /// <remarks>
    /// Zone-onafhankelijk: een verschil tussen twee momenten is overal even groot.
    /// </remarks>
    public static string Relative(DateTimeOffset value, DateTimeOffset now)
    {
        var seconds = (long)Math.Round((now - value).TotalSeconds);

        return seconds < 0
            ? $"over {Span(-seconds)}"
            : $"{Span(seconds)} geleden";
    }

    /// <summary>
    /// Absolute tijd voor de tooltip, zoals "19-08-2026 11:22:31 (UTC+02:00)".
    /// </summary>
    /// <param name="value">Het moment.</param>
    /// <param name="zone">De zone waarin het wordt getoond. Standaard <see cref="DefaultZone"/>.</param>
    /// <returns>Datum en klok in de doelzone, met de offset er expliciet bij.</returns>
    /// <remarks>
    /// De offset staat erbij en niet "CET"/"CEST": hij klopt voor elke zone en laat zien welke
    /// klok je leest wanneer je dit naast een logregel in UTC legt.
    /// </remarks>
    public static string Absolute(DateTimeOffset value, TimeZoneInfo? zone = null) =>
        In(value, zone).ToString(
            "dd-MM-yyyy HH:mm:ss '(UTC'zzz')'",
            CultureInfo.InvariantCulture);

    /// <summary>Alleen de klok, zoals "11:22:31". Voor de tijdkolom van een logtabel.</summary>
    /// <param name="value">Het moment.</param>
    /// <param name="zone">De zone waarin het wordt getoond. Standaard <see cref="DefaultZone"/>.</param>
    /// <returns>De tijd in de doelzone, zonder datum.</returns>
    public static string Clock(DateTimeOffset value, TimeZoneInfo? zone = null) =>
        In(value, zone).ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>De machineleesbare vorm voor het <c>datetime</c>-attribuut van <c>&lt;time&gt;</c>.</summary>
    /// <param name="value">Het moment.</param>
    /// <returns>ISO 8601 in UTC, bijvoorbeeld <c>2026-08-19T09:22:31Z</c>.</returns>
    /// <remarks>
    /// Blijft UTC, ook als het scherm Nederlandse tijd toont. Dit attribuut is voor machines:
    /// één vorm, geen zomertijd, geen dubbelzinnig uur in de nacht van de terugstelling.
    /// </remarks>
    public static string Iso(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Rekent een moment om naar de doelzone.</summary>
    /// <param name="value">Het moment.</param>
    /// <param name="zone">De zone, of <c>null</c> voor <see cref="DefaultZone"/>.</param>
    /// <returns>Hetzelfde moment, uitgedrukt in de klok van die zone.</returns>
    public static DateTimeOffset In(DateTimeOffset value, TimeZoneInfo? zone = null) =>
        TimeZoneInfo.ConvertTime(value, zone ?? DefaultZone);

    /// <summary>Zoekt een zone op zijn IANA-id.</summary>
    /// <param name="id">De IANA-id, bijvoorbeeld <c>Europe/Amsterdam</c>.</param>
    /// <returns>De zone.</returns>
    /// <exception cref="TimeZoneNotFoundException">Als het systeem de zone niet kent.</exception>
    /// <remarks>
    /// .NET kent IANA-id's op Windows én Linux zolang ICU beschikbaar is. Draait het proces met
    /// <c>InvariantGlobalization</c> of op een Windows-machine zonder ICU, dan valt deze methode
    /// terug op de Windows-naam van dezelfde zone.
    ///
    /// Er is bewust géén terugval op UTC. Stilletjes de verkeerde klok tonen is precies de soort
    /// onwaarheid die dit portaal niet maakt; dan liever hard stuk bij het opstarten.
    /// </remarks>
    public static TimeZoneInfo Resolve(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
            when (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId))
        {
            return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
        }
    }

    /// <summary>Het tijdsverschil zelf, zonder "geleden" of "over".</summary>
    /// <param name="seconds">Het verschil in seconden, nooit negatief.</param>
    /// <returns>De grofste eenheid die nog informatie geeft.</returns>
    private static string Span(long seconds)
    {
        if (seconds < 45)
        {
            return $"{seconds} sec";
        }

        var minutes = (long)Math.Round(seconds / 60d);

        if (minutes < 60)
        {
            return $"{minutes} min";
        }

        var hours = minutes / 60;

        return hours < 24
            ? $"{hours} u {minutes % 60} min"
            : $"{hours / 24} d";
    }
}
