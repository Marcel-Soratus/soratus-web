namespace Soratus.Portal.Data;

/// <summary>
/// De klantslug: de sleutel waar alles op aansluit.
/// </summary>
/// <remarks>
/// <para>Dezelfde tekenreeks is de partitiesleutel in de portaalopslag, het pad in de URL, én
/// <c>customerId</c> in elk telemetriedocument dat een agent wegschrijft. Dat laatste maakt hem
/// bijzonder: hij is <strong>niet</strong> uit de klantnaam af te leiden. De mockup doet dat wel —
/// <c>name.toLowerCase().replace(...)</c> levert daar <c>bakkerlogistiek</c> op — terwijl de agents
/// van diezelfde klant <c>bakker</c> publiceren. Een afgeleide slug zou een klant opleveren die
/// bestaat en waarvan de agents onvindbaar zijn.</para>
///
/// <para>De slug is dus een veld dat de operator invult, en hij moet gelijk zijn aan wat in de
/// agentconfiguratie staat. Daarom is hij ook niet te wijzigen na het aanmaken: dan zou de
/// verwijzing van elk bestaand telemetriedocument stil verbreken.</para>
///
/// <para>Het toegestane teken-alfabet is krap gehouden. Een slug komt in een URL, in een
/// documentsleutel en in een partitiesleutel terecht; Cosmos verbiedt in een id de tekens
/// <c>/ \ # ?</c>, en een URL kent nog een handvol tekens met betekenis. Kleine letters, cijfers en
/// koppelstreepjes raken geen van die grenzen.</para>
/// </remarks>
public static class PortalSlug
{
    /// <summary>De kortste toegestane slug.</summary>
    public const int MinimumLength = 2;

    /// <summary>De langste toegestane slug.</summary>
    public const int MaximumLength = 40;

    /// <summary>
    /// Controleert een slug en geeft de reden als hij niet klopt.
    /// </summary>
    /// <param name="slug">De ingevoerde slug.</param>
    /// <returns><c>null</c> als de slug klopt, anders de foutmelding voor het formulier.</returns>
    public static string? Validate(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return "Vul een klant-id in. Dat is de slug die ook in de URL en in de telemetrie van " +
                   "deze klant staat, bijvoorbeeld 'bakker'.";
        }

        var value = slug.Trim();

        if (value.Length is < MinimumLength or > MaximumLength)
        {
            return $"Een klant-id is tussen {MinimumLength} en {MaximumLength} tekens lang.";
        }

        if (!char.IsAsciiLetterLower(value[0]) && !char.IsAsciiDigit(value[0]))
        {
            return "Een klant-id begint met een kleine letter of een cijfer.";
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-')
            {
                return "Een klant-id bestaat uit kleine letters, cijfers en koppelstreepjes. " +
                       "Hij komt in de URL en in de documentsleutels terecht, dus hoofdletters, " +
                       "punten en spaties kunnen niet.";
            }
        }

        return value.EndsWith('-') ? "Een klant-id eindigt niet op een koppelstreepje." : null;
    }
}

/// <summary>
/// Het e-mailadres van een portaaltoegang.
/// </summary>
/// <remarks>
/// <para>De controle is opzettelijk krap en niet slim. Een volledige RFC-5322-controle is hier het
/// verkeerde gereedschap: het adres moet straks in Entra bestaan, en dát is de echte toets. Wat
/// hier wordt tegengehouden zijn de fouten die anders een document met een onbruikbare sleutel
/// opleveren — een adres zonder <c>@</c>, met een spatie, of met een teken dat Cosmos in een id
/// verbiedt.</para>
///
/// <para>Genormaliseerd naar kleine letters, en die vorm is de enige die wordt bewaard. Zie de
/// opmerkingen bij <see cref="AccessDocument.Email"/>: twee schrijfwijzen van hetzelfde adres
/// zouden twee toegangen zijn, waarvan er één intrekken niets doet.</para>
/// </remarks>
public static class PortalEmail
{
    /// <summary>De tekens die Cosmos in een documentsleutel verbiedt.</summary>
    private static readonly char[] Forbidden = ['/', '\\', '#', '?'];

    /// <summary>
    /// Normaliseert een e-mailadres naar de vorm die wordt opgeslagen.
    /// </summary>
    /// <param name="email">Het ingevoerde adres.</param>
    /// <returns>Het adres zonder witruimte en in kleine letters.</returns>
    public static string Normalize(string? email) =>
        email?.Trim().ToLowerInvariant() ?? string.Empty;

    /// <summary>
    /// Controleert een e-mailadres en geeft de reden als het niet bruikbaar is.
    /// </summary>
    /// <param name="email">Het genormaliseerde adres.</param>
    /// <returns><c>null</c> als het adres bruikbaar is, anders de foutmelding.</returns>
    public static string? Validate(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "Vul een e-mailadres in.";
        }

        var value = email.Trim();

        if (value.Length > 254)
        {
            return "Dit e-mailadres is te lang.";
        }

        var at = value.IndexOf('@', StringComparison.Ordinal);

        if (at < 1 || at != value.LastIndexOf('@') || at == value.Length - 1)
        {
            return "Vul een geldig e-mailadres in, met precies één @-teken.";
        }

        if (!value[(at + 1)..].Contains('.', StringComparison.Ordinal))
        {
            return "Het domein van dit e-mailadres mist een punt.";
        }

        if (value.Any(char.IsWhiteSpace))
        {
            return "Een e-mailadres bevat geen spaties.";
        }

        return value.IndexOfAny(Forbidden) >= 0
            ? "Dit e-mailadres bevat een teken dat niet in een documentsleutel kan (/ \\ # ?)."
            : null;
    }
}
