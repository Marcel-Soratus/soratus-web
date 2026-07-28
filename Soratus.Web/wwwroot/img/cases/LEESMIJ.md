# Screenshots voor de case-pagina's

De zes afbeeldingen staan er. Ze zijn niet met de hand gemaakt maar opgenomen
door de MBV-demo zelf te doorlopen, zodat ze uniform zijn en herhaalbaar.

Alle zes zijn 1400x1059 (verhouding 1.32). Die verhouding staat als
`--shot-ratio` in `wwwroot/css/cases.css`. Vervang je een afbeelding door een
met een andere verhouding, normaliseer dan opnieuw en werk dat getal bij.

| Bestandsnaam | Wat erop staat |
|---|---|
| `declaraties-betalingen.png` | Beginstand: ontvangen betalingen 2025 |
| `declaraties-matching.png` | Matchingresultaat met KPI-tegels, filters en bevindingen |
| `declaraties-agent.png` | Afhandeling door AI-agent: 6 van 8 automatisch, 2 ter beoordeling |
| `jaarverslag-start.png` | Startscherm: chat links, leeg rapport-paneel rechts |
| `jaarverslag-rapport.png` | Opgesteld bestuursverslag naast de samenvatting in de chat |
| `jaarverslag-kengetallen.png` | Kengetallentabel met de formule per ratio |

## Opnieuw maken

Start eerst beide MBV-projecten, want de web-app heeft de API nodig:

```
dotnet run --project D:/SORATUS/MBV/MBV.Web/MBV.Web.csproj --launch-profile https
dotnet run --project D:/SORATUS/MBV/MBV.Api/MBV.Api.csproj --launch-profile https
```

Dan, vanuit de root van deze repo:

```
node handoff/cases/maak-screenshots.mjs              # jaarverslag + beginstand declaraties
node handoff/cases/maak-screenshots-declaraties.mjs  # declaraties met upload en agent
python handoff/cases/normaliseer-screenshots.py --breedte 1400
```

De scripts sturen een eigen headless Chrome aan via het DevTools-protocol en
snijden bij tot de app-container, zodat er geen loze marge omheen staat.

## Let op bij herhalen

De agent is niet deterministisch: de zekerheidsscores verschillen per run. Op de
case-pagina staan nu 98 procent (dubbele declaratie) en 58 procent (laagste
vraag) genoemd. Wijk je daarvan af, pas dan de tekst in
`Models/CaseStudy.cs` aan, anders spreken tekst en afbeelding elkaar tegen.

## Privacy

De namen in `declaraties-matching.png` komen uit het voorbeeldbestand van de app
zelf (`/api/declaraties/voorbeeldbestand`), dus het zijn synthetische demonamen
en geen echte verzekerden. De klantnaam staat nergens in beeld: de rapportvoet
met "Mijn MBV" valt buiten de uitsnede. De demo draait op "Demo
Handelsonderneming B.V." met een zichtbare Demo-cijfers-badge.
