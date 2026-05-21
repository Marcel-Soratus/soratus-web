# 03 · Components

Build in this order — leaf first, page last. Each spec gives you props, markup shape, and the lines of the prototype to mirror.

## Atoms (`Components/Atoms/`)

### `BrandMark.razor`

The 3-dot mark, scaled to the requested size.

```razor
@code {
    [Parameter] public int Size { get; set; } = 32;
    [Parameter] public string? Mono { get; set; }  // hex; null = brand colors
}
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32"
     width="@Size" height="@Size" role="img" aria-label="Soratus">
    <circle cx="6"  cy="26" r="3" fill="@(Mono ?? "#2A2FCC")"/>
    <circle cx="16" cy="16" r="4" fill="@(Mono ?? "#5C82FF")"/>
    <circle cx="26" cy="6"  r="5" fill="@(Mono ?? "#34E27A")"/>
</svg>
```

Use this inline rather than referencing the SVG file when it's part of UI chrome (nav, footer). Reference the file (`<img src="/brand/logo-light.svg">`) for OG images, og:image meta, social cards.

### `Wordmark.razor`

```razor
@code {
    [Parameter] public int MarkSize { get; set; } = 32;
    [Parameter] public string TextColor { get; set; } = "var(--ink)";
}
<a href="/" class="brand" aria-label="Soratus home">
    <BrandMark Size="@MarkSize" />
    <span class="brand-name" style="color: @TextColor">soratus</span>
</a>
```

`.brand` and `.brand-name` styles port from the prototype (lines 81–86 of `index.html`).

### `Button.razor`

```razor
@code {
    [Parameter] public string Variant { get; set; } = "default"; // default | primary | glow
    [Parameter] public string? Href { get; set; }
    [Parameter] public EventCallback OnClick { get; set; }
    [Parameter] public bool ShowDot { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? Attrs { get; set; }
}
@{
    var cls = $"btn btn-{Variant}";
}
@if (Href is not null) {
    <a href="@Href" class="@cls" @attributes="Attrs">
        @if (ShowDot) { <span class="dot"></span> }
        @ChildContent
    </a>
} else {
    <button class="@cls" @onclick="OnClick" @attributes="Attrs">
        @if (ShowDot) { <span class="dot"></span> }
        @ChildContent
    </button>
}
```

Three variants:
- `default` — outlined, 1px `--line-strong`, transparent background.
- `primary` — solid `--ink` background, dark text, drop shadow.
- `glow` — outlined + the conveyor-belt sheen animation on hover. Used for the chat CTAs.

The `glow` variant has a pseudo-element doing a slow horizontal gradient pan (prototype lines ~107–122). Port it verbatim.

### `Pip.razor`

A colored dot with a glow halo; used in the hero meta row and float-cards.

```razor
@code {
    [Parameter] public string Color { get; set; } = "var(--blue-2)";
    [Parameter] public bool Pulse { get; set; } = true;
}
<span class="pip @(Pulse ? "pip-pulse" : "")"
      style="background:@Color; box-shadow: 0 0 8px @Color"></span>
```

### `SectionHeader.razor`

The recurring "01 · Wat we doen" + H2 + aside layout.

```razor
@code {
    [Parameter] public string Tag { get; set; } = ""; // "01 · Wat we doen"
    [Parameter] public RenderFragment? Title { get; set; }
    [Parameter] public string? Aside { get; set; }
}
<div class="sec-head reveal">
    <div>
        <div class="sec-tag">@Tag</div>
        <h2 class="sec-title">@Title</h2>
    </div>
    @if (Aside is not null) {
        <p class="sec-aside">@Aside</p>
    }
</div>
```

`Title` is a `RenderFragment` so callers can embed `<span class="italic">` inside without HTML-encoding pain.

### `Eyebrow.razor`

The year-badge + tagline pill in the hero.

```razor
@code {
    [Parameter] public string Year { get; set; } = DateTime.UtcNow.Year.ToString();
    [Parameter] public string Tagline { get; set; } = "De toekomst van programmeren · gebouwd in Nederland";
}
<div class="eyebrow reveal">
    <span class="badge">@Year</span>
    <span>@Tagline</span>
</div>
```

