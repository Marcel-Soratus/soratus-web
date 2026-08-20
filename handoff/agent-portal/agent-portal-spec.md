# Soratus Agent Portal — functioneel ontwerp en faseplan

Bron van waarheid voor de UI: `Soratus Agent Portal.dc.html` (interactieve mockup, dummy-data in het `DATA`-object bovenaan de logica).
Merk/tokens komen uit `Marcel-Soratus/soratus-web` (`wwwroot/css/tokens.css`, `wwwroot/brand/`), toegepast in een lichte variant.

---

## 1. Het idee

Eén portaal, twee scopes.

- **Klant** ziet uitsluitend zijn eigen omgeving: draaien de agents, doen ze hun werk, wat is er gebeurd, wat kost het, en waar staat de sprint.
- **Soratus-operator** ziet alle klanten in één overzicht en klikt door naar exact dezelfde klantweergave, met beheerfuncties erbovenop.

Elke klant heeft een eigen geïsoleerde Azure-omgeving (eigen subscription/resource group) waarin een paar autonome agents draaien. Het portaal is een operationeel scherm: je opent het 's ochtends om te zien of er iets stuk is.

Daarnaast beheert Soratus het platform met **eigen agents** (interne klant "Soratus — intern beheer"): factureren, storingen melden, kosten ophalen, uren en DevOps synchroniseren. Het portaal is dus ook het werkoppervlak van die beheeragents.

**Ontwerpregels** (gelden voor elke fase)
- Statuskleuren zijn functioneel: groen/amber/rood alleen voor status, al het andere neutraal grijs. Status nooit alleen door kleur — altijd label + glyph (● ◐ ✕ ○).
- Informatiedicht, geen witruimte zonder doel, geen hero's/gradients/emoji.
- Getallen tabulair; monospace voor logs, runId's, versies, tijdstempels, bedragen.
- Relatieve tijden in beeld, absolute tijd in de tooltip.
- Geen knoppen die suggereren dat je kunt ingrijpen zolang dat niet kan (pauzeren/herstarten): laat de beperking zien in plaats van hem weg te poetsen.
- Eerlijke systeemeigenschappen benoemen (bijv. "historische logs lopen ~1 min achter").

---

## 2. Rollen en zichtbaarheid

| | Klant | Soratus-operator |
|---|---|---|
| Overzicht alle klanten | – | ✓ |
| Agents + logs/runs/config van eigen omgeving | ✓ (read-only) | ✓ |
| Sprint (DevOps) | ✓ (read-only) | ✓ (read-only) |
| Contract + toegangsbeheer | lezen | lezen + bewerken |
| Uren: gefiatteerde regels | ✓ | ✓ |
| Uren: te fiatteren regels, fiatteren/afwijzen, boeken | **nee** | ✓ |
| Koppelingen (MCP/DevOps-details) | **nee** | ✓ |
| Facturatie: bedragen en status | ✓ | ✓ |
| Facturatie: Azure per dienst + beheeropslag | **nee** | ✓ |
| Support: bericht sturen, AI-eerstelijn | ✓ | antwoorden als mens |

In de mockup zit een rolwisselaar rechtsboven, expliciet gemarkeerd als demo-hulpmiddel. In productie volgt de rol uit Entra ID.

---

## 3. Schermen en functionaliteit

### 3.1 Soratus-overzicht (operator)
- KPI-rij: aantal klanten (+ aantal in onboarding), totaal agents, statusverdeling live/degraded/failed/idle, runs vandaag (+ mislukt), foutpercentage 24u.
- Klantenlijst: naam, omgeving (regio · subscription), aantal agents, statusverdeling als compacte balk + tekst, ernstigste status, laatste activiteit.
- **Sortering op ernst, dan recentheid**: failed(4) > degraded(3) > live(2) > idle(1) > geen agents(0). Idle tilt een klant dus nooit naar boven.
- Klik op een rij → klantweergave. Knop **Nieuwe klant** opent het aanmaakformulier.

### 3.2 Klantweergave — Agents
- Kop met klantnaam, laatst bijgewerkt, omgeving.
- Per agent: naam, type, status-badge, laatste run (relatief), duur, sparkline van runs over 24u (mislukte blokken rood), volgende run, versie.
- Statuslegenda onderaan in gewone taal (een klant kent "degraded" niet vanzelf).
- Lege staat voor een net aangesloten klant zonder agents.

