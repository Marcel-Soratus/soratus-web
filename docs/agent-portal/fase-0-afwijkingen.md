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

## 14. `errorType` op een mislukte run is operator-only

**Aanleiding, gemeten.** Punt 13 knipte `errorMessage` af en liet één veld bewust staan: `errorType`.
In de echte opslag hebben 7 van de 112 runs dat veld gevuld, met drie verschillende waarden — en alle
drie bevatten een naamruimte:

| klant | `errorType` |
|---|---|
| `bakker` | `SoratusAgent.Sync.ValidationException` |
| `vandijk` | `SoratusAgent.Mail.ClassificationException` |
| `soratus` | `System.Net.Http.HttpRequestException` |

De eerste twee staan op documenten van **echte klanten**. Anders dan bij een logregel heeft een run
geen operator-only variant: `AgentRunRow` droeg beide foutvelden letterlijk en werd door beide
overloads van `BuildRunsAsync` gebruikt, en het portaal zet ze in de tooltip van de resultaatbadge.

**Besluit: de klant ziet dit veld niet.** Een klant doet niets met een .NET-typenaam. Hij moet weten
dát de run mislukte en of er werk blijft liggen, en dat staat in `errorMessage` — één Nederlandse zin
die de agentbouwer schrijft voor precies deze lezer.

**Waarom niet afkorten, en waarom dat het interessante deel is.** De korte naam na de laatste punt
levert `ValidationException` op. Dat is de reparatie die zich aanbiedt en hij lost niets op: voor een
klant is `ValidationException` even betekenisloos als de volledige naam, en voor de operator gooit
het juist het nuttige deel weg — `Sync.ValidationException` en `Mail.ValidationException` zijn dan
niet meer te onderscheiden, terwijl dat twee verschillende defecten zijn.

Daar zit de asymmetrie met punt 13, en die bepaalt waar de oplossing hoort te zitten:

- bij `errorMessage` **verplaatst** afkappen de informatie. Wat eraf valt blijft operator-only
  bewaard, in `extra` van de bijbehorende `run.failed`-logregel. Daarom kan die knip bij het
  **schrijven** staan: er raakt niets weg.
- bij `errorType` **gooit** afkappen hem weg. Er is geen tweede plek waar de volledige naam blijft
  staan. Een knip bij het schrijven zou de diagnose vernietigen voor de enige lezer die er iets aan
  heeft.

Dus: niet inkorten, niet tonen. De oplossing zit in de **projectie naar de klant** en niet aan de
schrijfkant. Het contract legt aan die kant de tegengestelde regel vast — `errorType` houdt zijn
volledige typenaam — en dat blijft zo.

**Hoe het is afgedwongen: het klanttype heeft het veld niet.** Dezelfde vorm als bij §12 en §9, en om
dezelfde reden: wat er niet is kan niet lekken, ook niet als iemand er over een half jaar een tooltip
bij zet. `AgentRunRow` in `Soratus.Portal/Views/AgentRunsView.cs` is nu abstract en draagt alleen wat
beide rollen mogen zien; `CustomerRunRow` en `OperatorRunRow` staan eronder, en alleen de tweede heeft
`ErrorType`. Beide ontstaan via een eigen expliciete projectie uit `RunRecord` — niet de één uit de
ander, want dan bestaat er een pad van de volle vorm naar de smalle waarlangs een veld kan meeliften.
De weergave eromheen is mee gesplitst (`CustomerAgentRunsView` / `OperatorAgentRunsView`), zodat de
compiler afdwingt welke rij op welk pad terechtkomt.

**Er blijft één runtabel, en dat is geen inconsistentie met §12.** Bij de logs staan er twee tabellen
naast elkaar omdat de klantvariant geen uitklap heeft: een echt verschil in gedrag. Bij de runs is het
enige verschil de tekst in één tooltip, en de kolommen en het `RowGrid` zijn identiek. Twee tabellen
zouden hier een tweede kopie van dezelfde kolomsporen betekenen — precies wat §6 verbiedt. `RunsTable`
neemt daarom het basistype en vraagt de rij om de tekst (`FailureDetail`) in plaats van hem zelf uit
velden samen te stellen. Reikt de tabel ooit tóch naar `ErrorType`, dan vraagt dat een cast, en een
cast is zichtbaar in review.

**Wat bij het nakijken nog naar boven kwam, en meeveranderd is:**

1. **Het type stond er alleen als de melding leeg was.** De tooltip nam `errorType` als
   terugvaloptie. Gevolg: in de gewone weergave zag de operator hem nooit — de seeder wéigert een
   mislukte run zonder `errorMessage` — en de klant zag hem precies dán wel, namelijk als een agent
   zijn boodschap vergat. De verkeerde kant op, bij beide rollen. De operator krijgt nu melding én
   type, gescheiden door een `·`.
2. **`AgentRunSummary.ErrorType` had geen enkele lezer.** Dat type hangt via
   `CustomerAgentRow.LastRun` aan de klantweergave van de agentlijst en de agentkop, en het veld werd
   geprojecteerd en nooit afgedrukt. Een veld dat niemand leest en dat onze naamruimtestructuur bij de
   klant neerlegt is verwijderd in plaats van gesplitst; de operator vindt de typenaam op het
   runtabblad.
3. **`errorMessage` op een run had geen knip aan de leeskant.** Punt 13 zette die knip op twee
   plekken voor logberichten — bij het schrijven en in de klantprojectie — maar op een run stond
   alleen de schrijfkant. Dat gat is groter dan bij de logs: logregels leven 30 dagen en runs 400 (zie
   §8), dus elk rundocument dat er vandaag staat is weggeschreven vóór de knip bestond, en de
   foutmelding gaat op het klantscherm in een tooltip. `CustomerRunRow.From` en `AgentRunSummary.From`
   gebruiken nu dezelfde `CustomerMessage.FirstLine` als de logprojectie. Er is een test die de
   vindplaatsen afgaat in plaats van de regel, want een knip op twee van de drie plekken is geen knip.

**Wat er openblijft.** `errorMessage` blijft vrije tekst van de agentbouwer: staat er een interne naam
of een pad in de eerste zin, dan lekt dat alsnog, en daar is aan deze kant niets tegen te doen. Dat is
dezelfde restrisico als bij `msg` (§13) en het staat als eis in het contract. En zoals bij §12: een
agentbouwer kan in `errorMessage` gegevens van een ándere klant zetten. Kleiner dan het lek dat hier is
gedicht, maar niet nul.

---

## 15. Een contractbedrag dat ontbreekt is niet nul

**Niet in de spec, wel een besluit van dezelfde soort als §2 en de Entra-toestand.**

`bundelUren`, `uurTarief` en het Azure-opslagpercentage waren niet-nullable `decimal`. Daarmee
zijn "nul" en "niet ingevuld" dezelfde waarde, en dat is bij een bedrag geen detail.

**Drie plekken waar dat stil misging, gevonden bij het nullable maken:**

Een klant aanmaken zonder tarief legde `uurTarief: 0` vast. De parser gaf een leeg veld terug
als `0m` en het formulier schreef dat getal onvoorwaardelijk weg. Een operator die het tarief
nog niet wist, legde daarmee een bedrag vast dat hij nooit heeft ingetypt en dat als afspraak
in de opslag staat. Bij onleesbare invoer gebeurde hetzelfde.

En de spiegel daarvan: een contract met een afgesproken nul openen en op Bewaren drukken
veranderde die nul in "niet vastgelegd" — zonder toetsaanslag, en zonder dat de conflictlijst
er iets over meldde, want die vergelijkt de formuliertekst en niet de waarde.

De uitleg onder het tariefveld bevestigde de conflatie ook nog: "leeg of nul betekent: geen
tarief buiten de bundel". Een operator vult op grond van zo'n regel iets in.

**Waarom dit nu goedkoop was:** er wordt in het portaal nergens met deze drie getallen
gerekend. Uren en facturatie zijn fase 3 en 4 en bestaan nog niet. Het opslagpercentage is
daarbij het gevaarlijkste veld — nul procent opslag is een afspraak, geen opslag ingevuld is
een afspraak die nog moet komen, en het verschil is onze marge.

**Bestaande documenten zijn bewust niet gemigreerd.** Daar staat `"bundelUren": 0`, en van zo'n
document is niet te weten of die nul een afspraak was. Er achteraf `null` van maken gooit
misschien een echte afspraak weg. Nieuwe documenten schrijven `null` uit.

Dit is dezelfde regel als "geen document betekent geen status" (§2) en als de drie
Entra-toestanden in plaats van een `bool`: een waarde die "onbekend" moet kunnen uitdrukken,
kan dat niet met een getal dat ook een geldig antwoord is.

---

## 16. Een handmatige urencorrectie is nóg een urenregel, geen ander getal

**Spec:** §3.6 vraagt twee dingen van hetzelfde getal. "**Eén bron van waarheid**: het maandtotaal
is de som van de gefiatteerde regels." En in dezelfde alinea: "Een handmatige correctie is
mogelijk maar wordt als afwijking in de tooltip gemeld." De acceptatie van fase 3 herhaalt de
eerste helft als eis.

**Waarom dat niet allebei kan.** Van één getal kan het niet. Overschrijft de correctie het
maandtotaal, dan is het geen som meer — dan staat er een getal boven een tabel die iets anders
optelt, en is er voor de klant geen manier om te zien welk van de twee klopt. Negeert de som de
correctie, dan doet de correctie niets. De mockup kiest de eerste variant: `hourEdits` is een
override op het maandtotaal, en de tooltip zegt dan "Handmatig gecorrigeerd — specificatie telt
n u". Dat is precies de tegenspraak die regel 7 verbiedt, en hij staat in dezelfde rij.

**Besluit: een correctie wordt opgeslagen als een extra urenregel.** Bron `portaal`, categorie
`Correctie`, stand `approved`, en het aantal uren mag negatief zijn. Verder een gewoon
`HourEntry`-document, met `createdAt`, `createdBy` en een verplichte omschrijving.

**Wat dat oplevert:**

1. Het maandtotaal blijft een zuivere som. Er bestaat geen veld waarin een afwijkend totaal
   past — `HourBalance.Booked` is de som van de gefiatteerde regels en niets anders, en
   `HourBalanceCalculator.ForMonth` filtert daar zelf op in plaats van het aan de aanroeper te
   laten.
2. De correctie is zichtbaar als rij in de specificatie, met wie hem maakte en waarom. §3.6 wil
   dat hij "gemeld" wordt; een rij is een sterkere melding dan een tooltip.
3. De tooltip is alsnog te vullen, en met een getal dat betekenis heeft:
   `HourBalance.CorrectionHours` is hoeveel van het maandtotaal uit correcties komt.
4. **De openstaande vraag uit §9 vervalt.** Die vraagt of er per correctie een audittrail
   bijgehouden moet worden — wie, wanneer, waarom. Dat is nu geen aparte voorziening maar het
   document zelf, en het staat op het scherm in plaats van in een tabel die niemand opvraagt.

**Waarom een eigen type en niet een vlag op de boeking.** `HourCorrection` staat naast
`HourBooking` om precies één verschil: de uren mogen negatief zijn. Met één type en een vlag
wordt de controle "groter dan nul" een `if` op die vlag, en dan is een negatieve *boeking* één
verkeerd geschreven `if` ver weg. Nu weigert de boeking alles wat niet positief is, en weigert de
correctie alleen nul.

**Wat er tegenover staat, eerlijk.** Een maand corrigeren kost nu twee rijen op het scherm waar
het er één was, en een klant ziet de correctie. Dat tweede is geen bijzaak maar noodzaak: zou de
klant de correctierij niet zien, dan telt zijn specificatie niet op tot zijn maandtotaal — en dan
is de eigenschap waarvoor dit hele besluit bestaat weg op het enige scherm waar hij te
controleren valt.

---

## 17. Een afgewezen urenregel blijft staan, met de reden erbij

**Spec:** §3.6 geeft de operator de acties Fiatteren en Afwijzen. Wat afwijzen met de regel doet
staat er niet.

**De twee opties.** Verwijderen houdt de lijst schoon; bewaren houdt het antwoord op "waarom staat
dit niet op mijn factuur". Dat tweede argument alleen was niet beslissend — een lijst die volloopt
met regels die niet meetellen is een echt bezwaar.

**Wat het wél beslist: idempotentie tegen een koppeling die herhaalt.** Een MCP- of DevOps-regel
draagt een id die uit de bron is afgeleid (`HourEntryKeys.ForIntegration`), zodat een herhaalde
aanroep na een netwerkfout op een 409 loopt in plaats van een tweede uur te factureren. Wordt een
afgewezen regel verwijderd, dan slaagt die herhaling — en staat de regel bij de volgende run van
de koppeling opnieuw als te fiatteren in de lijst. Afwijzen zou dan geen besluit zijn maar een
handeling die je blijft herhalen, en de operator die het door heeft gaat de lijst wegkijken.

**Besluit: de regel blijft staan met `status: rejected`, `rejectedAt`, `rejectedBy` en een
verplichte `rejectReason`.** Hij telt in geen enkele som mee en is voor de klant onzichtbaar — dat
laatste dubbel: hij is niet gefiatteerd, en de klantquery vraagt alleen om gefiatteerde regels.

**Het bezwaar is opgelost in de weergave en niet in de opslag.** Afgewezen regels staan niet in de
specificatie maar in een eigen lijst eronder (`OperatorHoursView.Rejected`), die er niet is als hij
leeg is. Dat is de gewone toestand.

**Waarom dit anders is dan een ingetrokken portaaltoegang**, waar de afwezigheid van het document
juist het antwoord is (zie de opmerkingen bij `AccessDocument`): daar *is* het document het recht,
hier is het een bewering van een koppeling waarover een oordeel is gegeven. Een recht dat je
intrekt hoort te verdwijnen; een bewering die je afwijst niet.

---

## 18. Gefiatteerd is definitief, en de bundel rolt niet door

Twee kleinere besluiten die uit dezelfde eigenschap volgen: het maandtotaal van een afgesloten
maand mag niet met terugwerkende kracht veranderen.

**Een gefiatteerde regel kan niet meer worden afgewezen.** Kan een uur later uit de som verdwijnen,
dan is het totaal van vandaag niet dat van gisteren, en dan wijkt een conceptfactuur af van de
maand waarover hij gaat zonder dat er iets is toegevoegd. Terugdraaien gebeurt met een correctie
ertegenover (punt 16) — een tweede rij die zichtbaar is en zichzelf verklaart. De prijs is één
handeling extra voor een operator die per ongeluk fiatteert; dat is de ruil. De regel staat in
`HourEntryTransitions`, en de weergave gebruikt diezelfde functie om te bepalen of er een knop
hoort te staan, zodat er nooit een knop staat die een melding oplevert.

Omgekeerd kan een *afgewezen* regel wél alsnog gefiatteerd worden. Afwijzen is een besluit van een
mens en mensen klikken mis; was dat onomkeerbaar, dan was de enige uitweg de koppeling opnieuw
laten inschieten — en dat kan niet, want de idempotentiesleutel botst op het document dat er al
staat (punt 17).

**De uren boven bundel over een jaar zijn de som van de overschrijdingen per maand**, en niet het
jaartotaal minus de jaarbundel. De bundel is een afspraak per maand (§3.5) en rolt niet door: een
maand met vier uur over betaalt niet voor een maand met vier uur te veel. Wordt het jaarbedrag uit
de jaartotalen berekend, dan salderen die twee maanden elkaar en verdwijnt de overschrijding uit de
facturatie. De mockup doet dat wel (`max(0, totSpent - totBundel)`); dat is een fout die op
dummy-data niet opvalt en op een factuur wel.

Om dezelfde reden telt het jaaroverzicht **niet altijd twaalf maanden bundel** maar alleen de
maanden die binnen de contractperiode vallen en al zijn begonnen. Een bundel voor een maand die nog
niet is begonnen is geen tegoed. Een maand waarop tóch uren zijn geboekt valt nooit weg, ook niet
als hij buiten die periode ligt — uren die uit het overzicht verdwijnen omdat een grens ze
uitsluit, verdwijnen ook uit het jaartotaal, en dat is stil.

---

## 19. Er is een vierde urenstand nodig: "geen bundel vastgelegd"

**Spec:** §3.6 kent drie standen per maand — Binnen bundel, Boven bundel, Niets geboekt.

**Probleem:** sinds punt 15 mag `bundelUren` `null` zijn. Dan bestaat de maand waarin uren staan
terwijl er geen bundel is afgesproken, en die is geen van de drie. Hem als "Boven bundel" tonen —
wat er gebeurt zodra iemand `?? 0m` schrijft — zegt dat een klant zijn bundel overschrijdt die er
nooit een had. Dat is precies het stille misgaan dat punt 15 wilde voorkomen, en het saldo is de
plek waar het gebeurt.

**Besluit:** `HourMonthStatus.NoBundleAgreed`, met `Balance` en `OverBundleHours` op `null` in
plaats van een negatief getal. Voor de kleur volgt de stand punt 3: geen nieuwe kleur en geen
nieuwe rang, maar rang 0 hergebruiken (`#767c94` / `#f6f7fb` / `#e3e5ee`, glyph `–`). Er is niets
mis; er is alleen niets om aan te toetsen. `StatusVisuals` in `Components/Shared` heeft daar een
regel voor nodig.

**Dit is geen theoretisch geval.** In `platform/customers` staan vandaag zeven klantdocumenten en
géén enkel contractdocument. Élke klant valt dus op dit moment in deze vierde stand, en met een
niet-nullable bundel zou élke klant met geboekte uren "Boven bundel" hebben gestaan.

---

## 20. Een urenregel heeft geen `date`, alleen een `createdAt`

**Spec:** §6 geeft `HourEntry` een veld `date` en zegt niet wat het betekent. De mockup laat het twee
dingen zijn: de seed-regels hebben datums verspreid over de maand (dat leest als de dag waarop het werk
is gedaan) en een nieuwe boeking krijgt `DATA.now` (dat is de dag van vastleggen).

**Waarom alleen de tweede betekenis voor élke bron waar kan.** §3.6 geeft het boekformulier maand, uren,
categorie, boeker en omschrijving — géén datumveld, dus een operator kán geen werkdatum opgeven. De
MCP-tool uit §5 heeft evenmin een datumparameter. Alleen `devops-sync` zou er een kunnen leveren, uit de
revisie van het work item.

**Eerste besluit, en het onvolledige: `date` betekent de dag van vastleggen.** Zou het veld "werkdatum
waar we die hebben, en anders de dag van vastleggen" betekenen, dan betekent de datumkolom in de
specificatie twee verschillende dingen afhankelijk van de rij — precies het defect dat bij
`AgentRunRow.Duration` is afgewezen, waar "de tijd die hij al bezig is" en "zijn duur" niet in dezelfde
kolom mochten staan.

**Wat er bij het opschrijven pas zichtbaar werd: dan is `date` een duplicaat van `createdAt`.** Dat veld
staat er al, het is hetzelfde moment, en het is canoniek UTC in plaats van een kalenderdag in
Nederlandse tijd. Twee velden over hetzelfde moment op verschillende korrel en in verschillende
tijdzones kunnen van elkaar gaan afwijken, en dan is niet te zeggen welke van de twee de specificatie
haalt. Dat is een zwaarder gebrek dan een misleidende naam, en het is dezelfde reden waarom `tarief`
niet naast `uurTarief` op het contract staat en waarom een agent zijn eigen status niet publiceert
(punt 2).

**Besluit: `date` bestaat niet op een urenregel.** Er is één tijdstip, `createdAt`, en de specificatie
laat daaruit de Nederlandse dag zien onder de kop **Geboekt** — niet "Datum", want dat woord belooft de
werkdatum. Het omrekenen gebeurt bij het weergeven en niet bij het opslaan (punt 7), op één plek
(`HourDay.Of`), want twee plekken lopen op de zomertijdgrens uiteen. De sortering van de specificatie
loopt over `createdAt` en niet over een kalenderdag; dat is fijner van korrel en breekt gelijke stand
vanzelf.

**De werkperiode zit in `month`, en dat is waarom dat veld bestaat.** Werk van 31 juli dat op 1 augustus
wordt vastgelegd heeft `createdAt` op 1 augustus en `month` op juli. Dat laatste is de vraag die de
facturatie stelt.

**Wat openblijft:** een aparte `workDate` voor de bronnen die er wél een hebben, als tweede veld met een
eigen naam en een eigen kolom. Nooit als tweede betekenis van dit veld.

---

## 21. De MCP-tool heet `uren_boeken` en niet `uren.boeken`

**Spec:** §5 schrijft de tool letterlijk zo op:
`uren.boeken({ klant, maand, uren, categorie, omschrijving })`.

**Afwijking:** de tool wordt geregistreerd als `uren_boeken`. Alleen de naam; de vijf parameters en
hun betekenis zijn ongewijzigd.

**Waarom het niet anders kan.** De Messages-API van Anthropic eist dat een toolnaam past op
`^[a-zA-Z0-9_-]{1,64}$`. Claude Code stuurt de naam van een MCP-tool niet los mee maar met zijn eigen
voorvoegsel, als één toolnaam: `mcp__soratus-uren__uren.boeken`. Een punt daarin levert
`400 tools.N.custom.name: String should match pattern` op.

**Wat het duur maakt is niet de fout maar waar hij valt.** Die 400 komt bij **elke prompt in de
sessie**, ook een die niets met uren te maken heeft — de toolomschrijvingen gaan bij ieder verzoek mee.
Het symptoom is dus "Claude Code werkt niet meer" en niet "het boeken van uren werkt niet", en niemand
zoekt de oorzaak bij een tool die hij niet heeft aangeroepen. Vandaar dat de naam een test heeft
(`ToolvormTests`) die hem tegen het patroon houdt, inclusief het voorvoegsel: zonder die test valt dit
pas op nadat iemand de server heeft aangesloten.

**Het is een clientgrens en geen protocolgrens**, en dat is het vermelden waard omdat het bepaalt waar
de oplossing hoort. De MCP-specificatie stelt geen eis aan een toolnaam; de eis komt van de API
waarlangs Claude Code praat. Zou de tool ooit door een andere client worden gebruikt, dan is de punt
daar geen probleem — maar deze server bestaat voor Claude Code, dus die grens is de bindende. Hetzelfde
soort correctie als §4 en §11: de spec beschrijft een aanname die met de platformkeuze is vervallen.

**Wat er tegenover staat, zodat het niet stil verdwijnt:** `uren.boeken` staat in de `title` van de
tool ("Uren boeken in het Soratus Agent Portal (uren.boeken)"), in de beschrijving en in
[`mcp-uren.md`](mcp-uren.md), zodat wie op de naam uit de spec zoekt hem vindt.

**Terzijde, uit dezelfde ronde:** de parameternamen zijn Nederlands (`klant`, `maand`, `uren`,
`categorie`, `omschrijving`) en dat is géén afwijking van de conventie "Engelse identifiers". Het
C#-SDK gebruikt de parameternaam letterlijk als veldnaam in het JSON-schema van de tool, dus dit zijn
geen identifiers die wij kiezen maar de publieke vorm die §5 vastlegt. Er staat een test op, zodat een
refactor die ze hernoemt de vorm uit de spec niet stil verandert.

---

## 22. Het omgevingsbeheer bestond als viewmodel en niet als scherm

**Spec:** §3.9 laat een operator een klant aanmaken met alle velden, waaronder de omgeving en de
subscription met resource group. §2 geeft hem op contract en toegang lezen én bewerken. Over
corrigeren ná het aanmaken zegt de spec niets, en dat gat is precies waar dit misging.

**Wat er stond.** `OperatorContractView` droeg `Environment`, `EnvironmentDetail` en `CustomerETag`,
de projectie in `ContractViews` vulde ze, en `ContractZichtbaarheidTests` bewaakte dat het klanttype
ze níet had. Alleen: geen enkel scherm rendeerde ze, en `IPortalDataStore.SaveCustomerAsync` werd
nergens aangeroepen. Het contractscherm deed het contract en de toegangen; de omgeving was
alleen-lezen data die niemand las.

**Wat een mens hieruit verkeerd zou concluderen.** Twee dingen, en de tweede is de erge. Wie de
zichtbaarheidstest groen zag staan, concludeerde dat het operator-only veld goed was afgeschermd —
en dat was ook waar, maar het is de verkeerde vraag. Er stond nergens een test op de tegenhanger:
*kán iemand het zien.* Dit is dezelfde vorm als §14, waar `errorType` alleen in de tooltip stond als
de foutmelding leeg was — het veld bestond, de klant zag het soms, en de operator nooit. De test op
typeniveau is daar tevreden omdat het veld *bestaat*, niet omdat iemand er iets mee kan.

De tweede: wie de acceptatie van fase 2 las — "een nieuwe klant kan volledig zonder database-actie
worden ingericht" — mocht aannemen dat dat ook voor een correctie gold. Dat gold niet. Een verkeerd
getypt subscription-id, een verkeerde Cosmos-endpoint of een verkeerd gespelde klantnaam was na het
aanmaken alleen nog met de hand in Cosmos te herstellen. Aanmaken lukte, corrigeren niet, en een
tikfout in een subscription-id is de eerste week en geen randgeval.

**Wat er nu staat.** Een tweede kaart op het operator-eiland van het contractscherm, boven de
contractkaart: klantnaam, korte omgevingsaanduiding, subscription met resource group,
Cosmos-endpoint en databasenaam. Hij schrijft via `SaveCustomerAsync`, met de etag uit het formulier
en niet uit een verse lezing, en handelt een botsing af zoals de contractkaart dat doet — met de
verschillenkaart eronder en een etag die opschuift, zodat een tweede klik de eigen waarden alsnog
vastlegt maar pas nadat de operator heeft gezien wat hij overschrijft. De verschillenkaart is één
`RenderFragment` die beide kaarten gebruiken; twee kopieën van die lus zouden uit elkaar gaan lopen
zoals de gekopieerde CSS uit §6.

