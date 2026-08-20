# Fase 0 — afwijkingen van de spec, en waarom

De opdracht vraagt per fase een korte notitie over waar we van `agent-portal-spec.md`
afwijken. Dit is die notitie voor fase 0. Alles wat hier niet staat is gebouwd zoals de spec
het beschrijft.

De volgorde is die van gewicht: bovenaan staat wat het ontwerp verandert, onderaan wat alleen
een waarde of een woord verandert.

---

## 1. De telemetriebron is Cosmos DB, niet Container Apps met Log Analytics

**Spec:** §4 en §5 gaan uit van Azure Container Apps met Log Analytics als bron voor
agentstatus, heartbeat, runs en logs.

**Werkelijkheid:** in de eerste klantomgeving (`MBV`) staan geen Container Apps en geen eigen
Log Analytics workspace. Er draaien Linux App Services. WebJobs bestaan daar niet — de API
geeft `Conflict`. De Application Insights-componenten voeren bovendien af naar
`DefaultWorkspace-…-WEU` buiten de klant-resource group, gedeeld met PackCompany en
AllSprinklers.

**Wat we doen:** agents publiceren volgens een vastgelegd contract naar een Cosmos DB-account
per klant. Zie [`agent-contract.md`](agent-contract.md).

**Waarom Cosmos en niet Log Analytics:**

1. Log Analytics is een append-only logstore zonder begrip van huidige staat. "Is agent X nu
   gezond" wordt daar altijd een aggregatiequery over een tijdvenster, met throttling en
   kosten per query. Cosmos geeft een puntlezing van één document op een partitiesleutel —
   dat is de juiste vorm voor een statusscherm. Dit argument blijft gelden als de drempels
   morgen veranderen.
2. De degraded-drempel staat op twee minuten. Log Analytics heeft één tot drie minuten
   ingestievertraging. Een gezonde agent zou dan structureel als degraded verschijnen.
3. Retentie is in Cosmos een TTL op de container, dus zelfhandhavend. In Log Analytics is het
   een instelling per tabel die iemand per omgeving moet zetten — en dat is in MBV nul keer
   gebeurd.
4. Eén account per klant met `disableLocalAuth` maakt de isolatie fysiek in plaats van een
   queryfilter. Een verkeerd geraden klant-id kan niet bij andermans data, want de verbinding
   bestaat niet.

**Wat het kost, eerlijk:** geen KQL, geen ad-hoc joins, geen workbooks, en vrije tekstzoek
over logberichten is zwakker dan `search` in KQL. Application Insights blijft bestaan op de
apps, maar als ontwikkelaarsgereedschap, niet als bron van het portaal. Die tweedeling gaat
ooit iemand verwarren; hij staat daarom expliciet in het contractdocument.

Log Analytics is *niet* afgevallen op kosten. De hele huidige workspace kost € 6,47 per maand
voor vier klanten. Het is een vormbeslissing.

---

## 2. Een agent publiceert zijn eigen status niet

**Spec:** §6 modelleert `Agent` met een veld `status`.

**Afwijking:** dat veld bestaat niet in het contract. Een agent die om is kan niet melden dat
hij om is; een gepubliceerde status is dus per definitie onbetrouwbaar op precies het moment
dat het ertoe doet.

**Wat we doen:** de agent publiceert feiten — laatste hartslag, lifecycle, laatste
runresultaat — en `AgentStatusCalculator` leidt de status af. Die functie is één pure methode
in `Soratus.Agents.Contracts`, gedeeld door het scherm en de storingsmelder, zodat die twee
elkaar niet kunnen tegenspreken.

Bijeffect: "geen document" levert automatisch `Unknown` op. Het scherm kan dus structureel
niet groen staan omdat het niets weet.

---

## 3. Er is een zesde toestand nodig, maar geen zesde status

**Spec:** §8 kent vijf statussen met vaste rangen.

**Probleem:** een agent die verwacht wordt maar niets publiceert — nog niet uitgerold, of de
telemetriebibliotheek ontbreekt — is niet idle en niet live.

