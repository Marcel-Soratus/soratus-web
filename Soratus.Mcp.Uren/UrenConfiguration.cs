using Microsoft.Extensions.Configuration;

namespace Soratus.Mcp.Uren;

/// <summary>
/// Leest <see cref="UrenOptions"/> uit de configuratie en valt om met een leesbare regel als er
/// iets ontbreekt of niet klopt.
/// </summary>
/// <remarks>
/// Dezelfde houding als <c>AddSoratusAgent</c>: misconfiguratie is een programmeerfout en hoort bij
/// het opstarten zichtbaar te worden, niet pas als iemand zich afvraagt waarom een boeking nooit is
/// aangekomen. Een MCP-server die stil half werkt is bijzonder onaangenaam, want de aanroeper ziet
/// alleen dat de tool er niet is.
/// </remarks>
public static class UrenConfiguration
{
    /// <summary>De basis-URL van het portaal.</summary>
    public const string PortalKey = "SORATUS_UREN__PORTAL";

    /// <summary>De Entra-scope waarvoor een token wordt opgehaald.</summary>
    public const string ScopeKey = "SORATUS_UREN__SCOPE";

    /// <summary>De app-id van de eigen public-client-registratie.</summary>
    public const string ClientIdKey = "SORATUS_UREN__CLIENT_ID";

    /// <summary>De tenant waarin wordt aangemeld.</summary>
    public const string TenantIdKey = "SORATUS_UREN__TENANT_ID";

    /// <summary>Optionele lijst klantslugs waarvoor deze installatie mag boeken.</summary>
    public const string CustomersKey = "SORATUS_UREN__KLANTEN";

    /// <summary>Optionele tijdslimiet in seconden.</summary>
    public const string TimeoutKey = "SORATUS_UREN__TIMEOUT_SECONDEN";

    /// <summary>Optionele proefdraaimodus.</summary>
    public const string DryRunKey = "SORATUS_UREN__DROOGLOOP";

    /// <summary>
    /// Bouwt de instellingen op uit de configuratie.
    /// </summary>
    /// <param name="configuration">De configuratie van de host.</param>
    /// <returns>De gevalideerde instellingen.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">Als verplichte configuratie ontbreekt of niet klopt.</exception>
    public static UrenOptions Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new UrenOptions
        {
            DryRun = ReadBoolean(configuration, DryRunKey),
            AllowedCustomers = ReadList(configuration, CustomersKey),
        };

        options.PortalBaseAddress = ReadPortal(configuration);

        if (ReadTimeout(configuration) is { } timeout)
        {
            options.Timeout = timeout;
        }

        options.Scope = ReadScope(configuration, options.DryRun);
        options.ClientId = ReadGuid(configuration, ClientIdKey, options.DryRun);
        options.TenantId = ReadGuid(configuration, TenantIdKey, options.DryRun);

