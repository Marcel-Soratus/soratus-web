using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Soratus.Mcp.Uren;

/// <summary>
/// Praat met het urenendpoint van het portaal.
/// </summary>
/// <remarks>
/// <para><strong>Deze server schrijft niet zelf naar Cosmos, en dat is de kern van het ontwerp.</strong>
/// Een urenregel staat in de container <c>customers</c> van de database <c>platform</c>, naast het
/// klantdocument, het contract en de toegangsdocumenten — omdat die vier in één partitie horen en
/// een klant aanmaken daardoor atomair is. Een dataplane-rol in Cosmos is niet fijner te scopen dan
/// een container, dus schrijfrecht op urenregels ís schrijfrecht op de toegangsdocumenten. Wie daar
/// een regel bij kan schrijven, verleent zichzelf portaaltoegang. Dat is geen lek maar een
/// rechtenverhoging, en hij zou niet als storing zichtbaar zijn.</para>
///
/// <para>Er is een tweede reden die net zo zwaar weegt. De vaste regel uit §5 — alles wat een
/// koppeling inschiet landt als te fiatteren — is alleen een eigenschap van het systeem als hij aan
/// de schrijfkant staat waar niemand bij kan. Zou deze server rechtstreeks schrijven, dan is die
/// regel een <c>const string</c> in een programma op iemands laptop, en dan is hij een gewoonte en
/// geen garantie. Achter het endpoint is hij structureel: het schrijfpad voor een koppeling heeft
/// geen statusveld.</para>
///
/// <para>Wat het kost, eerlijk: deze server werkt niet zonder een bereikbaar portaal met een API die
/// bearer-tokens aanneemt. Lokaal draaien vraagt dus een portaal (of de proefdraaimodus, zie
/// <see cref="UrenOptions.DryRun"/>), en de melding bij een onbereikbaar portaal moet dat kunnen
/// uitleggen. Rechtstreeks naar Cosmos zou lokaal eenvoudiger zijn geweest.</para>
/// </remarks>
internal sealed class PortalUrenClient(HttpClient http, IOptions<UrenOptions> options)
{
    private readonly UrenOptions _options = options.Value;

    /// <summary>
    /// Stuurt de boeking naar het portaal.
    /// </summary>
    /// <param name="request">Het gevalideerde verzoek.</param>
    /// <param name="cancellationToken">Afbreken.</param>
    /// <returns>Wat er is gebeurd.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <c>null</c>.</exception>
    public async Task<BookingOutcome> BookAsync(
        HourBookingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_options.DryRun)
        {
            return new BookingOutcome.DryRun(request);
        }

        try
        {
            using HttpResponseMessage response = await http
                .PostAsJsonAsync(HourEntryContract.BookingPath, request, cancellationToken)
                .ConfigureAwait(false);

            return await InterpretAsync(response, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Een tijdslimiet. Het verzoek kan zijn aangekomen en pas het antwoord kan zijn
            // weggevallen, dus "mislukt" zeggen zou een gok zijn — en de gok die de verkeerde kant
            // op valt levert een dubbele boeking op.
            return new BookingOutcome.Unavailable(
                $"Het portaal antwoordde niet binnen {_options.Timeout.TotalSeconds:0} seconden. " +
                "Of de regel is aangekomen is hiermee niet vast te stellen.",
                MayHaveLanded: true);
        }
        catch (HttpRequestException exception)
        {
            return new BookingOutcome.Unavailable(
                $"Het portaal op {http.BaseAddress} is niet bereikbaar: {exception.Message}",
                MayHaveLanded: false);
        }
        catch (AuthenticationRequiredException)
        {
            // De server vraagt bewust nooit interactief om een aanmelding: de device-code-instructie
            // zou op stdout moeten en dat is het JSON-RPC-kanaal, en op stderr ziet de aanroeper hem
            // niet — dan hangt de tool tot de tijdslimiet. Aanmelden is daarom een eigen commando.
            return new BookingOutcome.Unavailable(
                "Er is geen geldige aanmelding op deze machine. Meld je eenmalig aan met " +
                $"'{UrenCommands.SignIn}' en probeer het opnieuw; daarna onthoudt deze machine de " +
                "aanmelding. Deze server vraagt er zelf niet om, omdat een device-code-prompt de " +
                "JSON-RPC-stroom zou verstoren.",
                MayHaveLanded: false);
        }
        catch (CredentialUnavailableException exception)
        {
            return new BookingOutcome.Unavailable(
                "De aanmelding op deze machine is niet te gebruiken: " + exception.Message +
                $" Meld je opnieuw aan met '{UrenCommands.SignIn}'.",
                MayHaveLanded: false);
        }
        catch (AuthenticationFailedException exception)
        {
            return new BookingOutcome.Unavailable(
                $"Er is geen token voor scope '{_options.Scope}' te krijgen. Meestal is de scope niet " +
                "blootgesteld op de portaal-registratie, of heeft deze client er geen toestemming " +
                $"voor. ({exception.Message}) Controleer met '{UrenCommands.Check}'.",
                MayHaveLanded: false);
        }
    }

