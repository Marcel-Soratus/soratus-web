# Screenshots voor de case-pagina's

Zet de afbeeldingen hier neer met exact de naam uit de tabel. De pagina pikt ze
vanzelf op: geen code-wijziging nodig, alleen de pagina verversen. Zolang een
bestand ontbreekt, toont de pagina een placeholder met de verwachte naam erin.

## Declaraties — /cases/snelstart-declaraties-matchen

| Bestandsnaam | Welke afbeelding |
|---|---|
| `declaraties-betalingen.png` | Beginstand: ontvangen betalingen 2025 |
| `declaraties-matching.png` | Matchingresultaat met KPI-tegels, filters en bevindingen |
| `declaraties-agent.png` | "Afhandeling door AI-agent" met voorstellen en zekerheidsscores |

## Jaarverslag — /cases/snelstart-jaarverslag-agent

| Bestandsnaam | Welke afbeelding |
|---|---|
| `jaarverslag-start.png` | Startscherm: chat links, leeg rapport-paneel rechts |
| `jaarverslag-rapport.png` | Opgesteld bestuursverslag naast de samenvatting in de chat |
| `jaarverslag-kengetallen.png` | Kengetallentabel met de toelichting per ratio |

## Voor publicatie nog checken

- **`jaarverslag-kengetallen.png`** bevat onderaan de tekst "Opgesteld met
  demo-cijfers · Mijn MBV". Dat verklapt de klant, terwijl de case anoniem is.
  Wegsnijden of onleesbaar maken.
- **`declaraties-matching.png`** bevat namen van verzekerden (M. Jansen,
  P. de Boer en verder). Ze ogen als gegenereerde demonamen en er staat een
  DEMO-badge bij, maar dit is zorgcontext. Bevestig dat het synthetische namen
  zijn voordat dit publiek gaat.

## Praktisch

- Formaat: PNG. Breedte rond 1400px is ruim voldoende; de kaders zijn ongeveer
  700px breed en tonen de afbeelding met `object-fit: cover`.
- Vervang je later een bestand, dan ververst de browser vanzelf: de pagina hangt
  de laatste schrijftijd als `?v=` achter de URL.
- Dit LEESMIJ-bestand mag blijven staan, het wordt niet uitgeleverd als pagina.