## Layout (`Components/Layout/`)

### `MainLayout.razor`

Wraps every page. Holds the `<div class="field">` background, the noise layer, the nav, the footer, and a `@Body` slot for the page content.

```razor
@inherits LayoutComponentBase
<div class="field" aria-hidden="true"></div>
<div class="noise" aria-hidden="true">
    <svg xmlns="http://www.w3.org/2000/svg">
        <filter id="n">
            <feTurbulence type="fractalNoise" baseFrequency="0.9" numOctaves="2" stitchTiles="stitch"/>
            <feColorMatrix values="0 0 0 0 1  0 0 0 0 1  0 0 0 0 1  0 0 0 0.6 0"/>
        </filter>
        <rect width="100%" height="100%" filter="url(#n)"/>
    </svg>
</div>

<main>
    <TopNav />
    @Body
    <Footer />
</main>

<ChatWidget @rendermode="InteractiveServer" />
```

The chat widget is rendered here, **once**, so it floats above any page. Its render mode is the only interactive island in the app.

### `TopNav.razor`

Sticky, blur-backed nav. Port lines 668–688 of the prototype. Add a mobile sheet for ≤720px (the prototype doesn't have one).

```razor
<nav class="top">
    <div class="wrap nav-row">
        <Wordmark />
        <div class="nav-links">
            <a href="#wat">Wat we doen</a>
            <a href="#hoe">Hoe</a>
            <a href="#branches">Branches</a>
            <a href="#bewijs">Bewijs</a>
            <a href="#contact">Contact</a>
        </div>
        <div class="nav-cta">
            <Button Variant="glow" onclick="soratus.openChat()" ShowDot="true">
                Praat met onze AI
            </Button>
        </div>
        <button class="nav-burger" aria-label="Open menu"
                onclick="soratus.openNavSheet()">
            <span></span><span></span><span></span>
        </button>
    </div>
</nav>

<NavSheet />  @* sliding mobile menu, see below *@
```

For the mobile sheet: a `position: fixed; inset: 0` panel that slides in from the right, contains the same five links at large body type and one full-width CTA. Implementation is a plain CSS-only `<dialog>` with `:target` toggling — no Blazor interactivity required.

### `Footer.razor`

```razor
@inject IOptions<CompanyOptions> Company
<footer>
    <div class="wrap foot">
        <div class="foot-brand">
            <BrandMark />
            <span class="brand-name">soratus</span>
            <span class="foot-divider"></span>
            <span class="foot-slogan">
                <span class="foot-slogan-em">time</span><span> changing software</span>
                <span class="foot-slogan-cursor" aria-hidden="true"></span>
            </span>
        </div>
        <div>
            © @DateTime.UtcNow.Year @Company.Value.LegalName ·
            KvK @Company.Value.Kvk · gebouwd in @Company.Value.Country
        </div>
    </div>
</footer>
```

The cursor blink is a CSS animation (`@keyframes footBlink`); port from the prototype.

## Sections (`Components/Sections/`)

### `Hero.razor`

Pulls in:
- `Eyebrow`
- The `.h1` headline with the strike-through pen animation
- The lede + CTA row
- The meta-row of three pips
- The neural-mesh SVG visual (right side)
- The codestrip and three float-cards overlaid on the visual

Port lines 695–757 of the prototype verbatim. Bind the meta-row numbers:

```razor
@inject IOptions<BrandOptions> Brand
…
<div class="meta-row">
    <Pip Color="var(--blue-2)" /> @Brand.Value.Stats.ActiveProjects actieve projecten
    <Pip Color="var(--green)" /> @Brand.Value.Stats.AgentsInProduction AI-agents in productie
    <Pip Color="var(--warn)" /> Antwoord &lt; @Brand.Value.Stats.ResponseTimeHours uur
</div>
```

The neural-mesh visual is driven by `wwwroot/js/neural-mesh.js`. The component just renders the `<svg id="orbStage">` container — the script fills in nodes, edges, pulses, and animates the sweep.

### `Marquee.razor`

```razor
@inject IOptions<BrandOptions> Brand
<div class="marquee" aria-hidden="true">
    <div class="marquee-track">
        @* doubled so the keyframes -50% loop is seamless *@
        @foreach (var term in Brand.Value.Marquee.Concat(Brand.Value.Marquee))
        {
            <span>@term <span class="star">✦</span></span>
        }
    </div>
</div>
```

The doubled loop is what makes the CSS-only marquee seamless. Keep it.

### `WhatWeDo.razor`

Six cards in a bento grid. Spans:
- `c-1` (AI Agents) — spans 2 columns, contains a terminal mock with typewriter
- `c-2` (Custom Software) — single col, pill set
- `c-3` (Snelheid) — single col, big "14d" stat
- `c-4` (Integratie) — single col
- `c-5` (Privacy) — single col
- `c-6` (CTA strip) — spans full row

Grid:
```css
.bento {
    display: grid;
    grid-template-columns: repeat(3, 1fr);
    gap: 1px;
    background: var(--line);
    border: 1px solid var(--line);
    border-radius: var(--radius-lg);
    overflow: hidden;
}
.bento .c-1 { grid-column: span 2; }
.bento .c-6 { grid-column: span 3; }
```

Terminal typewriter lines come from `wwwroot/js/codestrip.js`. Port the prototype lines.

### `HowWeWork.razor`

Four-step flow. Port lines 911–949. Static markup, no interactivity. Glyphs:
- Step 1 → `→`
- Step 2 → `◊`
- Step 3 → `⊕`
- Step 4 → `✓`

These are Unicode, not SVG. Don't replace with icons.

### `Branches.razor`

8 cells (4×2 grid). Port lines 950–1009.

Default copy:
1. Retail & E-commerce
2. Productie & Industrie
3. Financieel & Accounting
4. Logistiek & Transport
5. Zorg & Gezondheid
6. Bouw & Vastgoed
7. Juridisch & HR
8. Jouw branche → vraag de AI

Card #8 has special copy ("Jouw branche") and on click opens the chat with a pre-filled message: `"Wat zou Soratus kunnen doen in mijn branche?"`. Use a JS interop call:

```razor
<a class="branche" @onclick="@(() => JS.InvokeVoidAsync("soratus.openChatWith", "Wat zou Soratus kunnen doen in mijn branche?"))">…</a>
```

### `Testimonials.razor`

Carousel of 7 quote cards. Port lines 1080–1175. Each card has:
- A `<blockquote>` with one `<em>` highlight (the em gets the gradient text treatment).
- A `qmeta` row: gradient disc (no initials), role + industry, success chip.

**Important:** the cards intentionally carry no names. Don't add author names — even fake ones. The disc, role, and industry are the entire identifier.

Carousel logic (prev/next + dots + auto-advance every 8s) is in `wwwroot/js/testimonials.js`. The Razor side just renders all 7 cards with the active one having class `active`.

### `Clients.razor`

6 logo cells in a 4-up grid (2 wrap on second row). Min height 200px so the row matches the branches grid above. Each cell:
- `cli-name` (the company name)
- `cli-sub` (subtitle line)
- `gov-badge` (category pill that fades in on hover only)

```razor
@code {
    record Client(string Name, string Sub, string Badge, string Style);
    Client[] _clients =
    [
        new("Tweede Kamer",  "der Staten-Generaal", "Overheid",    "italic-serif"),
        new("Eerste Kamer",  "der Staten-Generaal", "Overheid",    "italic-serif"),
        new("Brunel",        "technical staffing",  "Detachering", "sora"),
        // … keep the existing roster from the prototype
    ];
}
<div class="clients reveal">
    <div class="clients-tag">In vertrouwen gebouwd voor</div>
    <div class="clients-row">
        @foreach (var c in _clients)
        {
            <div class="cli cli-@c.Style">
                <span class="gov-badge">@c.Badge</span>
                <div class="cli-name">@c.Name</div>
                <div class="cli-sub">@c.Sub</div>
            </div>
        }
    </div>
</div>
```

### `FinalCta.razor`

The closing block ("Praat nu met onze AI"). Port lines 1180–1200.

## Chat (`Components/Chat/`)

See `05-backend.md` for the wire protocol. Component shapes:

### `ChatLauncher.razor`

The bottom-right floating button with a pulse halo. Always rendered (because the parent is interactive). Clicking sets `_open = true` on the parent.

### `ChatWidget.razor` *(the only `@rendermode InteractiveServer` component)*

```razor
@rendermode InteractiveServer
@inject AnthropicClient Anthropic
@inject IJSRuntime JS

<div class="chat-window @(IsOpen ? "open" : "")" aria-hidden="@(!IsOpen)">
    <div class="chat-head">
        <div class="agent">
            <div class="agent-avatar">
                @* sun icon *@
            </div>
            <div>
                <div class="agent-name">SORA · AI Agent</div>
                <div class="agent-status">● online · antwoordt direct</div>
            </div>
        </div>
        <button class="chat-close" @onclick="Close" aria-label="Sluit chat">…</button>
    </div>

    <div class="chat-body" @ref="_bodyRef">
        @foreach (var turn in _turns) {
            <ChatBubble Turn="turn" />
        }
        @if (_streaming) {
            <ChatBubble Turn="_partial" Streaming="true" />
        }
    </div>

    <Suggestions Visible="!_hasSent" OnPick="QuickPick" />

    <div class="chat-foot">
        <input @bind="_draft" @bind:event="oninput"
               @onkeyup="OnKey"
               placeholder="Vraag me iets over Soratus…"
               autocomplete="off" />
        <button class="chat-send" @onclick="Send" aria-label="Verstuur">…</button>
    </div>
    <div class="chat-tos">aangedreven door soratus · claude-haiku-4.5</div>
</div>

<ChatLauncher OnClick="Toggle" />
```

State to track:
- `bool IsOpen`
- `List<ChatTurn> _turns` — both user + assistant
- `string _draft`
- `bool _streaming` — used to show the typing indicator
- `ChatTurn _partial` — the assistant turn being streamed
- `bool _hasSent` — drives whether the suggestion chips show

The "leadcapture" suggestion (`Laat me terugbellen →`) routes to a small inline form, not Claude. Render an inline `<LeadForm />` inside the chat panel when the user clicks it.

### `ChatBubble.razor`

```razor
@code {
    [Parameter] public ChatTurn Turn { get; set; } = default!;
    [Parameter] public bool Streaming { get; set; }
}
<div class="bubble bubble-@Turn.Role">
    <div class="bubble-content">
        @((MarkupString) Turn.Html)
        @if (Streaming) { <span class="caret"></span> }
    </div>
</div>
```

Trust Claude's output to be plain text or simple Markdown; render via a Markdown-to-HTML pass (Markdig is fine here — add it only when this component is built).

### `Suggestions.razor`

Four chips. Three send a literal prompt; the fourth opens the lead form.

```razor
@code {
    [Parameter] public bool Visible { get; set; }
    [Parameter] public EventCallback<string> OnPick { get; set; }
}
@if (Visible)
{
    <div class="chat-suggestions">
        <button class="sugg" @onclick="@(() => OnPick.InvokeAsync("Wat kost een AI agent?"))">Wat kost een AI agent?</button>
        <button class="sugg" @onclick="@(() => OnPick.InvokeAsync("Kunnen jullie iets met mijn ERP?"))">Kunnen jullie iets met mijn ERP?</button>
        <button class="sugg" @onclick="@(() => OnPick.InvokeAsync("Hoe snel kan iets live staan?"))">Hoe snel kan iets live staan?</button>
        <button class="sugg sugg-lead" @onclick="@(() => OnPick.InvokeAsync("__lead"))">Laat me terugbellen →</button>
    </div>
}
```

## A11y notes per component

- All buttons have an accessible name (visible text or `aria-label`).
- The chat window uses `role="dialog"` and `aria-modal="false"` (it doesn't trap focus).
- Decorative SVGs use `aria-hidden="true"`; the brand mark uses `role="img"` + `aria-label="Soratus"`.
- Focus is trapped *within* the chat panel only while it has an active conversation, not on initial open.
- All animated decoration honors `prefers-reduced-motion`. The reveal-on-scroll skips its tween and shows the final state immediately under reduced motion.