    private async Task<BookingOutcome> InterpretAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
            case HttpStatusCode.Created:
                return await InterpretSuccessAsync(response, cancellationToken).ConfigureAwait(false);

            case HttpStatusCode.BadRequest:
            case HttpStatusCode.UnprocessableContent:
            case HttpStatusCode.Conflict:
                return new BookingOutcome.Refused(
                    await ReadProblemAsync(response, cancellationToken).ConfigureAwait(false),
                    Sent: true);

            case HttpStatusCode.Unauthorized:
                return new BookingOutcome.Unavailable(
                    "Het portaal heeft de aanmelding niet geaccepteerd. Meld je aan met 'az login' als " +
                    "de operator die deze uren boekt.",
                    MayHaveLanded: false);

            case HttpStatusCode.Forbidden:
                return new BookingOutcome.Unavailable(
                    "Deze identiteit mag geen uren boeken. Uren boeken is operatorwerk (§2 van de " +
                    "spec): het portaal eist de app-rol 'Operator' op het token. Een klantrol komt " +
                    "hier niet door, en dat is de bedoeling.",
                    MayHaveLanded: false);

            case HttpStatusCode.NotFound:
                return new BookingOutcome.Unavailable(
                    $"Het portaal kent '{HourEntryContract.BookingPath}' niet. Het urenendpoint is " +
                    "vermoedelijk nog niet uitgerold; zie docs/agent-portal/mcp-uren.md voor het " +
                    "contract dat er hoort te staan.",
                    MayHaveLanded: false);