Twee kaarten met twee knoppen en niet één, want het zijn twee documenten met twee etags. Eén kaart
zou van elke correctie op een subscription-id een gelijktijdigheidsbotsing maken op een
contractdocument dat de operator niet heeft aangeraakt.

**Wat er níet te wijzigen is, en dat blijkt uit het scherm.** Het klant-id staat er als platte tekst
en niet als uitgegrijsd vak: §8 zegt dat read-only platte tekst is, en een uitgegrijsd vak belooft
dat het ooit open gaat. Dat gaat het niet — de slug is de partitiesleutel van élk document van deze
klant en de `customerId` waaronder zijn agents publiceren, dus hem wijzigen is een migratie en geen
bewerking. `CustomerEdit` heeft het veld daarom ook niet, en daar stond al een test op. Of dit een
interne beheeromgeving is staat er om dezelfde reden als tekst: dat raakt de facturatie (§4).

**Twee dingen die bij het bouwen naar boven kwamen en zijn meegegaan.**

De eerste is een stille bewering van een `bool`, en het is dezelfde familie als §15. `SaveCustomerAsync`
vervangt het hele klantdocument en zette `IsInternal = current?.IsInternal ?? false`. Bij een klant
die nog geen document heeft — die alleen uit de configuratie komt, en dat is de klant wiens
inrichting je zit te repareren — legde de eerste wijziging daarmee `isInternal: false` vast, ongeacht
wat de configuratie zei. Bij de interne beheerklant maakte één klik hem tot een gewone,
factureerbare klant: stil, en zonder dat de verschillenkaart er iets over kon zeggen, want het
formulier heeft dat veld niet. `CustomerEdit` draagt hem nu door en de schrijfkant leest
`current?.IsInternal ?? edit.IsInternal` — wat er staat gaat vóór wat het formulier meestuurt, dus
een bestaande klant kan hier niet omslaan en een formulierfout kan geen schade doen. Dat is dezelfde
regel als bij een contractbedrag: een waarde die "niet ingevuld" moet kunnen uitdrukken, kan dat niet
met een type waarin die toestand niet bestaat. Bij een `bool` is de standaardwaarde geen leegte maar
een bewering, en hier luidde die bewering "deze klant is factureerbaar".

De tweede: `TelemetryEndpoint` en `TelemetryDatabase` stonden niet op het operatortype en horen daar
wel. Niet omdat het scherm ze zo graag toont, maar omdat het ze moet terugsturen — een veld dat het
formulier niet draagt wordt door een volledige vervanging leeggemaakt. Zonder die twee zou een
operator die de klantnaam verbetert de telemetrie van die klant afsluiten, waarna het overzicht
"status onbekend" zegt en niemand weet waardoor. Ze staan als operator-only opgesomd in
`ContractZichtbaarheidTests`, met `CustomerChangedAt` en `CustomerChangedBy`, die er zijn omdat het
klantdocument een eigen geschiedenis heeft naast die van het contract.

**Twee kleinere onwaarheden die hierbij zijn rechtgezet.** De melding voor de niet-gemigreerde klant
zei "je eerste wijziging hier legt het klantdocument alsnog aan"; geen enkele knop op dat scherm kon
dat, want een contractwijziging schrijft alleen het contractdocument en een toegang alleen een
toegangsdocument. Nu is er één kaart die het wél doet en de melding wijst die aan. En de opslag in
het geheugen van het testproject liet een bewerking zónder etag op een klant die inmiddels wél een
document had gewoon slagen, terwijl `UpsertAsync` in dat geval een `CreateItemAsync` doet en op een
409 loopt. De fixture overschreef dus stil waar productie een botsing geeft — precies in het geval
van de klant die alleen uit de configuratie komt, en dus precies onder de test die dat geval meet.

**Wat er openblijft.** De kop van het contractscherm komt uit de scope die de pagina bij het openen
heeft opgehaald, en die pagina is static SSR. Wijzigt een operator de klantnaam, dan volgt het
eiland maar blijft de kop tot een herlaadslag de oude naam dragen. Dat staat als voetregel onder de
kaart in plaats van dat het een verrassing is; het weg te werken zou vragen dat een eiland de pagina
eromheen laat hertekenen, en dat is precies wat de render-mode-grens niet toestaat.

---

## 23. Drie cijfers achter een scheidingsteken is een duizendscheiding en geen bedrag

**Niet in de spec, wel een besluit over een getal dat op een factuur belandt.**

**Wat er stond.** `ContractText.TryNumber` probeerde de invoer eerst in `nl-NL` en dan invariant,
beide zonder `AllowThousands`. Die dubbele cultuur is er met een goede reden: "125.50" is wat een
browser teruggeeft voor een `type="number"`-veld waarin een Nederlander 125,50 typte, en dat moet
doorkomen. De prijs stond in een test met een eerlijke naam:
`EenPuntIsAltijdEenDecimaaltekenOokWaarIemandDuizendenBedoelde`. "1.250" werd 1,25 en "12.500" werd
12,5 — een factor duizend, stil, met `true` als uitkomst. "1.250,50" werd wél geweigerd, want twee
scheidingstekens in één getal kan in geen van beide culturen.

**Wat een mens hieruit verkeerd zou concluderen.** Dat de afruil onvermijdelijk was. Zo was hij ook
opgeschreven en verdedigd, en dat klonk sluitend: je kunt een punt niet tegelijk als decimaalteken
accepteren en als duizendscheiding weigeren. Alleen is dat niet waar — het *aantal cijfers achter het
scheidingsteken* maakt het onderscheid, en dat stond nergens. Een groep van een duizendscheiding is
per definitie exact drie cijfers lang. Eén of twee cijfers kan dus geen groep zijn, vier of meer ook
niet, en alleen bij exact drie zijn de twee lezingen niet te scheiden.

Wat het kostte: een uurtarief boven de duizend typt een Nederlandse operator als "1.250", het veld
is `FieldKind.Amount` en dus vrije tekst, en er kwam een tarief van € 1,25 in de opslag zonder één
melding. Dat is een factuurfout die niemand ziet tot de klant belt.

**Wat er nu staat.** Bij exact drie cijfers achter één enkel scheidingsteken weigert `TryNumber`, en
de melding onder het veld vraagt om een komma: hij zegt dat dit een duizendscheiding kan zijn of een
decimaalteken, dat het verschil een factor duizend is, en wat de operator dan moet typen. Alle andere
gevallen gaan ongewijzigd door — "125.5", "125.50", "1.2500" en "125." komen door, en "1.250.000" en
"1.250,50" blijven geweigerd door de parser zelf omdat twee scheidingstekens nergens kunnen. Dat
laatste is geen dubbelzinnigheid maar een fout, dus daar staat de algemene melding en niet de vraag;
welke van de twee het wordt, beslist dezelfde functie die de weigering doet, zodat er geen melding
kan verschijnen die niet bij de reden van de weigering hoort.

**Eén regel en niet twee, en de komma valt eronder.** De regel is niet "de punt is verdacht" maar
"drie cijfers achter een scheidingsteken is een groep". Dat de komma er net zo goed onder valt is
geen symmetrie om de symmetrie: bij alle drie de getallen die deze parser bedient — een urenbundel,
een uurtarief in euro's en een opslagpercentage — is een derde decimaal zinloos, dus een waarde met
exact drie cijfers achter het scheidingsteken is nooit een geldige waarde van zo'n veld, welk teken
er ook staat. Alleen de punt afvangen zou het gat open laten voor een bedrag uit een Engelse bron:
`nl-NL` leest "1,250" als 1,25, en dat is dezelfde factor duizend de andere kant op. En het houdt de
regel vrij van veldkennis, wat de voorwaarde is om één parser voor drie schermen te hebben.

**Wat het kost, eerlijk.** "0,500" en "12,500" zijn in het Nederlands te lezen als 0,5 en 12,5 met
een overbodige nul erachter, en die worden nu geweigerd. Dat is de prijs. Hij is klein en hij is
zichtbaar: de operator ziet een melding en typt "0,5". De fout die hiervoor wegvalt was onzichtbaar
en stond op een factuur.

**De regel geldt op elke vindplaats van de melding, en dat is de reden dat er één is.** Het
contract-eiland en het aanmaakformulier geven de invoer mee, zodat ze de scherpe melding krijgen; de
signatuur laat dat argument weg zolang niemand het meegeeft, en dan verschijnt de algemene melding —
die niet onwaar is, maar wel minder scherp bij precies het geval waar hij het meest te zeggen heeft.
Er is nog één aanroeper die hem niet meegeeft: `HourFormText.HoursError` in `Components/Pages/Klant/`.
Die weigert "1.250" dus wel, maar met de algemene melding. Dat bestand hoort bij fase 3 en de
wijziging is één regel; hij staat hier zodat hij niet als "bijna overal goed" wegzakt.

---

## 24. Razorcommentaar hoort niet in een `@code`-blok

Dit is geen afwijking van de spec maar een val in het gereedschap, en hij staat hier omdat hij een
uur werk heeft gekost en elke `.razor` met overloads hem kan krijgen.

**Aanleiding:** `ContractPanel.razor` gaf `CS1503: cannot convert from 'CustomerDocument' to
'ContractDocument'` op de aanroep `Changes(_customerConflict)`, terwijl `Changes(CustomerDocument)`
twintig regels lager in dezelfde klasse stond.

**Oorzaak:** een `@* … *@` verderop in datzelfde `@code`-blok. Razor knipt het blok daar in twee
gegenereerde stukken, en twee overloads van dezelfde naam aan weerszijden van die knip zien elkaar
niet meer. De naamzoektocht vindt alleen de eerste en klaagt dan over een conversie die niemand heeft
gevraagd. Nagemeten op de gegenereerde `ContractPanel_razor.g.cs` met
`-p:EmitCompilerGeneratedFiles=true`: beide methoden staan wél in dezelfde klasse, dus het is puur
die knip. In twee richtingen bewezen — de overload hernoemen laat de fout verdwijnen, en alléén het
commentaar op `//` zetten laat hem óók verdwijnen zonder hernoeming.

**Regel:** commentaar binnen `@code` altijd met `//` of `///`. Razorcommentaar hoort in de markup.

**Waarom dit duur is:** de melding is een typeconversiefout op een regel die klopt, dus de eerste
reflex is de aanroep of het type aanpassen — precies de twee dingen die niet stuk zijn. De fout wijst
naar de verkeerde plek, en dan zoek je daar. `Components/` is gescand: dit was de enige vindplaats.

Er zit een tweede les onder. Dit kon ontstaan doordat twee sessies in hetzelfde bestand werkten: aan
het contract-eiland is een omgevingsblok van ruim vierhonderd regels toegevoegd door iemand anders
dan de auteur. Het blok is functioneel in orde, maar één bestand met twee auteurs is de plek waar dit
soort dingen ontstaat. Bij verder werk in `Components/Pages/Klant/` hoort een bestand aan één sessie
toegewezen te worden, of het blok een eigen eiland te krijgen.

---

## 25. De schrijfkant van het portaal schreef tijden niet canoniek weg

Punt 7 zegt dat opslag canoniek UTC met vaste breedte is en dat er een assertie op staat. Dat gold
voor de agentkant. Voor de schrijfkant van het portaal gold het niet, en dat was aan niets te zien
omdat er nergens op een tijdveld werd gesorteerd.

**De gemeten fout.** Eén klantdocument door de opties die het portaal aan de Cosmos-SDK gaf, naast
hetzelfde document door de gerepareerde opties:

```
STANDAARD >>> …,"createdAt":"2026-08-20T17:04:05.678+02:00","changedAt":"2026-08-20T17:04:05.678+02:00",…
PORTAAL   >>> …,"createdAt":"2026-08-20T15:04:05.6780000Z","changedAt":"2026-08-20T15:04:05.6780000Z",…
```

Een offset in plaats van een `Z`, en een variabel aantal decimalen. Cosmos bewaart deze velden als
tekst en `ORDER BY` vergelijkt lexicografisch, dus dat sorteert stil verkeerd — geen fout, een
verkeerde volgorde die eruitziet als een goede. Bij de urenregels van fase 3 wordt dat een val:
`ORDER BY c.createdAt` is de optimalisatie die zich aanbiedt en juist die sorteert verkeerd.

**Eén implementatie in plaats van drie.** De normalisatie stond in `Soratus.Agents.Telemetry` en was
daar `internal`; het seed-gereedschap had er een eigen kopie van, met in de eigen documentatie de
toegift dat de assertie erop niet kon zien of de twee nog gelijk wáren. Een derde exemplaar in het
portaal zou de reeks afmaken. De regel staat nu in `TimestampNormalization` in
`Soratus.Agents.Contracts` — het project dat agents en portaal al delen, dezelfde afweging als bij
`MessageTruncation` — en de drie schrijvers roepen alle drie `Register` en `AssertCanonical` aan op
precies de opties waarmee zij schrijven. "Nog gelijk zijn" is daarmee geen meting meer maar een
eigenschap van de code.

**Hoe bewezen is dat het portaal nu canoniek schrijft.** Niet door naar de code te kijken. De opties
zijn `internal` gemaakt zodat een test het echte exemplaar kan pakken in plaats van een nagebouwde
kopie — het nabouwen van die opties is precies de fout die hier gerepareerd wordt.
`Soratus.Portal.Tests/Portaalgegevens/PortaaltijdvormTests.cs` pint op `ReferenceEquals` vast dat het
object dat de SDK als `UseSystemTextJsonSerializerWithOptions` krijgt hetzelfde object is dat de
overige tests uitoefenen, en serialiseert daarmee de echte documenttypen. Dat laatste is nodig omdat
een `[JsonConverter]` op een property vóór een converter in de opties gaat en een tijdveld dat als
`string` is gemodelleerd er helemaal langs heen gaat; geen van beide is aan de opties te zien.

**De assertie had twee blinde vlekken, en die zijn gemeten en gedicht.** Dit is het deel dat de
reparatie bijna stil onvolledig liet.

1. **De `DateTime`-proef kon een ontbrekende converter niet zien.** Haar enige waarde had zeven
   gevulde decimalen, en juist die schrijft `System.Text.Json` van zichzelf al canoniek:
   `2026-08-19T15:13:19.9449045Z`, 28 tekens, afsluitende `Z`. Gemeten met de standaardopties:

   ```
   944 ms + 9045 ticks (7 cijfers gevuld) -> 2026-08-19T15:13:19.9449045Z  (28 tekens)  ← canoniek!
   944 ms exact (3 cijfers)               -> 2026-08-19T15:13:19.944Z      (24 tekens)
   geen fractie                           -> 2026-08-19T15:14:00Z          (20 tekens)
   ```

   Met alleen de `DateTimeOffset`-converter geregistreerd bleef `AssertCanonical` dus groen, terwijl
   het `DateTime`-pad — de structured-logging-state van een agent, in hetzelfde document — open
   stond. Er is een tweede proef bij met afsluitende nullen, die de standaard wél tot 24 tekens
   trimt.

2. **De volgordecontrole kon niet afgaan.** Zij stond er met vier momenten uit één augustusdag, en
   die vier sorteren op tekst toevallig hetzelfde als op tijd — óók helemaal zonder normalisatie. De
   controle las als dekking en was leeg. De reeks is vervangen door vijf momenten die per foute vorm
   nagemeten zijn: twee momenten in dezelfde seconde met en zonder decimaaldeel (betrapt wisselende
   breedte), een moment in `+02:00` dat tussen twee UTC-momenten valt (betrapt een niet-omgerekende
   offset), en een moment in een andere maand (betrapt een formaat met de dag vooraan).

**De assertie gaat af bij het eerste gebruik en niet bij het opstarten.** In het portaal is het een
statische veldinitialisatie in `CosmosClientCache`, en die loopt lui. Dat is nog altijd vóór elke
lees- of schrijfactie — al het Cosmos-verkeer loopt via deze klasse — maar niet vóór het eerste
verzoek, en de fout komt dan verpakt in een `TypeInitializationException`. Bewust zo gelaten: eerder
afgaan vraagt een aanroep in `Program.cs`, en dan staat de controle niet meer op de plek waar de
opties gemaakt worden.

**De blokletter-waarschuwing in `CosmosPortalHoursStore` is gecorrigeerd en niet weggehaald.** Haar
zwaarste been — "de serializer van dit portaal schrijft ze niet in de canonieke vorm" — is gemeten
onwaar geworden en moest weg; een comment dat iets beweert wat niet klopt maakt de rest van datzelfde
comment ook onbetrouwbaar. Het verbod zelf staat er nog, op het been dat overblijft: de tie-break.
Bij een gelijk moment moet de sleutel de volgorde bepalen, en een `ORDER BY` op één veld laat die
gevallen in willekeurige volgorde staan. Wie het ooit naar de query verplaatst heeft een composite
index op `(createdAt DESC, id ASC)` nodig plus de zekerheid dat elk document in de container canoniek
is.

**Wat de migratie van de bestaande documenten vraagt.** Gemeten in `platform/customers` op
`cosmos-soratus-prod` (alleen gelezen, 2,36 RU): **8 documenten, 8 tijdstempelwaarden, alle 8 niet
canoniek.**

| veld | documenten | vorm | canoniek |
|---|---|---|---|
| `createdAt` | 7 (`kind=customer`) | `2026-08-20T13:58:47.276957+00:00` (32 tekens) | 0 van 7 |
| `ranAt` | 1 (`kind=bootstrap`) | `2026-08-20T13:58:47.276957+00:00` (32 tekens) | 0 van 1 |
| `changedAt` | 0 | komt op geen enkel document voor | — |
| `grantedAt` | 0 | komt op geen enkel document voor | — |

`platform` heeft precies één container (`customers`), dus dit is de volledige omvang. Er is nog geen
urencontainer, dus elke urenregel die er ooit in komt wordt door de gerepareerde schrijfkant
geschreven.

Twee dingen aan die tabel zijn de moeite. Ten eerste dat alle acht waarden identiek zijn: ze komen
uit één bootstrap-run, dus de container bevat op dit moment **één** vorm en sorteert daarmee nog
consistent — verkeerd van vorm, maar niet gemengd. Ten tweede dat `changedAt` en `grantedAt` nergens
voorkomen: er is nog geen klant gewijzigd en er is nog geen toegang vastgelegd.

Daaruit volgt de urgentie, en die zit niet waar je hem verwacht. **De eerste schrijfactie van het
gerepareerde portaal maakt de container gemengd**, en gemengd is erger dan uniform verkeerd omdat het
er dan uitziet alsof het klopt. Dat kan een nieuwe klant zijn, een wijziging die `changedAt` zet, of
een toegang die `grantedAt` zet. Migreren hoort dus vóór de volgende schrijfactie, niet "een keer".

Wat de migratie is: de acht documenten lezen, `createdAt` respectievelijk `ranAt` herschrijven naar
`TimestampNormalization.ToCanonical` van dezelfde waarde, en terugschrijven met `If-Match` op de
`_etag` zodat een gelijktijdige portaalwijziging niet stil wordt overschreven. Het is geen conversie
van betekenis maar van spelling: `…T13:58:47.276957+00:00` wordt `…T13:58:47.2769570Z`, hetzelfde
moment. Daarmee is het idempotent en herhaalbaar. `changedAt` en `grantedAt` staan in de migratie
omdat ze er morgen wél kunnen zijn, niet omdat er nu iets aan te doen valt.

**Deze migratie is niet uitgevoerd.** Er is alleen gelezen. Het besluit om te schrijven is niet van
de sessie die de fout vond.

---

## 26. Het urenendpoint gebruikt het bestaande bewijstype; de vaste regel hangt aan wat het pad níet kan

**Wat er stond.** `mcp-uren.md` noemt onder "Wat de portaalkant nog moet regelen" één punt de zwaarste
van allemaal: *"Een bewijstype voor een aanroeper die geen mens is — en dit is de echte ontbrekende
schakel. `CustomerWriteScope` betekent 'operator die naar déze klant kijkt'. Deze server is dat
niet."* Dezelfde tekst staat in de documentatie van `IPortalHoursStore` en in
`HourEntryKeys.ForIntegration`, en hij is de reden dat er op die interface geen schrijfmethode voor een
koppeling staat.

**Waarom dat niet meer klopt.** Het is juist voor het ontwerp waarin de MCP-server een
service-identiteit gebruikte. Het huidige ontwerp — vastgelegd in datzelfde document, onder
"Autorisatie" — haalt via device-code een token op de identiteit van de **persoon** achter Claude Code.
Er is geen sleutel, geen client secret en geen service-identiteit. De aanroeper is dus dezelfde
operator die het boekformulier van §3.6 mag versturen, en dat document zegt het zelf: *"de autorisatie
is letterlijk dezelfde als op het scherm."* Twee alinea's in één document die uit twee ontwerpen komen.

**Besluit: het endpoint gebruikt `CustomerWriteScope`, langs
`ICustomerScopeResolver.ResolveWriteAsync`.** Er komt geen nieuw scope-type. Dat levert bovendien twee
dingen op die een nieuw type niet zou geven: de klantslug wordt in de klantenlijst opgezocht in plaats
van uit het verzoek vertrouwd (anders staat er een urenregel in een partitie die geen klant is), en
`by` komt uit `CustomerWriteScope.Actor` — precies dezelfde eigenschap die een boeking via het scherm
op zijn naam zet.

**Wat de vaste regel uit §5 dan vasthoudt, is niet het bewijstype maar wat er mee te doen is.** De zorg
in `mcp-uren.md` is dat een aanroeper die "doet alsof hij een operator is" ook kan fiatteren. Dat is
hier niet zo, en de reden is dat er geen HTTP-oppervlak voor bestaat:

1. Het verzoektype heeft geen `status`, geen `by`, geen `source` en geen registratietijd. Niet op
   `pending` vastgezet — afwezig. Dezelfde vorm als `CustomerLogLine` zonder `extra` (§12).
2. Het endpoint heeft `IMcpHoursWriter` in handen en niet `IPortalHoursStore`. Die interface heeft
   precies één methode, `BookPendingAsync`, en die heeft geen stand- en geen bronparameter. Fiatteren,
   afwijzen en corrigeren zijn langs dit pad geen aanroep die je verkeerd doet maar een aanroep die niet
   bestaat.
3. Er is één endpoint. `POST /api/uren` en niets anders.

Een eigen scope-type zou de *store* beschermen tegen een aanroep die vanaf hier niet te doen is.

**Een meegestuurd veld wordt geweigerd en niet stil genegeerd.** Het verzoektype draagt
`[JsonUnmappedMemberHandling(Disallow)]`. Zonder die regel slaat `System.Text.Json` een meegestuurde
`"status": "approved"` over: het verzoek slaagt, de regel landt goed, en de aanroeper heeft geen enkele
aanwijzing dat zijn veld is weggegooid — niet te onderscheiden van een portaal dat het veld wél
overneemt. Dat verschil is of iemand op naam van een ander kan boeken. Nu is het antwoord een `400`.

**Wat hieraan zwak is, en het staat hier zodat het niet wegzakt: dit is een tweede schrijver van
`hourEntry`-documenten.** De juiste plek voor `BookPendingAsync` is `IPortalHoursStore`, naast de
andere vijf schrijfpaden; dan bestaat er één klasse die weet hoe een urenregel wordt weggeschreven. Hij
staat in `Soratus.Portal/Api/` omdat `Soratus.Portal/Data/` in deze sessie niet gewijzigd mocht worden.
Wat er *niet* verdubbeld is: de documentvorm (`HourEntryDocument`), de sleutelregel (`HourEntryKeys`),
de validatie (`HourBooking.Validate`) en de tijdvormnormalisatie (die zit op de opties van de
Cosmos-SDK, §25). Wat wél verdubbeld is: de containerlezing en de foutafhandeling eromheen, zo'n dertig
regels. Verhuizen is een bestandsverplaatsing en geen herontwerp — en met de verhuizing horen twee
opmerkingen in `Data/` te worden bijgesteld: die van `IPortalHoursStore` en die van
`HourEntryKeys.ForIntegration`, die beide zeggen dat het bewijstype nog niet bestaat.

**Eén naam die niet klopt en die in `Data/` staat.** `HourEntryKeys.ForPortal` is niets anders dan het
recept "tijdstempel plus vier bytes inhoudshash"; het endpoint gebruikt hem, want er is geen andere
plek waar dat recept staat en een tweede exemplaar ervan levert twee documenten voor één boeking op.
Zijn naam zegt dat hij bij het portaalformulier hoort. Herdopen naar iets bronneutraals hoort bij een
wijziging in `Data/`.

**Wat er van deze regel gemeten is.** `CosmosMcpHoursWriter.Build` is een eigen methode zodat de
kernregel te meten is zónder naar Cosmos te schrijven. Dat is niet alleen gemak: een regel die alleen
te meten is door naar productie te schrijven, wordt niet gemeten. De test kijkt naar `Status`,
`Source`, `Counts`, `ApprovedAt`, `ApprovedBy` en de vorm van de id; de mutaties "pending → approved"
en "mcp → portaal" maken hem rood.

---

## 27. Antiforgery raakt dit endpoint niet — en de foutpagina maakte van een 401 een 400

Twee vragen over de middlewareketen die het endpoint met de formulieren deelt. De eerste had het
antwoord dat je hoopt; de tweede niet.

**Antiforgery: niets aan de hand, en dat is gemeten.** `app.UseAntiforgery()` valideert alleen als het
endpoint `IAntiforgeryMetadata` draagt met validatie aan, en dat komt er bij een minimal-API-endpoint
alleen op als het formulierinvoer bindt (`IFormCollection`, `IFormFile`, `[FromForm]`). Gemeten over de
volledige routetabel van de draaiende app:

```
/api/uren               -> antiforgery=(geen metadata)
/klant/{Slug}/uren      -> antiforgery=True
/klant/{Slug}/contract  -> antiforgery=True
/klanten/nieuw          -> antiforgery=True
/overzicht, /, /Error   -> antiforgery=True
/healthz, /_blazor/*    -> (geen metadata)
```