### 3.3 Agentdetail
- Kop: naam, status, type, klant; feiten: versie, draait sinds, laatste heartbeat, laatste run, volgende run.
- Statusspecifieke melding: degraded (heartbeat ouder dan drempel, werk loopt door), failed (transactie teruggedraaid), idle (naar nul geschaald, geen storing).
- Tabs:
  - **Logs** — tijd, level, event, bericht, runId; filters op info/warn/error met tellingen; zoekveld over event/bericht/runId; regel uitklapbaar naar de volledige JSON (incl. stacktrace, breekt netjes af); **Live tail**-toggle met de eerlijke tekst "historische logs lopen ~1 min achter".
  - **Runs** — starttijd, duur, resultaat, aantal verwerkte items, runId.
  - **Configuratie** — read-only: planning/trigger, resource-limieten, image, resource group, identity, logretentie. Expliciet: ingrijpen kan (nog) niet.

### 3.4 Sprint (read-only, uit Azure DevOps)
- Sprintnaam, periode, boardpad, tijdstip van laatste ophalen.
- Statistieken: work items, afgerond, openstaande uren, story points, geblokkeerd.
- Work items van deze klant: id, type, titel + tags + herkomst (aangemaakt door agent of handmatig), state (New/Active/Blocked/Resolved/Closed), toegewezen, open/gedane uren.
- Het portaal schrijft niets terug: agents maken items aan in DevOps, het portaal haalt bij openen de laatste status op.

### 3.5 Contract
- Contractkaart: contractnummer, soort, ingangsdatum, looptijd, opzegtermijn, urenbundel per maand, uurtarief buiten bundel, indexatie, SLA, contactpersoon, beheerd door. Operator kan alle velden bewerken.
- **Toegang tot het portaal**: e-mailadres + naam + rol (Beheerder klant / Lezer). Operator geeft toegang en trekt in; toegang loopt via Entra ID.

### 3.6 Uren
- Standaard alleen de **huidige maand**; knop "Alle maanden" klapt de historie en het jaartotaal open.
- Per maand: bundel, besteed (invulbaar door Soratus), saldo, status (Binnen bundel / Boven bundel / Niets geboekt) en — operator-only — "+ x u te fiatteren".
- **Klik op een maand** filtert de specificatie op die maand en zet die maand in het boekformulier.
- **Uren boeken** (operator): maand (default huidige), uren, categorie, geboekt door, omschrijving.
- Specificatie: datum, omschrijving + categorie/maand, **bron** (Portaal · MCP/Claude Code · Azure DevOps), **geboekt door**, uren, en bij AI/DevOps-regels de acties **Fiatteren / Afwijzen**.
- **Eén bron van waarheid**: het maandtotaal is de som van de gefiatteerde regels. Een handmatige correctie is mogelijk maar wordt als afwijking gemeld in de tooltip.
- Koppelingenkaart (operator): MCP-server `soratus-uren` met tool `uren.boeken(...)`, en de DevOps-mapping "Task completed → urenregel".

### 3.7 Facturatie
- **Facturen uit SnelStart** per maand: factuurnummer, verstuurd- en vervaldatum, extra uren, Azure, totaal, status **Concept · Verzonden · Openstaand (vervallen) · Betaald** met betaaldatum. Read-only, met tijdstip van ophalen.
- De lopende maand staat bovenaan als concept met live berekende bedragen.
- **Facturatie-agent `maandfactuur-snelstart`**: zet op de 1e van de volgende maand een conceptfactuur klaar in SnelStart; toont laatste/volgende run, conceptnummer en fiatteringsstatus. Versturen doet Soratus zelf.
- **Azure-verbruik uit de resource group** (operator): per dienst (Container Apps, Azure OpenAI, Storage, Log Analytics, Key Vault), subtotaal, instelbare beheeropslag %, door te belasten bedrag. Zelf te beheren.
- **Extra uren boven bundel** rollen automatisch mee (uren × uurtarief) en staan met Azure op één totaal, achteraf gefactureerd.
- **Maandoverzicht mailen** naar de contactpersoon, met verzendbevestiging.

### 3.8 Support
- Berichtendraad klant ↔ Soratus.
- **AI-eerstelijnsagent** antwoordt direct, gegrond in de portaalgegevens: agentstatus + uitleg + laatste/volgende run + bijbehorend DevOps-item; uren vs. bundel; laatste factuur en betaalstatus; open sprintitems. Weet hij het niet, dan zegt hij dat en escaleert naar het team binnen de SLA.
- Elke AI-bubbel toont het badge "AI · eerstelijn" en de bron waarop het antwoord is gebaseerd, plus "Toch een mens van Soratus spreken".
- De klantcontext wordt niet als paneel getoond — dat is simpelweg alles wat we van de klant weten.
- In de operatorrol antwoordt een mens; de agent springt er dan niet tussen.

