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

## Wat bewust nog niet is gebouwd

Facturatie, sprint en support. Uit §9 van de spec staat daarmee nog één besluit open dat aan uren
raakt: de Azure-uitsplitsing per dienst (fase 4). De audittrail op urencorrecties is met punt 16
vervallen als aparte vraag.

Binnen uren zelf staat één pad open: het **aannamepad van een koppeling**. De MCP-server
`soratus-uren` en `devops-sync` schieten regels in die als `pending` landen (§5). De documentvorm
en de sleutelregel liggen vast, maar er is nog geen bewijstype voor een aanroeper die geen mens is
— `CustomerWriteScope` betekent "operator die naar deze klant kijkt", en dat is een koppeling niet.
Zolang dat type niet bestaat, staat er ook geen schrijfmethode voor op `IPortalHoursStore`: een
leesbaar type met een gat erachter is erger dan geen type.

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
