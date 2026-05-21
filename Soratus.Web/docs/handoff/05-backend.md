# 05 · Backend

Two endpoints. Both POST. Both server-side only. No public surface beyond these two routes plus the static SPA shell.

## `POST /api/chat` — Sora chatbot

Streamed reply from Claude with a Soratus-flavored system prompt.

### Wire format

**Request (JSON):**
```json
{
  "messages": [
    { "role": "user",      "content": "Wat kost een AI agent?" },
    { "role": "assistant", "content": "Het hangt af van …" },
    { "role": "user",      "content": "En hoe lang duurt het?" }
  ]
}
```

**Response:** `text/event-stream` (SSE).
```
data: {"delta":"Een eenvoudige","done":false}

data: {"delta":" agent staat","done":false}

…

data: {"delta":"","done":true,"turnId":"01J…"}
```

SSE keeps the client implementation trivial and avoids the WebSocket buffer overhead for a one-direction stream. The client (`ChatWidget.razor`) reads it via the SignalR circuit — it doesn't fetch SSE directly because the Blazor server handles the upstream Claude stream on its side and pushes chunks through SignalR state updates.

### Implementation outline

```csharp
public static class ChatEndpoint
{
    public static void MapChatEndpoint(this WebApplication app)
    {
        app.MapPost("/api/chat", async (
            ChatRequest req,
            AnthropicClient claude,
            SystemPromptBuilder prompt,
            CancellationToken ct) =>
        {
            if (req.Messages is null || req.Messages.Count == 0)
                return Results.BadRequest();

            return Results.Stream(async stream =>
            {
                var writer = new StreamWriter(stream);
                await foreach (var chunk in claude.StreamAsync(
                    prompt.Build(),
                    req.Messages,
                    ct))
                {
                    await writer.WriteAsync(
                        $"data: {{\"delta\":{JsonSerializer.Serialize(chunk)},\"done\":false}}\n\n");
                    await writer.FlushAsync();
                }
                await writer.WriteAsync("data: {\"delta\":\"\",\"done\":true}\n\n");
                await writer.FlushAsync();
            }, "text/event-stream");
        })
        .WithName("Chat")
        .DisableAntiforgery(); // SSE doesn't carry the anti-forgery cookie
    }
}
```

### `AnthropicClient`

```csharp
public sealed class AnthropicClient(
    HttpClient http,
    IOptions<AnthropicOptions> opt,
    ILogger<AnthropicClient> log)
{
    public async IAsyncEnumerable<string> StreamAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> turns,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Headers =
            {
                { "x-api-key", opt.Value.ApiKey },
                { "anthropic-version", "2023-06-01" }
            },
            Content = JsonContent.Create(new
            {
                model = opt.Value.Model,
                max_tokens = opt.Value.MaxTokens,
                system = systemPrompt,
                stream = true,
                messages = turns.Select(t => new { role = t.Role, content = t.Content })
            })
        };
        req.Headers.Accept.Add(new("text/event-stream"));

        using var res = await http.SendAsync(req,
            HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();

        using var body = await res.Content.ReadAsStreamAsync(ct);
        using var rdr = new StreamReader(body);
        string? line;
        while ((line = await rdr.ReadLineAsync(ct)) is not null)
        {
            if (!line.StartsWith("data: ")) continue;
            var json = line[6..];
            if (json == "[DONE]") yield break;
            var evt = JsonSerializer.Deserialize<JsonElement>(json);
            if (evt.TryGetProperty("type", out var t) &&
                t.GetString() == "content_block_delta" &&
                evt.TryGetProperty("delta", out var d) &&
                d.TryGetProperty("text", out var text))
            {
                yield return text.GetString() ?? "";
            }
        }
    }
}
```

Polly resilience is wired up in `Program.cs` via `AddStandardResilienceHandler()`. That gives you exponential backoff on 429/5xx out of the box.

### `SystemPromptBuilder`

```csharp
public sealed class SystemPromptBuilder(IOptions<BrandOptions> brand, IOptions<CompanyOptions> company)
{
    public string Build() => $$"""
        Je bent Sora, de AI-assistent op de Soratus website.
        Soratus B.V. ({{company.Value.LegalName}}, KvK {{company.Value.Kvk}}) is een
        Nederlands AI-development bureau dat agents, custom software en integraties bouwt
        voor MKB en enterprise.

        Tone of voice:
        - Nederlands, direct, droog, zelfverzekerd.
        - Geen marketing-slogans. Geen emoji. Geen uitroeptekens.
        - Praat zoals een senior developer praat: kort, technisch waar nuttig,
          eerlijk over wat we wel en niet doen.
        - Voorbeeld: "Een eenvoudige agent staat binnen 14 dagen live. Complexere
          systemen tussen 4 en 8 weken. We werken met vaste prijzen, geen uurtarief."

        Wat we doen:
        - AI-agents (klantenservice, sales, ops, finance) die op klantdata draaien.
        - Custom software in .NET 9 / Blazor / Azure / SQL Server.
        - Integraties met Exact, AFAS, Salesforce, Shopify, HubSpot, Slack, Teams,
          WhatsApp, Outlook.
        - EU-hosted, AVG-proof, ISO 27001 via partners.

        Wat we NIET doen:
        - Templates of low-code platformen verkopen.
        - "Discovery phases" van 3 maanden.
        - Trainen op klantdata.

        Stats om aan te halen als relevant:
        - {{brand.Value.Stats.ActiveProjects}} actieve projecten
        - {{brand.Value.Stats.AgentsInProduction}} AI-agents in productie
        - Antwoord binnen {{brand.Value.Stats.ResponseTimeHours}} uur

        Als iemand wil bellen, mailen, of een afspraak wil:
        → Stuur ze naar de "Laat me terugbellen" knop in deze chat.
        → Of geef het mailadres {{company.Value.Email}}.

        Houd antwoorden onder de 4 zinnen tenzij om detail gevraagd wordt.
        """;
}
```

