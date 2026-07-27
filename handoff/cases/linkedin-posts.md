# LinkedIn-posts bij de twee SnelStart-cases

Geschreven in de Soratus-stem: geen em-dash, korte zinnen, één CTA. Klant blijft anoniem.
Geen verzonnen klantcijfers. Wat waar is: twee uur bouwtijd, de SnelStart-koppeling en wat
de agent doet. De verhouding "6 van 8" komt uit de demo met voorbeeldgegevens en is als
zodanig benoemd. De uren staan als scenario op de site.

Plaats ze niet op dezelfde dag. Laat er twee tot vier dagen tussen.

---

## Post 1 · Declaraties (pijler: bewijs)

De sterkste van de twee. Begin hiermee. De hook is wat de agent *niet* doet.

> Onze agent handelde 6 van de 8 bevindingen zelf af. De andere 2 legde hij terug met een vraag.
>
> Dat tweede is het hele punt.
>
> Een zorgpraktijk dient elke maand honderden declaraties in. Zilveren Kruis, VGZ, CZ, Menzis, ONVZ. De betalingen komen terug op eigen tempo, vaak gebundeld en zelden met een net declaratienummer.
>
> Het afvinken is vervelend werk, maar te doen. Het venijn zit in de rest.
>
> Een bedrag van 8.950 euro voor een verrichting die 89,50 kost. Een behandeldatum die ná de indiendatum ligt, dus is er ergens een dag en maand omgedraaid. Twee keer hetzelfde declaratienummer, waarvan de eerste al is uitbetaald. Een declaratie die 90 dagen na de behandeling is ingediend en daarmee buiten de termijn valt.
>
> We koppelden hun administratie in SnelStart aan het Excel-overzicht van ingediende declaraties. De agent matcht, benoemt wát er mis is, en stelt de vervolgstap voor: opnieuw indienen met de correctie er al bij, laten vervallen, of navragen bij de verzekeraar.
>
> En bij elk voorstel zet hij hoe zeker hij is. Boven 80 procent handelt hij het af. Daaronder stelt hij een vraag in plaats van iets te doen.
>
> Die grens maakt het verschil tussen een hulpmiddel dat je vertrouwt en een zwarte doos die je alsnog helemaal moet nakijken.
>
> Bouwtijd: twee uur.
>
> Een declaratie die je vergeet is honderd procent verlies. Wat kost dit soort werk jou per maand? Eén call van 45 minuten en we zeggen eerlijk of een agent het aankan.
>
> #AIagents #Zorg #SnelStart #Automatisering #MKB

---

## Post 2 · Jaarverslag (pijler: bewijs)

> De belangrijkste kolom in dit rapport is niet het getal. Het is de kolom ernaast.
>
> We bouwden een agent die meekijkt in de administratie en het jaarverslag opstelt terwijl je erover praat. Gekoppeld aan SnelStart, dus geen export en geen tussenbestand dat een dag later al niet meer klopt.
>
> Hij levert een compleet bestuursverslag. Balans, resultaatbestemming, continuïteit, en een tabel met de kengetallen.
>
> In die tabel staat current ratio 2,13. En ernaast staat waaruit dat is berekend: vlottende activa gedeeld door kortlopende schulden.
>
> Dat lijkt een detail. Het is het verschil tussen bruikbaar en onbruikbaar.
>
> Een accountant zet zijn naam onder dat verslag. Niemand tekent voor een getal dat hij niet kan navertellen. Dus een agent die alleen het juiste antwoord geeft, is nog niks waard. Hij moet laten zien hoe hij eraan komt.
>
> Vraag je om een formelere toon of een compacte versie voor de directie, dan herschrijft hij het en vertelt hij wat hij heeft aangepast. Het vakoordeel en de ondertekening blijven bij de accountant.
>
> Bouwtijd: twee uur.
>
> Zit er in jouw kantoor ook zo'n stuk werk dat elk jaar terugkomt? Eén call van 45 minuten.
>
> #AIagents #Accountancy #SnelStart #Automatisering #MKB

---

## Distributie

- **Vanaf Marcels persoonlijke profiel.** Bedrijfspagina versterkt binnen een uur met één eigen zin.
- **Eerste 90 minuten bepalen alles.** Post wanneer de doelgroep online is en reageer zelf actief
  in dat eerste uur op elke comment.
- **Format.** Post 1 werkt sterk als PDF-carrousel van 6 slides, want de bevindingen zijn visueel:
  1. de hook (6 van 8 zelf afgehandeld, 2 teruggelegd)
  2. het probleem (honderden declaraties, vijf verzekeraars)
  3. de vier rare gevallen (kommafout, omgedraaide datum, dubbel, termijn verstreken)
  4. wat de agent voorstelt per bevinding
  5. de 80 procent-grens en waarom die er is
  6. de CTA
  Carrousels scoren structureel het hoogst op LinkedIn.
- **Link.** Zet de link naar de case in de eerste comment, niet in de post zelf.

---

## Screenshots: welke waar

Sla de aangeleverde afbeeldingen op in `Soratus.Web/wwwroot/img/cases/` met exact deze namen.
Zolang een bestand ontbreekt, laat de pagina een placeholder zien met de verwachte naam.

**Declaraties**

| Bestandsnaam | Welke afbeelding |
|---|---|
| `declaraties-betalingen.png` | Beginstand: ontvangen betalingen 2025 |
| `declaraties-matching.png` | Matchingresultaat met KPI-tegels, filters en bevindingen |
| `declaraties-agent.png` | "Afhandeling door AI-agent" met voorstellen en zekerheidsscores |

**Jaarverslag**

| Bestandsnaam | Welke afbeelding |
|---|---|
| `jaarverslag-start.png` | Startscherm: chat links, leeg rapport-paneel rechts |
| `jaarverslag-rapport.png` | Opgesteld bestuursverslag naast de samenvatting in de chat |
| `jaarverslag-kengetallen.png` | Kengetallentabel met de toelichting per ratio |

Daarna in `Soratus.Web/Models/CaseStudy.cs` bij de betreffende `CaseShot` het argument
`Src: "/img/cases/<bestandsnaam>"` toevoegen. De placeholder verdwijnt dan automatisch.

## Voor publicatie nog checken

- **`jaarverslag-kengetallen.png` bevat de tekst "Opgesteld met demo-cijfers · Mijn MBV".**
  Dat verklapt de klant, terwijl de case anoniem is. Wegsnijden of onleesbaar maken.
- **`declaraties-matching.png` bevat namen van verzekerden** (M. Jansen, P. de Boer en verder).
  Ze zien eruit als gegenereerde demonamen en er staat een DEMO-badge, maar dit is
  zorgcontext. Bevestig dat het synthetische namen zijn voordat dit publiek gaat.
