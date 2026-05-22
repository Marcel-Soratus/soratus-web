# OG image generator

Renders `Soratus.Web/wwwroot/brand/og-image.png` (1200×630) from a static
HTML template via headless Chrome/Edge.

## Files

- `og.html` — the design. Edit headline/tagline here.
- `render.ps1` — runs headless Chrome, writes the PNG.

## Regenerate

```pwsh
pwsh tools/og-image/render.ps1
```

Then commit the updated `Soratus.Web/wwwroot/brand/og-image.png`.

## Notes

- Fonts come from Google Fonts (Sora, Space Grotesk, Instrument Serif,
  JetBrains Mono) — `--virtual-time-budget=4000` gives Chrome 4s to fetch
  them before snapshotting.
- The HTML matches the live site's palette (`#13162a` bg, `#34E27A` green,
  `#5C82FF` blue) so the social card feels native.
- LinkedIn aggressively caches OG images. After deploying a new image,
  paste the URL into <https://www.linkedin.com/post-inspector/> to force
  a re-scrape.
