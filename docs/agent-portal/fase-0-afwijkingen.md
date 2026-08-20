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

## 11. Vier velden uit de read-only configuratie: drie bestaan niet, één is een echt gat

**Spec:** §3.3 vraagt op het configuratietabblad van het agentdetail onder meer
resource-limieten, image, resource group, identity en logretentie.

**Aanleiding:** `AgentRegistration` publiceert vier daarvan niet, en de eerste reflex was om ze
allemaal als "komt zodra het contract ze meestuurt" te melden. Dat is voor drie van de vier
onwaar, en een melding die drie dingen belooft die nooit gaan komen is erger dan geen melding.

**Besluit, per veld:**

1. **Resource-limieten (CPU, geheugen) en image bestaan niet.** Dat zijn Container
   Apps-begrippen. Wij draaien op App Service (zie §1): een agent heeft daar geen eigen image, en
   limieten hangen aan het App Service-plan en niet aan de agent. Er staat dus **geen rij** voor
   deze twee, ook niet een lege en ook niet een met een streepje. Een leeg veld belooft dat er
   ooit een waarde komt; die komt hier niet, want de vraag zelf klopt niet op dit platform. Dit
   is dezelfde soort correctie als `idle` in §4: de spec beschrijft een aanname die met de
   platformkeuze is vervallen.
2. **Resource group is er al.** `OperatorCustomerScope.EnvironmentDetail` draagt subscription en
   resource group, en `OperatorAgentConfigurationView` geeft dat door. Het staat op het
   configuratietabblad als "Azure-omgeving" — **alleen voor de operator**. Een klant ziet het
   niet, en dat is geen omissie maar §2: koppeling- en infrastructuurdetails zijn operator-only.
3. **Identity is een echt gat.** De managed identity van een agent is Azure-metadata en geen
   telemetrie, dus hij hoort niet in het agentcontract — een agent die zijn eigen identity
   publiceert, publiceert iets wat hij van buiten zichzelf zou moeten opvragen. De rij staat er
   daarom niet, en er staat één regel onder het tabblad die zegt dát hij er niet staat en waar
   hij wél te vinden is (Azure, onder de resource group erboven). Zwijgen zou het scherm korter
   maken zonder dat iemand merkt dat er iets mist.

**Gevolg voor de code:** `AgentConfigurationNotice.NotPublished` in
`Soratus.Portal/Views/AgentConfigurationView.cs` noemt alle vier de velden in één zin en zegt
dat ze erbij komen zodra het contract ze meestuurt. Die constante is met dit besluit onwaar
geworden en wordt door het scherm niet gebruikt; het configuratietabblad draagt in plaats
daarvan alleen de identity-melding. De constante hoort te verdwijnen of te worden herschreven
door wie `Views/` beheert.

**Wat er openblijft:** of identity op termijn ergens hóórt. Als het portaal hem moet tonen, dan
haalt het portaal hem op bij Azure Resource Manager en niet bij de agent — dat is een nieuwe
integratie (§5) en geen contractwijziging. Zolang niemand ernaar vraagt is de melding genoeg.

---

## 12. `extra` op een logregel is operator-only

**Spec:** §2 geeft de klant leesrecht op de logs van zijn eigen omgeving en maakt koppelingen
(MCP- en DevOps-details) expliciet operator-only. §3.3 vraagt op het logtabblad een regel die
uitklapt naar de volledige JSON, inclusief stacktrace.

**Aanleiding:** die twee botsen. `extra` is vrije JSON die de agentbouwer vult, en bij het bouwen
van de datalaag voor fase 1 is nagekeken wat er werkelijk in staat. Bij **gewone klant-agents**,
dus in wat een klant zou zien:

| sleutel | aangetroffen waarde | agent |
|---|---|---|
| `endpoint` | `GET /v1.0/me/messages/delta` | `bakker-mail-triage`, `vandijk-mail-triage` |
| `endpoint` | `POST /v2/purchase-invoices` | `vandijk-factuur-intake` |
| `scope` | `Mail.ReadWrite` | `bakker-mail-triage`, `vitaal-mail-triage` |
| `scope` | `https://storage.azure.com/.default` | `vandijk-factuur-intake` |
| `model` | `gpt-4.1` | `vandijk-offerte-generator` |
| `containerState`, `probe` | `Running`, `liveness` | `vandijk-mail-triage` |
| `replicas`, `memMb`, `uptimeSec` | `3`, `731`, `108000` | diverse |
| `stacktrace` | `… at SoratusAgent.Mail.Rules.SenderDomainRule.Apply(MailItem item) in /src/Mail/Rules/SenderDomainRule.cs:line 34` | `vandijk-mail-triage` |

