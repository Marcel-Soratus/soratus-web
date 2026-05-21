# Soratus.Web

De publieke marketingsite voor Soratus B.V. — Blazor Web App op .NET 9, gedeployed naar Azure App Service.

## Snel starten

```bash
dotnet restore
dotnet watch run --project Soratus.Web
```

Open `http://localhost:5xxx`. Hot-reload werkt voor `.razor` en `.css`.

## Solution layout

```
Soratus.slnx
├── Soratus.Web/            ← deployable Blazor Web App
│   ├── Components/
│   │   ├── Atoms/          BrandMark, Wordmark, Button, Pip, Eyebrow, SectionHeader
│   │   ├── Layout/         MainLayout, TopNav, Footer
│   │   ├── Sections/       Hero, Marquee, WhatWeDo, HowWeWork, Branches, Testimonials, Clients, FinalCta
│   │   ├── Chat/           ChatWidget (Interactive Server), ChatLauncher, ChatBubble, Suggestions, LeadForm
│   │   └── Pages/          Home, Error, NotFound
│   ├── Endpoints/          ChatEndpoint (/api/chat SSE), LeadEndpoint (/api/lead)
│   ├── Services/           AnthropicClient, SystemPromptBuilder, LeadSink, JsonLd
│   ├── Models/             BrandOptions, CompanyOptions, AnthropicOptions, SendGridOptions, ChatTurn, Lead
│   ├── wwwroot/
│   │   ├── brand/          van handoff/brand/ — favicons, logo-varianten, og-image
│   │   ├── css/            tokens.css, app.css, reveal.css
│   │   ├── js/             reveal.js, neural-mesh.js, codestrip.js, testimonials.js, soratus.js (entry)
│   │   ├── robots.txt      AI-crawlers allowlisted (GPTBot, ClaudeBot, PerplexityBot, …)
│   │   ├── sitemap.xml
│   │   └── llms.txt        GEO — beknopt overzicht voor AI-search engines
│   ├── appsettings.json    Brand, Company, Anthropic, SendGrid configuratie
│   └── docs/handoff/       de complete design-handoff (read-only referentie)
└── Soratus.Web.Tests/      xUnit + bUnit
```

## Configuratie

`appsettings.json` houdt alle bewerkbare copy en stats. In productie wordt het volgende via Azure App Service config gezet:

| Setting | Bron (dev) | Bron (prod) |
|---|---|---|
| `Anthropic:ApiKey` | `dotnet user-secrets` | Key Vault reference |
| `SendGrid:ApiKey` | `dotnet user-secrets` | Key Vault reference |
| `Company:VatId` | leeg | App Service config |

```bash
cd Soratus.Web
dotnet user-secrets init
dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..."
dotnet user-secrets set "SendGrid:ApiKey" "SG...."
```

Tot er een API-key staat reageert de chatbot met een placeholder — de UI werkt al volledig.

## Architectuur

- **Render mode** — Static SSR voor alles, behalve `ChatWidget.razor` (`@rendermode InteractiveServer`). First paint is HTML; alleen de chat krijgt een SignalR-circuit.
- **Geen UI-kit** — CSS hand-rolled in `wwwroot/css/`. Sora / Space Grotesk / Instrument Serif / JetBrains Mono via één Google Fonts link.
- **Geen bundler** — JS zijn ES modules direct uit `wwwroot/js/`, < 8 KB minified totaal.
- **Endpoints** — `/api/chat` (SSE-stream van Claude), `/api/lead` (SendGrid → `hallo@soratus.com`). Beide rate-limited en zonder anti-forgery (worden uit eigen circuit/JSON fetch aangeroepen).
- **Security headers** — HSTS in prod, CSP, X-Frame-Options, Referrer-Policy, Permissions-Policy. Allemaal in `Program.cs`.

## SEO / GEO

- **JSON-LD** in `<head>`: Organization, WebSite, ProfessionalService, FAQPage.
- **Open Graph + Twitter card** meta.
- **Sitemap** + **robots.txt** met expliciete allow voor `GPTBot`, `ClaudeBot`, `PerplexityBot`, `Google-Extended`, `Applebot-Extended`.
- **`/llms.txt`** — beknopte sitebeschrijving voor AI-search engines (zie [llmstxt.org](https://llmstxt.org/)).
- **canonical, hreflang nl-NL, theme-color** — allemaal aanwezig in `App.razor` / `Home.razor`.

## Deploy

GitHub Actions workflow op `main` → Azure App Service Linux P1V3 in West Europe.

Benodigde GitHub secrets:

- `AZURE_CLIENT_ID` — federated identity (OIDC), geen client secret nodig
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

App Service config (in Azure portal):

```
Anthropic__ApiKey         → Key Vault reference
Anthropic__Model          → claude-haiku-4-5
SendGrid__ApiKey          → Key Vault reference
Company__VatId            → NL...
ASPNETCORE_ENVIRONMENT    → Production
WEBSITE_RUN_FROM_PACKAGE  → 1
```

Health probes: `/healthz` (App Service), `/readyz` (Front Door / pingt Anthropic).

## Wat is opzettelijk wel/niet gedaan

- **Wel:** alle copy uit de prototype 1:1 geporteerd, neural-mesh / codestrip / testimonials carrousel als plain JS, AVG-vriendelijk (geen tracking-cookies, geen analytics).
- **Niet:** Tailwind/Bootstrap/MudBlazor, een blog, meertaligheid (NL-only by design), GA4. Zie `docs/handoff/CLAUDE.md` voor de regels.

## Openstaand

- `Company:VatId` zetten zodra het BTW-nummer beschikbaar is.
- `wwwroot/brand/og-image.png` is nu een 256×256 fallback; vervang door een echte 1200×630.
- AzureDevOps NuGet-feed staat in user-level `NuGet.config`; deze repo overrideed met een eigen `NuGet.config` op solution-niveau dat alleen nuget.org gebruikt.