        return options;
    }

    /// <summary>
    /// Leest een app- of tenant-id en eist dat het een GUID is.
    /// </summary>
    /// <remarks>
    /// Een verkeerd geplakte waarde levert anders pas bij het aanmelden een melding op die over
    /// "invalid_client" gaat, en dat is een melding waar niemand een plakfout in herkent.
    /// </remarks>
    private static string ReadGuid(IConfiguration configuration, string key, bool dryRun)
    {
        string? raw = Read(configuration, key);

        if (raw is null)
        {
            if (dryRun)
            {
                return string.Empty;
            }

            throw new InvalidOperationException(
                $"soratus-uren kan niet opstarten. Ontbrekende configuratie: {key}. " +
                "De aanmelding loopt via een eigen public-client-registratie; de commando's om die " +
                "aan te maken staan in docs/agent-portal/mcp-uren.md. " +
                $"Wil je zonder aanmelding draaien, zet dan {DryRunKey}=true.");
        }

        if (!Guid.TryParse(raw, out _))
        {
            throw new InvalidOperationException(
                $"{key} heeft de waarde '{raw}', maar dat is geen GUID.");
        }

        return raw;
    }

    private static Uri ReadPortal(IConfiguration configuration)
    {
        string? raw = Read(configuration, PortalKey);

        if (raw is null)
        {
            throw new InvalidOperationException(
                $"soratus-uren kan niet opstarten. Ontbrekende configuratie: {PortalKey}. " +
                "Zet die op de basis-URL van het portaal, bijvoorbeeld https://portal.soratus.com.");
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri? portal)
            || (portal.Scheme != Uri.UriSchemeHttps && !portal.IsLoopback))
        {
            throw new InvalidOperationException(
                $"{PortalKey} moet een absolute https-URL zijn, maar is '{raw}'. " +
                "Alleen een loopback-adres mag http gebruiken, voor lokaal draaien.");
        }

        if (!string.IsNullOrEmpty(portal.Query) || !string.IsNullOrEmpty(portal.Fragment))
        {
            // Een basis-URL heeft geen querystring. Staat er wél een, dan heeft iemand er een
            // SAS-token of een sleutel in geplakt, en dan hoort dit om te vallen en niet mee te
            // reizen in elk verzoek.
            throw new InvalidOperationException(
                $"{PortalKey} bevat een querystring of fragment ('{raw}'). Geef alleen de basis-URL; " +
                "de server bouwt zelf het pad. Een sleutel of SAS-token hoort hier niet in — de " +
                "verbinding loopt over een token op je eigen identiteit.");
        }

        return portal;
    }

    private static string ReadScope(IConfiguration configuration, bool dryRun)
    {
        string? raw = Read(configuration, ScopeKey);

        if (raw is null)
        {
            if (dryRun)
            {
                // In proefdraaimodus wordt er geen verzoek gedaan, dus is er geen token nodig. Dit
                // is de enige stand waarin de server zonder scope wil draaien, en hij zegt in elke
                // melding dat hij niets boekt.
                return string.Empty;
            }

            throw new InvalidOperationException(
                $"soratus-uren kan niet opstarten. Ontbrekende configuratie: {ScopeKey}. " +
                "Zet die op de scope van de portaal-API, bijvoorbeeld api://soratus-portal/.default. " +
                $"Wil je zonder portaal draaien, zet dan {DryRunKey}=true; dan boekt de server niets.");
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                $"{ScopeKey} moet een absolute scope-URI zijn, maar is '{raw}'. " +
                "Bijvoorbeeld api://soratus-portal/.default.");
        }

        if (!raw.EndsWith("/.default", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{ScopeKey} moet op '/.default' eindigen, maar is '{raw}'. De credentials binnen " +
                "DefaultAzureCredential vragen resource-breed aan; een losse scope faalt daar met " +
                "een melding die niet over de oorzaak gaat.");
        }

        if (raw.Contains("AccountKey", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{ScopeKey} lijkt een sleutel te bevatten. Hier hoort alleen een scope-URI; " +
                "de verbinding loopt over een token op je eigen identiteit.");
        }

        return raw;
    }

    private static TimeSpan? ReadTimeout(IConfiguration configuration)
    {
        if (Read(configuration, TimeoutKey) is not { } raw)
        {
            return null;
        }

        if (!int.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out int seconds)
            || seconds is < 1 or > 300)
        {
            throw new InvalidOperationException(
                $"{TimeoutKey} heeft de waarde '{raw}', maar dat is geen aantal seconden tussen 1 en 300.");
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static bool ReadBoolean(IConfiguration configuration, string key)
    {
        if (Read(configuration, key) is not { } raw)
        {
            return false;
        }

        if (!bool.TryParse(raw, out bool value))
        {
            // Niet stil op false terugvallen. Wie DROOGLOOP=1 zet bedoelt "boek niets", en dat
            // stilzwijgend als "boek wel" lezen is precies de verkeerde kant om fout te gaan.
            throw new InvalidOperationException(
                $"{key} heeft de waarde '{raw}', maar dat is geen 'true' of 'false'.");
        }

        return value;
    }

    private static IReadOnlyList<string> ReadList(IConfiguration configuration, string key)
    {
        if (Read(configuration, key) is not { } raw)
        {
            return [];
        }

        string[] items = raw
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static item => item.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (items.Length == 0)
        {
            throw new InvalidOperationException(
                $"{key} staat er wel maar levert geen enkele klantslug op ('{raw}'). " +
                "Laat de sleutel weg als je geen beperking wilt; een lege lijst die " +
                "als 'geen beperking' wordt gelezen is te verrassend.");
        }

        foreach (string item in items)
        {
            if (!HourBookingValidation.IsWellFormedSlug(item))
            {
                throw new InvalidOperationException(
                    $"{key} bevat '{item}', en dat is geen klantslug. Een slug bestaat uit kleine " +
                    "letters, cijfers en koppelstreepjes en begint met een letter of cijfer.");
            }
        }

        return items;
    }

    /// <summary>
    /// Leest een sleutel in beide vormen: als sectie (waar een omgevingsvariabele met dubbel
    /// liggend streepje op uitkomt) en als platte sleutel.
    /// </summary>
    private static string? Read(IConfiguration configuration, string key)
    {
        string sectioned = key.Replace("__", ":", StringComparison.Ordinal);
        string? value = configuration[sectioned] ?? configuration[key];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