Dat zijn API-paden van onze koppelingen, de OAuth-scopes waarmee we verbinden, welk model we
gebruiken, hoe onze processen geschaald staan en de indeling van onze broncode. Bij de interne
beheerklant — nu alleen zichtbaar voor een operator, maar hetzelfde mechanisme — staat het
zwaardere spul: `resourceGroup = rg-soratus-bakker`, `customerIds = ["bakker","meijer"]`,
`tool = uren.boeken`, `sprint`, `workItemId`, en via de structured-logging-state van `ILogger`
zelfs `ContentRoot = D:\SORATUS\Website`.

**Besluit: een klant ziet `extra` niet.** De klantweergave van een logregel bestaat uit tijd,
niveau, event, bericht en runId. Geen uitklap, en dus ook geen chevron die niets doet.

**Waarom niet filteren op inhoud.** Dat was de eerste reflex en hij sluit niet. `extra` is vrije
JSON en de sleutelnamen komen van de agentbouwer: een blokkeerlijst met `endpoint` erin is morgen
omzeild door `svcEndpoint`. Een half filter is erger dan geen filter, want het suggereert een grens
die er niet is. De echte grens hoort bij het **schrijven** te liggen, in `Soratus.Agents.Telemetry`,
waar de betekenis van een sleutel bekend is. Dat kost een contractwijziging en werk aan agentkant;
tot die tijd staat het lek open. Dit besluit is een strikte deelverzameling van die oplossing en
sluit die route niet af.

**Hoe het is afgedwongen: het klanttype heeft het veld niet.** Geen `null` met een `@if` eromheen en
geen vlag. `CustomerLogLine` in `Soratus.Portal/Views/AgentLogsView.cs` heeft zes velden en `extra`
zit er niet bij; de operatorvariant draagt het volledige `LogRecord`. Dat is dezelfde regel die
`CustomerAgentsView` al volgt (§9), en hier is hij niet alleen netter maar noodzakelijk: het
logtabblad is een interactief eiland en krijgt zijn parameters over een serialisatiegrens. Een
viewmodel met `extra` erin staat daarmee in de paginabron, ongeacht welke `@if` in de Razor staat.
Wat er niet op het type staat, kan die grens niet over.

**Wat dit besluit níet dekte.** `msg` bleef klantleesbaar en vrije tekst van de agentbouwer. Stond
daar een pad of een interne naam in, dan lekte dat alsnog. Dat is inmiddels gemeten en gedicht;
zie punt 13.

**Wat er openblijft:** een agentbouwer kan klantgegevens van een ándere klant in `extra` zetten en
dan ziet de operator dat op het verkeerde scherm. Dat is een kleiner probleem dan het lek dat hier
is gedicht, maar het is niet nul.

---

## 13. `msg` wordt afgeknipt op de eerste regelovergang

**Aanleiding, gemeten.** Punt 12 dichtte `extra` voor de klant en noemde één gat expliciet:
`msg` is vrije tekst en blijft klantleesbaar. Een verificatie over 19 agents en 120 logregels vond
dat gat bewoond. In `msg` van `bakker-voorraad-sync / payload.dump` stond 3349 tekens met zestien
regels stacktrace — `/src/`-paden, klassenamen, methodenamen, regelnummers — zichtbaar voor een
klant.

**Een filter aan de leeskant kan dit niet.** Dat was bij punt 12 al de conclusie voor `extra` en
geldt hier sterker: de inhoud kan in elk vrij tekstveld staan, niet alleen in het veld dat je
afschermt. De grens moet dus bij het schrijven liggen.

**Wat we eerst wilden en waarom dat niet kon.** Het eerste voorstel was een lengtegrens van de orde
van 300 tekens: een royale Nederlandse zin is rond de 200. Toen zijn alle 93 klantzichtbare
logregels doorgemeten:

| | |
|---|---|
| regels met méér dan één regel in `msg` | 1 |
| regels met verdachte inhoud in de volledige `msg` | 1 |
| regels met verdachte inhoud in **alleen de eerste regel** | **0** |
| langste eerste regel | **1417 tekens** |

