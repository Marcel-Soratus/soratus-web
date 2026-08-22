using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Soratus.Support.FirstLine;

/// <summary>
/// De enige implementatie: één chat completion op een Azure OpenAI-deployment, met de managed
/// identity van het portaal.
/// </summary>
/// <remarks>
/// <para><strong>Geen sleutel, nergens.</strong> Het token komt van de
/// <see cref="TokenCredential"/> die het portaal al heeft staan — in productie
/// <c>id-soratus-portal</c>, lokaal de Azure CLI. De scope is
/// <c>https://cognitiveservices.azure.com/.default</c>, en de rol die daarbij hoort is
/// <em>Cognitive Services OpenAI User</em>: die geeft alleen inferentie en geen
/// <c>listKeys</c>. Dat is dezelfde afweging als bij de Communication Service, waar met opzet een
/// custom role staat in plaats van Contributor — een rol die de sleutel kan uitlezen is machtiger dan
/// het geheim dat we juist wilden vermijden.</para>
///
/// <para><strong>Er wordt niets van het antwoord van de dienst gelogd behalve de statuscode.</strong>
/// Dat is punt 13 en 14 van de fase-0-afwijkingen in een nieuwe richting: het foutlichaam van een
/// externe dienst kan onze eigen prompt terugkaatsen (een guardrail-melding noemt waar hij op sloeg),
/// en die prompt bevat de vraag van een klant en de feiten van die klant. Een logregel van dit
/// portaal komt op een operatorscherm; klantgegevens die daar via een omweg in belanden zijn er
/// niemand mee geholpen.</para>
///
/// <para><strong>Geen herhaling, en geen backoff.</strong> Anders dan bij
/// <c>AzureCostClient</c>, die een 429 uitzit omdat er niemand op wacht. Hier wacht een mens op een
/// pagina, en de vraag staat al in de draad. Een 429 is dus geen reden om te wachten maar om te
/// escaleren: een mens antwoordt, en dat is een goede uitkomst.</para>
/// </remarks>
internal sealed class AzureOpenAiChooser(
    IHttpClientFactory clients,
    TokenCredential credential,
    IOptions<FirstLineOptions> options,
    ILogger<AzureOpenAiChooser> logger) : IFirstLineChooser
{
    /// <summary>
    /// De naam van de <see cref="HttpClient"/> in de fabriek.
    /// </summary>
    /// <remarks>
    /// Een fabriek en geen geïnjecteerde <see cref="HttpClient"/>, om dezelfde reden als bij
    /// <c>AzureCostClient</c> en <c>DevOpsSprintClient</c>: zo kan deze klasse niet stil een
    /// singleton worden die jaren dezelfde handler vasthoudt en een DNS-wijziging van
    /// <c>openai.azure.com</c> niet meer volgt.
    /// </remarks>
    internal const string HttpClientName = "eerstelijn-aoai";

    /// <summary>De tokenscope van Azure AI Services.</summary>
    private static readonly string[] TokenScope =
        ["https://cognitiveservices.azure.com/.default"];

    /// <inheritdoc />
    public async Task<FirstLineChoice?> ChooseAsync(
        FirstLineQuestion question,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(question);

        var settings = options.Value;

        if (settings.CompletionsUri() is not { } address)
        {
            // Onbereikbaar via de registratie — die zet deze klasse alleen neer als het endpoint en
            // de deployment er staan. Hij blijft staan als vangnet voor de dag dat iemand de
            // instellingen tijdens de rit leegmaakt, en dan is niet-vragen de goede uitkomst.
            logger.LogWarning(
                "De eerstelijn is aangeroepen zonder endpoint of deployment. Er wordt niets "
                + "gevraagd en de vraag gaat naar een mens.");

            return null;
        }

        if (question.Facts.Count == 0)
        {
            // Geen feiten, dus niets te kiezen. Niet vragen is hier eerlijker dan vragen — hetzelfde
            // besluit als bij de kostencollector op de 1e van de maand — en het scheelt een aanroep
            // die alleen maar een overdracht kan opleveren.
            logger.LogInformation(
                "De eerstelijn heeft geen feiten om uit te kiezen; de vraag gaat naar een mens "
                + "zonder aanroep aan het model.");

            return FirstLineChoice.ToAHuman(FirstLineHandoff.OutsideTheData);
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        try
        {
            var content = await AskAsync(address, settings, question, limit.Token)
                .ConfigureAwait(false);

            var choice = FirstLinePrompt.Read(content);

            switch (choice)
            {
                case null:
                    logger.LogWarning(
                        "De eerstelijn kon het antwoord van {Deployment} niet lezen. De vraag gaat "
                        + "naar een mens.",
                        settings.Deployment);
                    break;

                case { Handoff: { } reason }:
                    logger.LogInformation(
                        "De eerstelijn draagt over ({Reason}); {Aantal} feiten aangeboden op "
                        + "{Deployment}.",
                        reason,
                        question.Facts.Count,
                        settings.Deployment);
                    break;

                case { Index: { } index }:
                    logger.LogInformation(
                        "De eerstelijn wees plaats {Index} van {Aantal} feiten aan op {Deployment}.",
                        index,
                        question.Facts.Count,
                        settings.Deployment);
                    break;
            }

            return choice;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // De klant heeft zijn tabblad gesloten. Doorgeven: SupportDesk laat deze uitzondering
            // met opzet door, en er valt hier niets weg door niets te doen — de vraag is vastgelegd
            // vóór deze aanroep.
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "De eerstelijn wachtte langer dan {Seconds} seconden op {Deployment}. De vraag gaat "
                + "naar een mens.",
                settings.TimeoutSeconds,
                settings.Deployment);

            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            // Zonder het lichaam van de respons, en dat is de opmerking bovenaan deze klasse. De
            // uitzondering zelf mág mee: die gaat naar de logregel van een operator en niet naar de
            // draad van een klant.
            logger.LogError(
                exception,
                "De eerstelijn kon {Deployment} niet bereiken. De vraag gaat naar een mens.",
                settings.Deployment);

            return null;
        }
    }

    /// <summary>
    /// Doet de aanroep en levert de inhoud van de boodschap op.
    /// </summary>
    /// <returns>De tekst van het model, of <c>null</c> als er geen bruikbare respons was.</returns>
    /// <remarks>
    /// <para><strong>De vier vaste velden van het verzoek, en waarom ze zo staan.</strong>
    /// <c>temperature: 0</c> omdat er niets te variëren is: dezelfde vraag met dezelfde feiten hoort
    /// hetzelfde nummer op te leveren, en een tweede antwoord op dezelfde vraag is verwarring en geen
    /// rijkdom. <c>max_tokens: 32</c> omdat het antwoord <c>{"kies": 3}</c> is; wie hier meer nodig
    /// heeft, is aan het schrijven in plaats van aan het kiezen. <c>response_format: json_object</c>
    /// omdat de deployment die capability meldt (<c>jsonObjectResponse: true</c>, gemeten) en
    /// json-schema niet. En <c>n</c> staat er niet: één antwoord.</para>
    /// </remarks>
    private async Task<string?> AskAsync(
        Uri address,
        FirstLineOptions settings,
        FirstLineQuestion question,
        CancellationToken cancellationToken)
    {
        var token = await credential
            .GetTokenAsync(new TokenRequestContext(TokenScope), cancellationToken)
            .ConfigureAwait(false);

        var body = new
        {
            messages = new object[]
            {
                new { role = "system", content = FirstLinePrompt.System },
                new { role = "user", content = FirstLinePrompt.User(question) },
            },
            temperature = 0,
            max_tokens = 32,
            response_format = new { type = "json_object" },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, address)
        {
            Content = JsonContent.Create(body),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        using var http = clients.CreateClient(HttpClientName);

        using var response = await http
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError(
                "{Deployment} antwoordde met {Status}. De vraag gaat naar een mens.",
                settings.Deployment,
                (int)response.StatusCode);

            return null;
        }

        using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        using var document = await JsonDocument
            .ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var first = choices[0];

        // Een afgekapt antwoord is geen antwoord. Bij finish_reason "length" staat er een halve JSON,
        // en die is soms nog te parseren — dan zou een half nummer een heel feit aanwijzen. Bij
        // "content_filter" is de tekst weggehaald door de guardrail; ook dan is er niets gekozen.
        if (first.TryGetProperty("finish_reason", out var finish)
            && finish.ValueKind == JsonValueKind.String
            && finish.GetString() is { } reason
            && reason != "stop")
        {
            logger.LogWarning(
                "{Deployment} stopte met {Reason} in plaats van stop. De vraag gaat naar een mens.",
                settings.Deployment,
                reason);

            return null;
        }

        return first.TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var text)
            && text.ValueKind == JsonValueKind.String
                ? text.GetString()
                : null;
    }
}
