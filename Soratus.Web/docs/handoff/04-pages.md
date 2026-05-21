# 04 · Pages

There is one page: `Pages/Home.razor` mapped to `/`. It is statically rendered (no `@rendermode`) and composes the sections in the exact order below.

```razor
@page "/"
@inject IOptions<BrandOptions> Brand

<PageTitle>Soratus — Time changing software</PageTitle>
<HeadContent>
    <meta name="description" content="Soratus is de AI-development partner voor MKB en enterprise. Wij leveren software die zichzelf verbetert, processen die zichzelf optimaliseren, en agents die het werk doen waar je vandaag nog mensen voor inhuurt." />
    <meta property="og:title" content="Soratus — Time changing software" />
    <meta property="og:description" content="AI-development partner. Agents, automation, integraties. Gebouwd in Nederland." />
    <meta property="og:image" content="/brand/og-image.png" />
    <meta property="og:type" content="website" />
    <meta property="og:locale" content="nl_NL" />
    <meta name="theme-color" content="#13162a" />
</HeadContent>

<Hero />
<Marquee />
<WhatWeDo />
<HowWeWork />
<Branches />
<Testimonials />
<Clients />
<FinalCta />
```

The footer is rendered by `MainLayout`; the chat widget too.

## Section anchors

| Section | id | Nav label |
|---|---|---|
| Hero | `top` | (logo links here) |
| What we do | `wat` | "Wat we doen" |
| How we work | `hoe` | "Hoe" |
| Branches | `branches` | "Branches" |
| Testimonials | `bewijs` | "Bewijs" |
| Final CTA | `contact` | "Contact" |

These IDs are referenced by the nav. Don't rename without updating both ends.

## Scroll behavior

- `html { scroll-behavior: smooth; }` — already in the prototype. Keep.
- Nav is sticky with `backdrop-filter: blur(18px)`. Test on Safari — the prefix is `-webkit-backdrop-filter`.
- The hero's neural-mesh visual auto-runs once on `DOMContentLoaded` (via `soratus.init()`). Don't wait for the user to scroll into it.

## SEO / structured data

Add the org JSON-LD inside `<HeadContent>`:

```html
<script type="application/ld+json">
{
  "@context": "https://schema.org",
  "@type": "Organization",
  "name": "Soratus B.V.",
  "url": "https://soratus.com",
  "logo": "https://soratus.com/brand/favicon-512.png",
  "address": { "@type": "PostalAddress", "addressCountry": "NL" },
  "vatID": "NL...",
  "taxID": "68752326"
}
</script>
```

Fill the VAT ID when the user provides it.

## OG image generation

Add an `og-image.png` to `wwwroot/brand/` (1200×630). Generate it once from a `OgImage.razor` page (`/og`) and screenshot it, or have a designer hand the file in. The page itself isn't routed for users — it's just there for `/og` to render so a PNG can be exported. **Don't** ship `OgImage.razor` mapped at `/og` in production unless rate-limited.

## Print stylesheet

Out of scope. Don't add a print stylesheet unless requested.

## Error pages

```
Pages/
├── Home.razor
├── _Error.razor   → 500
├── _NotFound.razor → 404
```

Both error pages reuse `MainLayout` and contain only a centered headline + return-home CTA, in the same type/tone as the rest of the site. No stack traces in production.