Die 1417 maakt elke lengtegrens onbruikbaar. Bij die `payload.dump` is de eerste regel legitiem
Nederlands proza en beginnen de stacktrace-regels daarná:

```
grens 200–500  → lek weg, maar geldig bericht middenin gemangeld
grens 1500     → eerste regel heel + "at SoratusAgent.Sync…Validate(…) in /src/…"
grens 2000     → eerste regel heel + ~580 tekens stacktrace
```

Het middengebied is het gevaarlijkst, want het lijkt de veilige ruime keuze.

**Besluit: knip op de eerste regelovergang** (`\n`, `\r\n`, losse `\r`). Wat erachter stond gaat
naar de gereserveerde sleutel `msgOverflow` in `extra` en is daarmee operator-only. Achter de
overgebleven regel komt `" … (ingekort)"`, zodat een lezer weet dat er meer was en niet denkt dat de
agent halverwege stopte.

**Waarom dit klopt en niet toevallig werkt.** Het is even mechanisch als een lengtegrens — geen
inhoudsheuristiek, nooit "is dit een stacktrace" — maar het volgt rechtstreeks uit de contractregel
die er al stond: één zin, en een zin bevat geen regelafbreking. Op de echte data verdwijnen alle
zestien stacktrace-regels en worden de andere 92 regels niet aangeraakt omdat die al één regel
waren. Nul valse positieven, één ware positief.

Er zit een hygiënegrens van 8000 tekens bij tegen één absurd lange ononderbroken regel. Ruim boven
de gemeten 1417, dus hij gaat in de praktijk nooit af. Slaat hij toch toe, dan wordt er op een
grafeemgrens geknipt: nooit midden in een surrogaatpaar of een samengestelde glyph. Dat is geen
theorie — aan de weergavekant is precies dat defect al aangetroffen in een `message[..400]`, dat een
losse surrogaat in een attribuut achterliet zodra iemand een emoji in een productnaam had.

**De knip staat op twee plekken en is één functie.** Bij het schrijven in
`Soratus.Agents.Telemetry`, en bij het projecteren naar de klant in het portaal. Die tweede dekt wat
de eerste niet kan: de dertig dagen documenten die er al staan, een agent op een oudere
bibliotheekversie, en een agent die de bibliotheek niet gebruikt. Zouden die twee elk hun eigen knip
schrijven, dan bestaan er twee definities van "één zin" en gaan die divergeren — hetzelfde patroon
dat hier al drie keer met gekopieerde CSS is misgegaan. De functie staat daarom in
`Soratus.Agents.Contracts` (`MessageTruncation.Cut`), dat bewust geen afhankelijkheden heeft en door
beide kanten wordt gebruikt. De overloop komt apart terug in plaats van dat de functie hem ergens
neerzet, want het klantpad heeft geen veld om hem in te zetten.

**Het werkt alleen vooruit.** Wat vóór deze wijziging is weggeschreven houdt zijn lange `msg` tot de
TTL het na 30 dagen opruimt. De knip in de projectie vangt dat op het scherm af; de documenten zelf
blijven zoals ze zijn. De seed is opnieuw gedraaid.

**Wat er openblijft:** het portaal moet `msgOverflow` daadwerkelijk renderen. Doet het dat niet, dan
is de stacktrace stil wég in plaats van verplaatst, en dan hebben we bij een gefaalde run juist de
informatie weggegooid die de operator nodig heeft. Dat is een leesbaarheidsregressie in plaats van
een lek, maar wel een echte.

---

## Wat bewust nog niet is gebouwd

Uren, facturatie, sprint en support. Die komen pas als de statusweergave klopt, zoals de
opdracht voorschrijft. Twee besluiten uit §9 van de spec staan daarmee ook nog open: de
Azure-uitsplitsing per dienst (fase 4) en de audittrail op urencorrecties (fase 3).

Voor dat laatste ligt er al een voorstel dat de vraag laat vervallen: sla een handmatige
correctie op als nóg een goedgekeurde urenregel met bron `portaal` en categorie `Correctie`.
Dan blijft het maandtotaal een zuivere som én is de correctie zichtbaar als rij — de spec
vraagt nu om allebei van één getal, en dat kan niet.