**Wat we doen:** we hergebruiken rang 0 ("Geen agents", `#767c94` / `#f6f7fb` / `#e3e5ee`,
glyph `–`). Alleen het label verschilt per context: op een klantrij "Geen agents", op een
agentrij "Geen telemetrie". Geen nieuwe kleur, geen nieuwe rang, en de sorteerregel uit §3.1
blijft ongewijzigd.

Rang 0 zorgt ervoor dat zo'n agent nooit een echte storing van de bovenkant van het overzicht
verdringt. Om te voorkomen dat hij daardoor onzichtbaar wordt, krijgt de KPI-rij een neutrale
teller "n zonder telemetrie".

---

## 4. `idle` betekent "draait en heeft niets te doen", niet "geschaald naar nul"

**Spec:** §3.3 omschrijft idle als naar nul geschaald.

**Afwijking:** op App Service bestaat schalen naar nul niet, dus die tekst is op ons platform
onwaar. De copy is herschreven naar "de agent draait en heeft niets te doen — dit is normaal
en geen storing".

Dit is met Marcel afgestemd: idle betekent inderdaad "niets te doen". Was het antwoord "echt
geschaald naar nul" geweest, dan was het platform Container Apps geworden en had dat de hele
blauwdruk en de CI veranderd.

---

## 5. Klikbare rijen zijn een echte `<a>` of `<button>`

**Spec:** §8 schrijft `role="button" tabindex="0"` voor met een eigen Enter- en
Space-afhandeling.

**Afwijking:** in Blazor is `preventDefault` op de spatiebalk niet per toetsaanslag te regelen
zonder JavaScript. Doe je het niet, dan scrollt de pagina bij elke Space; doe je het altijd,
dan breekt Tab.

**Wat we doen:** een navigatierij is een `<a class="data-row">`, een uitklaprij een
`<button type="button" class="data-row">`. Toetsenbordgedrag werkt dan native, de rij is
deep-linkbaar, middelklik opent een nieuw tabblad, en een schermlezer zegt "link" waar dat
klopt.

**Harde regel die hieruit volgt:** genest interactief is ongeldig, dus een rij met
rij-acties (fiatteren, afwijzen, intrekken) is nooit zelf activeerbaar. Vastgelegd in de
documentatie van `DataRow`.

---

## 6. Kolomdefinities zijn C#-data, geen CSS

**Niet in de spec, wel een ontwerpbesluit dat het noemen waard is.**

Er komen acht tabellen in het portaal, elk met eigen kolommen. Zou elk scherm zijn eigen
`grid-template-columns` in eigen CSS zetten, dan moet elk scherm ook zijn eigen 768px-regel
schrijven — acht keer, en de eerste die iemand vergeet valt pas in productie op.

Een scherm declareert nu een `RowGrid` in C#. De kaart draagt dat als `--row-cols`, en de
responsieve regel bestaat één keer in `layout.css` voor alle tabellen tegelijk. Cellen krijgen
hun `data-label` automatisch uit de kolomkop, dus geen enkel scherm hoeft iets te doen om
onder 768px leesbaar te blijven.

---

## 7. Tijden: UTC opslaan, Nederlandse tijd tonen

De mockup toont UTC. Dat is een artefact van een mockup, geen ontwerpbesluit. Een operator in
Nederland die om 17:13 "15:13" leest, denkt dat er iets twee uur geleden gebeurde.

Opslag is canoniek UTC met vaste breedte (`yyyy-MM-ddTHH:mm:ss.fffffffZ`). Dat is geen
schoonheidsprijs maar noodzaak: Cosmos slaat tijdstempels op als tekst en `ORDER BY`
vergelijkt lexicografisch. Met gemengde offsets of wisselende precisie sorteert de logtabel
stil verkeerd. Er staat een assertie op die bij het opstarten van elke agent afgaat.

Weergave gaat naar `Europe/Amsterdam`. Het `datetime`-attribuut van het `<time>`-element
blijft UTC, want dat is machineleesbaar.

---

## 8. Kleinere punten

**Failed-vlak is `#fdeceb`.** §8 en de mockup spreken elkaar tegen (`#fdeceb` tegen
`#fdedeb`). De opdracht zegt dat tokens uit §8 komen, dus §8 wint.

**De rolwisselaar uit de mockup gaat niet mee**, ook niet achter een vlag en ook niet in
Development. Rol komt uit Entra ID. Een rolwisselaar in productiecode is één configuratiefout
van een volledige doorbraak van de zichtbaarheidsregels.