Elke Razor-pagina houdt zijn validatie; het endpoint vraagt er geen. Een POST met JSON en een
bearer-token, zonder antiforgery-token, komt door de échte pijplijn — daar staat een test op.

**Er is bewust géén `DisableAntiforgery()` aangeroepen.** Die aanroep zou vandaag niets doen, en het
gevaar zit in wat hij morgen betekent: hij zet de validatie ook uit als dit endpoint ooit
formulierinvoer gaat binden, en dan is er een gat waar niemand naar kijkt. Wat de toekomst afdekt is de
meting, niet de aanroep: wordt validatie in een volgende .NET-versie de standaard voor élke POST, dan
wordt de test rood en niet de eerste urenboeking van een operator. De tweede assertie in die test is de
belangrijkere — dat de *andere* endpoints hun validatie nog hebben. Zou `UseAntiforgery()` zijn
weggehaald of de mapping ervoor zijn verplaatst om het endpoint aan de praat te krijgen, dan slaagt de
eerste assertie ook, en dan is er een gat in élk formulier van het portaal.

**Een tweede reden dat CSRF hier geen vraag is, en hij staat in de code en niet in een gewoonte.** Het
beleid op dit endpoint is vastgezet op het bearer-schema. Een cookie uit een browsersessie
authenticeert er dus niet, ook niet met de operatorrol — een pagina op een andere site kan geen boeking
doen op de sessie van een ingelogde operator, ook niet met `credentials: include`.

**Wat wél stuk was: `UseStatusCodePagesWithReExecute` maakte van een 401 een 400.** Die middleware
voert het oorspronkelijke verzoek opnieuw uit op `/not-found` — met dezelfde methode en hetzelfde
lichaam. Een POST met JSON op een Razor-pagina levert daar `The request has an incorrect Content-type.`
op met status 400, en omdat er dan al een lichaam is geschreven kan de oorspronkelijke code niet meer
worden teruggezet. Gemeten op een aanroep zonder token:

```
voor:  STATUS=400  WWW-Authenticate=Bearer  body="The request has an incorrect Content-type."
na:    STATUS=401  WWW-Authenticate=Bearer  body=(leeg)
```

**Waarom dat duur is.** De MCP-server onderscheidt vijf uitkomsten, en 400 en 401 vallen aan
verschillende kanten van de belangrijkste grens. Op een 401 zegt hij "er is geen geldige aanmelding" en
verwijst naar `soratus-uren aanmelden`; op een 400 zegt hij "NIET geboekt" met de reden uit het
antwoord — en die reden was hier een klacht over een content-type. Dan zoekt een operator de fout in
zijn boeking terwijl hij niet is aangemeld. Hetzelfde gold voor een 403, waar de melding over de
ontbrekende app-rol `Operator` de enige aanwijzing is die iemand krijgt.

**Oplossing: onder `/api` blijft de lege 401/403 staan zoals de autorisatiemiddleware hem schreef.** Eén
`app.UseWhen` om de bestaande aanroep heen; voor de browser verandert er niets. De mutatie die de
`UseWhen` weghaalt maakt vier tests rood.

---

## 28. De Entra-registratie in één blok, en twee grenzen die niet overeenkomen

### Het blok

Tenantniveau, dus dit doet Marcel. **Dit vervangt het blok in `mcp-uren.md` en is er de superset van**;
er zijn twee verschillen, beide hieronder benoemd. Zet het abonnement er niet bij: dit is Graph, geen
ARM. Er is bij het schrijven hiervan **niets aan de tenant gewijzigd** — alles hieronder is
onbeproefd.

```bash
export MSYS_NO_PATHCONV=1   # Git Bash op Windows, anders verbouwt MSYS de Graph-paden
```

**1. De public client aanmaken.** Levert de `appId` op die in `SORATUS_UREN__CLIENT_ID` komt.
`--is-fallback-public-client true` is wat device-code mogelijk maakt; zonder die vlag weigert Entra de
flow met een melding over een ontbrekend client secret. Verwacht: een object met `appId` en `objectId`.
Bewaar beide.

```bash
az ad app create \
  --display-name "soratus-uren" \
  --sign-in-audience AzureADMyOrg \
  --is-fallback-public-client true \
  --public-client-redirect-uris "http://localhost" \
  --query "{appId:appId, objectId:id}"
```

**2. De service principal aanmaken.** Zonder dit object kan de tenant geen toestemming vastleggen.
Verwacht: een object met een `id`. **Staat er `already in use` of `already exists`, dan is dat geen
fout** — de principal bestond al, bijvoorbeeld omdat stap 1 eerder is gedraaid. Ga door.

```bash
az ad sp create --id <appId-uit-stap-1> --query "{id:id, appId:appId}"
```

**3. De object-id van de portaal-registratie opzoeken.** Niet dezelfde als de service-principal-id uit
`infra.md`. Verwacht: één regel. Staan er meer, kies op `appId` en niet op naam.

```bash
az ad app list --display-name "soratus-portal" \
  --query "[].{naam:displayName, appId:appId, objectId:id}" -o table
```

**4. Kijken wat er nu op de `api`-eigenschap staat, vóór je hem overschrijft.** Doe dit echt; de
volgende stap vervangt de hele eigenschap. Verwacht op een onaangeroerde registratie een lege
`oauth2PermissionScopes` en `requestedAccessTokenVersion: null`.

```bash
az rest --method GET \
  --uri "https://graph.microsoft.com/v1.0/applications/<objectId-uit-stap-3>?\$select=api,identifierUris"
```

**5. Eén `PATCH` die de scope blootstelt, de client vooraf autoriseert en de tokenversie vastzet.**

> **Dit is het eerste verschil met `mcp-uren.md`, en het is de reden dat het één blok is.** Daar staan
> de scope (stap 4) en de voorautorisatie (stap 6) als twee `PATCH`-aanroepen op dezelfde
> `api`-eigenschap. Een Graph-`PATCH` op een complex type vervangt de waarde van dat type; de tweede
> aanroep, die alleen `preAuthorizedApplications` meestuurt, loopt daarmee het risico de scopes uit de
> eerste weer weg te halen — en dan faalt het aanmelden met een melding over een ontbrekende scope
> terwijl je die scope net hebt aangemaakt. Eén `PATCH` met alles erin heeft dat risico niet, ongeacht
> hoe Graph het precies doet. **Niet nagemeten** — er is geen tenantwijziging uitgevoerd — maar het is
> de veilige vorm van de twee.

> **Dit is het tweede verschil: `requestedAccessTokenVersion: 2`.** Zonder die instelling geeft Entra
> een v1-access-token af, en dan is de `aud` de App ID URI (`api://soratus-portal`) in plaats van de
> appId, en de `iss` `https://sts.windows.net/<tid>/` in plaats van het v2-endpoint waar
> `AddMicrosoftIdentityWebApi` op is ingesteld. Het portaal accepteert beide `aud`-vormen — daar staat
> een test op — maar één bekende vorm is beter dan twee mogelijke. Dit is veilig omdat er vandaag geen
> enkele client een access-token voor deze API vraagt: de browseraanmelding gebruikt een id-token, en
> die valt niet onder deze instelling.

```bash
SCOPE_ID=$(cat /proc/sys/kernel/random/uuid)   # of: python -c "import uuid;print(uuid.uuid4())"
echo "Scope-id: $SCOPE_ID"                     # bewaar deze; stap 6 heeft hem nodig

cat > /tmp/uren-api.json <<'JSONEINDE'
{
  "identifierUris": ["api://soratus-portal"],
  "api": {
    "requestedAccessTokenVersion": 2,
    "oauth2PermissionScopes": [
      {
        "id": "VUL-SCOPE_ID-IN",
        "value": "Uren.Boeken",
        "type": "User",
        "isEnabled": true,
        "adminConsentDisplayName": "Uren boeken in het portaal",
        "adminConsentDescription": "Staat de aanroeper toe uren te boeken als te fiatteren regel.",
        "userConsentDisplayName": "Uren boeken",
        "userConsentDescription": "Boekt uren die Soratus daarna moet fiatteren."
      }
    ],
    "preAuthorizedApplications": [
      {
        "appId": "VUL-APPID-UIT-STAP-1-IN",
        "delegatedPermissionIds": ["VUL-SCOPE_ID-IN"]
      }
    ]
  }
}
JSONEINDE

# De twee waarden erin zetten. Met een aanhalingsteken om JSONEINDE hierboven doet de shell niets
# aan de inhoud van het bestand — dat is opzet, want inline quoting in Git Bash is hier al eerder
# stukgelopen. sed vult ze daarna in, zodat er geen shell-expansie in de JSON nodig is.
sed -i "s/VUL-SCOPE_ID-IN/$SCOPE_ID/g; s/VUL-APPID-UIT-STAP-1-IN/<appId-uit-stap-1>/g" /tmp/uren-api.json
cat /tmp/uren-api.json          # nakijken vóór je hem verstuurt

az rest --method PATCH \
  --uri "https://graph.microsoft.com/v1.0/applications/<objectId-uit-stap-3>" \
  --headers "Content-Type=application/json" \
  --body @/tmp/uren-api.json
```

Verwacht: **geen uitvoer.** Een `PATCH` op Graph geeft `204 No Content` bij succes; uitvoer betekent
hier dus een fout. Staat er al een scope op `soratus-portal` (stap 4 laat dat zien), zet die dan mee in
dit bestand — anders is hij weg.

De payload staat in een bestand en niet inline. Dat is niet netheid: `az ad app update --set api.x`
werkt niet op subeigenschappen van `api`, en inline JSON met quoting is in Git Bash al eerder
stukgelopen.

**6. De permissie declareren op `soratus-uren`.** Dit is de stap die je zou overslaan, en dan faalt het
aanmelden met een melding over ontbrekende scopes. `/.default` betekent "alles waarvoor deze client
statisch toestemming heeft" — staat de permissie niet op de client, dan is dat niets. Verwacht: een
waarschuwing dat je nog toestemming moet geven. Die is hier onnodig: de voorautorisatie uit stap 5 doet
dat werk.

```bash
az ad app permission add \
  --id <appId-uit-stap-1> \
  --api <appId-van-soratus-portal-uit-stap-3> \
  --api-permissions "$SCOPE_ID=Scope"
```

**7. Nakijken dat de boeker de app-rol `Operator` heeft.** Alleen lezen. Dit is de val uit
`stand-van-zaken.md`: een toewijzing zonder rol (`appRoleId 00000000-…`) laat je wél binnen maar levert
geen `roles`-claim, en dan geeft het portaal een `403` op een token dat verder in orde is. Verwacht:
een regel met `appRoleId = e9290944-a9f0-4390-a69d-fb4ab0e5b7e0` — dat is `Operator`, uit
`infra/entra/app-roles.json`.

```bash
az rest --method GET \
  --uri "https://graph.microsoft.com/v1.0/users/marcel@soratus.com/appRoleAssignments?\$select=appRoleId,resourceDisplayName" \
  --query "value[?resourceDisplayName=='soratus-portal']" -o table
```

**8. Controleren, vanaf de machine waar de server komt te draaien.** Verwacht bij `controleer`: `aud`
gelijk aan de portaal-appId (na stap 5 met `requestedAccessTokenVersion: 2`) en `Rollen: Operator`.
Staat er `Rollen: (geen)`, dan mist de toewijzing uit stap 7. Afsluitcode 0 betekent bruikbaar.

```bash
export SORATUS_UREN__PORTAL=https://portal.soratus.com
export SORATUS_UREN__SCOPE=api://soratus-portal/.default
export SORATUS_UREN__CLIENT_ID=<appId-uit-stap-1>
export SORATUS_UREN__TENANT_ID=091b5069-3bea-4abd-80ec-b1c3e6ed1d51

dotnet run --project Soratus.Mcp.Uren -- aanmelden
dotnet run --project Soratus.Mcp.Uren -- controleer
```

**Opruimen na afloop:** `rm /tmp/uren-api.json`. Er staat geen geheim in, maar wel de tenantstructuur.

### Twee grenzen die niet overeenkomen, en die zo blijven

`mcp-uren.md` legt de client vast op `uren ≤ 200` en `omschrijving 5–500 tekens`. De datalaag van het
portaal staat 16 uur per regel toe (`HourLimits.MaximumPerEntry`) en 400 tekens
(`HourLimits.MaximumNoteLength`). Er is dus een band waarin de client een boeking doorlaat en het
portaal hem weigert: 17 tot 200 uur, en 401 tot 500 tekens.

**Dat is geen storing en het wordt niet gerepareerd.** Het portaal is de eigenaar van deze grenzen —
dat is de hele reden dat de validatie achter het endpoint staat — en de afwijzing komt met een leesbare
Nederlandse reden bij de aanroeper terecht, dus die kan het in één ronde herstellen. Wat er wél niet
klopt is de tabel in `mcp-uren.md`; die suggereert een grens die niet de bindende is. Er staat een test
op de discrepantie (`DeGrenzenVanHetPortaalZijnStrakkerDanDieVanDeClient`), zodat hij niet stil de
andere kant op wordt "opgelost" door de portaalgrens naar een getal uit een document op te rekken.

### Wat er níet is overgenomen uit `mcp-uren.md`

Het voorbeeldantwoord daar toont `geboekt door  Claude Code — Marcel`, en de documentatie van
`HourEntryDocument.By` geeft dezelfde vorm als voorbeeld. Het portaal zet daar alleen de **naam uit het
token** in. Reden: datzelfde document splitst `by` en `createdBy` — *"`createdBy` de koppeling die de
regel wegschreef, naast `by` voor de mens die het werk deed"* — en §3.6 toont de bron al als eigen
kolom (`Portaal · MCP/Claude Code · Azure DevOps`). Met "Claude Code" in `by` staat de koppeling in drie
velden en de mens in geen enkel. `createdBy` is daarom `soratus-uren`.

## 29. Het maandoverzicht per mail: de bevestiging is een feit met drie standen, en de claim gaat vóór de mail

**Spec:** §3.7, laatste regel — *"Maandoverzicht mailen naar de contactpersoon, met
verzendbevestiging."* Eén regel, en er zit meer in dan er staat. §5 noemt SendGrid; de werkelijkheid
is Azure Communication Services (`docs/agent-portal/fase-4-haalbaarheid.md` §3, gemeten). Dat is de
kleinste van de afwijkingen hieronder.

De code staat in `Soratus.Portal/Mail/`. Er is niets gewijzigd in `Data/`, `Views/` of `Api/`.

### 29.1 Versturen is een handeling met gevolgen buiten ons systeem

Elk ander schrijfpad in dit portaal is terug te draaien: een urenregel is te corrigeren, een
contractveld te overschrijven, een toegang in te trekken. Een mail niet. Dat verandert welke fout de
duurste is, en daarmee de ordening van het ontwerp.

Drie gevallen, en ze hebben elk een eigen antwoord.

