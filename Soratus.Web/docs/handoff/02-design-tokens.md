# 02 · Design Tokens

Every token below ports 1:1 into `wwwroot/css/tokens.css`. No new values, no rounded approximations, no "let me just use Tailwind colors". If a component needs a value not listed here, add it to this file in the appropriate section, commit it, and reference it from the component.

## Colors

### Surfaces (dark canvas, ~95% of UI)

| Token | Hex | Role |
|---|---|---|
| `--bg` | `#13162a` | Page canvas |
| `--bg-2` | `#1a1e36` | Card hover, chat panel |
| `--ink` | `#f4f5fb` | Primary text |
| `--ink-dim` | `#a8aec6` | Body / secondary text |
| `--ink-mute` | `#6b7290` | Meta, captions, mono labels |
| `--line` | `rgba(255,255,255,0.09)` | Hairlines |
| `--line-strong` | `rgba(255,255,255,0.2)` | Borders, button outlines |

### Brand (used sparingly — for accent, not fill)

| Token | Hex | Role |
|---|---|---|
| `--navy` | `#1B1F8C` | Deep accent, mark dot 1 |
| `--navy-2` | `#2A2FCC` | Mid-navy, gradients |
| `--blue` | `#2B5BFF` | Primary brand blue |
| `--blue-2` | `#5C82FF` | Mark dot 2, soft brand |
| `--green` | `#34E27A` | The signal color — CTAs, success, all "live" indicators |
| `--green-2` | `#9DF7BD` | Hover glow on green |
| `--warn` | `#FFD86B` | Latency / warning pip only |

### Gradients

| Token | Value |
|---|---|
| `--grad` | `linear-gradient(135deg, #2B5BFF 0%, #34E27A 100%)` |
| `--grad-2` | `linear-gradient(135deg, #1B1F8C 0%, #2B5BFF 50%, #34E27A 100%)` |

Use `--grad` for the H1 "intelligentie" sheen and the testimonial `<em>` highlights. Use `--grad-2` for the brand mark on splash screens (large size only). Never use either as a button background or full-section fill — they read as 2010s SaaS.

### Light-mode (not used today, but reserved)

| Token | Hex |
|---|---|
| `--light-bg` | `#f6f7fb` |
| `--light-ink` | `#0a0d1a` |

These exist so the brand assets and email signatures stay coherent. The website itself is dark-only.

## Typography

Four faces, no exceptions:

| Family | Source | Used for |
|---|---|---|
| **Sora** | Google Fonts, weights 200/300/400/500 | The "soratus" wordmark only. 200 weight, -0.04em tracking. |
| **Space Grotesk** | Google Fonts, 300–700 | Default UI. H2/H3, buttons, body. Variable axis is fine if available. |
| **Instrument Serif** | Google Fonts, regular + italic | Display italics in headlines (`<span class="italic">`), testimonial blockquotes, the marquee. Always italic, never roman. |
| **JetBrains Mono** | Google Fonts, 400–600 | All meta: kickers, eyebrows, section tags, status pills, the terminal mock, footer chrome. Tracking +0.04em to +0.14em depending on context. |

### Single `<link>` to rule them all

```html
<link href="https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@300;400;500;600;700&family=JetBrains+Mono:wght@400;500;600&family=Instrument+Serif:ital@0;1&family=Sora:wght@200;300;400;500&display=swap" rel="stylesheet">
```

`display=swap` is mandatory — never let the page block on font load.

### Type scale

```css
--text-meta:   12px;  /* JetBrains Mono, uppercase, +0.12em tracking */
--text-eyebrow:13px;  /* eyebrow pill */
--text-body:   15px;  /* card paragraphs */
--text-lede:   clamp(17px, 1.5vw, 21px);  /* hero lede */
--text-h4:     22px;  /* step / branche card titles */
--text-h3:     28px;  /* bento card titles */
--text-h2:     clamp(40px, 5.5vw, 76px);  /* section titles */
--text-h1:     clamp(56px, 7vw, 112px);   /* hero H1 */
--text-wordmark: 30px;
```

