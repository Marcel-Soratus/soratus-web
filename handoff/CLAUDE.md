# CLAUDE.md — Soratus Web (Blazor)

> Drop this file at the **root of the Blazor solution** alongside the `.sln`. Claude Code reads it on every session and treats it as standing orders.

## Project

`Soratus.Web` — the public marketing site for Soratus B.V. C# Blazor Web App on .NET 9, deployed to Azure App Service (Linux, P1V3). Repo is private. Branch model: `main` is live, feature branches via PR.

## Handoff source of truth

The full design package lives at `docs/handoff/` (copy the `handoff/` folder from the deliverable into `docs/`). Always defer to:

- `docs/handoff/reference/index.html` — original prototype. **This is the source of truth for layout, copy, and visual detail.** Open it in a browser whenever you're unsure how something should look.
- `docs/handoff/02-design-tokens.md` — exhaustive token list. Never invent a color, font, or radius that isn't here.
- `docs/handoff/03-components.md` — every Razor component, its props, and its responsibilities.

If a request would conflict with these docs, ask before changing them.

## Non-negotiable rules

1. **No UI kits.** Do not add Tailwind, Bootstrap, MudBlazor, Radzen, Syncfusion, Lucide, Heroicons, Font Awesome, or any similar library. CSS is hand-rolled. Icons are either small inline SVGs or monospace glyphs (✓ ◊ ⊕ →).
2. **No fonts other than the four declared.** Sora (wordmark), Space Grotesk (body), Instrument Serif (italic emphasis), JetBrains Mono (meta + code chrome). Load via the single `<link>` already in `App.razor`.
3. **No translation.** All copy is Dutch by intent. Don't rewrite it into English even when asked to "clean up". If copy needs editing, ask which Dutch phrasing to use.
4. **No tracking, analytics, or cookies** unless explicitly added by the user via a feature request.
5. **Server-side secrets only.** The Anthropic API key lives in `appsettings.Production.json` (set by Azure App Service config). Never reference `Anthropic__ApiKey` from a `.razor` file or any `wwwroot/js/*` file.
6. **Editable copy lives in `appsettings.json` under `Brand`.** When you see numbers like "47 actieve projecten" or "12 AI-agents in productie" in markup, refactor them to bind to `IOptions<BrandOptions>` instead of hard-coding.
7. **One render mode per island.** The site is mostly static SSR. Only `ChatWidget.razor` is `@rendermode InteractiveServer`. Don't promote other components to interactive without asking.
8. **Preserve direct-edit anchors.** Headings and CTA labels carry `data-comment-anchor` attributes in the prototype — port them across so review tooling keeps working.

## Where things live

```
Soratus.Web/
├── Components/
│   ├── Layout/
│   │   ├── MainLayout.razor          → wraps Pages with field, noise, nav, footer
│   │   ├── TopNav.razor
│   │   └── Footer.razor
│   ├── Sections/                     → one component per scroll section
│   │   ├── Hero.razor
│   │   ├── Marquee.razor
│   │   ├── WhatWeDo.razor
│   │   ├── HowWeWork.razor
│   │   ├── Branches.razor
│   │   ├── Testimonials.razor
│   │   ├── Clients.razor
│   │   └── FinalCta.razor
│   ├── Atoms/                        → reusable bits
│   │   ├── BrandMark.razor           → the three-dot SVG mark
│   │   ├── Wordmark.razor            → mark + "soratus" wordmark
│   │   ├── Button.razor              → btn / btn-primary / btn-glow
│   │   ├── Pip.razor                 → animated colored pip
│   │   ├── Eyebrow.razor             → year badge + tagline pill
│   │   └── SectionHeader.razor       → "01 · Wat we doen" + h2 + aside
│   ├── Chat/
│   │   ├── ChatWidget.razor          → @rendermode InteractiveServer
│   │   ├── ChatLauncher.razor        → bottom-right pulsing button
│   │   ├── ChatBubble.razor          → single message
│   │   └── Suggestions.razor         → 4 chip suggestions
│   └── Pages/
│       └── Home.razor                → composes Sections in order
├── Endpoints/
│   ├── ChatEndpoint.cs               → POST /api/chat (streaming)
│   └── LeadEndpoint.cs               → POST /api/lead (callback request)
├── Services/
│   ├── AnthropicClient.cs            → typed HttpClient wrapping Anthropic
│   ├── LeadSink.cs                   → writes to Cosmos / SQL / sendgrid
│   └── SystemPromptBuilder.cs        → composes Soratus persona
├── Models/
│   ├── BrandOptions.cs               → IOptions binding for appsettings
│   ├── Testimonial.cs
│   └── ChatTurn.cs
├── wwwroot/
│   ├── brand/                        → from handoff/brand/
│   ├── css/
│   │   ├── tokens.css                → :root custom properties
│   │   ├── app.css                   → everything else, ported from prototype <style>
│   │   └── reveal.css                → reveal-on-scroll keyframes
│   └── js/
│       ├── reveal.js                 → IntersectionObserver, adds .in to .reveal
│       ├── neural-mesh.js            → the hero SVG animation
│       ├── marquee.js                → (if scroll-anim needs JS; CSS is enough)
│       ├── codestrip.js              → hero terminal typewriter
│       └── soratus.js                → namespaced init: window.soratus.init()
├── appsettings.json                  → Brand, Company, Stats
├── appsettings.Development.json
├── Program.cs
└── Soratus.Web.csproj
```

## Build / run

```bash
dotnet restore
dotnet watch run --project Soratus.Web
```

Hot reload works for `.razor` and `.css`. Restart the host for `Program.cs` or `appsettings.json` changes.

## Testing

- **Visual regression:** keep `docs/handoff/reference/index.html` open side-by-side; pixel-compare after every section is ported.
- **Unit tests:** `Soratus.Web.Tests` (xUnit). Required coverage: `SystemPromptBuilder`, `AnthropicClient` retry/timeout logic, `LeadEndpoint` validation.
- **A11y:** run `axe-core` via Playwright before each PR. Zero criticals, ≤ 2 minor.

## Conventions

- C#: file-scoped namespaces, nullable enabled, `var` only when type is obvious. Use primary constructors for typed-options consumers.
- Razor: PascalCase components, kebab-case CSS classes. One component per file. CSS isolation (`Component.razor.css`) for component-specific overrides; shared tokens stay in `wwwroot/css/`.
- JS: ES modules. Each module exports `init()` and `dispose()`. The page calls `soratus.init()` once after the DOM is ready.

## Commit style

Conventional Commits, scoped by component name when relevant.
```
feat(hero): port neural-mesh visual
fix(chat): retry on 429 with exponential backoff
chore(tokens): add --line-strongest variant
```

## Don't do these without asking

- Bumping a NuGet major version.
- Changing the deployment target away from Azure App Service.
- Adding a CMS (Strapi, Sanity, Contentful). Copy lives in source until requested.
- Adding internationalization. The site is Dutch-only by intent.
- Replacing `claude-haiku-4-5` with another model.

## Where to push back

If the user asks for "more polish" or "make it pop", reach for one of:
1. A subtler animation curve.
2. A tighter type scale at the breakpoint that's bothering them.
3. More breathing room (vertical rhythm).
Do **not** reach for: gradients on more elements, more iconography, a "hero illustration", or a feature comparison table. The site's whole posture is restraint; respect it.