### 3.9 Klantbeheer
- Nieuwe klant aanmaken met alle velden: naam, omgeving (kort + resource group/subscription), contractnummer, soort, ingangsdatum, looptijd, opzegtermijn, urenbundel, uurtarief, indexatie, SLA, contactpersoon, Azure-opslag %, en e-mailadressen die toegang krijgen.
- Klant start zonder agents (lege staat) tot Soratus de eerste agent uitrolt.

---

## 4. Beheeragents van Soratus (interne klant)

| Agent | Type | Planning | Doet |
|---|---|---|---|
| `maandfactuur-snelstart` | Facturatie | `0 6 1 * *` | Zet conceptfactuur klaar in SnelStart: Azure-kosten + opslag + uren boven bundel |
| `storingsmelder` | Monitoring | elke minuut | Mailt Soratus bij failed/degraded (drempel per status) |
| `kosten-collector` | Cost Management | dagelijks 04:00 | Haalt Azure-kosten per resource group op |
| `urensync-mcp` | Integratie | op trigger | Ontvangt urenregels via MCP uit Claude Code, zet ze op "te fiatteren" |
| `devops-sync` | Integratie | elke 15 min | Haalt sprint/work items en Completed Work op uit Azure DevOps |

Interne klant loopt op een **beheercontract** (intern, niet gefactureerd) en verschijnt gewoon in het overzicht, zodat je de beheeragents net zo monitort als klantagents.

---

## 5. Integraties

| Systeem | Richting | Gebruik |
|---|---|---|
| Azure Container Apps / Log Analytics | lezen | agentstatus, heartbeat, runs, logs |
| Azure Cost Management | lezen | kosten per resource group per maand |
| SnelStart | lezen + concept schrijven | facturen, betaalstatus; conceptfactuur klaarzetten |
| Azure DevOps | lezen (+ schrijven door agent) | sprint, work items, Completed Work → urenregels |
| MCP (`soratus-uren`) | schrijven | `uren.boeken({ klant, maand, uren, categorie, omschrijving })` uit Claude Code |
| Entra ID | auth | rol (operator/klant) en toegang per e-mailadres |
| Mail (SendGrid) | schrijven | storingsmeldingen aan Soratus, maandoverzicht aan klant |

**Vaste regel:** alles wat een agent of koppeling inschiet landt als **te fiatteren** en telt pas mee in uren en facturatie na akkoord van Soratus.

---

## 6. Datamodel (kern)

- **Customer** — id, naam, intern?, env (kort), envFull (subscription/RG), agents[]
- **Agent** — id, short, type, status (live/degraded/failed/idle), version, lastRun, durMs, next, schedule, trigger, uptime, heartbeatSec, runs24[]
- **LogLine** — ts, level, event, msg, runId, extra{} (volledige JSON, incl. payload/stacktrace)
- **Run** — ts, durMs, result, items, runId
- **Contract** — nr, type, start, looptijd, opzeg, sla, bundelUren, uurTarief, indexatie, contact, eigenaar
- **HourEntry** — cid, date, month, category, note, hours, **source** (portaal/mcp/devops), **by**, **status** (pending/approved/rejected)
- **AzureCost** — cid, maand, regels[dienst, bedrag], opslag%
- **Invoice** — cid, month, nr, date, due, uren, azure, status, paidAt
- **WorkItem** — id, type, title, state, by, points, remaining, done, tags, origin
- **Access** — cid, email, name, role
- **Message** — cid, from (klant/soratus/ai), who, at, text, context

---

## 7. Faseplan

**Fase 0 — Fundament**
Projectopzet, tokens/typografie uit `soratus-web` in de lichte variant, auth via Entra ID met de twee rollen, klant- en agentmodel, alles nog op seed-data. Op te leveren: overzicht + klantweergave read-only.

**Fase 1 — Observability (de kern)**
Azure Container Apps/Log Analytics koppelen: status, heartbeat, runs, logs. Agentdetail met logfilters, zoek, uitklapbare JSON, runs, read-only configuratie, live tail met de ~1 minuut vertraging benoemd. Acceptatie: een operator ziet binnen twee seconden of ergens iets mis is, en kan de foutregel van een gefaalde run vinden.

