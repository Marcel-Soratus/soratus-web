using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Soratus.Web.Models;

namespace Soratus.Web.Services;

/// <summary>
/// Streams chat completions from an Azure OpenAI deployment.
/// Uses the OpenAI Chat Completions API surface (system + user/assistant messages).
/// </summary>
public sealed class AzureOpenAIClient(
    HttpClient http,
    IOptions<AzureOpenAIOptions> opt,
    ILogger<AzureOpenAIClient> log)
{
    private readonly AzureOpenAIOptions _opt = opt.Value;

    public async IAsyncEnumerable<string> StreamAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> turns,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opt.ApiKey) || string.IsNullOrWhiteSpace(_opt.Endpoint))
        {
            log.LogWarning("Azure OpenAI not configured; returning placeholder.");
            yield return "De chat is nog niet geconfigureerd. Vraag het team om de Azure OpenAI key in App Service te zetten.";
            yield break;
        }

        // Compose the messages array. OpenAI-style: system first, then turns.
        var messages = new List<object> { new { role = "system", content = systemPrompt } };
        foreach (var t in turns)
            messages.Add(new { role = t.Role, content = t.Content });

        var url = $"openai/deployments/{_opt.DeploymentName}/chat/completions?api-version={_opt.ApiVersion}";
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new
            {
                messages,
                max_tokens = _opt.MaxTokens,
                stream = true
            })
        };
        req.Headers.Add("api-key", _opt.ApiKey);
        req.Headers.Accept.Add(new("text/event-stream"));

        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            log.LogError("Azure OpenAI request failed {Status}: {Body}", res.StatusCode, body);
            yield return "Onze AI is even niet bereikbaar. Probeer het zo opnieuw, of klik op \"Laat me terugbellen\".";
            yield break;
        }

        using var body2 = await res.Content.ReadAsStreamAsync(ct);
        using var rdr = new StreamReader(body2);
        string? line;
        while ((line = await rdr.ReadLineAsync(ct)) is not null)
        {
            if (!line.StartsWith("data: ")) continue;
            var json = line[6..];
            if (json == "[DONE]") yield break;

            string? delta = null;
            try
            {
                var evt = JsonSerializer.Deserialize<JsonElement>(json);
                if (evt.TryGetProperty("choices", out var choices) &&
                    choices.ValueKind == JsonValueKind.Array &&
                    choices.GetArrayLength() > 0)
                {
                    var first = choices[0];
                    if (first.TryGetProperty("delta", out var d) &&
                        d.TryGetProperty("content", out var content) &&
                        content.ValueKind == JsonValueKind.String)
                    {
                        delta = content.GetString();
                    }
                }
            }
            catch (JsonException ex)
            {
                log.LogDebug(ex, "Could not parse SSE line: {Line}", line);
            }

            if (!string.IsNullOrEmpty(delta))
                yield return delta;
        }
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.ApiKey) || string.IsNullOrWhiteSpace(_opt.Endpoint)) return false;
        try
        {
            // Lightweight check — list deployments via management-style endpoint isn't available
            // through the data plane, so just do a tiny completion with max_tokens=1.
            var url = $"openai/deployments/{_opt.DeploymentName}/chat/completions?api-version={_opt.ApiVersion}";
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new
                {
                    messages = new[] { new { role = "user", content = "hi" } },
                    max_tokens = 1
                })
            };
            req.Headers.Add("api-key", _opt.ApiKey);
            using var res = await http.SendAsync(req, ct);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