**De navigatie schuift horizontaal in plaats van te wrappen.** De mockup gebruikt
`flex-wrap: wrap` in een header van vaste 52px; op een smal scherm valt de eerste rij dan
buiten beeld. Schuiven sluit aan bij §8, dat hetzelfde voorschrijft voor tabellen.

**Debug en trace bestaan niet in het logcontract.** Alleen info, warn en error. Deze regels
worden gelezen door een operator die wil weten of er iets mis is; vijfhonderd debugregels per
run maken dat moeilijker.

**Retentie is niet één getal.** Logs 30 dagen zoals afgesproken, runs 400 dagen. Bij een
factuurdiscussie of de vraag "wat is er in mei gebeurd" wil je de runs nog hebben.

---

## 9. De ernst van een klant telt alleen productie-agents

**Spec:** §3.1 beschrijft de klantenlijst met "ernstigste status" en de sortering
failed(4) > degraded(3) > live(2) > idle(1) > geen agents(0). Er staat niet bij of agents op
acceptatie en ontwikkeling meetellen. §2 zegt wel dat de klantweergave alleen productie toont,
maar het operatoroverzicht is nadrukkelijk een ander scherm.

**Aanleiding:** de interne klant draait naast vijf beheeragents ook `heartbeat-demo`, een
demo-agent op `dev` die meestal uit staat. Met alle omgevingen in de telling stond
"Soratus — intern beheer" daardoor permanent op `degraded` en kwam hij op plek 2 van het
overzicht, boven klanten waar in productie niets aan de hand was.

**Besluit:** de ernstrang, de sortering en de statusbalk van een klantrij gaan **uitsluitend
over productie-agents**. Wat daarbuiten draait telt niet mee in de rang en niet in de
KPI-statusverdeling.

**Waarom:**

1. Het overzicht beantwoordt één vraag: *is er ergens iets mis bij een klant?* Een uitgezette
   acceptatie-agent is dat niet. Dit is dezelfde redenering die de contractbibliotheek al
   toepast op de klantweergave — "een acceptatie-agent die omvalt is geen storing" — en er is
   geen reden waarom die voor de operator anders zou liggen.
2. Zou een kapotte dev-agent een klant naar de bovenkant tillen, dan verliest de sortering
   precies de betekenis waarvoor hij bestaat. Een operator die elke ochtend bovenaan een
   klant ziet staan waar niets mee is, gaat de bovenste rijen wegkijken. Zo mis je een echte
   storing — het middel wordt dan de oorzaak van wat het moest voorkomen.
3. De statusbalk en de rangkolom staan in dezelfde rij naast elkaar. Zou de balk alle
   omgevingen tellen en de rang alleen productie, dan spreken twee cellen in één rij elkaar
   tegen. Dat is de tegenspraak die regel 7 verbiedt, op de kleinst mogelijke afstand.

**Wat er tegenover staat, zodat het niet stil verdwijnt:**

- Niet-productie-agents blijven voor de operator volledig zichtbaar in de klantweergave, met
  hun omgeving erbij.
- De klantrij draagt een aparte telling (`NonProductionStatuses`) en de KPI-rij een rustige
  teller — "n agents buiten productie, waarvan m met problemen". Bewust zonder statuskleur:
  §8 reserveert groen, amber en rood voor status, en dit is informatie en geen alarm.
- Een klant met agents die geen enkele in productie heeft, komt op rang 0 uit — hetzelfde als
  een klant zonder agents. Om te voorkomen dat het scherm dan "geen agents" zegt terwijl dat
  onwaar is, draagt de rij `HasOnlyNonProductionAgents`. De eerlijke formulering is "geen
  agents in productie", met het aantal daarbuiten erachter.

**Gevolg voor de fase-0-acceptatie:** de verwachte volgorde van het overzicht op de seed-data
is `bakker` (failed) › `vandijk` (degraded) › de live klanten › `kroon` (idle) › `nieuw` (geen
agents). De interne klant staat daarbij tussen de live klanten en niet meer op plek 2.

---

## 10. De sparkline wordt in Cosmos geaggregeerd, niet in het portaal

