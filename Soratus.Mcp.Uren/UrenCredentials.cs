using Azure.Core;
using Azure.Identity;

namespace Soratus.Mcp.Uren;

/// <summary>De commandonamen waarmee deze server buiten de MCP-modus wordt aangeroepen.</summary>
/// <remarks>
/// Staan als constante zodat de foutmeldingen in <see cref="PortalUrenClient"/> naar een naam kunnen
/// verwijzen die echt bestaat. Een melding die "meld je aan met X" zegt terwijl X niet bestaat, is
/// erger dan geen melding.
/// </remarks>
public static class UrenCommands
{
    /// <summary>Eenmalig aanmelden met device-code.</summary>
    public const string SignIn = "soratus-uren aanmelden";

    /// <summary>De aanmelding en het token controleren zonder iets te boeken.</summary>
    public const string Check = "soratus-uren controleer";
}

/// <summary>
/// Bouwt de credential waarmee deze server een token voor de portaal-API haalt.
/// </summary>
/// <remarks>
/// <para><strong>Een eigen public client met device-code, en expliciet géén
/// <c>DefaultAzureCredential</c>.</strong> Dat laatste was het eerste ontwerp en is bewust verlaten.
/// <c>DefaultAzureCredential</c> pakt op een ontwikkelmachine de aanmelding van de Azure CLI op, en
/// dat werkt alleen als de CLI-client (<c>04b07795-…</c>) vooraf is geautoriseerd op onze API. Doe je
/// dat, dan kan élk script dat op die machine met <c>DefaultAzureCredential</c> werkt een token voor
/// het portaal krijgen en uren wegschrijven.</para>
///
/// <para>Het gaat daarbij niet om nieuwe macht — die persoon is al operator en kan het via de browser
/// ook. Het gaat erom dat de macht dan bereikbaar is voor code die er niets mee te maken heeft, en
/// dat dat niet te zien is. Een schrijfpad naar facturatiegegevens hoort een expliciete stap te
/// hebben. Hetzelfde patroon als waarom het portaal geen <c>AppRoleAssignment.ReadWrite.All</c>
/// krijgt.</para>
///
/// <para>En daarom staat <c>DefaultAzureCredential</c> hier ook niet als terugvaloptie. Zou hij als
/// vangnet blijven staan, dan heropent hij de route die dit besluit sluit zodra iemand ooit de CLI
/// alsnog autoriseert — stil, en zonder dat er iets aan deze code verandert.</para>
///
/// <para><strong>De server vraagt zelf nooit interactief om een aanmelding.</strong> In MCP-modus
/// staat <c>DisableAutomaticAuthentication</c> aan, dus een ontbrekende aanmelding levert een
/// <c>AuthenticationRequiredException</c> op in plaats van een prompt. Een device-code-instructie zou
/// namelijk op stdout moeten — het JSON-RPC-kanaal — en op stderr ziet de aanroeper hem niet; dan
/// hangt de tool tot de tijdslimiet zonder dat iemand weet waarop hij wacht.</para>
/// </remarks>
public static class UrenCredentials
{
    /// <summary>
    /// De naam van de tokencache. Bepaalt welke aanmelding hergebruikt wordt.
    /// </summary>
    public const string CacheName = "soratus-uren";

    /// <summary>
    /// Bouwt de credential die alleen een bestaande aanmelding gebruikt en nooit vraagt.
    /// </summary>
    /// <param name="options">De instellingen.</param>
    /// <returns>De credential.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    public static TokenCredential CreateSilent(UrenOptions options) =>
        Create(options, interactive: false);

    /// <summary>
    /// Bouwt de credential die wél om een device-code mag vragen.
    /// </summary>
    /// <param name="options">De instellingen.</param>
    /// <param name="prompt">Waar de instructie voor de gebruiker naartoe gaat.</param>
    /// <returns>De credential.</returns>
    /// <exception cref="ArgumentNullException">Een verplichte parameter is <c>null</c>.</exception>
    public static TokenCredential CreateInteractive(UrenOptions options, TextWriter prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        return Create(options, interactive: true, prompt);
    }

    private static TokenCredential Create(UrenOptions options, bool interactive, TextWriter? prompt = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var credentialOptions = new DeviceCodeCredentialOptions
        {
            ClientId = options.ClientId,
            TenantId = options.TenantId,
            DisableAutomaticAuthentication = !interactive,
            TokenCachePersistenceOptions = new TokenCachePersistenceOptions
            {
                Name = CacheName,
                // Nooit aanzetten. Een tokencache voor een schrijfpad naar facturatiegegevens die
                // onversleuteld op schijf staat, is een credential in rust. Draait dit ooit op een
                // Linux-machine zonder libsecret, dan hoort dat om te vallen en niet stil terug te
                // vallen op een bestand dat iedereen kan lezen.
                UnsafeAllowUnencryptedStorage = false,
            },
        };

        if (interactive && prompt is not null)
        {
            credentialOptions.DeviceCodeCallback = (info, _) =>
            {
                prompt.WriteLine();
                prompt.WriteLine(info.Message);
                prompt.WriteLine();
                prompt.Flush();
                return Task.CompletedTask;
            };
        }

        if (TryReadRecord(options.AuthenticationRecordPath) is { } record)
        {
            credentialOptions.AuthenticationRecord = record;
        }

        return new DeviceCodeCredential(credentialOptions);
    }

    /// <summary>
    /// Bewaart de aanmelding zodat een volgend proces hem stil kan hergebruiken.
    /// </summary>
    /// <param name="record">Wat het aanmelden opleverde.</param>
    /// <param name="path">Waar het bestand komt.</param>
    /// <param name="cancellationToken">Afbreken.</param>
    /// <returns>Een taak.</returns>
    /// <exception cref="ArgumentNullException">Een verplichte parameter is <c>null</c>.</exception>
    /// <remarks>
    /// Dit bestand is <strong>geen</strong> token en <strong>geen</strong> geheim: het bevat de
    /// gebruikersnaam, de tenant en de account-id, zodat de credential weet welk account hij in de
    /// versleutelde cache moet zoeken. De tokens zelf staan in die cache en niet hier.
    /// </remarks>
    public static async Task SaveRecordAsync(
        AuthenticationRecord record,
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using FileStream stream = File.Create(path);
        await record.SerializeAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static AuthenticationRecord? TryReadRecord(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            return AuthenticationRecord.Deserialize(stream);
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            // Een onleesbaar bestand is geen fout om op te vallen: het gevolg is dat er opnieuw moet
            // worden aangemeld, en dat zegt de melding dan ook. Omvallen zou de server onbruikbaar
            // maken door een bestand dat te repareren is door het weg te gooien.
            return null;
        }
    }
}
