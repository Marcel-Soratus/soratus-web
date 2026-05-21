# 01 · Architecture

## Solution layout

```
Soratus.sln
├── Soratus.Web/            ← Blazor Web App (this is what gets deployed)
├── Soratus.Web.Tests/      ← xUnit + bUnit
└── docs/
    └── handoff/            ← this entire handoff package, untouched
```

One solution, one deployable. Don't split into "Client/Server/Shared" — there's no second client. If a Maui or Avalonia surface ever appears, that's the moment to extract shared models, not before.

## Framework

**.NET 9** (`<TargetFramework>net9.0</TargetFramework>`).

**Blazor Web App** template:
```bash
dotnet new blazor -o Soratus.Web --interactivity Server --auth None --empty
```

`--empty` to skip the counter / weather demo scaffolding. `--interactivity Server` to get SignalR by default; we'll restrict it to `ChatWidget` only.

## Render-mode strategy

| Surface | Mode | Why |
|---|---|---|
| `Home.razor` and all `Sections/*.razor` | **Static SSR** (no `@rendermode` directive) | First paint < 100ms. Marketing pages don't need interactivity beyond scroll-triggered CSS + lightweight JS. |
| `ChatWidget.razor` | `@rendermode InteractiveServer` | Streamed Claude responses over the SignalR circuit. No client-side API key risk. |
| `MainLayout.razor`, `TopNav.razor`, `Footer.razor` | Static SSR | Pure markup. |

Don't promote anything else to `InteractiveServer` "just in case". Every interactive island multiplies the SignalR overhead.

## NuGet dependencies

Keep the list short. Every package added is a future security update.

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.AspNetCore.OpenApi` | 9.* | API metadata for `/api/chat` and `/api/lead` |
| `Anthropic.SDK` *or* a hand-rolled `HttpClient` | (decide once; hand-rolled is fine, ~150 lines) | Talk to Claude |
| `Polly` | 8.* | Retry/circuit-breaker on the Anthropic call |
| `Microsoft.Extensions.Http.Resilience` | 9.* | Or use this in place of Polly directly |
| `SendGrid` *or* `MailKit` | latest | Outbound mail for lead notifications |

Do **not** add:
- A logging package beyond the built-in `ILogger`. Serilog is fine if the user asks; default to built-in.
- `AutoMapper`, `MediatR`, `FluentValidation` — the domain is too small to warrant them. Use plain methods and `DataAnnotations`.
- ORM / Cosmos SDK until lead storage is actually wired (see `05-backend.md`).

## Program.cs (shape)

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<BrandOptions>(builder.Configuration.GetSection("Brand"));
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection("Anthropic"));
builder.Services.Configure<CompanyOptions>(builder.Configuration.GetSection("Company"));

builder.Services.AddHttpClient<AnthropicClient>()
    .AddStandardResilienceHandler();

builder.Services.AddSingleton<SystemPromptBuilder>();
builder.Services.AddScoped<LeadSink>();

builder.Services.AddOpenApi();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseStaticFiles();

app.MapStaticAssets(); // .NET 9 fingerprinted static asset map
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.MapChatEndpoint();   // extension method, defined in Endpoints/ChatEndpoint.cs
app.MapLeadEndpoint();
app.MapOpenApi();

app.Run();
```

## appsettings.json (shape)

```jsonc
{
  "Brand": {
    "Stats": {
      "ActiveProjects": 47,
      "AgentsInProduction": 12,
      "ResponseTimeHours": 4
    },
    "Marquee": [
      "Agentic AI", "LLM-native", "Multimodal",
      "Edge inference", "Vector-search", "Autonomous workflows"
      // … keep all 40 from the prototype, but here so they're editable
    ]
  },
  "Company": {
    "LegalName": "Soratus B.V.",
    "Kvk": "68752326",
    "Email": "hallo@soratus.com",
    "Country": "Nederland"
  },
  "Anthropic": {
    "BaseUrl": "https://api.anthropic.com",
    "Model": "claude-haiku-4-5",
    "MaxTokens": 1024,
    "ApiKey": "" // set via Azure App Service config in prod
  }
}
```

`appsettings.Development.json` mirrors this with a developer key (or use `dotnet user-secrets`).

## App.razor

The root document. Loads fonts, tokens, app CSS, then the page:

```razor
<!DOCTYPE html>
<html lang="nl">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Soratus — Time changing software</title>
    <base href="/" />

    <link rel="icon" type="image/png" sizes="32x32" href="/brand/favicon-32.png" />
    <link rel="icon" type="image/png" sizes="16x16" href="/brand/favicon-16.png" />
    <link rel="apple-touch-icon" sizes="180x180" href="/brand/favicon-180.png" />

    <link rel="preconnect" href="https://fonts.googleapis.com" />
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
    <link href="https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@300;400;500;600;700&family=JetBrains+Mono:wght@400;500;600&family=Instrument+Serif:ital@0;1&family=Sora:wght@200;300;400;500&display=swap" rel="stylesheet" />

    <link rel="stylesheet" href="@Assets["css/tokens.css"]" />
    <link rel="stylesheet" href="@Assets["css/app.css"]" />
    <link rel="stylesheet" href="@Assets["css/reveal.css"]" />

    <HeadOutlet />
</head>
<body>
    <Routes />
    <script src="_framework/blazor.web.js" autostart="false"></script>
    <script>Blazor.start();</script>
    <script type="module" src="@Assets["js/soratus.js"]"></script>
</body>
</html>
```

`@Assets["…"]` uses .NET 9's MapStaticAssets fingerprinting so the browser cache never serves stale CSS after a deploy.

## Static JS — keep it tiny

Everything in `wwwroot/js/` is plain ES modules, no bundler, no npm. The grand total should be < 8 KB minified across all five files. `soratus.js` is the only public entry point:

```js
// wwwroot/js/soratus.js
import { initReveal }     from './reveal.js';
import { initNeuralMesh } from './neural-mesh.js';
import { initCodestrip }  from './codestrip.js';

window.soratus = {
  init() {
    initReveal();
    initNeuralMesh(document.getElementById('orbStage'));
    initCodestrip(document.getElementById('terminal'));
  }
};

// auto-init on first load (static SSR pages don't have OnAfterRender)
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => window.soratus.init());
} else {
  window.soratus.init();
}
```

No bundler means no source maps and no tree-shaking, but the modules are tiny and you keep the dev loop trivial. If the JS footprint ever grows past ~30 KB, introduce esbuild — not before.

## CSS isolation strategy

- **Global tokens** in `wwwroot/css/tokens.css` (loaded in `App.razor`, declared on `:root`).
- **Global layout/utilities** in `wwwroot/css/app.css` — port the prototype's `<style>` block here verbatim, organized by the same `/* ─── Section ─── */` comment markers.
- **Scoped tweaks** in `Component.razor.css` when a single component needs a one-off override. Don't put core tokens here — they belong in the global file so other components can reach them.

## Routing

Single route: `/`. The page is one long scroll. Don't add anchor-based routing or sub-pages until the user asks.

## What lives outside Blazor

The build artifact is a single ASP.NET Core process. No separate API project, no Azure Functions, no Static Web Apps frontend. If you find yourself wanting to add one, ask first — the surface is small enough that monolithic is correct.