**Spec:** §3.2 vraagt per agent een "sparkline van runs over 24u (mislukte blokken rood)". Over
hoe die data wordt opgehaald zegt de spec niets.

**Besluit:** één aggregatiequery per klant, die per agent en per heel uur telt hoeveel runs er
waren en hoeveel daarvan mislukten. Het portaal verdeelt die uren over twaalf blokken van twee
uur. Niet één query per agent, en niet een platte projectie van alle runs.

**Waarom niet één query per agent:** dat zou bij twintig agents twintig query's per
paginaweergave betekenen. Precies de vorm die bij de eerste klant met veertig agents pijn gaat
doen, en die je dan niet meer goedkoop verandert.

**Waarom niet een platte projectie van alle runs:** die is bij de huidige seed-data juist
goedkoper — 3,6 RU tegen 5,0 RU — maar het aantal rijen groeit lineair met het aantal runs. Een
agent die elke minuut draait levert 1440 rijen per dag; met twintig zulke agents zijn dat bijna
dertigduizend rijen per paginaweergave. Met de aggregatie is het aantal rijen begrensd door
*agents × 24*, ongeacht hoe vaak een agent draait. We betalen nu 1,4 RU meer om die grens te
hebben.

**Vorm van de query:** groeperen op `SUBSTRING(c.startedAt, 0, 13)` — de eerste dertien tekens
van de tijdstempel, dus het hele uur. Dat mag omdat de opslagvorm vast is (ISO-8601 in UTC, zie
punt 7). Het aantal mislukte runs komt uit een conditionele `SUM`, zodat er één rij per agent per
uur overkomt in plaats van één rij per agent per uur per afloop.

**Geen `pk IN (...)`.** Dit lag voor de hand: 24 uur beslaat twee dagpartities per agent, dus de
partities zijn vooraf bekend. Gemeten is die clausule echter *duurder* — 5,80 RU met, 5,13 RU
zonder. Het filter op `customerId` doet het werk al, en een `IN`-lijst van tweemaal het aantal
agents kost meer aan queryplanning dan hij aan scan bespaart. Deze regel staat er zodat niemand
de optimalisatie later "alsnog even" toevoegt.

**Het venster is uitgelijnd op een even UTC-uur**, niet simpelweg "nu min 24 uur". De query telt
per heel uur; met een venster dat om 17:20 begint valt het uur 19:00–19:59 half in het ene blok
en half in het andere, en dan is er geen eerlijke toewijzing. Met uitlijning valt elk uur in
precies één blok. Het laatste blok is daarmee het blok waarin "nu" valt en dus meestal nog niet
vol — voor een sparkline juist goed: het rechtse blokje groeit terwijl je kijkt.

**Kosten, gemeten op de seed-data (7 klanten, 20 agents):**

| Scherm | Query's | RU | Warm |
|---|---|---|---|
| Klantweergave (3 agents) | 5 | 17,8 | 109 ms |
| Overzicht (7 klanten) | 41 | 133,4 | 208 ms |

De sparkline is 1 query en 5,0 RU van die klantweergave. Het overzicht heeft geen sparklines en
betaalt er dus niets voor. Wat het overzicht wél duur maakt is iets anders: twintig losse
"laatste afgeronde run"-query's, één per agent. Zie de opmerking daarover in
`CosmosAgentTelemetryStore` — dat is een bewuste keuze voor correctheid boven kosten, en het is
de eerste plek om naar te kijken zodra het aantal agents richting de honderd loopt.

---

## Wat bewust nog niet is gebouwd

Uren, facturatie, sprint en support. Die komen pas als de statusweergave klopt, zoals de
opdracht voorschrijft. Twee besluiten uit §9 van de spec staan daarmee ook nog open: de
Azure-uitsplitsing per dienst (fase 4) en de audittrail op urencorrecties (fase 3).

Voor dat laatste ligt er al een voorstel dat de vraag laat vervallen: sla een handmatige
correctie op als nóg een goedgekeurde urenregel met bron `portaal` en categorie `Correctie`.
Dan blijft het maandtotaal een zuivere som én is de correctie zichtbaar als rij — de spec
vraagt nu om allebei van één getal, en dat kan niet.