**Fase 2 — Contract en toegang**
Contractmodel + beheer, toegangsbeheer per e-mailadres (Entra ID-uitnodiging), klant aanmaken met alle velden. Acceptatie: een nieuwe klant kan volledig zonder database-actie worden ingericht.

**Fase 3 — Uren**
Urenmodel met bron, boeker en fiatteringsstatus; boeken in het portaal; maandweergave + specificatie; fiatteren/afwijzen; MCP-server `soratus-uren` en DevOps Completed Work als bronnen. Acceptatie: maandtotaal is altijd de som van de gefiatteerde regels, en de klant ziet niets van de fiatteringsstroom.

**Fase 4 — Kosten en facturatie**
Kosten-collector (Cost Management per RG), beheeropslag, extra uren boven bundel, conceptfactuur in SnelStart op de 1e van de volgende maand, betaalstatus terug uit SnelStart, maandoverzicht mailen. Acceptatie: één factuur per maand met Azure + extra uren, en in het portaal is per maand te zien of die verstuurd en betaald is.

**Fase 5 — Sprint en support**
DevOps-sprintweergave (read-only), berichtendraad, AI-eerstelijnsagent op de eigen portaalgegevens met expliciete escalatie naar een mens. Acceptatie: de agent beantwoordt statusvragen, urenvragen en factuurvragen zonder te verzinnen, en escaleert als hij het niet zeker weet.

**Fase 6 — Beheeragents en alerting**
Storingsmelder (mail bij failed/degraded), automatische maandfactuur, interne klant "Soratus — intern beheer" met de beheeragents in het gewone overzicht. Acceptatie: het platform meldt zichzelf en factureert zichzelf; wij monitoren de beheeragents met hetzelfde scherm.