            default:
                return new BookingOutcome.Unavailable(
                    $"Het portaal antwoordde met {(int)response.StatusCode} " +
                    $"({response.ReasonPhrase}). Dit is geen antwoord dat over de boeking gaat, dus of " +
                    "de regel is vastgelegd is niet vast te stellen.",
                    MayHaveLanded: (int)response.StatusCode >= 500);
        }
    }

    private static async Task<BookingOutcome> InterpretSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        HourBookingResponse? entry;

        try
        {
            entry = await response.Content
                .ReadFromJsonAsync<HourBookingResponse>(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            return new BookingOutcome.Unavailable(
                $"Het portaal gaf {(int)response.StatusCode} terug met een antwoord dat niet te lezen " +
                $"is ({exception.Message}). De regel is mogelijk wél vastgelegd.",
                MayHaveLanded: true);
        }

        if (entry is null)
        {
            return new BookingOutcome.Unavailable(
                $"Het portaal gaf {(int)response.StatusCode} terug zonder urenregel. De regel is " +
                "mogelijk wél vastgelegd.",
                MayHaveLanded: true);
        }

        // Dit is de controle waar de hele server om draait. Een geslaagd antwoord is nog geen bewijs
        // dat de regel als te fiatteren is vastgelegd, en dát is wat de aanroeper te horen krijgt.
        // Melden we hier "geboekt" terwijl de status iets anders zegt, dan denkt de boeker dat er
        // een mens naar gaat kijken terwijl het bedrag al meetelt.
        if (!string.Equals(entry.Status, HourEntryContract.PendingStatus, StringComparison.Ordinal))
        {
            return new BookingOutcome.Suspect(
                entry,
                $"Het portaal gaf status '{entry.Status ?? "(geen)"}' terug in plaats van " +
                $"'{HourEntryContract.PendingStatus}'. §5 legt vast dat alles wat een koppeling " +
                "inschiet als te fiatteren landt en pas na akkoord van Soratus meetelt. Dit antwoord " +
                "zegt iets anders, dus behandel deze regel als onbetrouwbaar en kijk hem na in het " +
                "portaal voordat er gefactureerd wordt.");
        }

        if (entry.Source is not null
            && !string.Equals(entry.Source, HourEntryContract.Source, StringComparison.Ordinal))
        {
            return new BookingOutcome.Suspect(
                entry,
                $"Het portaal gaf bron '{entry.Source}' terug in plaats van " +
                $"'{HourEntryContract.Source}'. De regel is als te fiatteren vastgelegd, maar niet als " +
                "MCP-regel, en dan staat er in het portaal iets anders dan wat hier is gebeurd.");
        }

        return new BookingOutcome.Booked(entry);
    }

    /// <summary>
    /// Haalt de afwijzingsredenen uit het antwoord.
    /// </summary>
    /// <remarks>
    /// Verwacht <c>application/problem+json</c> (RFC 9457), zoals ASP.NET Core dat standaard
    /// schrijft. De uitbreidingen <c>errors</c>, <c>categories</c> en <c>customers</c> worden
    /// meegenomen als ze er staan: dat is precies de kennis die het portaal heeft en deze server
    /// niet, en die de aanroeper nodig heeft om het in één keer goed te doen.
    /// </remarks>
    private static async Task<IReadOnlyList<string>> ReadProblemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var reasons = new List<string>();

        try
        {
            using JsonDocument document = await JsonDocument
                .ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            JsonElement root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                AddString(root, "detail", reasons);

                if (reasons.Count == 0)
                {
                    AddString(root, "title", reasons);
                }

                AddStrings(root, "errors", reasons);
                AddList(root, "categories", "Geldige categorieën", reasons);
                AddList(root, "customers", "Bekende klanten", reasons);
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            // Het antwoord is geen leesbare JSON. Dat is zelf de melding; er wordt niets verzonnen.
        }

        if (reasons.Count == 0)
        {
            reasons.Add(
                $"Het portaal wees de boeking af met {(int)response.StatusCode} en zonder toelichting.");
        }

        return reasons;
    }

    private static void AddString(JsonElement root, string name, List<string> into)
    {
        if (root.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 } text)
        {
            into.Add(text);
        }
    }

    private static void AddStrings(JsonElement root, string name, List<string> into)
    {
        if (!root.TryGetProperty(name, out JsonElement value))
        {
            return;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Array:
                into.AddRange(value.EnumerateArray()
                    .Where(static item => item.ValueKind == JsonValueKind.String)
                    .Select(static item => item.GetString()!)
                    .Where(static item => item.Length > 0));
                break;

            case JsonValueKind.Object:
                // De vorm die ASP.NET Core bij modelvalidatie schrijft: veld → lijst meldingen.
                foreach (JsonProperty field in value.EnumerateObject())
                {
                    if (field.Value.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    into.AddRange(field.Value.EnumerateArray()
                        .Where(static item => item.ValueKind == JsonValueKind.String)
                        .Select(static item => item.GetString()!)
                        .Where(static item => item.Length > 0)
                        .Select(message => $"{field.Name}: {message}"));
                }

                break;

            default:
                break;
        }
    }

    private static void AddList(JsonElement root, string name, string label, List<string> into)
    {
        if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        string[] items = [.. value.EnumerateArray()
            .Select(static item => item.ValueKind switch
            {
                // Een categorielijst is een lijst strings; een klantenlijst is een lijst objecten
                // met een cid. Beide vormen komen hier langs, dus beide worden gelezen.
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Object when item.TryGetProperty("cid", out JsonElement cid)
                    && cid.ValueKind == JsonValueKind.String => cid.GetString(),
                _ => null,
            })
            .OfType<string>()
            .Where(static item => item.Length > 0)];

        if (items.Length > 0)
        {
            into.Add($"{label}: {string.Join(", ", items)}.");
        }
    }

}