**Een dubbele verzending.** De verzendbevestiging is één document per klant per maand, met een
**afgeleide sleutel**: `statement-2026-08`, op de partitiesleutel van de klant. Hij wordt met
`CreateItemAsync` geschreven — geen upsert — **vóórdat** er een verbinding met Communication Services
wordt opgezet. Een tweede poging levert daarmee een `409` op bij Cosmos en niet een tweede mail. Dat
is dezelfde eigenschap en dezelfde reden als bij `PortalDocumentIds.HourEntry` ("een dubbel
weggeschreven regel is een dubbel gefactureerd uur"), en §6 van het haalbaarheidsrapport schrijft
precies deze volgorde voor bij de conceptfactuur: *"Stap 1 vóór stap 3. Nooit andersom, want dat is
precies de volgorde waarin een dubbele factuur ontstaat."*

Wat de claim kost, eerlijk: valt het proces om tussen de claim en het bevestigen, dan staat er een
bevestiging op *onbekend* terwijl er misschien niets is verstuurd. Dat is de goede kant om fout te
zitten. De andere volgorde — eerst versturen, dan vastleggen — laat bij dezelfde storing een
verstuurde mail zonder enig spoor achter, en dan verstuurt de volgende poging er een tweede.

**Een mislukking halverwege.** Elke reden om níet te versturen staat vóór de claim: geen afgesloten
maand, mailen niet ingericht, geen meting, een onbekend bedrag, een onvolledige meting, geen
contactpersoon, een onbruikbaar adres. Een weigering laat daarom **geen document** achter. Er staat
een test op dat drie verschillende weigeringen alle drie een lege partitie achterlaten, want een
halve bevestiging is later niet van een halve verzending te onderscheiden.

**Een uitkomst waarvan onbekend is of hij is aangekomen.** Dat is §29.2.

### 29.2 Verzonden / niet verzonden / onbekend — drie standen, en met opzet geen `bool`

Dit is in dit portaal de vierde keer dezelfde afweging: `Views.AccessEntraState` (drie standen voor
de Entra-toegang), punt 2 (geen document betekent geen status), punt 15 (een contractbedrag dat
ontbreekt is niet nul) en `recorded` in `mcp-uren.md`. `StatementSendState` heeft daarom drie waarden.

| Stand | Betekenis | Wat er dan mag |
|---|---|---|
| `unknown` | Niet vast te stellen of het bericht is aangenomen | **niets.** Zie hieronder |
| `sent` | Communication Services heeft het bericht aangenomen | niets. Klaar |
| `notSent` | Er is zeker niets verstuurd | opnieuw versturen mag |

En de vierde toestand is de **afwezigheid van het document**: er is nooit een poging gedaan. Er is
daarom geen enumwaarde `NotAttempted`. Zou die bestaan, dan kan er een document met die waarde staan
zonder dat er iets is gebeurd, en dan is de afwezigheid van het document geen antwoord meer op
dezelfde vraag. Dat is punt 2, letterlijk.

**`unknown` is de eerste waarde van de enum, en dat is geen alfabet.** De standaardwaarde van een
niet-gezette enum hoort de veilige te zijn. Stond `sent` op nul, dan zou een document met een leeg of
onleesbaar `state`-veld lezen als "verstuurd" — en dan krijgt een klant zijn overzicht nooit en weet
niemand het.

**Er is geen stand die zegt "de verzending loopt nu".** Dat lijkt informatie die je wilt hebben en het
is precies de verkeerde: het verschil tussen "loopt nog" en "onbekend" is alleen door de tijd te
bepalen, en een proces dat halverwege omvalt laat "loopt nog" staan. Dan staat er een toestand die
zegt dat er iemand aan het werk is terwijl er niemand is. De claim staat dus meteen op `unknown`.

**Uit `unknown` komt het portaal alleen langs een mens.** `IStatementStore.ReleaseAsync` vraagt een
verplichte vaststelling van minstens tien tekens — *"gebeld met de contactpersoon, niets ontvangen"* —
en zet de stand daarna op `notSent`. Pas dan mag er opnieuw. Dezelfde vorm als de toestand `abandoned`
in §6 van het haalbaarheidsrapport, en om dezelfde reden: er is geen programma dat kan vaststellen of
een mail is aangekomen. Communication Services weet het niet, wij hebben geen leesrecht op de postbus
van de klant, en een tweede mail sturen om het te vragen is precies wat we wilden vermijden.

Het aantal pogingen staat als getal op het document en de vaststelling blijft staan na een tweede
verzending. Dat een klant twee overzichten over dezelfde maand heeft gekregen hoort op het scherm te
staan en niet uit tijdstempels te reconstrueren te zijn.

**"Verstuurd" en niet "Afgeleverd".** Aangenomen door Communication Services is niet in de inbox van
de klant: een spamfilter, een volle postbus of een geweigerde ontvanger komt daarna. Het scherm zegt
dat er ook bij. Dezelfde correctie die §7 van het haalbaarheidsrapport op de factuurstatus maakt —
"Gefactureerd" in plaats van "Verzonden" — want een label boven een gegeven dat iets anders betekent
is een onwaarheid met een tijdstempel eronder.

### 29.3 Waar een 4xx en een 5xx uit elkaar gaan, en waarom dat de hele beslissing is

De verzender kent drie uitkomsten en de indeling zit in twee `catch`-blokken.

| Wat er gebeurt | Uitkomst | Stand |
|---|---|---|
| `SendAsync` komt terug met een operatie-id | `Accepted` | `sent` |
| `RequestFailedException` met status **400–499** | `Refused` | `notSent` |
| Al het andere: `5xx`, tijdslimiet, verbroken verbinding, annulering | `Unknown` | `unknown` |

De 4xx-tak is de enige waarin "er is zeker niets verstuurd" waar is, en daarom de enige die
`notSent` mag zetten. **Een `429` hoort daar ook bij**: throttling betekent "niet aangenomen" en niet
"misschien wel".

`OperationCanceledException` wordt bewust als *onbekend* gelezen en niet doorgegooid. Dat is tegen de
gewoonte in en het is hier de juiste keuze: de annulering komt van een afgebroken HTTP-verzoek — een
operator die zijn tabblad sluit — en op dat moment kan het bericht al de deur uit zijn. Doorgooien
zou de claim op `unknown` laten staan zonder dat er iets wordt vastgelegd: dezelfde uitkomst met
minder informatie.

**Er zit nergens een herhaling in dit pad.** Geen `retry`, geen backoff, geen tweede poging bij een
tijdslimiet. Dat is de vaste stelregel van dit project, en een dubbele mail naar een klant is erger
dan een dag later mailen.

### 29.4 Wat er in een mail kan sluipen dat er niet in hoort, en hoe het is gesloten

Punt 13 en punt 14 gaan over deze klasse fout: tekst die door onze eigen systemen is geschreven en
bij een klant belandt. Beide keren stond die tekst op een **scherm**, waar een operator hem nog kon
zien. In een postbus staat hij definitief. Zeven paden, met de sluiting erbij.

1. **Een stacktrace of een pad in een vrij tekstveld.** De klantnaam en de naam van de contactpersoon
   zijn vrije tekst uit onze eigen administratie. Ze gaan door `MessageTruncation.Cut` uit
   `Soratus.Agents.Contracts` — **dezelfde functie** die de agentbibliotheek en de klantprojectie van
   de logregels gebruiken. Punt 13 zegt met zoveel woorden dat twee kopieën van die knip gaan
   schuiven; dit is de derde aanroeper en niet de tweede definitie. Er is een test met het geval uit
   punt 13 zelf: legitiem proza op de eerste regel, zestien regels stacktrace erachter.

2. **Tekens die geen regelovergang zijn en toch een regel breken.** `Cut` knipt op `\n`, `\r\n` en
   `\r`. Een tab, een verticale tab, NEL (U+0085), LINE SEPARATOR (U+2028) en PARAGRAPH SEPARATOR
   (U+2029) overleven dat. Die worden apart verwijderd. **Dat is geen tweede definitie van "één
   regel"**: waar de regel eindigt wordt nog steeds alleen door `Cut` bepaald; hier worden tekens
   weggehaald die in géén enkele regel horen. En het is uitdrukkelijk geen verdediging tegen
   kopinjectie — het onderwerp gaat als veld in een JSON-lichaam over HTTPS en niet als SMTP-kop, dus
   er is geen kop om in te injecteren. Die reden staat er niet bij, want een reden die niet klopt
   wordt later weggehaald en neemt de echte mee.

3. **De omschrijving van een urenregel.** Dit is het grootste gat en het is met een ontwerpbesluit
   gesloten in plaats van met een filter: **de urenspecificatie staat niet in de mail.** De
   omschrijving van een urenregel is vrije tekst die door een koppeling kan zijn geschreven — de
   MCP-server neemt hem letterlijk over uit een gesprek met een taalmodel (`mcp-uren.md`) — en de mail
   is de enige plek waar zulke tekst buiten het bereik van een operator komt. Achter een aanmelding
   staat hij op een scherm dat een mens kan lezen en corrigeren. De mail noemt de bedragen en verwijst
   naar het portaal.

4. **Een foutmelding van een dienstverlener.** `MailSendResult` draagt géén meldingsveld (hij heette
   `StatementSendResult` tot punt 43 de verzendlaag extraheerde), en
   `StatementRefusal` en `StatementFigureGap` zijn **enums en geen strings**. Een reden die als tekst
   reist komt uit een `catch`-blok. Een enum kan die tekst niet dragen. De melding gaat naar de
   logregel met de `ErrorCode` en de status erbij; het scherm zegt dát het is geweigerd en waar de
   reden staat. Er is een broncodetest die de twee opmaakbestanden afgaat op `Exception.Message`,
   `StackTrace`, `ToString` en `ErrorCode`.

5. **Operator-only gegevens uit §2.** Geen dienstuitsplitsing, geen opslagpercentage, geen resource
   group, geen subscription. Dat is niet met een `@if` gesloten maar met het retourtype van de naad:
   `MonthlyStatementFigures` heeft die velden niet. Er is een test die de mail afgaat op negen
   verboden woorden, `opslag` en `%` daaronder.

6. **De fiatteringsstroom.** De acceptatie van fase 3 is dat de klant er niets van ziet, en een mail
   is de makkelijkste plek om die eis alsnog te breken — er kijkt niemand mee. Er staat een
   typecontrole op de boom van `MonthlyStatementFigures`, `StatementMail` en `StatementAddressing` met
   dezelfde woordenlijst als `UrencomponentTests` (`pending`, `approv`, `reject`, `etag`, `fiat`), en
   een tekstcontrole op de opgemaakte mail.

7. **Het e-mailadres van de één in de aanhef van de ander.** De aanhef krijgt alleen een naam bij
   precies één ontvanger; bij twee staat er "Beste relatie,". En het adres is **niet** de terugvaloptie
   als de naam ontbreekt — dat was de eerste opzet, en "Beste jan.bakker@example.nl," verraadt aan
   iedereen die meeleest welk adres wij van deze persoon in onze administratie hebben staan.

Wat er **niet** gesloten is, en dat hoort erbij te staan: de klantnaam en de naam van de
contactpersoon blijven vrije tekst uit onze eigen administratie. Staat er een interne aanduiding in de
eerste regel van een klantnaam, dan gaat die mee. Hetzelfde restrisico als bij `msg` (punt 13) en
`errorMessage` (punt 14), en aan deze kant is er niets tegen te doen.

### 29.5 De ontvanger komt uit de toegangsdocumenten, en dat maakt de twee aanduidingen voor het eerst ongelijk

§3.7 zegt "mailen naar de contactpersoon" en §3.5 zet de contactpersoon op de contractkaart. Maar
`ContractDocument.Contact` is een **naam** en geen adres. Het enige veld in dit portaal dat een
e-mailadres van de klant bevat is `AccessDocument.Email`.

De mail gaat dus naar de toegangsregels met de aanduiding **"Beheerder klant"**. En daarmee
onderscheiden die twee aanduidingen voor het eerst iets van elkaar — `PortalAccessRoles` zegt met
zoveel woorden dat ze *"precies hetzelfde mogen: lezen"*, en `ContractNotice.AccessLabelsAreEqual`
zegt het aan de klant. **Dat blijft waar.** Dit gaat niet over recht maar over adressering: een
"Lezer" mag meekijken en is niet degene die het maandoverzicht hoort te krijgen. Het staat hier omdat
het de eerste barst is in een tekst die "beide aanduidingen geven hetzelfde leesrecht" belooft, en de
volgende die dat leest hoort te weten dat er nu één plek is waar de keuze iets doet.

**Eén onbruikbaar adres houdt de hele verzending tegen**, ook als er een goed adres naast staat. Dat
is de duurdere van de twee keuzes en hij is de juiste: versturen naar wat wél klopt levert een
bevestiging op die "verstuurd" zegt terwijl de persoon voor wie het overzicht bedoeld was niets heeft
gekregen.

De adrescontrole is **geen tweede adresvalidatie** — die staat bij het invoeren, in fase 2 — maar een
smallere vraag: is deze tekst als één ontvanger van één bericht te gebruiken. Dat is niet overbodig:
in de opslag staan documenten uit de configuratiemigratie, en een adres dat als tekst in een
JSON-bestand stond is nooit door een veldcontrole gekomen. Geweigerd worden onder andere
`Jan <jan@acme.nl>` en `jan@acme.nl, iemand@elders.nl` — als één adres opgeslagen is dat een tweede
ontvanger die niemand heeft toegevoegd.

### 29.6 De bedragen komen van elders, en de bevestiging legt vast wát er is gemaild

De mailkant **rekent niets**. De Azure-kosten, de beheeropslag en de uren boven bundel komen uit
`IMonthlyStatementFigures`, één naad met één smalle retourvorm. Er is een broncodetest die elke
rekenkundige operator naast de naam van een bedrag in `Soratus.Portal/Mail/` afkeurt: een tweede plek
die een bedrag berekent is een tweede plek die het anders kan berekenen, en dan kan de mail een ander
bedrag noemen dan het scherm.

Elk bedrag op die naad is `decimal?`, en **`null` betekent onbekend en nooit nul**. Is een bedrag
onbekend, of zegt de kostenkant dat de meting nog niet volledig is, dan gaat er **geen mail**. Er
staat geen "onbekend" en geen streepje in een maandoverzicht: op een factuurregel is € 0,00 geen lege
waarde maar een verkeerd bedrag, en dat is niet te herstellen door te verversen. Regel 1 van §9 van
het haalbaarheidsrapport, en punt 15.

**Het belangrijkste veld op de bevestiging zijn de bedragen zelf.** Er staat niet alleen dát er is
gemaild maar ook *wat*. Zonder die drie getallen is de enige manier om te weten wat de klant heeft
gekregen: het opnieuw uitrekenen — en dat levert over een maand een ander getal op, want de
kostenmeting is dan bijgewerkt, de bundel kan zijn gewijzigd en er kan een urencorrectie zijn
geplaatst. Bij een factuurdiscussie is "wat stond er in de mail die u op 3 september kreeg" de vraag.

Wat er níet op staat is de opgemaakte tekst van de mail. Die is uit de bedragen en de vorm te
herleiden en zou anders twee keer bestaan. De onderwerpregel staat er wél in: dat is de enige tekst
die de klant in zijn postbuslijst ziet en dus het enige waarop hij de mail terugvindt.

### 29.7 Een maandoverzicht gaat over een afgesloten maand

Er wordt geweigerd zodra de gevraagde maand de lopende of een toekomstige maand is, en die controle
staat vóór alle andere — hij is de goedkoopste en leest niets. Een overzicht over een lopende maand
noemt een bedrag dat morgen anders is.

Een onleesbare maand (`augustus`, `08-2026`, `2026-13`) levert **dezelfde** weigering op. Dat is geen
luiheid: in beide gevallen is er geen afgesloten maand om een overzicht van te maken, en een aparte
melding voor "dit is geen maand" zou een operator iets vertellen over de adresbalk in plaats van over
zijn klant. De maandgrens loopt over de Nederlandse zone (`PortalTimeZone.Display`) en via
`HourMonths.Of`, dezelfde grens als het urenscherm — zouden die twee verschillen, dan is op 1 augustus
tussen middernacht en twee uur in de nacht juli op het ene scherm afgesloten en op het andere niet.

### 29.8 De proefdraaimodus staat standaard aan, en legt niets vast

`PortalMail:DryRun` is `true` als er niets is geconfigureerd. **De onveilige stand hoort iets te zijn
dat iemand aanzet en niet iets dat je vergeet uit te zetten.** Dezelfde vorm als
`SORATUS_UREN__DROOGLOOP` in de MCP-server, met dit verschil dat het daar de uitzondering is en hier
de standaard: een urenregel is te corrigeren en een verzonden mail niet.

Twee eigenschappen ervan zijn opzet:

- **Een proefdraai legt niets vast.** Hij staat vóór de claim. Zou hij een document achterlaten, dan
  staat er een bevestiging bij een mail die nooit is verstuurd — precies de stille onwaarheid met een
  tijdstempel eronder die dit portaal elders al drie keer heeft afgewezen.
- **De getoonde mail is letterlijk de mail die zou zijn verstuurd.** Geen markering in de tekst, geen
  aanpassing. Een proefdraai die iets anders toont dan hij zou versturen, bewijst niets. De markering
  staat op het scherm eromheen, bovenaan en niet onderaan: een operator die denkt dat hij heeft
  gemaild terwijl er niets is verstuurd, is de gevaarlijkste van de twee vergissingen.

### 29.9 De verzendbevestiging heeft geen klantvorm, en dat is het typeverschil

Bij het contract- en het urenscherm zijn er twee overloads — een klantscope levert het klanttype, een
schrijfrecht het operatortype. Hier is er **één**, en er is geen klantvariant van het viewmodel en
geen klantvariant van het component. Een verzendbevestiging draagt de adressen waarop wij de klant
hebben gemaild, de onderwerpregel, het aantal pogingen en de vaststelling van een operator over een
mislukte verzending. Dat is allemaal Soratus-werk.

`MonthlyStatementCard.razor` neemt daarom precies één parameter: een `CustomerWriteScope`. Dat type is
door een klantgebruiker niet te produceren — de constructor is `internal` en alleen
`CustomerScopeResolver.ResolveWriteAsync` levert hem — dus er is geen klantpagina die dit component
kan renderen, ook niet per ongeluk. Er staat een reflectietest op dat dit de enige parameter is en een
broncodetest dat er geen rolvoorwaarde in de markup staat.

**De twee queryparameters doen niets, en dat is gemeten.** De kaart kent `?jaar=` en
`?vaststellen=jjjj-MM`. Die tweede is een werkwoord in een `GET`, en dat is een vorm die aandacht
verdient: een `GET` is aan te roepen door een link in een mail, door een prefetch van een browser, door
een linkchecker, door een spamfilter dat elke URL in een bericht opent, en door een tabblad dat na een
herstart zijn adressen opnieuw bezoekt. Bij een gewoon scherm is dat hinderlijk. Hier zou het de deur
openzetten naar een tweede maandoverzicht, want *vaststellen dat er niets is aangekomen* is precies de
handeling die opnieuw versturen toestaat.

`?vaststellen=` is daarom uitsluitend een **keuze in het scherm**: hij bepaalt vóór welke maand het
formulier wordt opgemaakt, en verder niets. De vaststelling zelf is de `POST` van dat formulier. Dat
staat niet als afspraak maar als meting — `GetdoetnietsTests` rendert vier adressen en toetst daarna
dat het document nog op precies dezelfde stand staat, zonder vaststelling, zonder extra poging, met
dezelfde etag, en dat er niets is verstuurd en niets is geclaimd. Een mutatie die de vaststelling wél
in `OnInitializedAsync` zet, maakt twee van die tests rood; een mutatie die er een verzending van maakt
één.

Twee dingen die daarbij níet zijn gemeten en die als zodanig horen te staan. bUnit rendert een
`EditForm` als `<form blazor:onsubmit="1">` en niet als `<form method="post">` met een
antiforgery-token — dat is de renderer van bUnit en niet die van static SSR. Dát de `POST` een `POST`
met een token is, volgt hier dus uit de vorm (`EditForm` met een `FormName`, dezelfde vorm als de drie
formulieren op het urenscherm) en niet uit een meting. Die meting kan pas als deze kaart op een pagina
met een route staat en er een echte host omheen kan.

Eén afwijking van het urenscherm, met de reden: **na een verzendpoging volgt géén redirect.** Op het
urenscherm is die nodig omdat een verversing een tweede urenregel oplevert. Hier levert een verversing
een tweede POST op die door de claim wordt tegengehouden, en de operator ziet "dit overzicht is al
verstuurd" in plaats van dat er een tweede mail uitgaat. Dat is hier de sterkere van de twee: een
redirect helpt niet tegen twee operators die tegelijk op de knop drukken, en een `409` wel.

### 29.10 De rol in Azure: een custom role, en niet `Contributor`

Gemeten in de resource provider (haalbaarheidsrapport §3): mail versturen met een managed identity
vraagt `Microsoft.Communication/CommunicationServices/Read` en `.../Write`, en dat zijn
**control-plane**-acties. Microsofts eigen voorbeeld noemt daarvoor `Contributor`. Die rol geeft er
`ListKeys/action` bij — dus het recht om de connection string op te halen — en `Delete`. **Dan heb je
een identity die machtiger is dan het geheim dat je met de identity wilde vermijden.** Dat is precies
het patroon waarom het portaal ook geen brede Graph-rechten krijgt (punt 28, en `AccessDocument`).

De ingebouwde rol `Communication and Email Service Owner` is beheerrecht en niet wat we zoeken.

**Eén grens die niet klopt, en die hoort benoemd te worden.** Het `AssignableScopes` hieronder staat
op `rg-soratus-prod`, dus de rol is alleen daar toe te wijzen. Maar `az role definition create` zelf
is een schrijfactie op **abonnementsniveau** (`Microsoft.Authorization/roleDefinitions/write`): een
roldefinitie leeft in het abonnement en niet in een resource group. De afspraak "schrijfrechten alleen
in `rg-soratus-prod` en `MBV`" wordt door stap 1 dus overschreden, en er is geen versie van dit
commando die dat niet doet. Dat is een besluit voor Marcel en geen implementatiedetail.

#### Het blok

Uitvoeren door Marcel, in Git Bash. Per commando één regel over wat het doet en wat de verwachte
uitvoer is.

```bash
# ── 0. Voorbereiding ─────────────────────────────────────────────────────────────────────────
# MSYS verbouwt op Windows argumenten die op een pad lijken, en een resource-id is er één.
export MSYS_NO_PATHCONV=1

# Het abonnement waarin rg-soratus-prod staat. Verwacht: één guid.
SUB=$(az account show --query id -o tsv) && echo "$SUB"
```

```bash
# ── 1. De roldefinitie ───────────────────────────────────────────────────────────────────────
# Twee acties en niets meer: geen ListKeys, geen RegenerateKey, geen Delete. De payload staat in
# een bestand en niet inline — inline JSON met quoting is in Git Bash in dit project al eerder
# stukgelopen (zie punt 28 en mcp-uren.md stap 4).
cat > /tmp/acs-verzender.json <<JSONEINDE
{
  "Name": "ACS Email Sender (Soratus portaal)",
  "IsCustom": true,
  "Description": "Lezen en schrijven op een Communication Services-resource: precies genoeg om mail te versturen met een managed identity. Geen ListKeys, dus geen weg naar de connection string.",
  "Actions": [
    "Microsoft.Communication/CommunicationServices/Read",
    "Microsoft.Communication/CommunicationServices/Write"
  ],
  "NotActions": [],
  "DataActions": [],
  "NotDataActions": [],
  "AssignableScopes": [
    "/subscriptions/$SUB/resourceGroups/rg-soratus-prod"
  ]
}
JSONEINDE

# Verwacht: een object met roleName "ACS Email Sender (Soratus portaal)" en een guid als id.
# Staat er "RoleDefinitionWithSameNameExists", dan is dit al gedaan; ga door naar stap 2.
az role definition create --role-definition @/tmp/acs-verzender.json \
  --query "{naam:roleName, id:name, bereik:assignableScopes}"
```

Let op: hier staat `<<JSONEINDE` **zonder** aanhalingstekens, want `$SUB` moet door de shell worden
ingevuld. Dat is het omgekeerde van het blok in punt 28, waar de aanhalingstekens er juist wél om
stonden om invulling te voorkomen. Het verschil is opzet en het is de reden dat er in dit bestand geen
`$`-teken in de JSON staat behalve die ene.

```bash
# ── 2. Het principal-id van de portaal-identity ──────────────────────────────────────────────
# Verwacht: één guid. Dit is het object-id van de managed identity en NIET het client-id.
PRINCIPAL=$(az identity show \
  --name id-soratus-portal \
  --resource-group rg-soratus-prod \
  --query principalId -o tsv) && echo "$PRINCIPAL"

# Staat de identity in een andere resource group, dan levert het commando hierboven een
# ResourceNotFound. Zoek hem dan op — verwacht: één regel met naam, groep en principalId.
az identity list \
  --query "[?name=='id-soratus-portal'].{naam:name, rg:resourceGroup, principal:principalId}" -o table
```

```bash
# ── 3. Het resource-id van het communicatieaccount ───────────────────────────────────────────
# Via `az resource show` en niet via `az communication show`: dat tweede vraagt de
# communication-extensie, en een extensie installeren is een wijziging op de machine van de
# uitvoerder. Verwacht: één resource-id dat eindigt op /acs-soratus-prod.
ACS=$(az resource show \
  --name acs-soratus-prod \
  --resource-group rg-soratus-prod \
  --resource-type Microsoft.Communication/CommunicationServices \
  --query id -o tsv) && echo "$ACS"
```

```bash
# ── 4. De roltoewijzing ──────────────────────────────────────────────────────────────────────
# --assignee-object-id met --assignee-principal-type en NIET --assignee: dat laatste doet een
# Graph-opzoeking en faalt bij een verse identity met "Cannot find user or service principal in
# graph database" — een replicatievertraging die eruitziet als een rechtenfout.
#
# Het bereik is de ACS-resource en niet de resource group: het portaal hoort niet bij alles in
# rg-soratus-prod te kunnen.
#
# Verwacht: een object met de rolnaam en het bereik. Bij een tweede keer "RoleAssignmentExists";
# dat is geen fout.
az role assignment create \
  --assignee-object-id "$PRINCIPAL" \
  --assignee-principal-type ServicePrincipal \
  --role "ACS Email Sender (Soratus portaal)" \
  --scope "$ACS" \
  --query "{rol:roleDefinitionName, bereik:scope}"
```

```bash
# ── 5. Nakijken ──────────────────────────────────────────────────────────────────────────────
# Verwacht: vier regels. De drie uit het haalbaarheidsrapport B5 (Key Vault Secrets User op
# kv-soratus-prod, Cost Management Reader en Reader op de resource group MBV) plus de nieuwe op
# acs-soratus-prod. Staat "ACS Email Sender" er niet bij, dan is stap 4 niet aangekomen.
az role assignment list --assignee "$PRINCIPAL" --all \
  --query "[].{rol:roleDefinitionName, bereik:scope}" -o table

# Opruimen. Er staat geen geheim in, wel de abonnementsstructuur.
rm /tmp/acs-verzender.json
```

**Wat er daarna nog in configuratie moet, en dat is geen `az`-werk in dit blok:** de vijf sleutels van
de sectie `PortalMail` op `app-soratus-prod`.

```
PortalMail__Endpoint        https://acs-soratus-prod.europe.communication.azure.com/
PortalMail__FromAddress     DoNotReply@soratus.com
PortalMail__ReplyToAddress  hallo@soratus.com
PortalMail__DryRun          true      ← pas op false zetten ná de eerste proefdraai
PortalMail__PortalBaseUri   https://portal.soratus.com
```

Geen van de vijf is een geheim, dus ze kunnen als gewone app-setting. `DryRun` hoort op `true` te
blijven tot er één keer met eigen ogen is bekeken wat er zou zijn verstuurd. De endpoint hierboven is
de vorm die ACS voor `dataLocation: europe` uitgeeft; controleer hem tegen stap 3 in plaats van hem
over te typen.

**Het afzenderadres blijft `DoNotReply@soratus.com`**, want dat is het enige geverifieerde adres en
een tweede toevoegen kan pas ná een quotaverhoging (haalbaarheidsrapport §3). Daarom is er een
`Reply-To`: een maandoverzicht waarop je niet kunt antwoorden stuurt de klant naar de telefoon.

### 29.11 Wat er níet is: een plaatshouder voor de bedragenbron

De naad `IMonthlyStatementFigures` wordt door de kostenkant geïmplementeerd. Zolang die er niet is, is
`MonthlyStatementService` niet te registreren — en dat is **geen luie fout die pas bij de eerste
aanroep opvalt**. In Development staat `ValidateOnBuild` aan op de DI-container, dus een onvervulbare
`AddScoped` maakt `WebApplicationBuilder.Build()` onmogelijk en start het portaal niet.

Gemeten, en het was duurder dan het klinkt: het nam **alle 26 tests van het urenendpoint** mee, want
die starten via `WebApplicationFactory<Program>` het echte portaal. De melding wees naar
`Program.cs` en naar het urenendpoint, en niet naar de mailkant — dus de sessie die de fout in beeld
kreeg was niet de sessie die hem had gemaakt.

Er lag een plaatshouder klaar: een implementatie die `null` teruggeeft ("over deze maand is niets
gemeten"), geregistreerd vóór de echte, zodat de laatste registratie wint zodra de kostenkant de hare
neerzet. Hij werkt, hij faalt dicht — `null` levert `NoFigures` op, dus geen mail en geen document —
en hij is **afgewezen**. Twee redenen, en de tweede is de beslissende.

De eerste: hij leunt op registratievolgorde. `Program.cs` legt twintig regels hoger bij
`PostConfigure` juist uit waarom dit portaal dat vermijdt — dan hangt gedrag af van de volgorde
waarin iemand regels neerzet.

De tweede weegt zwaarder. **Die plaatshouder antwoordt "niets gemeten", en dat is niet te
onderscheiden van een echte "niets gemeten".** Verdwijnt de echte registratie ooit — een hernoeming,
een merge, iemand die opruimt — dan start de app gewoon door en wordt er stil nooit gemaild, met een
reden die op het operatorscherm plausibel oogt. Dat is een storing die zich voordoet als werkende
functionaliteit, en dat is precies de klasse fout die dit portaal elders overal dichtzet: de
MCP-server die geen Cosmos-verbinding krijgt (ook niet als afgeschermde optie voor later), het portaal
dat geen Graph-schrijfrecht krijgt, de toegangsdocumenten die in de Soratus-eigen opslag staan en niet
in die van de klant.

En het scherpste eraan: **een test die controleert of de container volledig is, zou op die
plaatshouder groen staan.** Hij maakt de plaatshouder niet alleen een tijdelijk gemak maar een blinde
vlek in de meting die ernaast is gebouwd.

Wat er in plaats daarvan staat: de vier registraties die op zichzelf staan
(`IMailOutbox` — tot punt 43 `IStatementMailSender` —, `IStatementStore`, `IStatementViews` en de
opties), de vijfde als commentaar
op de plek waar hij hoort met de reden erbij, en **drie tests op de registratie zelf** in
`RegistratieTests`:

| Test | Wat hij vastlegt |
|---|---|
| `DeDrieOnderdelenDieAltijdMoetenStaanStaanEr` | de drie die niet aan de naad hangen |
| `DeNaadEnDeDienstZijnAllesOfNiets` | de duurzame regel: samen komen en samen gaan. Groen in beide eindstanden, rood in de gebroken tussenstand |
| `ZolangDeNaadOntbreektIsDeMailkantNietAangesloten` | de tripwire. **Staat rood tot de naad landt**, en dat is opzet |

Die laatste is een bewust rode test en geen storing. De onafheid van fase 4a is daarmee op precies één
plek zichtbaar in plaats van nergens — en dat is wat een plaatshouder had weggenomen.

**Waarom mijn eigen tests dit niet zagen, en dat is de les.** De tests van het verzendpad bouwen
`MonthlyStatementService` met de hand op, met drie testdubbels. Dat is opzet: wat er in die klasse te
meten valt is de volgorde claimen–versturen–vastleggen, en die meet je door de afhankelijkheden te
vervangen en niet de klasse. Maar daarmee zagen ze de registratie nooit. Een testverzameling die elk
onderdeel los uitoefent en de samenstelling niet, is blind voor precies deze klasse fout — en hier was
de samenstelling het enige dat stuk was.

**En één ding dat de kostenkant heeft rechtgezet.** `StatementFigureGap` had bij mij vijf waarden,
waaronder `NoHourlyRate` en `NoSurcharge`. De adapter gooit met opzet weg *welk* contractveld
ontbreekt, dus die twee waren onbereikbaar geworden — en een onbereikbare enumwaarde is in dit
document al eerder een afwijkingspunt geweest. De enum is aan die kant gelijkgetrokken met wat er
werkelijk aankomt. Niets in de mailkant schakelt op deze enum, dus dat kon zonder gevolgen: de
weigeringen lopen over `StatementRefusal` en die is van deze kant.

---

## 30. Een geslaagd, leeg antwoord van Cost Management is niet nul — en dat is erger dan de 404

**Gemeten op 21 augustus 2026**, `POST .../providers/Microsoft.CostManagement/query`,
`api-version=2023-11-01`, ruim dertig aanroepen tegen `subscriptions/501a66d2-…` als `marcel@`.

Het haalbaarheidsonderzoek (`docs/agent-portal/fase-4-haalbaarheid.md` §2) noemt als gevaarlijkste
bevinding een **404** die "probeer opnieuw" betekent: `GtmDimensionDataProvider…returns null`, op een
verzoek dat er vlak ervoor en vlak erna 200 op gaf. Dat is opnieuw gezien — tweemaal in ruim twintig
aanroepen — en het klopt.

Er is iets ergers, en het stond nog nergens opgeschreven.

```
POST .../resourceGroups/RG-BESTAAT-NIET-XYZ/providers/Microsoft.CostManagement/query
→ HTTP 200
  {"properties":{"nextLink":null,"columns":[…],"rows":[]}}

POST .../resourceGroups/MBV/providers/Microsoft.CostManagement/query   (timeframe: alleen vandaag)
→ HTTP 200
  {"properties":{"nextLink":null,"columns":[…],"rows":[]}}
```

De eerste is een resource group die **niet bestaat**. De tweede is `MBV`, die élke dag € 1,88 kost,
bevraagd over een periode die nog niet is geboekt. **Twee volstrekt verschillende werkelijkheden, één
identiek geslaagd antwoord.** En daar komt de derde bij: een maand waarin werkelijk niets is verbruikt
geeft hetzelfde.

Waarom dit erger is dan de 404: **een 404 ziet uit als een storing en dit ziet uit als een antwoord.**
Een normale client rendert er € 0,00 op, en dat is geen randgeval maar de gewone gang van zaken —
tussen middernacht en ongeveer 08:00 UTC is er van de lopende dag nog niets geboekt, dus ook niet van
de nieuwe maand. De `kosten-collector` uit §4 draait volgens het onderzoek dagelijks om 04:00. **Op de
1e van de maand om 04:00 geeft een MonthToDate-query voor de nieuwe maand dus nul rijen, en een
naïeve lezing daarvan is "deze klant kostte deze maand € 0,00".** Voor een klant met een typefout in
zijn resource-groepnaam zou dat jaren zo blijven, zonder één rood lampje.

### Het besluit: er is een subtotaal dan en slechts dan als er regels zijn

`AzureCostReading.Subtotal` is `decimal?` en is de som van `Lines` — `null` zodra die lijst leeg is. Er
is geen veld waarin een bedrag past dat niet uit regels komt. Dat is geen `if` maar een invariant, en
het is dezelfde vorm als bij `HourBalance.Booked`, dat geen ander getal dan de som kán zijn.

En de keerzijde is even belangrijk: **nul mét regels is een echte nul.** In de gemeten uitvoer staan
`Bandwidth € 0,0000` en `Microsoft Entra € 0,0000` als gewone regels. Een maand die alleen zulke
regels heeft, heeft een subtotaal van nul, en dát mag als `€ 0,00` op het scherm. Het verschil tussen
een som die nul is en een som die niet bestaat is precies wat `decimal?` hier draagt en wat een
`decimal` niet kan.

`AzureCostState` heeft daarom **vier** waarden en niet twee:

| | betekenis | handeling |
|---|---|---|
| `Unknown` | de lezing is niet gelukt, of er is nooit gemeten | opnieuw meten |
| `NoLines` | de lezing is gelukt en gaf nul regels | **nakijken of we de juiste omgeving bevragen** |
| `Partial` | er zijn bedragen, de maand is niet af | wachten |
| `Measured` | volledig geboekt | factureren mag |

Een `bool` "compleet ja/nee" kan het verschil tussen "de API zei niets" en "de API zei nul regels" niet
dragen, en die twee vragen een verschillende handeling. Zelfde argument als bij de drie
Entra-toestanden en bij de vierde urenstand (punt 19).

### En de ambiguïteit die niet op te lossen is, staat op het scherm

De code kan "niets verbruikt", "nog niet geboekt" en "verkeerde omgeving" niet uit elkaar halen. Dat is
geen tekortkoming die met beter programmeren weggaat: het zijn drie oorzaken achter één identiek
antwoord. Wat er dan overblijft is de vraag aan een mens stellen, en dat kan alleen als op het scherm
staat wát er is bevraagd. Vandaar `AzureCostDocument.Scope`, die bij een maand zonder regels onder de
rij komt te staan met "bevraagd: /subscriptions/…/resourceGroups/…".

Dat is de enige beschikbare verdediging tegen een tikfout in een resource-groepnaam, en het is een
patroon dat dit portaal al kent: een eigenschap die je niet kunt garanderen laat je zien in plaats van
hem weg te rekenen (§1 van de spec, "eerlijke systeemeigenschappen benoemen").

---

## 31. De volledigheidscontrole rust op datums en niet op een percentage

Het onderzoek (§6) geeft twee wegen om te voorkomen dat er een halve dag Azure op een factuur belandt:
de volledigheid controleren, of de facturatie-agent later laten draaien. Het adviseert de eerste, met
als toets "staat er voor de laatste dag van de maand een bedrag dat in de lijn ligt van de dagen
ervoor?".

**Die toets is gemeten en verworpen.** Op `MBV`, dagkorrel, 1 t/m 20 augustus:

```
19 volle dagen   € 1,87731 – € 1,87967   (spreiding 0,13%)
de 20e om 06:55  € 1,80263               (95,97% van de mediaan)
de 21e           ontbreekt volledig
```

De toets werkt daar prachtig, en juist dat is het bezwaar: een drempel zou tussen 96% en 99,9% moeten
liggen. Die marge is gepast op een omgeving die elke dag hetzelfde kost omdat er een App Service in
staat die altijd aan is. Een klant met een agent die één keer per week een batch draait heeft een
dagspreiding die veel groter is dan 4% — en dan staat de controle permanent op "onvolledig" of laat hij
een halve dag door, afhankelijk van welke kant je de drempel op zet. **Een grens die op één klant is
gekalibreerd en op de volgende het omgekeerde doet, is geen grens.**

**Wat er in de plaats komt is de vertraging zelf.** Uit dezelfde meting: de boeking loopt ongeveer acht
uur achter. `AzureCostCompleteness.Judge` noemt een maand daarom volledig als de laatste dag van de
maand in de gegevens staat **én** de meting minstens twee dagen ná het einde van de maand is gedaan
(`SettlementDays = 2`). Geen percentage, één constante, en die constante heeft zijn meting ernaast
staan.

### Dit is het gemeten antwoord op openstaande vraag 9 van het onderzoek

Die vraag was: staat de laatste dag van een maand om 06:00 op de 1e volledig in Cost Management?
**Nee — en een meting op dat moment kan niet vaststellen dát hij er staat.** Om 06:55 op de 21e stond
de 20e op 95,97%; om 06:00 op de 1e is het laatste uur van de vorige maand dus nog niet binnen. De cron
`0 6 1 * *` uit §4 van de spec factureert daarmee een maand met een fractie van een dag te weinig, en
dat is aan het bedrag niet te zien.

Met deze controle is dat draaimoment onschadelijk in plaats van fout: een collector die om 04:00 op de
1e loopt krijgt `Partial` te horen en factureert niet. **Het draaimoment hoeft dus niet te verschuiven,
en dat is de winst van controleren boven later draaien.**

### Wat er met opzet níet wordt gecontroleerd

Een **gat midden in de maand**. Cost Management geeft voor een dag zonder kosten géén rij, dus een
klant wiens omgeving een dag uit stond heeft een echt gat — en dat gat is niet te onderscheiden van een
dag die nog niet is geboekt. Dat is dezelfde ambiguïteit als in punt 30, een niveau lager. Zou een gat
tot "onvolledig" leiden, dan is die klant nooit te factureren; zou een gat aan het eind tot "volledig"
leiden, dan factureren we een halve maand. Alleen de laatste dag bekijken lost precies het geval op dat
wél te weten is.

---

## 32. Het bedrag staat in Cosmos en wordt niet bij het bekijken opgehaald

Dit is de eerste van de twee weegvragen van dit werk. Het onderzoek beschrijft een `kosten-collector`
die dagelijks draait en een cache van 6–12 uur; de vraag was of het portaal niet net zo goed live kan
opvragen.

**Het kan niet, en dat is gemeten.** Op 21 augustus, als één aanroeper:

```
06:59:28  200   (na 40 s stilte)
06:59:38  429   entity-requests 2, clienttype-retry-after 29
06:59:42  429   entity-requests 1, clienttype-retry-after 26
06:59:45  429   entity-requests 0, clienttype-retry-after 22
06:59:49  429   entity-retry-after 38, clienttype-retry-after 19
```

Vier aanroepen binnen elf seconden, vier keer 429. Een geslaagde aanroep vroeg dertig tot veertig
seconden stilte ervoor. **Eén operator die twee klanten naast elkaar opent, trekt de emmer leeg** — en
de tweede pageview zou dan een bedrag missen dat er wél is.

Drie redenen, in gewicht:

1. **Het budget verdraagt geen pageview**, zie boven. Het hangt aan de aanroeper en niet aan de scope
   (de header heet `clienttype-retry-after`), dus meer klanten of meer abonnementen maken het niet
   ruimer. Dat bevestigt §2 van het onderzoek.
2. **Het lege antwoord is alleen met historie te wegen.** "Nul regels" betekent iets anders als er
   gisteren wél regels waren (punt 30). Die vergelijking vraagt een bewaarde reeks, dus er is hoe dan
   ook opslag nodig.
3. **Wat er op het scherm hoort te staan als de verzameling van vannacht is mislukt, is de lezing van
   eergisteren met het tijdstip erbij.** Dat getal is werkelijk gemeten; een mislukte aanroep heeft
   niets gemeten. Van die twee is het eerste het eerlijkere antwoord, zolang erbij staat wanneer het is
   gemeten — en dat staat er, per rij, want elke maand heeft zijn eigen laatste meting.

**De prijs, eerlijk:** het scherm loopt tot een etmaal achter op wat Cost Management weet. Voor een
maandbedrag dat achteraf wordt gefactureerd is dat geen bezwaar — de gegevens van Cost Management lopen
zelf al acht uur achter, dus "live" bestaat hier niet. Het portaal zou een verse onnauwkeurigheid
ruilen tegen een oude, en daar een aanroepbudget voor opbranden.

### Vier headers die je niet moet gebruiken, en één hypothese die is weerlegd

Het onderzoek waarschuwt terecht tegen `x-ms-ratelimit-remaining-subscription-resource-requests`: die
stond in élke meting op **1099**, ook op de 429's. Er zijn nu vier headers bijgemeten die er wél nuttig
uitzien en het niet zijn:

```
x-ms-ratelimit-remaining-microsoft.costmanagement-entity-requests      DefaultQuota:3 → 0
x-ms-ratelimit-remaining-microsoft.costmanagement-tenant-requests      DefaultQuota:19 → 15
x-ms-ratelimit-remaining-microsoft.costmanagement-clienttype-requests  DefaultQuota:0  (altijd)
x-ms-ratelimit-microsoft.costmanagement-qpu-remaining                  QueriesPerHour:599 → 578
```

De eerste telt werkelijk af naar nul en is dus een echte teller. **En hij is toch onbruikbaar voor
bewaking: geen van deze vier headers staat op een 200.** Een geslaagd antwoord draagt géén enkele
cost-management-ratelimietheader — alleen de nutteloze 1099. Je kunt de ruimte dus pas zien nadat je er
al door bent. `clienttype-requests` staat bovendien altijd op 0, óók op het antwoord vlak vóór een
succes.

Twee dingen die het ontwerp raken:

- **Elke respons kost budget, ook een 404 en een 429.** `qpu-remaining` liep van 599 naar 578 over
  eenentwintig aanroepen waarvan de meeste mislukten. **Opnieuw proberen is niet gratis**, dus een
  backoff die snel herhaalt maakt het erger.
- **De wachthint is onbetrouwbaar in beide richtingen.** Gemeten waarden voor
  `clienttype-retry-after`: 1, 2, 8, 16, 17, 19, 22, 25, 26, 29, 34, 34, 35. Eén keer was 2 genoeg; het
  onderzoek meldt een 1 die te kort was. Er blijkt bovendien een **tweede** hint te bestaan
  (`entity-retry-after`) die alleen verschijnt zodra `entity-requests` op 0 staat, en die is groter.
  Lees ze beide en neem de grootste, met een eigen backoff eronder.

**Weerlegde hypothese, en hij is het opschrijven waard omdat hij plausibel was.** De header heet
`clienttype-retry-after`, en "client type" wordt bij Azure vaak uit de `User-Agent` afgeleid. Als dat
hier zo was, zou een eigen User-Agent een eigen emmer geven — en dan zou het portaal niet hoeven te
vechten met elk ander gereedschap in deze tenant. Gemeten: vier snelle aanroepen met
`User-Agent: Soratus.Portal/1.0 (kosten-collector)` gaven vier 429's, en de vijfde met
`User-Agent: curl/8.0` erna gaf er nog een — met dezelfde aflopende teller. **De emmer is niet per
User-Agent.** Dat is een meting die niets veranderde, en precies daarom hoort hij hier: de volgende
lezer hoeft hem niet opnieuw te doen.

---

## 33. De dienstuitsplitsing komt uit de API, en de kolomvolgorde ook

Het onderzoek (§2) meldt al dat §3.7 de diensten verkeerd benoemt. Bevestigd:

| §3.7 zegt | de API geeft |
|---|---|
| Container Apps, Azure OpenAI, Storage, Log Analytics, Key Vault | `Azure App Service`, `Azure Cosmos DB`, `Bandwidth`, `Key Vault`, `Microsoft Entra` |

Vier van de vijf namen uit de spec komen in de werkelijke uitvoer niet voor. De uitsplitsing komt
daarom uit `AzureCostQuery.Read` en niet uit een lijst in onze code — een vaste lijst zou vandaag al de
helft missen en zou op de dag dat er een dienst bijkomt stil geld buiten het subtotaal laten vallen.

**Er zit een tweede valkuil in die het onderzoek niet noemt: de kolomvolgorde verschilt per vraag.**

```
granularity: None    → Cost, ServiceName, Currency
granularity: Daily   → Cost, UsageDate, ServiceName, Currency
```

`ServiceName` staat op index 1 of op index 2, afhankelijk van of je dagkorrel vraagt. Een lezer met
vaste indices haalt bij de tweede vorm de dienstnaam uit de datumkolom en levert een dienst `20260801`
op met het bedrag van één dag. **Dat is geen crash maar een verkeerd bedrag per dienst, en het valt
alleen op als iemand het subtotaal natelt.** De indices komen daarom uit `columns[]`, op naam en
hoofdletterongevoelig.

Verder: `nextLink` was op de gemeten scope altijd `null` (vijf diensten; met dagkorrel vijfenzestig
rijen), maar hij wordt teruggegeven en niet weggegooid. Een lezer die een vervolgpagina laat liggen
heeft een subtotaal dat te laag is, en dat is even onzichtbaar als de fout hierboven.

En een onleesbaar bedrag **werpt** en wordt geen nul. De aanroeper hoort daar `AzureCostState.Unknown`
van te maken. Een `catch` die de rij overslaat en doorgaat levert een subtotaal op dat te laag is — en
een bedrag dat te laag is ziet er net zo geloofwaardig uit als een bedrag dat klopt.

---

## 34. Het beheeropslagpercentage blijft op het contract, tegen §6 in

**Spec:** §6 zet `opslag%` op `AzureCost` (dus per maand). §3.9 vraagt het bij het aanmaken van een
klant, en `ContractDocument.AzureSurchargePercentage` bestaat daar al sinds punt 15.

**Besluit: het staat alleen op het contract, en het verbruiksdocument heeft het veld niet.**

Twee redenen. Het is een **afspraak en geen meting**: de agent die het verbruik wegschrijft heeft geen
mening over onze marge, en er is geen scherm waarop een percentage per maand wordt vastgelegd. Een veld
dat niets ooit vult is een stille onwaarheid — dezelfde afweging als bij `AccessDocument`, waar om die
reden geen "uitnodiging verstuurd"-veld staat. En twee plekken waar hetzelfde percentage kan staan is
een tweede waarheid over onze marge; de eerste keer dat ze verschillen is dat een factuur die niet
overeenkomt met het contract.

Wat er in plaats van een invulveld op het facturatiescherm staat, is een regel die zegt waar het
percentage hoort, met een link naar het contractscherm. Een operator die hier een veld verwacht en er
geen vindt, hoort te weten waar hij moet zijn in plaats van te concluderen dat het niet kan.

Blijkt er ooit een klant te zijn met een afwijkend percentage in één maand, dan is dat een veld op het
verbruiksdocument **plus** een scherm dat het vult **plus** een regel over welke van de twee wint. Dat
is dan een besluit en geen detail.

### En het bedrag valt weg zodra het percentage ontbreekt

`MonthlyChargeCalculator` geeft `null` voor het door te belasten bedrag als er geen opslag is
afgesproken — niet het subtotaal, en niet het subtotaal plus nul. Dat is punt 15 op de plek waar hij
werkelijk geld kost: nul procent opslag is een afspraak, geen opslag ingevuld is een afspraak die nog
moet komen, en een niet-nullable `decimal` zou de tweede stil als de eerste doorrekenen. Het door te
belasten bedrag zou dan gelijk zijn aan de inkoop — onze marge weg, zonder dat er iets aan het getal te
zien is.

**Eén onbekende maakt het hele totaal onbekend.** Geen deelsom. §3.7 zet Azure en de uren boven bundel
uitdrukkelijk "op één totaal", en een totaal waarvan de helft ontbreekt is dat niet; erger, het is niet
van een compleet totaal te onderscheiden en het is lager. Van de twee mogelijke fouten — geen getal of
een te laag getal — is alleen de eerste zichtbaar. Datzelfde geldt een niveau hoger voor het
jaartotaal.

### Eén uitzondering die geen uitzondering is

**Nul uur boven bundel kost nul euro, ook zonder afgesproken tarief.** Bij een klant die binnen zijn
bundel blijft valt er niets te factureren, en dan is het ontbreken van een tarief geen belemmering. Zou
hier `null` uitkomen, dan is een klant die netjes binnen zijn bundel blijft niet te factureren zolang
niemand een tarief heeft ingevuld dat toch niet gebruikt wordt. Het tarief is pas nodig zodra er iets
boven de bundel staat, en dán is het ontbreken ervan wél een blokkade.

---

## 35. Naar buiten toe verdwijnt het onderscheid tussen drie contractgaten, met opzet

`MonthlyChargeGap` (operator) kent vier vlaggen: `AzureUnknown`, `NoSurchargeAgreed`, `NoBundleAgreed`,
`NoRateAgreed`. Vlaggen en geen enkele waarde, want een klant zonder contract mist er drie tegelijk —
en een operator die er één ziet gaat die oplossen en houdt dan een totaal dat nog steeds ontbreekt.

**Naar de klant gaan er twee van de vier over, en dat is informatieverlies met een reden.** De
klantvariant `CustomerChargeGap` heeft `ConsumptionUnknown`, `ContractIncomplete` en `NotCharged`. De
drie contractgaten vallen op één waarde, want:

- een waarde die `NoSurchargeAgreed` heet **noemt onze marge**, en de mededeling "we hebben nog geen
  opslag afgesproken" vertelt een klant dat er een opslag ís. "beheeropslag" staat in de lijst met
  woorden die op geen enkel klantscherm mogen staan (`KlantVangnetTests`);
- het zijn alle drie contractafspraken, en de handeling die erop volgt is voor alle drie dezelfde.

Dat de reden een **enum** is en geen `string` is de scherpste regel van deze keten, en hij komt van de
mailkant: een reden die als tekst reist kan uit een `catch`-blok komen, en dan staat de tekst van een
uitzondering in de inbox van een klant. Dat is de fout van de punten 13 en 14 voor de derde keer, nu in
een inbox in plaats van op een scherm.

### Het gevolg voor de mailkant: twee waarden waren onbereikbaar

`StatementFigureGap` in `Soratus.Portal/Mail/` had `NoHourlyRate` en `NoSurcharge`. Die zijn met dit
besluit **onbereikbaar** geworden: er is geen bron die ze ooit zet, want de kostenkant gooit het
onderscheid weg voor het de klantvorm bereikt. Punt 11 van deze notitie gaat precies over zulke velden
— waarden die bestaan, onwaar zijn en nooit worden gevuld — en één plek in dit portaal met dat gebrek
is genoeg. Ze zijn vervangen door één `ContractIncomplete`, en er staat een test die opsomt welke
waarden de adapter werkelijk kan opleveren en dat vergelijkt met de enum. Komt er een waarde bij zonder
bron, dan gaat die test rood.

### Twee vlaggen die niet hetzelfde zijn, en waar dat pas bleek

`MonthlyCharge` had eerst alleen `IsFinal` — "de maand is volledig gemeten én er is een totaal". De
adapter naar het maandoverzicht leunde daarop om te bepalen of het tijdvak nog liep, en **een test vond
dat dat fout is**: een klant zonder contract kreeg "het tijdvak is nog niet volledig" te horen over een
maand die allang volledig gemeten was. `IsFinal` is `false` zodra er íets ontbreekt, dus hij kan de twee
redenen niet scheiden.

Daarom staat er nu `IsPeriodComplete` naast, dat precies één ding zegt. Dat is een ware uitkomst met een
onware reden die alsnog is opgelost — en het is de reden dat de gaten in een aparte enum zitten in plaats
van uit een `bool` te worden afgeleid.

**Wat hier nog niet klopt en gemeld is:** een interne beheerklant (§4) wordt niet doorbelast, dus er
hoort geen maandoverzicht naartoe. `StatementFigureGap.NotCharged` zegt dat, maar `StatementRefusal`
heeft geen bijpassende waarde — dus zo'n klant weigert vandaag met `AmountsIncomplete`. De uitkomst is
goed (er gaat geen mail) en de reden is onwaar. Dat hoort in `StatementRefusal` te worden opgelost, of
eerder: met een controle in `MonthlyStatementService` vóór de bedragen worden gelezen.

---

## 36. Drie lessen over het meten zelf

Deze drie gaan niet over facturatie. Ze gaan over het gereedschap, en ze hebben in dit werk elk een uur
gekost.

### Een dode `cref` naast een compilatiefout kan een gevolg zijn en geen oorzaak

`GenerateDocumentationFile` staat sinds vandaag aan voor de hele repo, en de eerste keer dat hij iets
vond in nieuwe code meldde hij vier dingen: twee keer `CS1584` op een `cref="decimal?"` (een cref kan
geen nullable-annotatie dragen — schrijf `<c>decimal?</c>`) en **twee keer `CS1574` op een type dat
gewoon bestond**.

Die twee `CS1574`'s waren geen achtergebleven tekst van een hernoeming. Ze verdwenen zonder dat er één
verwijzing is aangeraakt, zodra de drie échte compilatiefouten in hetzelfde project weg waren: bij een
mislukte compilatie kan de crefresolutie niet meer bij de typen van de bestanden die niet zijn
gecompileerd.

**De regel die daaruit volgt: eerst de compilatiefout, dan opnieuw meten, dán pas een cref aanraken.**
Wie het omdraait haalt een goede verwijzing weg naar een type dat wel bestaat — en dat is precies de
schade die deze vlag hoort te voorkomen.

Nog één waarschuwing uit dezelfde vlag die het waard is om te kennen: `CS0419` (ambigue cref) sloeg toe
op het moment dat er een tweede overload bijkwam, exact zoals `Directory.Build.props` voorspelt. De
oplossing is de signatuur in de cref zetten.

### `ValidateOnBuild` maakt van één ontbrekende registratie een storing in élke hosttest

Er stond in dit werk een blok van **26 rode tests** in `Soratus.Portal.Tests`, waarvan 25 in `Urenapi`
— een namespace die met kosten en mail niets te maken heeft. De oorzaak was één regel:

```
Unable to resolve service for type 'Soratus.Portal.Mail.IMonthlyStatementFigures'
  while attempting to activate 'Soratus.Portal.Mail.MonthlyStatementService'
  at Program.<Main>$
```

`MonthlyStatementService` was geregistreerd en `IMonthlyStatementFigures` had geen implementatie.
`WebApplicationBuilder.Build()` werpt daarop, dus **elke test die de échte app opstart valt om**, ook de
tests die iets heel anders beweren te testen. `Soratus.Portal` bouwde schoon: dit is geen
compileerfout.

Dat is luidruchtig in plaats van stil, en dus goed. Maar het betekent dat twee sessies elkaars
testsuite kunnen platleggen met een halve registratie. **Zie je een blok rode hosttests dat niets met
elkaar te maken heeft, kijk dan eerst naar de DI-validatie en niet naar de functionaliteit die ze
beweren te testen.**

### Een afgebroken mutatieronde laat productiecode gemuteerd achter

De `finally` die de mutatie terugzet loopt bij een harde kill niet. Gemeten: na een afgebroken ronde
stond `AzureCostCompleteness.SettlementDays` op `0` in plaats van op `2`, en de build was schoon —
niets wees erop. Controleer na een afbreking met `git diff` of de boom is zoals hij hoort, en vertrouw
niet op de assertie aan het eind van het script: die wordt bij een afbreking nooit bereikt.

En de bijbehorende val in het script zelf: met `subprocess.run(..., shell=True)` geeft Windows de
argumentenlijst aan `cmd.exe`, en die leest de `&` in een testfilter als een commandoscheiding. Het
tweede deel van de filter valt dan weg. Gemeten: 1081 tests in plaats van 981, met vijf staande fouten
die elke uitslag onleesbaar maakten. `shell=False`.

---

## 37. De klant krijgt een machineleesbare Azure-scope, náást de weergavetekst

**Niet in de spec.** §6 geeft `Customer` de velden `env` en `envFull`; die tweede is de "volledige
omgeving (subscription · resource group)" en is vrije tekst voor een operator. Er was geen veld waarmee
een programma kon weten wat het moest bevragen.

**Dat werd vandaag fataal, en de reden staat in punt 30: een resource group die niet bestaat geeft
HTTP 200 met nul rijen.** Er is geen fout, geen 404 en geen lege body — er is een geslaagd antwoord.
Een collector die zijn scope uit een weergavetekst afleidt, levert bij een tikfout dus geen storing maar
een leeg antwoord dat als "geen kosten" doorrolt naar een factuur. En die weergaveteksten zijn niet te
ontleden:

```
de echte klant   501a66d2-de54-4d4f-9f7c-1fbb55bec17f mbv
de demoklanten   sub-soratus-acme · rg-acme-prod
```

De eerste heeft geen scheidingsteken en noemt de resource group in kleine letters terwijl hij `MBV`
heet; de tweede noemt een abonnement dat geen guid is. Er is geen ontleedregel die op beide werkt, en
een ontleedregel die op één werkt is de gevaarlijkste soort: hij lijkt te werken.

### Het besluit: één veld, in de exacte ARM-padvorm

`CustomerDocument.AzureScope` (`azureScope`), tekst, `null` toegestaan. `AzureScope` is het type dat hem
leest en controleert. De afweging tussen "twee velden" (abonnements-id plus resourcegroepnaam) en "één
pad" is echt, en dit is waarom het één pad is geworden:

1. **Eén veld heeft twee toestanden en twee velden hebben drie.** Leeg betekent "niet ingericht" en dat
   is een geldige toestand (punt 15). Met twee velden bestaat er een derde toestand — de één ingevuld en
   de ander niet — die niets betekent, en die apart moet worden afgevangen, gemeld en getest. Een pad is
   één waarde: hij is er of hij is er niet.
2. **Het is letterlijk de tekenreeks die de deur uit gaat.** De aanroep is
   `POST https://management.azure.com{scope}/providers/Microsoft.CostManagement/query`, dus er wordt
   niets samengesteld. Met twee velden bouwt de collector het pad, en bouwt het scherm het opnieuw voor
   de regel "bevraagd: …" — en de eerste keer dat die twee opbouwen verschillen staat er op het scherm
   een andere scope dan er is bevraagd. Dat is precies de tweede waarheid die punt 34 bij het
   opslagpercentage weigert, op het veld dat als enige verdediging tegen een tikfout dient.
3. **De operator plakt, hij typt niet.** Het veld *Resource-ID* op de eigenschappenpagina van een
   resource group in Azure is exact deze tekenreeks. De invoerweg is kopiëren, en de foutmelding zegt
   dat ook — een melding die dat niet zegt laat iemand het opnieuw intypen.

### Wat er wordt gecontroleerd, en wat niet kan

Het abonnements-id moet een guid **met streepjes** zijn (`Guid.TryParseExact(…, "D")` en niet
`TryParse`: ARM neemt de vorm met accolades en die zonder streepjes niet aan, en een pad dat de API met
een 400 afwijst staat dan maanden in de opslag zonder bedrag). De resourcegroepnaam moet aan de regels van
Azure voldoen: één tot negentig tekens, letters — ook unicodeletters, `café` is een geldige naam —
cijfers, `_`, `-`, `.` en ronde haakjes, en niet eindigend op een punt. De vaste segmenten mogen in elke
schrijfwijze worden ingevoerd en worden genormaliseerd.

**Wat níet te controleren is, is of die resource group bestaat.** Dat is de meting van punt 30 en er is
geen code die eraan ontkomt. Juist daarom is wat wél te controleren is ook echt gecontroleerd: dit is de
enige laag die er is, en daaronder ligt alleen nog "zet op het scherm wát er is bevraagd".

### De schrijfwijze van de resourcegroepnaam blijft van de operator — en dat is bijgesteld

De eerste opzet ging ervan uit dat een verkeerde hoofdletter fataal was, want de echte resource group
heet `MBV`. **Gemeten op 21 augustus 2026 en dat blijkt niet zo:**

```
POST …/resourceGroups/MBV/providers/Microsoft.CostManagement/query   → 200, 112 rijen
POST …/resourcegroups/mbv/providers/Microsoft.CostManagement/query   → 200, 112 rijen  (identiek)
```

Zelfde kolommen, zelfde bedragen. Het pad is hoofdletterongevoelig, dus de hoofdletter valt weg als
storingsoorzaak. Wat overblijft is één reden om de naam niet aan te raken: deze tekenreeks komt onder een
maand zonder regels op het scherm als "bevraagd: …", en daar hoort te staan wat er is ingevuld en niet
wat wij ervan hebben gemaakt. De twee *vaste* delen van het pad worden wél genormaliseerd — die zijn van
Azure en niet van de operator, en genormaliseerd zijn twee klantscopes met elkaar te vergelijken.

### Náást `envFull` en niet in plaats daarvan, met een regel die zegt als er één van de twee is

`envFull` is wat een mens leest; dit is wat een machine gebruikt. **Ze mogen uiteenlopen en dat is geen
fout:** een klant met twee resource groups heeft een weergavetekst die meer noemt dan er wordt gemeten.
Wat wél iets betekent is dat er precies één van de twee is ingevuld — dan denkt iemand dat de omgeving is
vastgelegd terwijl er niet wordt gemeten, of staat er een meetscope bij een klant waarvan niemand kan
zien waar hij hoort. Het omgevingsblok zet daar een regel bij, uit de twee velden van het formulier zelf,
dus ook over wat er net is getypt en nog niet bewaard. Een regel en geen blokkade: een blokkade zou van
"mag uiteenlopen" een leugen maken.

### Leeg is een geldige toestand, en het facturatiescherm zegt iets anders dan "onbekend"

Een klant zonder scope wordt niet bevraagd. Er komt dus geen verbruiksdocument, en de afwezigheid daarvan
wordt `AzureCostState.Unknown` — nooit € 0,00. Maar "onbekend" en "niet ingericht" vragen een
verschillende handeling: wachten tegenover iets invullen. Daarom staat er op het operatorfacturatiescherm
één regel boven de tabel: *"Voor deze klant is geen Azure-scope vastgelegd, dus er wordt niets gemeten."*

Dat is een **tekst op het viewmodel en geen vijfde waarde in `AzureCostState`**. Die enum beschrijft een
meting, en er is er geen; "geen document betekent geen status" (punt 2) blijft dus staan. En waaróm er
niet is gemeten is een eigenschap van de klant en niet van een maand, dus hij staat één keer boven de
tabel en niet twaalf keer in een rij.

Er is een derde geval en het heeft zijn eigen tekst: een scope die er wél is en niet te gebruiken is. Dat
kan alleen als iemand het document met de hand heeft aangepast — beide formulieren valideren — en het is
niet van een ontbrekende scope te onderscheiden aan de lege kostenkolom, terwijl de handeling anders is:
corrigeren in plaats van invullen.

### Rolzichtbaarheid

Het veld is operator-only, en dat is een typeverschil en geen filter: het staat op `OperatorContractView`
en `OperatorBillingView` en niet op de klantvarianten. §2 wijst de volledige omgeving aan de operator toe.
`ContractZichtbaarheidTests` somt het nu op met de regel erbij — die test werd rood bij het toevoegen van
het veld, precies zoals bedoeld.

### Bestaande documenten zijn niet gemigreerd

Vaste lijn in dit project, en hier goedkoop: de zeven demoklanten zijn verzonnen en verdwijnen toch, en
van de enige echte klant is de scope met de hand in te vullen op het contractscherm. Uit `envFull` raden
zou precies de fout maken waartegen dit veld bestaat — en die fout is stil.

---

## 38. De kostencollector draait in het portaal, en de dagclaim is een slot op een budget en niet op een handeling

De lezing, de volledigheidscontrole en de berekening bestonden (punten 30 t/m 35) en hadden geen
productie-aanroeper. Die is er nu: `AzureCostCollector`, een `BackgroundService` in het portaal, dagelijks
om 04:00 UTC.

### Waarom in het portaal

Alles wat de collector nodig heeft staat daar al en nergens anders: de managed identity die als enige
`Cost Management Reader` op de resource group heeft (B5 van het haalbaarheidsonderzoek), het schrijfrecht
op de portaalopslag, de klantenlijst, en de lees-, volledigheids- en rekencode. Een eigen deployable zou
vier dingen vragen die vandaag geen van alle bestaan — een eigen identity, een rolverlening in élk
abonnement waar een klant leeft, een eigen Cosmos-verlening en een eigen uitrol — en er één ding voor
teruggeven dat we juist niet willen: **een tweede aanroeper.** Het budget hangt aan de aanroeper en niet
aan de scope; de header heet `clienttype-retry-after`. Van alles wat dit werk zou kunnen doen, is het
portaal het enige dat het recht al heeft.

### De prijs, en het antwoord erop

Het portaal kan meer dan één instantie hebben, en dan draaien er twee collectors. Dat is niet alleen
dubbel werk: ze verdelen de emmer tot geen van beide nog een bedrag krijgt. `Soratus.Portal/Mail/` heeft
voor precies dit soort probleem een vorm — een document met een afgeleide sleutel, geschreven vóór de
handeling, waarbij een 409 betekent "iemand anders doet het al" — en die past hier. Eén claimdocument per
dag, `costRun-{jjjj-MM-dd}` in de gereserveerde partitie `$portal`, met een `CreateItemAsync` en geen
upsert. De tweede instantie krijgt een 409 en doet niets; dat is `information` en geen waarschuwing, want
op een portaal met twee instanties is dat elke nacht het normale gedrag van de ene van de twee.

**Maar de betekenis is anders dan bij de mail, en dat verschil is het opschrijven waard.** Daar is de
claim een slot op een *onherhaalbare handeling*: een verstuurde mail is niet terug te halen, dus "onbekend
of het gelukt is" is daar géén reden om het opnieuw te proberen en komt het portaal er alleen langs een
mens uit. Een kostenlezing is wél herhaalbaar — er gaat niets de deur uit — dus dit is geen slot op
herhalen maar een **wederzijdse uitsluiting tussen instanties**. Vandaar dat er geen toestand op het
claimdocument staat en geen uitgang: er valt niets vrij te geven.

**En daarom mag een halve run blijven liggen.** Valt de app om halverwege, dan blijft de claim van vandaag
staan en gebeurt er vandaag niets meer. Dat kost niets, en dat is het eigenlijke argument voor deze vorm:
*elke run leest de hele maand.* Een overgeslagen dag gaat niet verloren, hij wordt de volgende nacht
ingehaald. Ook voor de volledigheid maakt het niet uit — `SettlementDays` is twee, dus een maand die op de
3e wordt gelezen heet net zo goed volledig als een maand die op de 2e wordt gelezen. Een claim met een
verlooptijd zou daar niets aan verbeteren en wel iets kosten: het verschil tussen "loopt nog" en "is
omgevallen" is alleen door de klok te bepalen, en dat is precies de constructie die `StatementSendState`
afwijst.

### Het cronmoment is niet verschoven, en dat is de winst

Het onderzoek (§6) geeft twee wegen en adviseert de eerste: de volledigheid controleren in plaats van
later draaien. Die controle bestaat al — `AzureCostCompleteness.Judge`, punt 31 — en de collector gebruikt
hem. Er is geen tweede geschreven en er staat nergens een drempel of een percentage. Om 04:00 op de 1e
levert dat `Partial` of niets op, en dus geen factuur met een halve dag Azure erin.

### Twee aanroepen per klant per dag, en achtentwintig dagen per maand maar één

Per klant wordt de **vorige** maand gedaan en daarna de lopende. Die volgorde is niet cosmetisch: de
vorige maand is de maand die gefactureerd gaat worden, en loopt het budget halverwege leeg dan is de maand
die je wil hebben degene die je het eerst hebt gedaan.

De vorige maand wordt overgeslagen zodra hij op `Measured` staat. Zo'n maand kan niet meer veranderen — de
volledigheidsregel eist dat de laatste dag er staat én dat er twee dagen ná de maand is gemeten, en aan
beide is niets meer te doen. Dat is een besparing op het schaarse ding (een aanroep) met het goedkope (een
puntlezing van ongeveer één RU), en voor achtentwintig van de eenendertig dagen van een maand halveert het
het aantal aanroepen per klant. Alleen `Measured` telt: `Partial` wordt wél opnieuw opgevraagd, anders
wordt een maand die op de 1e om 04:00 onvolledig was nooit meer bijgewerkt.

Verder terug dan één maand gaat de collector niet. Een maand die drie maanden geleden nooit is gemeten
wordt door deze taak niet ingehaald — dat is een handmatige inhaalslag en geen nachtelijke gewoonte, want
hij kost per klant per maand een aanroep uit hetzelfde budget en zou de metingen van vannacht verdringen.
Gemeld als open punt.

### Eén vraagvorm, en het is de vorm die is gemeten

`type: ActualCost`, `timeframe: Custom` over een periode die **volledig in het verleden** ligt,
`granularity: Daily`, gegroepeerd op `ServiceName`. `Custom` en niet `MonthToDate`: dat tweede werkt
alleen voor de lopende maand, dus een afgesloten maand vraagt hoe dan ook `Custom` — en dan is één
vraagvorm beter dan twee, want de tweede is de vorm die op de dag dat hij misgaat niet is gemeten. Een
`to` in de toekomst is niet gemeten en wordt daarom niet gebruikt: de periode loopt tot en met
**gisteren**. Dat kost niets, want de boeking loopt ongeveer acht uur achter en de run staat om 04:00 UTC.

**En het levert een besparing op precies de dag waar punt 30 over gaat.** Op de 1e van de maand om 04:00
valt "gisteren" in de vorige maand, dus is de periode voor de nieuwe maand leeg. Er wordt dan **niet
gevraagd**, in plaats van een 200 met nul rijen op te halen die als `NoLines` zou worden weggeschreven.
Niet vragen is hier eerlijker dan vragen — "wij hebben niet gemeten" is iets anders dan "de API zei nul
regels" — en het scheelt een aanroep uit een emmer die er geen over heeft.

---

## 39. Een mislukte aanroep schrijft niets weg; een antwoord dat we niet konden lezen wél

Dit is de scherpste regel van de collector en hij bestaat in drie delen.

| Wat er terugkwam | Wat er wordt weggeschreven |
|---|---|
| **niets** — een 429 waarvan de pogingen op zijn, de 404 uit §2, een tijdslimiet | **niets** |
| **een antwoord dat niet te lezen was** — een ontbrekende kolom, een bedrag dat geen getal is | `Unknown`, met de reden en de bevraagde scope |
| **een antwoord** — nul rijen, of rijen | `NoLines`, `Partial` of `Measured` uit `Judge` |

**Bij niets wordt er niets geschreven, en dat is §32 letterlijk:** wat er op het scherm hoort te staan als
de verzameling van vannacht is mislukt, is de lezing van gisteren met het tijdstip erbij. Het bewaarde
getal is werkelijk gemeten; de mislukte aanroep heeft niets gemeten. Zou hier een document met `Unknown`
worden geschreven, dan wist één 429 een bedrag dat er wél was.

**Bij een onleesbaar antwoord wordt er wél geschreven, en dat overschrijft dus een goed getal van
gisteren.** Dat is de juiste richting en punt 33 zegt het al: een onleesbaar bedrag werpt en wordt geen
nul, en de aanroeper hoort er `Unknown` van te maken. Het betekent dat onze lezer niet meer bij de API
past, en dat is een defect dat zichtbaar hoort te zijn. Van de twee mogelijke fouten — geen bedrag of een
te laag bedrag — is alleen de eerste zichtbaar. Dit is bovendien de enige bron van
`AzureCostDocument.Failure`; zonder haar zou dat veld een stille onwaarheid zijn (punt 11).

**En er is een vierde geval dat bij het bouwen boven kwam.** `Judge` negeert dagen buiten de gevraagde
maand, en noemt de maand daarmee leeg — de veilige kant, en zo staat het in punt 31. Maar als er *wel*
regels zijn en géén dag binnen de maand, zou daar een document uit komen dat `NoLines` zegt naast een
subtotaal dat wél bestaat, want `AzureCostReading.Subtotal` is de som van de regels. Dat is geen toestand
maar een defect — de bevraagde periode was niet de maand — en het wordt `Unknown` met een reden. Eén `if`,
en `Judge` blijft de enige autoriteit over de toestand.

**Een 429 is geen mislukte run.** Hij logt als `warn` met `api.retry` — de vorm die de seed-data al
gebruikt — en de run slaagt. Bij de gemeten uitvalskans zou de collector anders permanent amber staan en
zou de storingsmelder van fase 6 gaan mailen over een gezonde agent.

### De backoff

Twee pogingen per maand per klant en geen drie. **Elke respons kost budget, ook een mislukte:** gemeten
liep `qpu-remaining` over eenentwintig aanroepen van 599 naar 578 terwijl de meeste ervan 429's waren. Een
derde poging kost de vólgende klant zijn meting.

De wachthint wordt gelezen als hij er is — `entity-retry-after` én `clienttype-retry-after`, de grootste
van de twee, want de eerste verschijnt alleen zodra de entiteitsteller op nul staat en is dan de grotere —
met een eigen vloer eronder. Die vloer is niet netheid: gemeten waarden 1, 3, 4 en 12 waren aantoonbaar te
kort. `x-ms-ratelimit-remaining-subscription-resource-requests` wordt niet gelezen en niet bewaakt; die
stond in élke meting op 1099, óók op de 429's.

Een 403 of 401 wordt **niet** herhaald. Dat gaat niet over van zichzelf — het is een ontbrekende
rolverlening — en herhalen kost budget en verandert niets.

Een `nextLink` wordt gevolgd, met dezelfde stilte ertussen en met een grens van twintig pagina's. Op de
gemeten scope was hij altijd `null` (112 rijen over een maand), dus **dat pad is niet gemeten**: dat het
een POST met dezelfde body naar dat adres is komt uit de documentatie. De grens is er zodat een verkeerde
aanname geen eindeloze lus wordt die de emmer leegtrekt, en raakt hij op dan is de uitkomst `Unreadable`
en geen halve som — want een lezer die een pagina laat liggen heeft een subtotaal dat te laag is, en dat
is even onzichtbaar als de overgeslagen rij uit punt 33.

### De schrijfkant is een eigen interface en geen twee methoden op de leeskant

`IPortalCostsStore` zegt "alleen lezen, en dat is geen tijdelijke beperking", en dat blijft staan. Elke
methode daar vraagt een scope: het bewijs dat er een mens naar een klant kijkt en dat hij dat mag. **De
collector heeft geen mens en dus geen scope, en zou er een moeten verzinnen om daar langs te komen** — een
operatorbewijs zonder operator, en dat is precies de constructie waarmee een autorisatiegrens ophoudt iets
te betekenen. Vandaar `IAzureCostCollectorStore`, met de klantslug als parameter.

Wat dat kost, eerlijk: de isolatie-eigenschap van de leeskant ("er is geen aanroep waarmee je met de scope
van klant A bij klant B komt") geldt daar niet. Wat er in de plaats staat is dat die interface alleen kan
schrijven, alleen de twee soorten van de collector, en dat de enige leesmethode één enum teruggeeft en
geen document — hij bestaat om een aanroep te vermijden en niet om iets te lezen. Het ergste dat een fout
kan doen is een verbruiksdocument in de verkeerde partitie zetten, en dát is op het scherm te zien: de
bevraagde scope staat eronder.

---

## 40. Opnieuw gemeten op 21 augustus: de stilte tussen twee aanroepen moet minuten zijn, en de emmer is niet de onze alleen

Vier metingen, `api-version=2023-11-01`, `timeframe: Custom` over juli 2026 met dagkorrel, scope
`resourceGroups/MBV`, als `marcel@`:

```
12:09:22  200   112 rijen, kolommen Cost/UsageDate/ServiceName/Currency, nextLink null
12:10:15  429   (+53 s)    clienttype-retry-after: 3    entity-requests DefaultQuota:3   qpu 598/59/11
12:12:07  429   (+112 s)   clienttype-retry-after: 12   entity-requests DefaultQuota:3   qpu 597/59/11
12:15:24  200   (+197 s)   112 rijen
12:25:15  429   (+591 s)   clienttype-retry-after: 4    entity-requests DefaultQuota:3   qpu 595/59/11
```

Drie dingen die het ontwerp raken.

**De stilte moet veel langer zijn dan het onderzoek suggereert.** §2 daarvan meldt dat een geslaagde
aanroep dertig tot veertig seconden stilte vroeg. Dat gold voor een `MonthToDate`-probe; een maandvraag met
dagkorrel is zwaarder, en drieënvijftig seconden was er niet genoeg voor. `PauseSeconds` staat daarom op
**240** en `MaxAttempts` op **2**. Bij zeven klanten met twee maanden is dat ongeveer een uur, en 's nachts
is een uur gratis.

**De tellers die je kunt zien, zijn niet de emmer die je tegenhoudt.** In alle drie de 429's stond
`entity-requests` op `DefaultQuota:3` — dus drie over — en stond `qpu` op ruim 595 per uur, 59 per minuut
en 11 per tien seconden. Punt 32 voor de tweede keer, nu met de aanvulling dat `qpu-remaining` er
inmiddels drie sub-tellers heeft (`QueriesPerHour`, `QueriesPerMin`, `QueriesPer10Sec`) waar die meting er
één noteerde. Geen van de vijf is bruikbaar.

**En de emmer wordt gedeeld met aanroepers die wij niet kennen.** Tussen 12:15 en 12:25 liep
`QueriesPerHour` van 597 naar 595 terwijl er één eigen aanroep tussen zat, en na bijna tien minuten stilte
kwam er alsnog een 429. Er werkte die dag meer dan één sessie aan deze lane. Dat maakt de 240 een
bovengrens met marge en geen gemeten minimum — de veilige kant is dezelfde — en het levert de
belangrijkste gedragsregel van deze lane op: **een 429 is geen mislukte run**, want de oorzaak kan buiten
ons liggen.

Wat er níet is bijgemeten en wel is bevestigd: `timeframe: Custom` over een afgesloten maand met dagkorrel
werkt, geeft de kolomvolgorde `Cost, UsageDate, ServiceName, Currency` van punt 33, en `nextLink` was
opnieuw `null`.

---

## 41. Wat de mutatieronde vond, en wat er niet gedekt is

Vijfenveertig mutaties over `AzureScope`, `AzureCostClient`, `AzureCostCollector`, `AzureCostOptions`, de
twee formulieren en de twee weergavelagen. Eenenveertig deden wat ze hoorden te doen. **Vier maakten niets
rood, en dat waren de nuttige vier.**

### Gat 1 — de validatie en de ontleding konden uiteenlopen

`AzureScope.TryParse` mocht de naamcontrole weglaten zonder dat er iets rood werd: de tests stonden alleen
op `Validate`. Dat is geen dubbele controle maar een eis, want de twee worden door verschillende kanten
gebruikt — de schrijfkant valideert, de collector en het facturatiescherm ontleden. Zouden ze uiteenlopen,
dan staat er "wordt gemeten" bij een klant die niet wordt gemeten, of weigert het formulier een scope
waarmee de collector prima uit de voeten kan. Er staan nu tests op beide kanten, in beide richtingen.

### Gat 2 — er werd na een 429 twee keer gewacht, en dat verborg de vloer

Het weghalen van de eigen vloer onder de wachthint maakte niets rood. De oorzaak was geen ontbrekende test
maar een fout in de code: er stond een `Task.Delay(Backoff)` aan het begin van de pogingenlus **en** een
`Task.Delay(Wait(response))` aan het eind. Na een geweigerd verzoek werd er dus twee keer gewacht, en
omdat beide op dezelfde waarde uitkwamen bleef de test groen als de ene wegviel. De wachttijd was daarmee
het dubbele van wat er staat, en de vloer was niet te meten. De lusdelay is weg; de delay voor het
exceptiepad staat nu in het `catch` waar hij thuishoort.

**Dit is de nuttigste vondst van de ronde**, en het is precies het soort fout dat een test niet vindt maar
een mutatie wel: twee stukken code die per ongeluk hetzelfde doen dekken elkaars afwezigheid.

### Gat 3 — de vlag `Enabled` had geen test die niet kon hangen

De vlag stond alleen in `ExecuteAsync`. Een test die daarop staat moet de dagelijkse lus starten, en met
een klok die niet wacht draait die lus eindeloos: het negeren van de vlag levert dan geen rode test op
maar een test die hangt. De vlag staat nu óók bovenaan `RunAsync`, met de reden erbij — dat is de enige
methode die werk doet, en zij is `internal` en dus rechtstreeks aanroepbaar. Eén veld, één betekenis, dus
geen tweede waarheid; twee plekken waar hij geldt, waarvan één te testen is.

### Gat 4 — het omgevingsblok mocht de scope laten vallen bij bewaren

Het weghalen van `AzureScope` uit de bewerking die het contractscherm verstuurt maakte niets rood. Dat is
exact de fout die voor `TelemetryEndpoint` wél een test had: `SaveCustomerAsync` vervangt het hele
klantdocument, dus een veld dat het formulier niet draagt wordt bij het eerste bewaren leeggemaakt — en
dan zet een operator die de klantnaam verbetert de kostenmeting van die klant uit, waarna het
facturatiescherm "niet ingericht" zegt en niemand weet waardoor. Er staan nu vier tests op dat blok:
bewaren laat de scope staan, een verkeerde scope is te herstellen, een onbruikbare scope komt de opslag
niet in, en leeghalen mag.

### Vier mutaties die met opzet niets rood maakten

Deze zijn gedraaid om vast te leggen wat er *niet* gedekt is, en ze deden wat ervan werd verwacht:

| Mutatie | Waarom hij niet gedekt is |
|---|---|
| het logniveau van een 429 wordt `error` | er staat geen test op logniveaus. Bewust: de regel "een 429 is geen mislukte run" leeft in de uitkomst van de run en niet in een logregel |
| het claimdocument noteert niet wie er heeft geclaimd | `ClaimedBy` is er om na te zoeken en niet om op te rekenen |
| de `kind`-controle op de puntlezing verdwijnt | `CosmosAzureCostCollectorStore` heeft geen test: hij praat met Cosmos |
| de échte opslag schrijft de scope niet op het klantdocument | zie hieronder |

### Wat er niet is gemeten, en dat is het eerlijkste deel van dit werk

**`CosmosAzureCostCollectorStore` en de klantdocumentmapping in `CosmosPortalDataStore` hebben geen
test.** Beide praten met Cosmos, en de testfixture bouwt de klantdocumentmapping ná in plaats van de
productiecode aan te roepen — anders dan bij het contract en de toegang, waar `Documentvorm` de
`internal` productiemapping gebruikt. De mutatie "de echte opslag schrijft de scope niet op het
klantdocument" maakte daarom niets rood, terwijl dezelfde fout in de fixture wél tests rood maakt.

Dat is een echt gat en het is niet met een test te dichten zonder óf naar Cosmos te schrijven óf de
mapping uit de methode te halen zoals bij `ToDocument`. Het tweede is de betere oplossing en het raakt
een bestand met meer schrijvers; het staat daarom als voorstel en niet als wijziging.

**De collector heeft nooit tegen Cosmos of tegen Cost Management gedraaid.** De claim (409 bij een tweede
instantie), de upsert van het maanddocument en de cross-partition query naar de klantenlijst zijn tegen
een fixture bewezen en niet tegen de opslag. De 409-eigenschap zelf is elders in dit project wél gemeten
(`infra.md`, de klant-batch), dus de vorm is niet nieuw — maar deze aanroepen zijn dat wel.

**En de gedragsregel over de 404 is niet opnieuw uitgelokt.** Dat hij bestaat is twee keer gemeten (§2 en
punt 30); dat de backoff hem overleeft is tegen een eigen handler bewezen en niet tegen Azure. Een 404 op
verzoek uitlokken kan niet.

---

## 42. Een agent kan een endpoint zijn: de hartslag komt dan van de host, en dat is niet hetzelfde als "hij werkt"

**Spec:** §4 en §5 gaan uit van agents met een eigen proces — een container met een schema, een lus, en
een hartslag die uit dat proces komt. Punt 1 verving de bron (Cosmos in plaats van Log Analytics) maar
liet die vorm staan: één proces, één agent, één schema.

**Werkelijkheid bij de eerste echte klant.** Er zijn geen achtergrondagents. Zijn drie "agents" zijn
diensten binnen één bestaande ASP.NET Core-webapplicatie, aangeroepen per verzoek: een chat tegen een
boekhoudkoppeling, een financieel overzicht, en het inlezen van declaraties uit Excel. Geen schema, geen
eigen proces, geen eigen lus. Wat er wél is: die webapplicatie draait op een App Service met **Always On
aan** op een Premium-plan, dus het proces is continu in leven.

Daaruit volgt het hele ontwerp.

### Wat er gebouwd is

- **De hartslag komt van de host, niet van het werk.** Eén achtergronddienst
  (`HostedAgentsRegistrationService`) klopt namens elke agent die het proces herbergt. Alle drie de
  hartslagen zijn per constructie even oud: er is één proces om over te kloppen.
- **De levensfase is een waarneming en geen mededeling.** Loopt er geen aanroep, dan is de fase
  `IdleWaiting`; met een verse hartslag levert `AgentStatusCalculator` daar `Idle` op — rang 1, dus een
  wachtende dienst tilt de klant in het overzicht nooit naar boven. Loopt er een aanroep, dan `Running`.
  Bij een agent met een eigen lus meldt de agent dat zelf, omdat een leeg wachtinterval van buiten niet
  van een vastgelopen lus te onderscheiden is; hier is dat onderscheid er wél, want de bibliotheek opent
  en sluit elke aanroep zelf. Een `ReportLifecycle` op een geherbergde agent zou een tweede, afwijkende
  waarheid over dezelfde toestand toestaan en bestaat daarom niet.
- **Elke aanroep is een run.** Eén chatgesprek, één inlezing: één `RunRecord`, tweemaal geschreven
  (`Running` bij het begin, de afloop bij het einde). Een mislukte inlezing wordt `Failed`, en dat weegt
  in de statusberekening zwaarder dan `Idle` — gemeten: een dienst met een verse hartslag, levensfase
  `IdleWaiting` en één mislukte run komt uit op `Failed`.
- **`TriggerKind` is nooit `timer`.** `Schedule` blijft leeg, `NextRunAt` blijft `null`. Een
  aankondiging met `timer` wordt geweigerd in plaats van stil gecorrigeerd: de documentatie van
  `AgentRegistration.Schedule` belooft bij een timer-agent een cron-expressie, en dan zou er in het
  portaal een agent op schema staan zonder schema.

### Waar het staat, en waarom in twee delen

Het geval splitst netjes in twee stukken, en de scheidslijn is de afhankelijkheid en niet het
gebruiksgeval:

1. **In `Soratus.Agents.Telemetry`** (`HostedAgents/`, plus vier internal typen): meerdere agents in één
   host, een hartslag van de host, een run per aanroep, en de logroutering. Daar zit niets van een
   webframework in. Een wachtrijhost met drie abonnementen heeft exact dezelfde vorm; er staan tests in
   `Soratus.Agents.Telemetry.Tests` die die laag zonder ASP.NET aandrijven, en dat is precies wat ze
   bewijzen.
2. **In `Soratus.Agents.AspNetCore`** (nieuw project, vijf typen): het antwoord op de enige vraag die
   hostspecifiek is — *welke* agents herbergt dit proces — plus de laag die een verzoek in een run zet.

Het scharnier daartussen is `IHostedAgentSource`: één methode, `GetAgents()`.

**Waarom een eigen project en geen map in de telemetriebibliotheek, gemeten.** Een
`FrameworkReference` naar `Microsoft.AspNetCore.App` is besmettelijk. De `runtimeconfig.json` van
`agents/heartbeat-demo` — een consoleagent — vraagt vandaag één framework:

```
"framework": { "name": "Microsoft.NETCore.App", "version": "10.0.0" }
```

Met die verwijzing in `Soratus.Agents.Telemetry` erbij gezet, opnieuw gebouwd en gemeten, vraagt hij er
twee:

```
"frameworks": [ { "name": "Microsoft.NETCore.App", … }, { "name": "Microsoft.AspNetCore.App", … } ]
```

Elke consoleagent zou dan de webruntime nodig hebben om te kunnen starten. Dat is de hele afweging; het
experiment is teruggedraaid en na terugdraaien opnieuw gemeten.

`Soratus.Agents.Contracts` is niet aangeraakt en heeft geen webafhankelijkheid gekregen.

### Wat een aanroeper moet doen

Eén registratie, één regel in de pijplijn, één regel per endpoint:

```csharp
builder.AddSoratusWebAgents();

app.UseRouting();
app.UseSoratusAgentRuns();

var chat      = app.MapPost("/api/chat", …).WithSoratusAgent("boekhoud-chat", "Chat", "POST /api/chat");
var overzicht = app.MapGet("/api/financieel", …).WithSoratusAgent("financieel-overzicht", "Rapportage");
var import    = app.MapPost("/api/declaraties", …).WithSoratusAgent("declaraties-import", "Document-intake");
```

De bestaande handlers worden niet aangeraakt. Wil een handler melden hoeveel regels hij verwerkte, dan
kost dat één regel: `context.SoratusAgentRun()?.Processed(regels)`.

**De lijst met agents staat maar op één plek, en dat is de plek waar het werk staat.** De hartslag leest
dezelfde endpoint-metadata als de aanroeplaag (`EndpointHostedAgentSource` over `EndpointDataSource`).
Er is dus geen tweede lijst in de opstartcode die met de eerste uit de pas kan lopen — en de fout die
dán ontstaat is een dienst die aanroepen verwerkt zonder in het portaal te staan, of een dienst in het
portaal die niet bestaat.

**Waarom middleware en niet een endpoint-filter.** Een filter zou nul extra regels kosten: hij kan mee in
dezelfde `WithSoratusAgent`-aanroep. Twee dingen wegen zwaarder. Een filter draait alleen om een
minimal-API-handler, dus een MVC-controller met dezelfde metadata krijgt een hartslag en nooit een run —
en in het portaal ziet dat eruit als een dienst die niemand aanroept, wat de duurste fout is die dit
contract kan maken. En een filter is klaar zodra de handler zijn resultaat teruggeeft, terwijl het
wegschrijven daarvan er nog na komt; bij een chat die zijn antwoord in stukjes stuurt is dat het grootste
deel van de duur.

**En die ene regel is niet weg te automatiseren.** Een `IStartupFilter` kan middleware alleen vóór of ná
de hele gebruikerspijplijn hangen: vóór `UseRouting` is het endpoint nog onbekend, en ná de endpointlaag
komt hij nooit meer aan de beurt. Vergeten wordt daarom niet stil gemaakt maar luid: `EndpointWiringCheck`
kijkt op `ApplicationStarted` of er endpoints een agent aankondigen terwijl de aanroeplaag niet in de
pijplijn staat, en schrijft dan per betrokken agent één `error`-logregel — "Deze dienst legt geen aanroepen
vast; de koppeling in de webapplicatie is niet volledig ingericht." Geen uitzondering: dit loopt in de
webapplicatie van een klant, en telemetrie mag zijn app niet neerhalen. Rood in het portaal, app in de
lucht.

### Wat "gezond" hier betekent, en wat het niet betekent

Dit is de kern, en het is een echte beperking en geen detail.

Een verse hartslag bewijst **dat het proces leeft en dat de weg naar de opslag open is**. Dat is precies
wat een klant over zijn webapplicatie wil weten, en het is niet weinig. Hij bewijst **niet** dat een van
deze drie diensten doet waarvoor hij er is. Een endpoint dat niemand meer aanroept, of dat achter een
kapotte inlog staat, of waarvan de knop uit de gebruikersinterface is verdwenen, klopt even trouw door als
een endpoint dat de hele dag werk verzet.

Dus: **`Idle` betekent hier letterlijk "de host leeft en er loopt geen aanroep", en niet "deze agent
werkt".** Het enige bewijs dat een agent op aanvraag werkt is zijn **laatste geslaagde run**.

Wat een operator uit een grijze `Idle`-stip op deze drie diensten wél mag concluderen:

- het webproces leeft en heeft in de laatste twee minuten iets weggeschreven;
- er liep op dat moment geen aanroep;
- de laatst afgeronde aanroep is niet mislukt (was hij dat wel, dan stond er `Failed`).

Wat hij er **niet** uit mag concluderen:

- dat de dienst vandaag is aangeroepen;
- dat de dienst ooit is aangeroepen;
- dat de dienst, als hij aangeroepen zou worden, zou werken.

Die drie staan in de kolom **laatste run**, en nergens anders.

**Het onderscheid is in de gegevens te maken, zonder nieuw veld.** Het handschrift van een agent op
aanvraag is een drietal: `triggerKind` is `http` (of `queue`/`webhook`), `schedule` is leeg en `nextRunAt`
is leeg. Bij een agent met dat drietal zegt de status niets over het werk; bij een agent met een schema
zegt hij dat wél, want daar is een gemiste run zichtbaar doordat `nextRunAt` in het verleden ligt. Het
portaal kan die twee dus onderscheiden met wat er nu al in het document staat.

### Wat het contract hiervoor mist — gemeld en niet zelf toegevoegd

Twee dingen zijn vandaag niet uit te drukken. Beide zijn bewust níet in `Soratus.Agents.Contracts`
bijgebouwd; ze staan hier als besluit voor de eigenaar van het contract.

1. **Er is geen verwachting van aanroep.** Bij een timer-agent is stilte te beoordelen: er staat een
   `nextRunAt`, en als die voorbij is zonder run is er iets mis. Bij een agent op aanvraag is er niets om
   stilte tegen af te meten, dus "drie maanden niet aangeroepen omdat de knop weg is" en "vanmiddag
   twintig keer aangeroepen en nu even niets" leveren exact hetzelfde document op. Wat zou helpen is één
   optioneel veld van de vorm "verwacht hoogstens zoveel tijd tussen twee runs", door de bouwer gezet en
   door het portaal gelezen. Dat is een contractuitbreiding met gevolgen voor het scherm, de statusrangen
   en de storingsmelder, en die keuze is niet aan deze sessie.
2. **`RunResult` heeft geen waarde voor "de aanroeper haakte af".** Een gebruiker die zijn tabblad
   sluit halverwege een chat levert een run op die niet `Ok` is (het werk is niet klaar), niet `Failed`
   (er is niets stuk, en rood dat afgaat op een dichtgeklapt tabblad is binnen een week niets meer waard)
   en niet `Skipped` (er was wél werk). Vandaag wordt het `Skipped`, met één `warn`-regel `run.aborted`
   erbij die vertelt wat er echt gebeurde. De uitkomst is goed — geen valse storing — en de reden is
   onnauwkeurig. Dezelfde vorm als de openstaande `StatementRefusal` in punt 29.

### De afhankelijkheid die niemand ziet: Always On

Deze hartslag bestaat alleen zolang het proces geladen blijft. Op een App Service is dat een **instelling
buiten de code**: staat Always On uit, dan laadt het platform de app na ongeveer twintig minuten zonder
verkeer uit, stopt de hartslag, en meldt het portaal na twee minuten stilte een storing terwijl er niets
aan de hand is. Eén vinkje in een ander scherm draait de betekenis van deze code om.

Daar is in code niets tegen te doen — een uitgeladen proces kan niets meer melden. Wat er wél kan is het
**afleesbaar maken**, en dat is gedaan zonder een veld te verzinnen dat het contract niet heeft:

1. **`startedAt` is het moment waarop het proces startte, gelijk op alle geherbergde agents.** Dat is een
   veld dat al bestond, en het is het diagnostische paar: schuift `startedAt` na elke stilte op, dan wordt
   het proces telkens uitgeladen en opnieuw gestart; blijft `startedAt` staan terwijl de hartslag stokt,
   dan is er iets mis met het proces zelf. Twee verschillende diagnoses uit één bestaand veld.
2. **Eén `host.started`-regel per agent per processtart.** Eén zo'n regel per uitrol is normaal. Staat hij
   elke twintig minuten opnieuw in de logtabel, dan is dát het patroon. De uitleg gaat operator-only mee in
   `extra`, want de lezer die het patroon aantreft zoekt op dat moment de betekenis en niet de
   documentatie: *"Deze regel hoort één keer per uitrol te staan. Staat hij elke twintig minuten opnieuw,
   dan wordt dit proces telkens uitgeladen en stopt de hartslag daartussen; op een Azure App Service is
   dat de instelling Always On."* In `msg` staat die uitleg níet — dat veld leest de klant, en die heeft
   aan een Azure-instelling niets (punten 13 en 14).

Verder staat de afhankelijkheid opgeschreven op de plek waar iemand komt kijken als de hartslag stopt: in
de `<remarks>` van `HostedAgentsRegistrationService`, naast de lus die klopt.

**Wat hier een aanname is en geen meting:** of Azure een app bij het uitladen netjes afsluit. Zo ja, dan
schrijft deze dienst nog een laatste document met `stoppedCleanly` en staat elke dienst na twintig minuten
stilte eerst op `Idle` en daarna op `Degraded`. Zo nee, dan blijft de laatste hartslag staan en wordt het
meteen `Degraded`. Dat verschil is hier niet te meten en het is niet nagerekend.

### Kleinere besluiten, met de reden

**Geen vierde rij "de webhost".** De verleiding is groot: de host is immers wat de hartslag echt bewijst.
Maar zijn status zou per constructie gelijk zijn aan die van de drie diensten, dus die rij voegt een regel
toe zonder een feit toe te voegen — en de klant heeft drie diensten en zou er vier zien. De hostidentiteit
bestaat wel (hij levert klant, versie, omgeving en `startedAt` aan de drie), maar wordt niet gepubliceerd.

**5xx is een storing, 4xx niet.** Een antwoord met een 5xx-code maakt de run `Failed`, ook als er geen
uitzondering ontsnapte: dan is de dienst zelf omgevallen. Een 4xx níet: dan heeft de aanroeper iets
verkeerd meegestuurd en heeft de dienst juist goed gewerkt door het te weigeren. Zou dat rood worden, dan
kleurt het portaal zodra een gebruiker een verkeerd Excel-bestand aanbiedt, en dan is rood niets meer waard.
Wie het anders wil, roept `Fail` zelf aan op de run.

**Een logregel buiten een aanroep gaat niet naar het portaal.** In een host met één agent hoort elke regel
bij die agent. In een host met drie is er buiten een aanroep geen eigenaar, en de bibliotheek verzint er
geen: dan zou er in de logtabel van de declaratie-inlezing een melding staan die uit de chat kwam. De regel
blijft wel in de gewone log van de host (console, Application Insights). Voor een mededeling van de host
die tóch aan een agent hoort — zoals `host.started` — is er `ISoratusHostedAgent.ReportEvent`.

**De twee vormen van de bibliotheek sluiten elkaar uit.** `AddSoratusAgent` en `AddSoratusHostedAgents`
naast elkaar zou twee hartslagen met verschillende betekenis over hetzelfde proces schrijven; de tweede
aanroep werpt. En `SORATUS_AGENT__SCHEDULE` op een host met diensten op aanvraag werpt ook: een
cron-expressie die niets plant wordt geloofd.

### Wat het meten opleverde

**Een echte race, en hij was intermittent.** De eerste versie publiceerde de registraties één keer bij het
starten van de achtergronddienst. Gemeten over vijf opstarts van dezelfde applicatie: **twee keer nul
agents, drie keer drie agents** — `EndpointDataSource` was op dat moment nog leeg, omdat de
verzoekpijplijn door een ándere achtergronddienst wordt gebouwd en de volgorde daarvan niet van ons is. In
de twee slechte gevallen zou het portaal een halve minuut niets van de diensten weten, en dat is op het
scherm geen fout maar afwezigheid. Opgelost door óók op `ApplicationStarted` te publiceren (het document is
een upsert, dus dat kost één schrijfactie per agent) en door de bronnen bij elke hartslag opnieuw te vragen
in plaats van één keer. Met de mutatie die de tweede publicatie weghaalt vallen 12 van de 31 tests om.

**En een tweede, die daaronder lag.** De eerste melding stond in `ExecuteAsync` van de
achtergronddienst, met de gedachte dat een `BackgroundService` zijn lijf synchroon begint. Dat is niet zo,
en het is gemeten: zes opstarts van dezelfde host, elke keer direct na `StartAsync` geteld, **zes keer nul
agents bekend**. De host wacht op `StartAsync` en niet op wat er in `ExecuteAsync` gebeurt, dus alles wat
"bij het starten" moet gebeuren en waar iemand op mag rekenen, hoort in een `StartAsync`-override.
Zichtbaar gevolg vóór de reparatie: vier van de tien testruns rood, met een lege opslag en niets in de
log — de duurste soort rood, want het lijkt op een toevalligheid. Na de reparatie zes runs op rij groen,
en de gemeten telling direct na `StartAsync` is één van één.

Diezelfde eigenschap zit in `AgentRegistrationService`, het pad met één agent: ook daar staat de eerste
registratie in `ExecuteAsync`. In productie is het verschil een handvol milliseconden en er is geen test
die erop leunt, dus het is hier niet meeveranderd — maar het is dezelfde vorm, en wie daar ooit een test
op zet die vlak na `StartAsync` kijkt, krijgt dezelfde flakiness.

**Een val in de configuratie van de bibliotheek, gevonden en niet gerepareerd.** De foutmelding bij een
ontbrekende omgeving zegt: *"Zet `SORATUS_AGENT__ENVIRONMENT` expliciet op prod, acc of dev."* Wie dat
letterlijk doet, krijgt bij het opstarten: *"heeft de waarde 'prod', maar dat is geen geldige
AgentEnvironment. Geldig zijn: Production, Acceptance, Development."* De lezing gaat via `Enum.TryParse` en
kent dus de enumnamen, terwijl `prod`/`acc`/`dev` de namen zijn waarmee het veld in het JSON-document
staat. De melding wijst een weg die de parser weigert. Dat is een bestaand gedrag van het pad met één
agent en het veranderen ervan verandert welke configuratiewaarden geldig zijn, dus het staat hier als
melding en niet als wijziging.

### Twee dingen voor wie hier straks op verder bouwt

**Er zit geen betekenis in een naam.** De agentnaam wordt door de aanroeper opgegeven en nergens
ontleed: niet uit de route, niet uit de naam van de handler, niet uit de HTTP-methode. Wie
`WithSoratusAgent("declaraties-import")` schrijft, krijgt die naam en verder niets. De enige plek waar
uit een naam iets wordt afgeleid is de terugvaloptie voor de typeaanduiding
(`declaraties-import` → `Declaraties import`), en dat is presentatie: hij staat in `displayType`, wordt
door niets gelezen dat een besluit neemt, en verdwijnt zodra de bouwer zelf een type opgeeft.

**Wat een storingsmelder van deze drie diensten moet weten om niet drie keer te mailen.** Ze zitten in
één proces. Valt dat proces uit, dan worden alle drie tegelijk `Degraded` — één oorzaak, drie agents. Het
veld waaraan dat te zien is, is `startedAt`: dat is bij geherbergde agents de start van het *proces* en
dus exact gelijk op alle drie de documenten (er is een test die dat vastpint). Een melder die groepeert op
`customerId` plus `startedAt` ziet daarmee "één host met drie diensten" en kan één bericht sturen in
plaats van drie. Zonder die groepering stuurt hij er drie, en dan is de derde mail de reden dat de eerste
ook niet meer gelezen wordt.

---

## 43. De verzendlaag is één plek geworden, en de storingsmelder is de tweede aanroeper — met de ontdubbeling in de melder en niet in de rekenregel

**Spec:** §4 (`storingsmelder`, elke minuut, "mailt Soratus bij failed/degraded"), §7 fase 6, en de
koppelingentabel bij §5: *"storingsmeldingen aan Soratus, maandoverzicht aan klant"*. Die laatste regel
is de belangrijkste van dit punt, en waarom staat onder 43.4.

Dit is één wijziging en niet twee. Er waren twee plekken die zelf een `EmailClient` bouwden en
`SendAsync` aanriepen — `Soratus.Web/Services/LeadSink.cs` en `Soratus.Portal/Mail/StatementMailSender.cs`
— en de melder zou de derde zijn geworden. Drie kopieën van één handeling is precies wat de knipregel
dit project heeft gekost (punt 13). **De laag is daarom met twee aanroepers tegelijk gebouwd**: een
gedeelde laag met één gebruiker bewijst niets, en de tweede aanroeper ontdekt anders pas later dat de
vorm niet past. Wat dat concreet opleverde staat in 43.2.

### 43.1 Wat er in de laag zit, en wat er buiten bleef

`IMailOutbox` in `Soratus.Portal/Mail/MailOutbox.cs`. Erin zit de **verzendsemantiek** en geen omhulsel
om `SendAsync`:

| | |
|---|---|
| `MailDelivery` | drie uitkomsten en geen twee: `Unknown` (eerste waarde, dus de veilige standaard), `Accepted`, `Refused` |
| de indeling | `RequestFailedException` met **400–499** is `Refused` — een `429` daaronder, want throttling betekent "niet aangenomen". Al het andere is `Unknown`: `5xx`, tijdslimiet, verbroken verbinding, annulering, onleesbare endpoint |
| geen herhaling | geen `retry`, geen backoff, geen tweede poging. Uit "onbekend" komt niets automatisch |
| `MailOutboxState` | `NotConfigured` / `DryRun` / `Ready`, met de proefdraaimodus standaard aan |
| `OutgoingMail` | abstract; de ontvangers zitten op het bericht en niet op de verzendaanroep |
| `MailText.OneLine` | één definitie van "één regel", voor elke onderwerpregel |
| `MailAddresses.IsUsable` | één controle op "is dit als één ontvanger te gebruiken" |

Erbuiten bleef precies wat per doel verschilt: **de opmaak en de ontvanger.**
`StatementMailComposer` maakt de klantmail, `AgentAlertComposer` de operatormail. En erbuiten bleef ook
de boekhouding eromheen — claimen, bevestigen, ontdubbelen — want die is per doel anders: bij het
maandoverzicht één document per klant per maand met drie standen, bij de melder één markering per agent
met een herhaalvenster.

**`MailOutboxState` is een vraag en geen uitkomst, en dat is het scherpste van dit ontwerp.** De
proefdraaimodus kon niet ín `SendAsync` zitten. §29.8 eist dat een proefdraai níets vastlegt, dus de
aanroeper moet de stand kennen *vóór* zijn onomkeerbare boekhouding — bij het maandoverzicht vóór de
claim, bij de melder vóór het zetten van de markering. Zat de controle in `SendAsync`, dan kende de
aanroeper hem pas nadat hij zich had vastgelegd. Wat de vorm dan kost: een aanroeper kán de stand
vergeten te lezen. Daarom **werpt `SendAsync` als de stand niet `Ready` is**. Dat is de enige plek waar
deze laag werpt, en het is met opzet: luidruchtig omvallen is beter dan een proefdraai die stil echte
mail verstuurt.

De regel zelf staat op `PortalMailOptions.Outbox()` en niet in de laag. Dat is geen ordelijkheid: de
testdubbel leest diezelfde methode. Zou de dubbel de stand zelf uitrekenen, dan meet elke test op de
proefdraaimodus zijn eigen kopie van die beslissing en blijft hij groen als de echte laag hem omdraait
— punt 41, gat 2, letterlijk: twee stukken code die per ongeluk hetzelfde doen dekken elkaars
afwezigheid.

### 43.2 Wat de tweede aanroeper aan de laag heeft veranderd

Dit is het antwoord op "waarom niet los bouwen", en het is meetbaar. Drie dingen zijn ánders geworden
doordat de melder er tegelijk op stond:

1. **`SendAsync` neemt geen `MailSender` meer aan.** De eerste vorm gaf de afzender per aanroep mee,
   omdat het maandoverzicht die toch al uit de opties haalde voor zijn `MailNotConfigured`-weigering.
   De melder heeft dat niet: hij wil één vraag stellen ("mag er iets uit?") en niet twee. De afzender
   zit nu in de laag, en daarmee is er precies één plek die de configuratie leest.
2. **`StatementSendResult` is `MailSendResult` geworden en `MailDelivery` is verhuisd.** Dat is niet
   alleen hernoemen: het type stond in het bestand van de klantmail, dus de melder zou een type over
   "statements" hebben aangenomen. Een naam die niet klopt wordt later weggehaald.
3. **`OutgoingMail` is abstract geworden.** De eerste vorm was één concreet berichttype. Met twee doelen
   valt dat om: op de opmaak van de klantmail staat een broncodetest die elke foutmelding weert (§29.4,
   punten 13 en 14), en op de operatormail staat die met opzet níet. Eén type maakt dat verschil een
   afspraak; twee subtypen onder één basis maken het een typeverschil. Dezelfde constructie als
   `AgentRunRow` in punt 14, en om dezelfde reden.

`Soratus.Web` is **niet** aangeraakt. Wat daarvoor nodig zou zijn staat in 43.8.

### 43.3 De melder: de volgorde is het ontwerp

`Soratus.Portal/Alerts/`. Lezen → groeperen → ontdubbelen → afremmen → claimen → versturen →
vastleggen. Twee dingen in die reeks staan vast en zijn met een mutatie beproefd:

- **De proefdraai staat vóór de claim.** Een proefdraai die een markering achterlaat is geen
  proefdraai: dan staat er "gemeld" bij een mail die nooit is verstuurd, en wordt de echte storing
  daarna zes uur onderdrukt. §29.8, met een eigen gevolg.
- **De rem staat vóór de claim.** Wat er door `MaxMailsPerRun` niet uitgaat wordt ook niet vastgelegd
  en komt de volgende ronde weer in aanmerking. De rij loopt zichzelf leeg in plaats van dat er
  meldingen verdwijnen.

**Alleen productie-agents.** Punt 9 zegt dat voor de ernstrang van het overzicht; hier weegt het
zwaarder. De interne klant draait `heartbeat-demo` op `dev`, die meestal uit staat en dus permanent
`Degraded` is. Zonder dat filter mailt de melder daar elke zes uur over, en dan is hij binnen een week
weggefilterd — precies de fout die punt 9 bij het overzicht beschrijft.

**Een klant die niet te lezen was levert geen melding op**, alleen een `warning`. "Wij konden niet
lezen" is geen storing van de agent, en `ShouldAlert` meldt niet over `Unknown`. Zonder die keuze zou
één hapering van Cosmos een mail per agent van die klant opleveren.

### 43.4 Waarom deze mail wél een stacktrace mag dragen, en hoe er geen weg naar een klant bestaat

De koppelingentabel bij §5 zegt het in één regel: **storingsmeldingen gaan naar Soratus.** Punt 13 ging
over een stacktrace die "zichtbaar voor een klant" was; punt 14 zegt letterlijk dat de operator de
typenaam op het runtabblad hoort te vinden. Beide regels beschermen dus *de klant* en niet de tekst.
Hier is er geen klant, en dan is het weglaten van een `errorType` of een foutmelding geen
zorgvuldigheid maar het weggooien van precies de informatie waarvoor de mail bestaat.

De melding draagt daarom: agentnaam, type, versie, de stilte in woorden, de laatste run met resultaat,
duur en `runId`, `rolledBack`, het **volledige** `errorType` mét naamruimte, de **volledige**
`errorMessage` inclusief regelovergangen, en een link naar het agentdetail. Punt 14 legt uit waarom de
korte naam hier de verkeerde reparatie is: `Sync.ValidationException` en `Mail.ValidationException` zijn
twee verschillende defecten.

**Dat er geen weg naar een klant bestaat, is drie keer vastgelegd en geen van de drie is een afspraak:**

1. De ontvangers komen uit `PortalAlerts:Recipients` — configuratie. Er is geen parameter op
   `AgentAlertComposer` waarin een klantadres past.
2. Er staat een broncodetest op dat de map `Alerts/` `AccessDocument`, `GetAccessAsync`,
   `StatementRecipients`, `StatementAddressing`, `IPortalDataStore`, `IPortalHoursStore` en
   `PortalAccessRoles` nergens aanraakt. Dat is een *afwezigheid*, en die is met een gedragstest niet
   aan te tonen.
3. `AgentAlertMail` en `StatementMail` zijn broertjes onder `OutgoingMail` en geen van beide is de
   ander. Er is geen pad waarlangs een storingsmelding het klantpad neemt, want dat pad neemt het
   andere type aan. Beide hebben een `internal` constructor en één opmaakfunctie.

En de scheiding zit ook in de mappen: de broncodetest van §29.4 gaat de opmaakbestanden van de
**klantmail** af op `Exception.Message`, `StackTrace`, `ToString` en `ErrorCode` — `Mail/StatementMail.cs`,
`Mail/StatementText.cs` en nu ook `Mail/MailText.cs`, want die knip is gedeeld. De operatoropmaak staat
in `Alerts/` en valt daar dus buiten. Dat is bedoeld, en het staat hier zodat niemand het later
"opruimt" door de mappen samen te voegen.

**Wat er níet gesloten is, en dat hoort erbij:** de klantnaam in de onderwerpregel blijft vrije tekst
uit onze eigen administratie. Hij gaat door dezelfde `MailText.OneLine` als bij het maandoverzicht,
maar staat er een interne aanduiding in de eerste regel van een klantnaam, dan gaat die mee — naar onze
eigen postbus, dus het risico is hier kleiner dan bij punt 13.

### 43.5 Ontdubbelen in twee lagen, en waarom die twee niet aan dezelfde sleutel hangen

`ShouldAlert` ontdubbelt met opzet niet — het is de zuivere vraag "hoort hier een melding over" en niet
"hebben we die al gestuurd", en het scherm gebruikt diezelfde functie. Voor `Failed` levert dat elke
aanroep `true`. De melder draait elke minuut, dus zonder ontdubbeling zestig mails per uur over dezelfde
mislukte run. **Die ontdubbeling staat dus in de melder**, en hij bestaat in twee lagen die
uitdrukkelijk *niet* dezelfde sleutel gebruiken:

**Laag 1 — groeperen op `customerId` + `startedAt`.** Punt 42: drie diensten in één webapplicatie worden
bij uitval van het proces alle drie tegelijk `Degraded` — één oorzaak, drie agents — en `startedAt` is
bij geherbergde agents de start van het *proces* en dus exact gelijk op alle drie de registraties. Eén
groep is één mail. Bij een agent met een eigen proces doet de groepering niets, en dat is juist: twee
losse agents zijn niet in dezelfde milliseconde gestart, dus twee groepen en twee meldingen — er zijn
dan ook twee oorzaken. Er staat nergens een controle op "is dit een geherbergde agent"; het veld doet
het werk. De klant staat in de sleutel omdat twee klanten met dezelfde starttijd anders in één mail
zouden belanden.

**Laag 2 — een markering per agent, en niet per groep.** Dit is het punt dat bij het bouwen boven kwam
en het is de reden dat de twee lagen gescheiden zijn: **een herstart schuift `startedAt` op.** Zou de
ontdubbeling aan de groepsleutel hangen, dan levert een proces dat elke minuut opnieuw start elke minuut
een nieuwe sleutel op, en dan ontdubbelt er niets — een crashlus wordt dan een mailstroom. De markering
hangt daarom aan (`customerId`, `agentName`): `agentAlert-{klant}-{agent}` in de gereserveerde partitie
`$portal`.

Dat het in `$portal` staat en niet bij de klant heeft twee redenen. Het is Soratus-eigen boekhouding
over onze eigen meldingen — de klant heeft er niets mee te maken. En het maakt de lezing goedkoop: alle
markeringen in één partitie, dus één query binnen één partitie per ronde in plaats van een
cross-partition query of één query per klant. Dezelfde plek en dezelfde reden als
`AzureCostRunDocument`.

### 43.6 Wanneer een melding herhaald mag worden, en waarom dat een keuze is en geen afgeleide

Er is geen goed antwoord dat uit de spec volgt. Wat er wél volgt is dat **beide uitersten fout zijn**:
elke minuut melden maakt de melder waardeloos, en één keer melden over een storing die drie dagen duurt
is een storing waarvan niemand meer weet dat hij er is. Dat tweede is even echt als het eerste, en het
is de fout die makkelijker over het hoofd wordt gezien.

Het besluit: **een venster van zes uur** (`PortalAlerts:RepeatAfterHours`), met twee uitzonderingen die
niet wachten.

- **Een veranderde status meldt meteen.** Beide kanten op. `Degraded` → `Failed` is nieuwe informatie en
  wachten zou die zes uur oud maken; `Failed` → `Degraded` is een ander beeld, en de operator hoort niet
  uit een oude mail te concluderen wat er nú aan de hand is.
- **Een afgesloten markering geldt als geen markering.** Een storing die weg was en terugkomt is een
  nieuwe storing, ook al is het dezelfde agent en dezelfde status.

Waarom zes en niet vierentwintig: zes betekent hoogstens vier meldingen per storing per dag, dus binnen
één werkdag komt een openstaande storing minstens één keer terug, en een storing die een weekend duurt
levert acht mails op in plaats van twee. Genoeg om op te vallen, weinig genoeg om te blijven lezen.
Vierentwintig is even verdedigbaar en het is één configuratiewaarde — het verschil is een voorkeur en
geen meting, en het staat hier als zodanig.

**Wat deze keuze kost, eerlijk.** Een agent die om het uur heen en weer flappert tussen `Degraded` en
`Failed` levert elke keer een melding op. Dat is bewust niet gedempt: zo'n agent *is* een storing, en de
dempening die dit zou tegenhouden — een venster op "er is over deze agent iets gemeld", ongeacht wat —
zou ook de escalatie tegenhouden. Van die twee fouten is de tweede duurder. Punt van twijfel.

**Bij herstel gaat er geen mail.** §7 vraagt te mailen bij `failed` en `degraded`; een tweede mail per
storing verdubbelt het volume om iets te melden dat op het scherm staat. De markering wordt wel
*afgesloten* en niet verwijderd — dat is het antwoord op "hoe lang duurde die storing" en het maakt een
terugkeer meteen weer meldbaar.

### 43.7 Twee instanties, en waarom de dagclaim van de kostencollector hier niet past

Het portaal kan meer dan één instantie hebben, en dan draaien er twee melders. `AzureCostCollector`
lost dat op met een dagclaim, en **punt 38 zegt zelf waarom die vorm hier niet past**: daar is de claim
een wederzijdse uitsluiting op een *schaars budget*, een kostenlezing is herhaalbaar en er gaat niets de
deur uit. Hier is het het mailgeval: een verstuurde mail is niet terug te halen.

Een dagclaim zou hier bovendien iets kapotmaken. Hij zou de eerste melder van de dag álle meldingen van
die dag laten doen en de tweede geen enkele, en bij een herstart zou er een dag lang niets meer worden
gemeld — precies wanneer je hem nodig hebt. De claim gaat daarom **per agent per melding**, op hetzelfde
document dat de ontdubbeling draagt: `CreateItemAsync` bij de eerste melding (409 = een ander doet het
al) en `ReplaceItemAsync` met een etagcontrole bij een herhaling (412 = idem). Twee instanties lezen
dezelfde etag, één vervanging slaagt.

**Wat dat niet dicht, en dat staat er expliciet:** raken twee instanties elkaar precies op dit moment,
dan kan één host twee mails opleveren, elk met een deel van de diensten — het geval dat §42 wilde
vermijden, nu alleen nog onder een race in plaats van standaard. De eigenschap die er wél is: **elke
mail noemt precies wat hij heeft geclaimd**, dus dezelfde dienst staat niet in twee mails. Die
eigenschap is door een mutatie ontdekt en niet door een ontwerp — zie 43.10, gat 1. De vorm die de race
ook zou dichten is één claim per groep, en die valt af omdat de groepsleutel bij elke processtart
verschuift.

**Een mislukte verzending wordt niet opnieuw geprobeerd, ook niet bij `Refused`.** Dat is de vaste
stelregel, met hier een tweede reden: de volgende ronde is een minuut later. Een `4xx` is bij deze mail
vrijwel altijd een inrichtingsfout — een ontvanger die niet klopt, een afzender die niet is geverifieerd
— en die gaat niet over binnen een minuut; elke minuut opnieuw proberen zou een storing in het melden
verergeren tot een storing bij de dienstverlener.

**En het scherpste van deze lane: dat het melden zelf stuk is, is niet met een mail te melden.** Er is
vandaag geen tweede kanaal. Het staat daarom als `error` in het log, op drie plekken: geen ingerichte
mail, geen bruikbare ontvanger, en een verzending die niet is aangenomen. Dat is een echte beperking en
geen detail.

### 43.8 Wat er nodig zou zijn om de marketingsite de derde aanroeper te maken

`Soratus.Web` is ongemoeid gebleven. Wat het zou vragen, zodat dat besluit op een lijst rust en niet op
een gevoel — vijf dingen, en alleen het eerste is code:

1. **Een gedeelde bibliotheek.** De laag staat nu in `Soratus.Portal/Mail/`, en de site is een eigen
   deployable zonder projectreferentie daarheen. Het zou een vijfde project worden (`Soratus.Mail`?) met
   `Azure.Communication.Email` erin. Beide projecten staan vandaag op 1.1.0, dus dat botst niet — maar
   het legt die versie voor beide vast.
2. **Een managed identity op `app-soratus-prod`.** Gemeten: die App Service staat niet in `infra/` en
   heeft in Azure geen user-assigned identity; `infra/portal/portal-rg.bicep` heeft er wél een
   (`id-soratus-portal`). Er is dus niets om een rol aan te verlenen.
3. **Een roltoewijzing op `acs-soratus-prod`** voor die identity, met de custom role uit §29.10.
4. **De connection string eruit.** `AzureEmail__ConnectionString` staat als platte app-setting op
   `app-soratus-prod`. Zolang hij er staat is de identity een tweede weg naar hetzelfde en geen
   vervanging — en dan is er niets opgelost, alleen iets bijgekomen. Er staat al een aparte taak voor
   dat geheim.
5. **Een uitrol van de site.** Elke wijziging hier raakt `deploy.yml` en de live marketingsite.

Wat het zou opleveren: één plek voor de drie takken en de drie uitkomsten, en één plek waar de
aanmelding wordt opgelost. Wat het kost is punt 2 tot en met 5, en dat is geen codewijziging. **Meting
die het besluit zou moeten dragen en die er nog niet is:** hoe vaak `LeadSink` faalt, en hoe. Vandaag
gooit hij bij een `RequestFailedException` een `InvalidOperationException` door en leest hij geen
`4xx`/`5xx`-onderscheid; of dat ooit iets heeft gekost is niet gemeten.

### 43.9 Wat er in Azure bij moet, en twee fouten in het blok van §29.10

**Voor het versturen zelf niets.** De melder gebruikt dezelfde identity, dezelfde ACS-resource en
dezelfde custom role als het maandoverzicht. Het `az`-blok in §29.10 volstaat; er is geen extra actie en
er is niets in Azure gewijzigd. De grens die daar is benoemd blijft ook staan: `az role definition create`
is een schrijfactie op abonnementsniveau en valt daarmee buiten de twee resource groups waar wij mogen
schrijven. Besluit voor Marcel.

Wat er wél bij komt is één configuratiesleutel, en die is geen geheim:

```
PortalAlerts__Recipients__0   storingen@soratus.com   ← het adres is een keuze voor Marcel
```

**Twee fouten in het configuratieblok van §29.10, gemeten en niet gerepareerd.**

1. **De vijf `PortalMail__*`-sleutels staan daar op `app-soratus-prod`, en dat is de marketingsite.**
   Het portaal is `app-soratus-portal-prod` (`infra/portal/portal-rg.bicep`, `param portalAppName`). Wie
   het blok letterlijk uitvoert, configureert de verkeerde app: het portaal blijft "mailen is niet
   ingericht" zeggen en de site krijgt vijf instellingen die hij nooit leest. Dat is een storing die
   zich voordoet als een inrichtingsfout op de verkeerde plek.
2. **Met de hand gezette app-settings op het portaal worden door de volgende uitrol gewist.** De
   `appSettings` van `portalApp` staan in `infra/portal/portal-rg.bicep` als volledige array, en die
   eigenschap is in ARM een vervanging en geen samenvoeging. `PortalMail__*` en `PortalAlerts__*` horen
   dus in die template en niet in een `az webapp config appsettings set`. Niet zelf gedaan: `infra/` is
   een andere lane, en het raakt een template waar een `what-if` naast hoort.

`PortalMail:DryRun` staat nergens in `appsettings.json`, dus de standaard uit de code geldt: **aan.**
`PortalAlerts` staat er ook niet, dus `Enabled` is aan en `Recipients` is leeg — de melder zegt bij elke
ronde als `error` dat hij niets kan melden, en er staat een test op dat dat de huidige stand is.

### 43.10 De mutatieronde: zesendertig mutaties, waarvan zes bewust stil

Zesendertig mutaties over de melder, de groepering, de ontdubbelregel, de opmaak, de gedeelde
verzendlaag en de test die het scopevrije pad bewaakt. **Negenentwintig werden meteen rood, zes maakten
met opzet niets rood, en één maakte niets rood terwijl hij dat wél hoorde te doen.** Die één was de
nuttigste vondst van de ronde.

#### Gat 1 — de mail mocht agents noemen die niet waren geclaimd

Het samenstellen van de melding uit de volledige groep in plaats van uit de geclaimde agents maakte
niets rood. Dat is precies de eigenschap waar 43.7 op leunt: raken twee instanties elkaar, dan noemt
elke mail alleen wat híj heeft geclaimd, want anders staat dezelfde dienst in twee mails en gaat een
operator twee keer hetzelfde zoeken. Er was geen test met een *gedeeltelijke* botsing — alleen met een
volledige, en dan is de geclaimde verzameling leeg en gaat er niets uit, dus de mutatie was onzichtbaar.
Er staat nu een test die van drie diensten in één host de middelste laat botsen, en die toetst dat de
mail de andere twee noemt, dat de onderwerpregel "2 diensten" zegt, en dat er twee markeringen staan en
niet drie.

#### Zes mutaties die met opzet niets rood maakten

Deze zijn gedraaid om vast te leggen wat er *niet* gedekt is, en ze deden wat ervan werd verwacht:

| Mutatie | Waarom hij niet gedekt is |
|---|---|
| het logniveau van de rem wordt `information` | er staat geen test op logniveaus. Bewust, dezelfde afweging als bij punt 41: de regel leeft in het gedrag en niet in een logregel |
| de markering noteert niet wie er heeft gemeld | `NotifiedBy` is er om na te zoeken en niet om op te rekenen |
| de echte markeringen komen in de partitie van de klant | `CosmosAgentAlertStore` heeft geen test: hij praat met Cosmos |
| de markeringenquery loopt over alle partities | idem — dit is een kostenkeuze en geen gedrag |
| de echte bron leest klanten zonder ingerichte opslag ook | `TelemetryAgentFaultSource` heeft geen test: hij praat met Cosmos |
| de echte bron gebruikt de klant*naam* als partitiesleutel | idem, en dit is de nare: hij zou álle agents van élke klant missen |

**Dat laatste rijtje is het eerlijkste deel van dit werk.** `CosmosAgentAlertStore` en
`TelemetryAgentFaultSource` hebben geen test, om dezelfde reden als
`CosmosAzureCostCollectorStore` in punt 41: ze praten met Cosmos, en de fixture bouwt hun gedrag ná in
plaats van de productiecode aan te roepen. De claim (409 bij een tweede instantie, 412 bij een tweede
herhaling), de etagcontrole en de query in de gereserveerde partitie zijn tegen een fixture bewezen en
niet tegen de opslag. De 409-eigenschap zelf is elders in dit project wél gemeten (`infra.md`, de
klant-batch), dus de vorm is niet nieuw — deze aanroepen zijn dat wel. **De melder heeft nooit tegen
Cosmos of tegen Communication Services gedraaid, en er is geen enkele echte mail verstuurd.**

Vier mutaties die de moeite van het noemen waard zijn omdat ze wél rood werden en laten zien wat de
tests meten: het productiefilter weghalen (punt 9), de grens van het herhaalvenster van `>` naar `>=`
zetten, `429` als onbekend lezen, en de proefdraaicontrole uit `MonthlyStatementService` halen — die
laatste wordt rood doordat de verzenddubbel zelf eist dat de stand `Ready` is, en dat is de invariant en
niet het gevolg.

#### En de test die het scopevrije pad bewaakt is zelf gemuteerd

Zie 43.11. Twee mutaties: het pad aanroepen uit een bestand in `Components/Pages/` maakt hem rood, en
het type consequent hernoemen maakt hem óók rood — dat tweede is de spiegel, want zonder die assertie
zou een hernoeming de test stil laten meten dat er nergens meer een aanroeper is.

### 43.11 Het scopevrije leespad, en waarom een type dit niet kon dichten

De melder is een achtergronddienst zonder mens en dus zonder `CustomerScope`. Elke methode van
`IAgentTelemetryStore` vraagt er een. Een scope verzinnen is wat punt 39 verbiedt — "een operatorbewijs
zonder operator" — en een eigen query schrijven zou een tweede definitie van "laatste afgeronde run"
opleveren, precies waar het correctheidsargument gedocumenteerd staat (`TOP 1`, niet-lopend, per agent en
niet één tijdvensterquery). Dat is punt 13 in een nieuwe jas.

Wat er dus is: `CosmosAgentTelemetryStore.GetAgentsAsync(CustomerScope)` is gesplitst en het lijf staat
eronder als `internal ScanAsync(AgentScanTarget)`. **Dezelfde twee query's, geen tweede lezing.**
`AgentScanTarget` is een benoemd type en geen tweede parameter naast een losse `string customerId` —
dat laatste is wat `CustomerWriteScope` in zijn eigen documentatie verbiedt, want met een string erbij
is "mag deze gebruiker hierbij" weer een vraag die de aanroeper hoort te stellen. De naam zegt wat het
is en de documentatie zegt wat het **niet** is: waar en van wie, en geen bewijs dat iemand het mag zien.

**Maar een type kan dit binnen één assembly niet dichten, en dat hoort er te staan.** `internal` reikt
tot in de schermen, dus een pagina zou dit pad kunnen aanroepen en de scopecontrole overslaan. De enige
echte bescherming is een test die de aanroepers telt en er precies één eist, in `Alerts/`. Dezelfde
vorm als de test die precies één implementatie van de telemetriestore eist. Hij is met een mutatie
beproefd (43.10) en de bestaande test `DeStoreVraagtOveralEenScopeEnNooitEenLosseKlantSlug` blijft
groen, want `ScanAsync` staat op de klasse en niet op de interface — en dat is precies de plek waar hij
hoort.

Bijkomend: `CosmosAgentTelemetryStore` staat nu als singleton geregistreerd met de interface ernaar
verwijzend, dezelfde constructie als `CosmosPortalDataStore` en om dezelfde reden — een achtergronddienst
kan geen scoped afhankelijkheid krijgen.

### 43.12 Wat de kosten van een ronde per minuut zijn, en dat is geredeneerd en niet gemeten

§4 zegt "elke minuut". Wat dat oplevert is smaller dan het lijkt: een `Degraded` meldt pas na
`AgentStatusThresholds.Alert` — tien minuten — dus alleen bij `Failed` maakt een minuut verschil met twee
minuten.

Wat het kost is niet verwaarloosbaar. Eén ronde is per klant één query voor de registraties plus één per
agent voor de laatste afgeronde run, plus één query voor de markeringen. De stand van zaken meet dat op
het overzicht: bij 20 agents ongeveer 130 RU, richting 200 agents ongeveer 1300 RU. Maal 1440 per dag is
dat 190 000 tot 1,9 miljoen RU per dag. **Dat is een berekening op een meting van een ánder scherm en
geen meting van deze taak.** Het interval is één configuratiewaarde, en de tweede knop is een goedkopere
lezing achter dezelfde naad (`IAgentFaultSource`) waarvoor de melder niet hoeft te veranderen. Gemeld als
open punt, met het getal erbij zodat het niet op "voelt goed" rust.

### 43.13 Kleinere besluiten, met de reden

**De storingsmelder publiceert zelf geen telemetrie.** §4 zet hem als agent in het overzicht, en dat zou
betekenen dat hij zich als geherbergde agent aanmeldt. Dat raakt `Soratus.Agents.AspNetCore` en de
registratielaag, en die zijn van een andere lane. Gemeld en niet gebouwd. Het gevolg vandaag: de melder
staat niet in het portaal, dus dat hij stil is gevallen is alleen in het log te zien.

**Er is geen vierde stand "de melding loopt nu".** Zelfde reden als bij `StatementSendState`: het verschil
tussen "loopt nog" en "onbekend" is alleen door de klok te bepalen, en een proces dat halverwege omvalt
laat "loopt nog" staan.

**De markeringen hebben geen verval.** De container `customers` staat in Bicep op `ttl: null`, dus een
item-TTL doet daar niets. Het zijn hoogstens zoveel documenten als er ooit agents met een storing zijn
geweest, dus het kost niets meetbaars — maar de markering van een agent die is opgeruimd blijft staan.
Dezelfde soort rommel als de dagclaims van de kostencollector, en dezelfde melding.

**`AgentAlertDocumentKeys` staat in `Alerts/` en niet bij `PortalDocumentKinds`.** Zelfde
werkomstandigheid en zelfde vangnet als bij `StatementDocumentKeys` (§29): er werken meer sessies in
`Data/`. Er staat een test op dat de nieuwe `kind` niet botst met een bestaande. Punt van twijfel, geen
ontwerp.

**Een onbruikbaar adres houdt een storingsmelding níet tegen**, en dat is precies andersom dan bij het
maandoverzicht. Daar is één fout adres een reden om helemaal niet te versturen, want de bevestiging zou
"verstuurd" zeggen terwijl de bedoelde lezer niets kreeg. Hier is de afweging omgekeerd: een
storingsmelding die niemand bereikt omdat er een tikfout in het tweede adres staat, is erger dan één die
één van de twee lezers bereikt. De overgeslagen adressen worden als `error` gelogd — stil overslaan zou
betekenen dat de eigenaar van dat adres denkt dat hij meldingen krijgt.

**Er staat geen relatieve tijd in de melding.** Op het scherm is "11 min geleden" het juiste; in een
postbus is het onwaar zodra de mail een uur ongelezen blijft. De absolute tijd staat er, in de
Nederlandse zone met de offset erbij, uit dezelfde `TimeFormat` die het scherm gebruikt. De stilte staat
er als duur, want een duur verandert niet van betekenis.

### 43.14 Eén meting die loog, en die hier hoort te staan

**`dotnet test Soratus.slnx --no-build` sloeg een project over met "The test source file ... was not
found" terwijl hij het in dezelfde regel aankondigde.** Vijf regels "Test run for …", vier uitslagen. Wie
dat leest denkt dat hij vijf projecten heeft gemeten. Dat is exact de klasse fout van §36 en van de drie
valse metingen in de stand van zaken: een groen signaal over de verkeerde verzameling.
**`dotnet test` zonder solutionargument vanuit de wortel vindt en meldt alle vijf**, en dat is de manier.

---

## Wat bewust nog niet is gebouwd

Facturatie, sprint en support. Uit §9 van de spec staat daarmee nog één besluit open dat aan uren
raakt: de Azure-uitsplitsing per dienst (fase 4). De audittrail op urencorrecties is met punt 16
vervallen als aparte vraag.

Binnen uren zelf stond één pad open: het **aannamepad van een koppeling**. Voor `soratus-uren` is dat
gebouwd — `POST /api/uren`, zie punt 26 — en de vraag naar een eigen bewijstype is daarmee vervallen
in plaats van beantwoord: de aanroeper is een mens met een token en dus dezelfde operator als op het
scherm. Wat er van dat pad nog niet is: `devops-sync`. Dat is wél een aanroeper zonder mens
erachter, en daar hoort de vraag opnieuw gesteld te worden voordat er een tweede aanroeper op
`IMcpHoursWriter` komt te staan — een work item dat uren inschiet heeft geen token en geen naam uit
Entra, en `by` is dan het work item en niet een persoon.

### Wat er van de urenopslag níet is gemeten

De documentvorm, de queryvormen, de etagcontrole en de weigering van een dubbele sleutel zijn tegen
`cosmos-soratus-prod` gemeten. Dat is gebeurd op de partitiesleutel `$portal-verificatie` — geen
geldige klantslug (`PortalSlug` eist een begin met een kleine letter of cijfer), dus geen enkel
codepad kan die documenten zien. Ze zijn na de meting op exacte id verwijderd en de partitie is
teruggemeten op nul; de container stond voor en na op acht documenten.

**Wat daarmee dus een aanname is en geen meting:** het gedrag in de partitie van een échte klant,
waar urenregels naast een klant-, contract- en toegangsdocument staan. Er is geen reden om verschil
te verwachten — de query filtert op `c.kind` en de RU-kosten van een gefilterde query hangen aan het
aantal *teruggegeven* rijen, niet aan wat er nog in de partitie staat — en de meting op de
klantenlijst wijst dezelfde kant op (2,96 RU, gelijk met en zonder urenregels in de container). Maar
het is redenering en niet bewijs. De eerste echte urenregel op een klantpartitie is het moment om de
maandquery opnieuw te meten.

Eén punt-lees is hier wél gedekt en dat is de plek waar één container echt iets kost:
`CosmosPortalHoursStore` leest een urenregel op een id uit een formulier, en in diezelfde partitie
liggen documenten van drie andere soorten. Er staat daarom een `kind`-controle op die lezing, zodat
een id die naar een contract of een toegangsregel wijst `null` oplevert in plaats van als urenregel
gelezen te worden.