**Later, buiten scope van dit ontwerp**
Ingrijpen vanuit het portaal (pauzeren, herstarten, limieten wijzigen), donkere variant, agent-provisioning vanuit klantaanmaak (nu handmatige uitrol, zie DevOps #4530).

---

## 8. Styling — tokens en componentpatronen

Afgeleid van `soratus-web/wwwroot/css/tokens.css` (dark-only marketingsite) en omgezet naar de **lichte** app-variant met de daar gereserveerde `--light-bg` / `--light-ink`. Neem deze waarden 1:1 over; verzin geen nieuwe kleuren.

### Kleuren

| Rol | Waarde | Herkomst |
|---|---|---|
| Paginacanvas | `#f6f7fb` | `--light-bg` |
| Oppervlak (kaart, rij) | `#ffffff` | — |
| Oppervlak subtiel (kop, totaalrij) | `#fbfbfd` | — |
| Tekst primair | `#0a0d1a` | `--light-ink` |
| Tekst secundair | `#575d75` | licht equivalent van `--ink-dim` |
| Tekst meta | `#767c94` | licht equivalent van `--ink-mute` |
| Lijn | `#e3e5ee` · rij-scheiding `#eef0f5` · veldlijn `#cfd3e0` | — |
| Vlak neutraal (chip, idle) | `#f1f2f6`, rand `#dcdfe9` | — |
| Merkblauw (focus, links, actieve tab) | `#2B5BFF` | `--blue` |
| Merkblauw diep (hover link) | `#1B1F8C` | `--navy` |
| Merkvlak (info/AI/DevOps) | `#eef2ff`, rand `#ccd6ff` | afgeleid van `--blue-2` |
| Merkmark (drie stippen) | `#2A2FCC` · `#5C82FF` · `#34E27A` | `logo-mark.svg` |

**Statuskleuren — alleen voor status, nooit decoratief**

| Status | Tekst/dot | Vlak | Rand | Glyph | Rank |
|---|---|---|---|---|---|
| Live | `#0f7a4a` | `#eaf6ef` | `#bfe3cd` | ● | 2 |
| Degraded | `#8a5a00` | `#fdf4e0` | `#ecd7a6` | ◐ | 3 |
| Failed | `#b3261e` | `#fdeceb` / rij `#fefaf9` | `#f0c4bf` | ✕ | 4 |
| Idle | `#575d75` / dot `#767c94` | `#f1f2f6` | `#dcdfe9` | ○ | 1 |
| Geen agents | `#767c94` / dot `#cfd3e0` | `#f6f7fb` | `#e3e5ee` | – | 0 |

Logniveaus: info `#575d75`, warn `#8a5a00` (rij `#fefcf6`), error `#b3261e` (rij `#fefaf9`).
Work item states: New = idle-grijs, Active = merkvlak `#eef2ff`, Blocked = degraded-amber, Resolved/Closed = live-groen.
Bronnen urenregels: Portaal = neutraal grijs, MCP/Claude Code en Azure DevOps = merkvlak `#eef2ff` met `#1B1F8C`.

### Typografie

- **Space Grotesk** — alle UI-tekst en labels. Body 14px/1.45, paginakop 20px/600 (-0.02em), kaartkop 15px/600, tabelcel 12,5–13px.
- **JetBrains Mono** — logs, runId's, versies, tijdstempels, bedragen, agentnamen, kolomkoppen en alle meta. Kolomkop 10px, `letter-spacing 0.1em`, uppercase. KPI-cijfer 26px/500. Metaregel 10–11px.
- **Sora 200** — uitsluitend het wordmerk "soratus", 19px, `letter-spacing -0.04em`.
- Instrument Serif uit het merk wordt in de app **niet** gebruikt (dat is een marketingdevice).
- Alle getalkolommen: `font-variant-numeric: tabular-nums`.

### Ruimte, radii, elevatie

- Kaartpadding `12–14px`; rijhoogte compact (`7–11px` verticaal); grid-`gap` in tabellen `10–12px`, tussen kaarten `14px`.
- Radii: `4px` badges, `6px` knoppen/velden/chips, `8px` kaarten, `999px` suggestiechips. Geen 24px+ rondingen.
- Geen schaduwen op kaarten (1px rand doet het werk); alleen het dropdownmenu heeft `0 8px 24px rgba(10,13,26,0.12)`.
- Header: sticky, 52px hoog, `#ffffff` met onderrand `#e3e5ee`. Content `max-width: 1280px`.

### Componentpatronen

- **Status-badge** — rand + vlak + glyph + woordlabel, `padding: 2px 8px 2px 6px`, radius 4px. Nooit kleur zonder label.
- **Datarij** — CSS grid met vaste + flexibele tracks (`minmax`), klikbare rijen als `role="button" tabindex="0"` met hover `#f6f7fb` en Enter/Space-handler. Kaart met `overflow-x: auto` als vangnet.
- **KPI-tegel** — 1px raster van tegels, mono label uppercase 10px, groot mono getal, subregel in 12px.
- **Sparkline** — 12 blokjes (2-uursblokken), 5px breed, hoogte geschaald op max; leeg blok `#e3e5ee`, normaal `#a9b0c6`, blok met mislukte run `#b3261e`.
- **Invoervelden** — 1px `#cfd3e0`, radius 6px, focusrand `#2B5BFF`; getallen rechts uitgelijnd in mono. Read-only variant is platte tekst, geen uitgegrijsd veld.
- **Primaire knop** — `#0a0d1a` vlak, wit label, hover `#2B5BFF`. Secundair = wit met `#cfd3e0` rand, hover merkblauwe rand. Geen transform bij klik.
- **Tabs** — tekstknoppen met 2px onderlijn `#2B5BFF` als actief.
- **Berichten** — bubbel met 1px rand; klant `#eef2ff`, mens Soratus wit, AI `#fbfbfd` met badge "AI · eerstelijn" en bronregel boven een gestippelde scheiding.
- **Focus** — `:focus-visible` 2px `#2B5BFF`, offset 2px, overal.

### Motion

Vrijwel niets: alleen `pulse` (1,6s) op de actieve live-tail-indicator. Geen bounces, geen staggered reveals, geen animatie die iets vertraagt.

### Responsief

Werkt tot ~1280px; onder **1180px** verdwijnen het "Agent Portal"-label en het "demo · rol"-label uit de header; onder **768px** klappen datarijen naar een tweekoloms lijstweergave (`[data-rowgrid]`) en verdwijnt de gebruikersnaam. Tabellen schuiven horizontaal in plaats van te clippen.

---

## 9. Openstaande keuzes

- Drempels: wanneer is een agent degraded (nu heartbeat > 2 min) en wanneer mailt de storingsmelder (nu degraded > 10 min)?
- Mag de klant het Azure-verbruik per dienst zien, of alleen het door te belasten totaal? (nu: alleen totaal)
- Bewaartermijn logs (nu 30 dagen) en of de klant verder terug moet kunnen kijken.
- Wie mag toegang geven aan de klantzijde — alleen Soratus, of ook een beheerder van de klant zelf?
- Correcties op uren: audittrail bijhouden per correctie (wie, wanneer, waarom)?
