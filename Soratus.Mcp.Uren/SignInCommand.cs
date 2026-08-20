using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

namespace Soratus.Mcp.Uren;

/// <summary>
/// De twee commando's rond de aanmelding: <c>aanmelden</c> en <c>controleer</c>.
/// </summary>
/// <remarks>
/// <para>Deze bestaan omdat het tokenpad anders het enige stuk is dat nooit gedraaid heeft. De
/// proefdraaimodus sloeg de aanmelding over, het endpoint bestaat nog niet, en dan is de eerste echte
/// poging tegelijk de eerste keer dat de aanmelding wordt geprobeerd — met een foutmelding op een
/// moment dat iemand uren aan het boeken is.</para>
///
/// <para><c>controleer</c> haalt een token en zegt wat erin staat. Dat is precies de diagnose die je
/// nodig hebt: geen <c>roles</c>-claim betekent dat de app-roltoewijzing ontbreekt (en dan sta je
/// binnen zonder rechten — dezelfde toestand die in fase 0 twee deploys kostte), en een verkeerde
/// <c>aud</c> betekent dat de scope niet die van het portaal is.</para>
///
/// <para><strong>Het token zelf wordt nooit afgedrukt</strong>, alleen de claims eruit. Een token in
/// een terminalbuffer of een logbestand is een credential die je niet meer kunt terugnemen.</para>
/// </remarks>
internal static class SignInCommand
{
    /// <summary>
    /// Meldt interactief aan met device-code en bewaart de aanmelding.
    /// </summary>
    /// <param name="options">De instellingen.</param>
    /// <param name="cancellationToken">Afbreken.</param>
    /// <returns>De afsluitcode.</returns>
    public static async Task<int> SignInAsync(UrenOptions options, CancellationToken cancellationToken)
    {
        // In deze modus loopt er geen JSON-RPC over stdout, dus de instructie mag daar staan — en
        // dáár kijkt de gebruiker die dit commando net heeft getypt.
        TokenCredential credential = UrenCredentials.CreateInteractive(options, Console.Out);
        var context = new TokenRequestContext([options.Scope]);

        try
        {
            AuthenticationRecord record = await ((DeviceCodeCredential)credential)
                .AuthenticateAsync(context, cancellationToken)
                .ConfigureAwait(false);

            await UrenCredentials
                .SaveRecordAsync(record, options.AuthenticationRecordPath, cancellationToken)
                .ConfigureAwait(false);

            Console.WriteLine($"Aangemeld als {record.Username} in tenant {record.TenantId}.");
            Console.WriteLine($"De aanmelding is bewaard in {options.AuthenticationRecordPath}.");
            Console.WriteLine();

            return await ReportTokenAsync(credential, options, context, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AuthenticationFailedException exception)
        {
            await Console.Error.WriteLineAsync(Explain(exception, options)).ConfigureAwait(false);
            return 1;
        }
    }

    /// <summary>
    /// Haalt stil een token op en meldt wat erin staat, zonder iets te boeken.
    /// </summary>
    /// <param name="options">De instellingen.</param>
    /// <param name="cancellationToken">Afbreken.</param>
    /// <returns>De afsluitcode.</returns>
    public static async Task<int> CheckAsync(UrenOptions options, CancellationToken cancellationToken)
    {
        TokenCredential credential = UrenCredentials.CreateSilent(options);
        var context = new TokenRequestContext([options.Scope]);

        try
        {
            return await ReportTokenAsync(credential, options, context, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AuthenticationRequiredException)
        {
            await Console.Error
                .WriteLineAsync(
                    "Er is geen aanmelding op deze machine. Meld je eenmalig aan met " +
                    $"'{UrenCommands.SignIn}'.")
                .ConfigureAwait(false);
            return 1;
        }
        // De volgorde telt: AuthenticationRequiredException erft van CredentialUnavailableException,
        // en die van AuthenticationFailedException. Van bijzonder naar algemeen, anders vangt de
        // eerste clausule alles.
        catch (CredentialUnavailableException exception)
        {
            await Console.Error
                .WriteLineAsync(
                    "De aanmelding op deze machine is niet te gebruiken: " + exception.Message +
                    $" Meld je opnieuw aan met '{UrenCommands.SignIn}'.")
                .ConfigureAwait(false);
            return 1;
        }
        catch (AuthenticationFailedException exception)
        {
            await Console.Error.WriteLineAsync(Explain(exception, options)).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> ReportTokenAsync(
        TokenCredential credential,
        UrenOptions options,
        TokenRequestContext context,
        CancellationToken cancellationToken)
    {
        AccessToken token = await credential.GetTokenAsync(context, cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"Token voor scope : {options.Scope}");
        Console.WriteLine($"Geldig tot       : {token.ExpiresOn:yyyy-MM-dd HH:mm:ss} UTC");

        TokenClaims claims = TokenClaims.Read(token.Token);

        Console.WriteLine($"Audience (aud)   : {claims.Audience ?? "(niet te lezen)"}");
        Console.WriteLine($"Gebruiker        : {claims.User ?? "(niet te lezen)"}");
        Console.WriteLine($"Client (appid)   : {claims.ClientId ?? "(niet te lezen)"}");
        Console.WriteLine($"Rollen           : {(claims.Roles.Count == 0 ? "(geen)" : string.Join(", ", claims.Roles))}");
        Console.WriteLine($"Scopes (scp)     : {claims.Scopes ?? "(geen)"}");
        Console.WriteLine();

        if (claims.Roles.Count == 0)
        {
            // Dit is de toestand die er het meest onschuldig uitziet en het meest kost: je bent
            // aangemeld, en elk rolbeleid staat stil dicht. Zie stand-van-zaken.md, "Eén valkuil".
            Console.WriteLine(
                "LET OP: er zit geen enkele rolclaim in dit token. Het portaal weigert de boeking dan " +
                "met 403. Meestal ontbreekt de app-roltoewijzing 'Operator', of staat er een " +
                "toewijzing zonder rol (appRoleId 00000000-…) — die laat je wél binnen maar levert " +
                "geen rolclaim.");
            return 1;
        }

        if (!claims.Roles.Contains("Operator", StringComparer.Ordinal))
        {
            Console.WriteLine(
                "LET OP: de rol 'Operator' zit niet in dit token. Uren boeken is operatorwerk (§2), " +
                "dus het portaal weigert de boeking met 403.");
            return 1;
        }

        Console.WriteLine("De aanmelding is bruikbaar: er zit een Operator-rol in het token.");
        return 0;
    }

    private static string Explain(AuthenticationFailedException exception, UrenOptions options) =>
        $"Aanmelden voor scope '{options.Scope}' is mislukt." +
        Environment.NewLine +
        Environment.NewLine +
        Detail(exception) +
        Environment.NewLine +
        Environment.NewLine +
        "Loop de drie dingen na die hier misgaan: is de scope blootgesteld op de registratie " +
        $"soratus-portal, is client {options.ClientId} daarop vooraf geautoriseerd, en klopt de " +
        $"tenant ({options.TenantId})? De commando's staan in docs/agent-portal/mcp-uren.md.";

    /// <summary>
    /// Zoekt de diepste boodschap in de keten.
    /// </summary>
    /// <remarks>
    /// De boodschap van <see cref="AuthenticationFailedException"/> is in de praktijk
    /// "DeviceCodeCredential authentication failed:" met niets erachter — de echte melding van Entra
    /// (<c>AADSTS700016: Application with identifier … was not found</c>) zit in de binnenste
    /// uitzondering. Alleen de buitenste afdrukken is dus letterlijk het gedrag waar dit project
    /// elders over klaagt: een foutmelding die niets over de oorzaak zegt. Gemeten met een verzonnen
    /// client-id, en toen stond er niets bruikbaars.
    /// </remarks>
    private static string Detail(Exception exception)
    {
        var messages = new List<string>();

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            string message = current.Message.Trim().TrimEnd(':').Trim();

            if (message.Length > 0 && !messages.Contains(message, StringComparer.Ordinal))
            {
                messages.Add(message);
            }
        }

        return messages.Count == 0
            ? "Er kwam geen melding mee. Controleer de netwerkverbinding."
            : string.Join(Environment.NewLine + "  → ", messages);
    }

    /// <summary>
    /// De claims uit een token, uitsluitend om te tonen.
    /// </summary>
    /// <remarks>
    /// Dit is <strong>geen</strong> validatie en het mag er nooit een worden. Een JWT die je zelf
    /// uitpakt zonder de ondertekening te controleren zegt niets over echtheid; hier is de lezer een
    /// mens die wil weten wat hij heeft gekregen, en het token is net door Entra afgegeven aan dit
    /// proces. Valideren doet het portaal.
    /// </remarks>
    private sealed record TokenClaims(
        string? Audience,
        string? User,
        string? ClientId,
        IReadOnlyList<string> Roles,
        string? Scopes)
    {
        public static TokenClaims Read(string token)
        {
            try
            {
                string[] parts = token.Split('.');
                if (parts.Length < 2)
                {
                    return Empty;
                }

                using JsonDocument payload = JsonDocument.Parse(DecodeSegment(parts[1]));
                JsonElement root = payload.RootElement;

                return new TokenClaims(
                    Text(root, "aud"),
                    Text(root, "preferred_username") ?? Text(root, "upn") ?? Text(root, "unique_name"),
                    Text(root, "appid") ?? Text(root, "azp"),
                    Strings(root, "roles"),
                    Text(root, "scp"));
            }
            catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
            {
                return Empty;
            }
        }

        private static TokenClaims Empty { get; } = new(null, null, null, [], null);

        private static byte[] DecodeSegment(string segment)
        {
            // Base64url zonder opvulling; die moet er weer bij voordat Base64 het aanneemt.
            string padded = segment.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');

            return Convert.FromBase64String(padded);
        }

        private static string? Text(JsonElement root, string name) =>
            root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static IReadOnlyList<string> Strings(JsonElement root, string name) =>
            root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
                ? [.. value.EnumerateArray()
                    .Where(static item => item.ValueKind == JsonValueKind.String)
                    .Select(static item => item.GetString()!)]
                : [];
    }
}
