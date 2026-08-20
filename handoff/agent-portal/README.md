# Agent Portal — ontwerpdossier

Ontwerp voor het Soratus Agent Portal (klant- en operatorportaal boven de agent-omgevingen). Alleen documentatie en een klikbare mockup; geen productiecode.

| Pad | Wat |
|---|---|
| `agent-portal-spec.md` | Functioneel ontwerp, rollen en zichtbaarheid, datamodel, integraties, styling-tokens, faseplan 0–6 |
| `handout.dc.html` | Printbare handout met een schermafbeelding per scherm (open in de browser, print naar PDF) |
| `mockup/Soratus Agent Portal.dc.html` | Interactieve mockup, dummy-data in het `DATA`-object bovenaan de logica |
| `mockup/support.js` | Runtime die de mockup nodig heeft |
| `screens/*.png` | Schermafbeeldingen, gebruikt door de handout |
| `doc-page.js` | Paginashell voor de handout |

Merk en tokens komen uit `Soratus.Web/wwwroot/css/tokens.css` en `Soratus.Web/wwwroot/brand/`, omgezet naar de lichte app-variant (`--light-bg` / `--light-ink`). Zie §8 van de spec voor de exacte waarden.

Voorgestelde plek in de repo: `Soratus.Web/docs/agent-portal/` (naast `docs/handoff/`).
