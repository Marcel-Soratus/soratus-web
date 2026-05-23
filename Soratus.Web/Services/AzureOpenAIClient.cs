using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Soratus.Web.Models;

namespace Soratus.Web.Services;

/// <summary>
/// Streams chat completions from an Azure OpenAI deployment.
/// Uses the OpenAI Chat Completions API surface (system + user/assistant/tool messages).
/// </summary>
public sealed class AzureOpenAIClient(
    HttpClient http,
    IOptions<AzureOpenAIOptions> opt,
    ILogger<AzureOpenAIClient> log)
{
    private readonly AzureOpenAIOptions _opt = opt.Value;

    /// <summary>
    /// Plain text-only streaming. Kept for callers that don't need tool support.
    /// </summary>
    public async IAsyncEnumerable<string> StreamAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> turns,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var ev in StreamChatAsync(systemPrompt, turns, tools: null, ct))
        {
            if (ev is TextDelta td) yield return td.Text;
        }
    }

    /// <summary>
    /// Streaming chat completion with optional tool/function calling.
    /// Emits a sequence of <see cref="StreamEvent"/> records that the caller
    /// pattern-matches on:
    /// <list type="bullet">
    ///   <item><see cref="TextDelta"/> — assistant text chunk</item>
    ///   <item><see cref="ToolCallStarted"/> — model started a tool call (id + name known)</item>
    ///   <item><see cref="ToolCallArgsDelta"/> — JSON-string fragment of the arguments</item>
    ///   <item><see cref="ToolCallReady"/> — full arguments assembled, safe to deserialize</item>
    /// </list>
    /// </summary>
    public async IAsyncEnumerable<StreamEvent> StreamChatAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> turns,
        IReadOnlyList<object>? tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opt.ApiKey) || string.IsNullOrWhiteSpace(_opt.Endpoint))
        {
            log.LogWarning("Azure OpenAI not configured; returning placeholder.");
            yield return new TextDelta(
                "De chat is nog niet geconfigureerd. Vraag het team om de Azure OpenAI key in App Service te zetten.");
            yield break;
        }

        var messages = BuildMessages(systemPrompt, turns);

        object body = tools is { Count: > 0 }
            ? new
            {
                messages,
                max_tokens = _opt.MaxTokens,
                stream = true,
                tools,
                tool_choice = "auto"
            }
            : new
            {
                messages,
                max_tokens = _opt.MaxTokens,
                stream = true
            };

        var url = $"openai/deployments/{_opt.DeploymentName}/chat/completions?api-version={_opt.ApiVersion}";
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Add("api-key", _opt.ApiKey);
        req.Headers.Accept.Add(new("text/event-stream"));

        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode)
        {
            var errorBody = await res.Content.ReadAsStringAsync(ct);
            log.LogError("Azure OpenAI request failed {Status}: {Body}", res.StatusCode, errorBody);
            yield return new TextDelta(
                "Onze AI is even niet bereikbaar. Probeer het zo opnieuw.");
            yield break;
        }

        using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var rdr = new StreamReader(stream);

        // Track in-flight tool calls per index (gpt-4o-mini typically uses index=0)
        var pending = new Dictionary<int, ToolCallBuffer>();
        var started = new HashSet<int>();

        string? line;
        while ((line = await rdr.ReadLineAsync(ct)) is not null)
        {
            if (!line.StartsWith("data: ")) continue;
            var json = line[6..];
            if (json == "[DONE]") break;

            JsonElement choice;
            try
            {
                var doc = JsonSerializer.Deserialize<JsonElement>(json);
                if (!doc.TryGetProperty("choices", out var choices) ||
                    choices.ValueKind != JsonValueKind.Array ||
                    choices.GetArrayLength() == 0)
                    continue;
                choice = choices[0];
            }
            catch (JsonException ex)
            {
                log.LogDebug(ex, "Could not parse SSE frame: {Line}", line);
                continue;
            }

            // 1. text content delta
            if (choice.TryGetProperty("delta", out var delta))
            {
                if (delta.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.String)
                {
                    var s = content.GetString();
                    if (!string.IsNullOrEmpty(s))
                        yield return new TextDelta(s);
                }

                // 2. tool_call deltas
                if (delta.TryGetProperty("tool_calls", out var calls) &&
                    calls.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tc in calls.EnumerateArray())
                    {
                        var idx = tc.TryGetProperty("index", out var i) ? i.GetInt32() : 0;
                        var buf = pending.TryGetValue(idx, out var existing)
                            ? existing
                            : pending[idx] = new ToolCallBuffer();

                        if (tc.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                            buf.Id = id.GetString() ?? buf.Id;

                        if (tc.TryGetProperty("function", out var fn))
                        {
                            if (fn.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String)
                                buf.Name = nm.GetString() ?? buf.Name;

                            if (fn.TryGetProperty("arguments", out var ag) && ag.ValueKind == JsonValueKind.String)
                            {
                                var fragment = ag.GetString() ?? "";
                                buf.Args.Append(fragment);

                                // Emit Started exactly once, after we have an id+name
                                if (!started.Contains(idx) && !string.IsNullOrEmpty(buf.Id) && !string.IsNullOrEmpty(buf.Name))
                                {
                                    started.Add(idx);
                                    yield return new ToolCallStarted(buf.Id, buf.Name);
                                }

                                if (started.Contains(idx) && fragment.Length > 0)
                                    yield return new ToolCallArgsDelta(buf.Id, fragment);
                            }
                        }
                    }
                }
            }

            // 3. finish_reason — flush completed tool calls
            if (choice.TryGetProperty("finish_reason", out var finish) &&
                finish.ValueKind == JsonValueKind.String &&
                finish.GetString() == "tool_calls")
            {
                foreach (var (_, buf) in pending)
                {
                    if (string.IsNullOrEmpty(buf.Id) || string.IsNullOrEmpty(buf.Name)) continue;
                    yield return new ToolCallReady(buf.Id, buf.Name, buf.Args.ToString());
                }
                pending.Clear();
                started.Clear();
            }
        }
    }

    /// <summary>
    /// Build the OpenAI <c>messages</c> array. Handles plain user/assistant/system
    /// turns, assistant turns that carry pending tool-calls, and tool-result turns.
    /// </summary>
    private static List<object> BuildMessages(string systemPrompt, IReadOnlyList<ChatTurn> turns)
    {
        var messages = new List<object> { new { role = "system", content = systemPrompt } };

        foreach (var t in turns)
        {
            if (t.Role == "tool")
            {
                // tool result message — must reference the call id
                messages.Add(new
                {
                    role = "tool",
                    tool_call_id = t.ToolCallId ?? "",
                    content = t.Content
                });
                continue;
            }

            if (t.Role == "assistant" && t.ToolCalls is { Count: > 0 })
            {
                // assistant message that issued one or more tool calls
                var calls = t.ToolCalls.Select(c => new
                {
                    id = c.Id,
                    type = "function",
                    function = new { name = c.Name, arguments = c.ArgumentsJson }
                }).ToArray();

                messages.Add(string.IsNullOrEmpty(t.Content)
                    ? (object)new { role = "assistant", tool_calls = calls }
                    : new { role = "assistant", content = t.Content, tool_calls = calls });
                continue;
            }

            messages.Add(new { role = t.Role, content = t.Content });
        }

        return messages;
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.ApiKey) || string.IsNullOrWhiteSpace(_opt.Endpoint)) return false;
        try
        {
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

    private sealed class ToolCallBuffer
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public StringBuilder Args { get; } = new();
    }
}