The prompt is intentionally tight. Tune it against real conversations once the site is live; don't add a 4-page "knowledge base" file yet.

## `POST /api/lead` — Callback request

Fired when a user clicks "Laat me terugbellen →" in the chat suggestions and fills the inline form (name, company, phone, optional note).

### Wire format

```json
{
  "name": "string",
  "company": "string",
  "phone": "string",
  "email": "string?",
  "note": "string?",
  "consent": true
}
```

### Implementation

```csharp
public static class LeadEndpoint
{
    public static void MapLeadEndpoint(this WebApplication app)
    {
        app.MapPost("/api/lead", async (
            Lead lead,
            LeadSink sink,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(lead.Name) ||
                string.IsNullOrWhiteSpace(lead.Phone) ||
                !lead.Consent)
                return Results.BadRequest();

            await sink.SaveAsync(lead, ct);
            return Results.Accepted();
        })
        .WithName("Lead")
        .RequireRateLimiting("lead");
    }
}
```

`RequireRateLimiting("lead")` — register a rate-limiter policy in `Program.cs` limiting `/api/lead` to 5 requests / minute / IP. The home page is public; this matters.

### `LeadSink`

Phase 1: send an email to `hallo@soratus.com` via SendGrid. Save nothing.
Phase 2: when volume justifies it, persist to Cosmos DB (single container, partition key = year-month). Don't pre-build phase 2.

```csharp
public sealed class LeadSink(ISendGridClient mail, IOptions<CompanyOptions> co, ILogger<LeadSink> log)
{
    public async Task SaveAsync(Lead lead, CancellationToken ct)
    {
        var msg = new SendGridMessage
        {
            From = new EmailAddress("noreply@soratus.com", "Soratus website"),
            Subject = $"Nieuwe terugbelaanvraag van {lead.Name}",
            PlainTextContent = $"""
                Naam:    {lead.Name}
                Bedrijf: {lead.Company}
                Tel:     {lead.Phone}
                Mail:    {lead.Email ?? "—"}

                Notitie:
                {lead.Note ?? "—"}
                """
        };
        msg.AddTo(co.Value.Email);
        var res = await mail.SendEmailAsync(msg, ct);
        if (!res.IsSuccessStatusCode)
        {
            log.LogError("SendGrid failed: {Status}", res.StatusCode);
            throw new InvalidOperationException("Mail send failed");
        }
    }
}
```

## Rate limiting

```csharp
builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter("lead", w =>
    {
        w.PermitLimit = 5;
        w.Window = TimeSpan.FromMinutes(1);
        w.QueueLimit = 0;
    });
    o.AddFixedWindowLimiter("chat", w =>
    {
        w.PermitLimit = 30;
        w.Window = TimeSpan.FromMinutes(1);
        w.QueueLimit = 0;
    });
});
app.UseRateLimiter();
```

Apply `chat` to the chat endpoint too. The Anthropic API has its own limits, but our reverse-proxy limit is cheaper and keeps a hostile client from burning credits.

## Secrets

| Secret | Source in dev | Source in prod |
|---|---|---|
| `Anthropic:ApiKey` | `dotnet user-secrets` | App Service Configuration |
| `SendGrid:ApiKey` | `dotnet user-secrets` | App Service Configuration |

Never commit either. The `.csproj` should reference user-secrets via `<UserSecretsId>`. `appsettings.json` keeps the keys as `""` so dev fails fast with a clear "set the secret" error if forgotten.

## CORS

Don't add CORS. There is no other origin that legitimately calls these endpoints. If someone asks for a Webflow site to embed the chat, that's a separate hosted widget conversation, not a CORS change.

## Anti-forgery

Blazor's anti-forgery middleware is on by default in `.NET 9`. Both endpoints disable it (`DisableAntiforgery()`) because they're called from JSON `fetch`, not form POSTs. The `<ChatWidget />` itself runs over the SignalR circuit, which already has its own forgery story.

## What about the prototype's `window.claude.complete()`?

That helper exists only inside the Anthropic preview sandbox. Strip it. The chat widget never reaches for `window.claude` — it talks to its own server through the SignalR circuit.
