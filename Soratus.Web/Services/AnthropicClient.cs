using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Soratus.Web.Models;

namespace Soratus.Web.Services;

public sealed class AnthropicClient(
    HttpClient http,
    IOptions<AnthropicOptions> opt,
    ILogger<AnthropicClient> log)
{
    private readonly AnthropicOptions _opt = opt.Value;

    public async IAsyncEnumerable<string> StreamAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> turns,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opt.ApiKey))
        {
            log.LogWarning("Anthropic API key not configured; returning placeholder.");
            yield return "De chat is nog niet geconfigureerd. Vraag het team om de Anthropic key aan te zetten in Azure App Service.";
            yield break;
        }

        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Content = JsonContent.Create(new
            {
                model = _opt.Model,
                max_tokens = _opt.MaxTokens,
                system = systemPrompt,
                stream = true,
                messages = turns.Select(t => new { role = t.Role, content = t.Content })
            })
        };
        req.Headers.Add("x-api-key", _opt.ApiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Headers.Accept.Add(new("text/event-stream"));

        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            log.LogError("Anthropic API failed {Status}: {Body}", res.StatusCode, body);
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
                if (evt.TryGetProperty("type", out var t) &&
                    t.GetString() == "content_block_delta" &&
                    evt.TryGetProperty("delta", out var d) &&
                    d.TryGetProperty("text", out var text))
                {
                    delta = text.GetString();
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
        if (string.IsNullOrWhiteSpace(_opt.ApiKey)) return false;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/v1/models");
            req.Headers.Add("x-api-key", _opt.ApiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");
            using var res = await http.SendAsync(req, ct);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
