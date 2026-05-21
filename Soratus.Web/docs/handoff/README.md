# Soratus — Design → Blazor Handoff

This package converts the static prototype at `reference/index.html` into a production-ready **C# Blazor Web App on .NET 9**. Everything Claude Code needs to do the port is in this folder; read it in order, then start at `CLAUDE.md` for execution.

```
handoff/
├── README.md                  ← you are here
├── CLAUDE.md                  → instructions to drop into the Blazor repo root
├── reference/
│   ├── index.html             → the original prototype, source of truth for layout
│   └── app.js                 → animations, chat widget, reveal logic
├── brand/
│   ├── logo-mark.svg          → 32×32 mark only
│   ├── logo-light.svg         → mark + "soratus" wordmark, for dark backgrounds
│   ├── logo-dark.svg          → mark + wordmark, for light backgrounds
│   ├── logo-lockup-*.svg      → with tagline "time changing software"
│   ├── logo-mono-*.svg        → single-color variants (print, etched, embossed)
│   ├── favicon-*.png          → 16/32/64/180/512
│   ├── logo-square-*-256.png  → for email signatures, social, app tiles
│   └── preview.html           → visual index of every variant
├── 01-architecture.md         → solution layout, render modes, dependencies
├── 02-design-tokens.md        → colors, type, spacing, radii, motion
├── 03-components.md           → Razor component spec, prop signatures
├── 04-pages.md                → home page broken into sections + spec per section
├── 05-backend.md              → chat endpoint, lead capture, contact form
└── 06-deployment.md           → Azure App Service / Static Web Apps notes
```

## TL;DR for Claude Code

1. Stand up a **Blazor Web App (.NET 9)** with the **Interactive Server** render mode (Auto is fine if WASM bundle stays small; Server is simpler and matches the realtime feel of the chat).
2. Drop `CLAUDE.md` at the repo root.
3. Copy `brand/` into `wwwroot/brand/`.
4. Implement design tokens from `02-design-tokens.md` as a single `wwwroot/css/tokens.css` referenced from `App.razor`.
5. Build components in the order in `03-components.md` — leaf first, page last.
6. Wire the chat widget to a server endpoint that proxies to Anthropic's `claude-haiku-4-5` model (`05-backend.md`).
7. Replace the marquee's hard-coded keyword list and the "47 actieve projecten / 12 AI-agents / 4u response" pips with values from an `appsettings.json` `Stats` section so marketing can edit without a redeploy.

## What stays exactly the same

- Color tokens — every hex listed in `02-design-tokens.md` is law.
- Typography stack — Sora 200 for the wordmark, Space Grotesk for body, Instrument Serif (italic) for emphasis, JetBrains Mono for meta/code chrome.
- The hero "neural mesh" SVG visual, the marquee, the reveal-on-scroll choreography, and the strike-through-hand animation in the H1. These are visual signatures; port them faithfully.
- All Dutch copy. Don't translate, don't paraphrase.
- The chat widget UX — pulsing launcher, suggested prompts, streaming-feel responses, "powered by claude-haiku-4.5" footer.

## What MUST change in the port

| Prototype behaviour | Production requirement |
|---|---|
| `window.claude.complete()` (sandbox helper) | `POST /api/chat` on the Blazor server, proxying to Anthropic with a server-side API key. Never expose the key client-side. |
| Hardcoded testimonial copy | Move to a CMS-shaped `appsettings.json` section or a `Testimonials.cs` static list — names are intentionally absent, keep roles + industry only. |
| Stats in hero (`47 actieve projecten`, etc.) | Configurable through `appsettings.json` → `BrandStats`. |
| KvK in footer (`68752326`) | Already correct — wire into `appsettings.json` → `Company` for future-proofing. |
| Static `2026` badge | Computed: `DateTime.UtcNow.Year`. |
| Reveal-on-scroll via IntersectionObserver | Keep — pure JS, isolated in `wwwroot/js/reveal.js`, no Blazor interop needed. |
| Neural mesh animation, marquee, codestrip typewriter | Keep as JS modules in `wwwroot/js/`. Use `IJSRuntime.InvokeVoidAsync("soratus.init")` from the home page's `OnAfterRenderAsync`. |

## What to push back on (if Claude Code is tempted)

- **Do not** replace Space Grotesk / Sora / Instrument Serif with Inter, system-ui, or Roboto. The type stack IS the brand voice.
- **Do not** add filler sections, hero illustrations from stock, or icon libraries (Lucide, Heroicons, etc.). Every glyph in the prototype is either a tiny inline SVG, a Mono character (✓ ◊ ⊕ →), or a brand mark. Keep it that way.
- **Do not** add a cookie banner, GDPR drawer, or "we use cookies" toast unless the user explicitly asks. The site sets no tracking cookies in the prototype.
- **Do not** introduce Tailwind, Bootstrap, MudBlazor, or any UI kit. The CSS is hand-rolled with custom properties and is intentional. Port it 1:1 into scoped Razor stylesheets or a single `app.css`.

## Render mode decision

Use **Blazor Web App** template with:

```
dotnet new blazor -o Soratus.Web --interactivity Server --auth None
```

Reasons:
- The chat widget is the only interactive piece on the page; it benefits from SignalR streaming for the typewriter effect on Claude responses.
- WASM has no SEO benefit for the marketing site since `.razor` pages prerender server-side regardless of mode.
- Static SSR alone (without `@rendermode InteractiveServer`) is enough for the home page; only the `<ChatWidget />` needs interactivity. Mark just that one component.

```razor
@* Home.razor — most of the page is static SSR *@
<Hero />
<Marquee />
<WhatWeDo />
<HowWeWork />
<Branches />
<Testimonials />
<Clients />
<FinalCta />
<Footer />

@* ↓ the only interactive island *@
<ChatWidget @rendermode="InteractiveServer" />
```

This keeps first paint instant and the JS payload tiny.

## How "done" looks

A reviewer should be able to:
1. `dotnet run` and see the exact prototype at `localhost:5xxx` on first load.
2. Tab through the page with keyboard — focus rings visible on every CTA, no traps.
3. Open the chat, type a question, and stream a reply from Claude that knows it's representing Soratus.
4. Resize to 360px and have everything reflow gracefully.
5. View the Lighthouse report: ≥95 on Performance, 100 on Best Practices and SEO, ≥95 on Accessibility.

Read on in `01-architecture.md`.