Tracking rules:
- Headlines (H1–H3): `letter-spacing: -0.02em` to `-0.035em` (tighter as size grows).
- Mono meta: `letter-spacing: 0.04em` to `0.14em`.
- The wordmark: `letter-spacing: -0.04em` (fixed).

### Italics

Italics are a deliberate punctuation device. Use `<span class="italic">` (Instrument Serif) inside an otherwise Space Grotesk headline to emphasize one word. **One italic word per heading, maximum.** Two becomes noise.

## Spacing

```css
--space-1: 4px;
--space-2: 8px;
--space-3: 14px;
--space-4: 20px;
--space-5: 28px;
--space-6: 40px;
--space-7: 56px;
--space-8: 88px;
--space-9: 120px;  /* section padding-y default */
```

Section vertical rhythm: `padding: var(--space-9) 0` by default; `--space-8` when sections sit adjacent to a divider (branches → clients).

## Radii

```css
--radius-sm: 8px;    /* pills inside cards */
--radius:    18px;   /* cards, chat bubbles */
--radius-lg: 28px;   /* big section cards (bento, flow, branches) */
--radius-pill: 999px;
```

## Shadows + glows

```css
--shadow-card:   0 8px 30px rgba(0, 0, 0, 0.3);
--shadow-btn:    0 8px 30px rgba(255, 255, 255, 0.18); /* primary button */
--glow-green:    0 0 12px var(--green);
--glow-green-lg: 0 0 40px rgba(52, 226, 122, 0.45);
--glow-blue:     0 0 12px var(--blue-2);
--glow-warn:     0 0 8px var(--warn);
```

## Motion

```css
--ease:       cubic-bezier(.6, .05, .3, 1);    /* default */
--ease-out:   cubic-bezier(.2, .8, .2, 1);     /* settle */
--ease-snap:  cubic-bezier(.7, 0, .2, 1);      /* CTA */

--dur-instant: 120ms;
--dur-fast:    200ms;
--dur:         350ms;
--dur-slow:    600ms;
--dur-reveal:  850ms;
```

### Named animations

| Name | Duration | Where |
|---|---|---|
| `pulse` | 1.6s infinite | The live dot on CTAs |
| `blink` | 1s steps(2) infinite | Terminal caret, footer slogan cursor |
| `sheen` | 6s linear infinite | H1 "intelligentie" gradient text |
| `scroll` | 120s linear infinite | Marquee |
| `spin` | configurable | Background dashed rings |
| `float` | 4–6s ease-in-out | Hero float-cards |
| `reveal` (custom) | 850ms `--ease` | IntersectionObserver-driven entrance for `.reveal` elements |
| `strikethrough` (custom) | 850ms `--ease` 250ms delay | The H1 `.strike` pen-draw |

All custom animations sit in `wwwroot/css/reveal.css`. Keep `prefers-reduced-motion: reduce` honored everywhere:

```css
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
  }
}
```

## Breakpoints

```css
--bp-xs: 480px;
--bp-sm: 600px;
--bp-md: 720px;
--bp-lg: 980px;
--bp-xl: 1240px;
```

Layout shifts:
- `bento` grid: 3 cols ≥ 980px, 2 cols < 980px, 1 col < 600px.
- `flow` (steps) grid: 4 cols ≥ 720px, 1 col < 720px.
- `branches` grid: 4 cols ≥ 980px, 2 cols < 980px, 1 col < 600px.
- `nav-links`: visible ≥ 720px, replaced by a sheet ≤ 720px (build this — the prototype doesn't have it yet; see `03-components.md` § TopNav).

## Layout container

```css
.wrap { max-width: 1440px; margin: 0 auto; padding: 0 32px; }
@media (max-width: 720px) { .wrap { padding: 0 20px; } }
```

Don't introduce a second wrapper width. Cards inside `.wrap` use their own `max-width` only when they need to (e.g. testimonial `<blockquote>` at `max-width: 24ch`).

## Z-index

```css
--z-field:    0;
--z-noise:    1;
--z-content:  2;
--z-nav:     50;
--z-chat:    80;
--z-modal:   90;
```

Don't free-style z-index values. If you need a new layer, add it here.
