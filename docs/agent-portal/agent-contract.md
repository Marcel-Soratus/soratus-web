# Agentcontract

Dit is de vorm waar elke Soratus-agent zich aan houdt. Wie een nieuwe agent bouwt, leest dit
document en gebruikt `Soratus.Agents.Telemetry`; dan komt de agent zonder verder werk in het
portaal te staan.

## Waarom dit contract bestaat

Het portaal moet van een agent die het nooit eerder heeft gezien kunnen zeggen of hij draait, wat
hij gedaan heeft en of er iets mis is. Dat kan alleen als elke agent dezelfde drie documenten in
dezelfde vorm publiceert.

Het alternatief — het portaal laten raden uit toevallige logregels — werkt precies zolang als
niemand zijn logging aanpast. Eén agent die "verwerkt: 12" schrijft in plaats van
`itemsProcessed: 12`, en de kolom is leeg zonder dat iemand het merkt. Daarom leidt het portaal
niets af uit toevallige telemetrie. Wat niet in dit contract staat, komt niet op het scherm.

Een tweede regel loopt door alles heen: **een agent publiceert feiten, geen oordelen.** Hij meldt
zijn hartslag, zijn levenscyclus en zijn runs. Hij meldt niet dat het goed met hem gaat. Zie
[Statusafleiding](#statusafleiding).

## De drie documenten

Alles staat in Cosmos DB. Veldnamen hieronder zijn de namen zoals ze op de draad staan:
`lastHeartbeatAt`, niet `LastHeartbeatAt`. Tijdstempels zijn ISO-8601 met zone
(`2026-08-19T14:03:11.482+00:00`); schrijf ze in UTC. Enums staan als string op de draad, met de
waarden die hieronder per veld genoemd worden — niet als getal, want een getal in een
opgeslagen document is niet te lezen en niet veilig te herordenen.

### 1. Registratie — `AgentRegistration`

Eén document per agent, telkens overschreven. Dit is "wie ben ik en leef ik nog".

| Veld | Type | Verplicht | Betekenis |
|---|---|---|---|
| `id` | string | ja | Documentsleutel. Gelijk aan `agentName`. |
| `pk` | string | ja | Partitiesleutel. Gelijk aan `agentName`. |
| `customerId` | string | ja | De klant waarvoor deze agent draait, als slug. |
| `agentName` | string | ja | Technische naam, kleine letters met koppelstreepjes (`factuur-intake`). Stabiel over uitrollen heen; alles sluit hierop aan. |
| `displayType` | string | ja | Typeaanduiding voor de typekolom (`Document-intake`). Alleen presentatie. |
| `version` | string | ja | Informational assembly version, door de pijplijn gestempeld. |
| `startedAt` | string (tijd) | ja | Wanneer dit proces startte. |
| `lastHeartbeatAt` | string (tijd) | ja | De laatste hartslag. Dit veld draagt in zijn eentje het verschil tussen live en degraded. |
| `lifecycle` | enum | ja | `running` · `idleWaiting` · `stoppedCleanly`. Wat de agent over zijn eigen levenscyclus meldt — geen status. |
| `schedule` | string | nee | De cron-expressie waarop de agent plant, of weglaten bij een agent die alleen op een trigger draait. Dit is de expressie waarmee daadwerkelijk gepland wordt, geen losse beschrijving. |
| `triggerKind` | enum | ja | `timer` · `queue` · `http` · `webhook` · `blob` · `manual`. |
| `triggerDetail` | string | nee | Toelichting voor het scherm (`Blob-drop (inbox-facturen)`). |
| `nextRunAt` | string (tijd) | nee | De eerstvolgende geplande run, berekend uit `schedule`. Weglaten bij een triggeragent: dan toont het scherm de trigger in plaats van een verzonnen tijdstip. |
| `environment` | enum | ja | `prod` · `acc` · `dev`. De klantweergave toont alleen `prod`. |
| `contractVersion` | int | ja | Versie van dit contract. Nu `1`. |

Wat hier bewust **niet** in staat: status, uptime, "aantal runs in de laatste 24 uur". Dat zijn
afleidingen; het portaal rekent ze uit.

### 2. Run — `RunRecord`

Eén document per run, tweemaal geschreven: bij het starten en bij het afronden.

| Veld | Type | Verplicht | Betekenis |
|---|---|---|---|
| `id` | string | ja | Documentsleutel, gelijk aan de runId (`r-8f3c`). |
| `pk` | string | ja | Partitiesleutel `{agentName}\|{yyyy-MM-dd}`, op de startdatum in UTC. |
| `customerId` | string | ja | De klant. |
| `agentName` | string | ja | De agent. |
| `startedAt` | string (tijd) | ja | Starttijd van de run. |
| `finishedAt` | string (tijd) | nee | Eindtijd. Leeg zolang de run loopt. |
| `durationMs` | number (long) | nee | Duur in milliseconden. Leeg zolang de run loopt. |
| `result` | enum | ja | `running` · `ok` · `failed` · `skipped`. `skipped` betekent: niets te doen gehad, geen fout. |
| `itemsProcessed` | int | ja (default 0) | Hoeveel items deze run verwerkte. Wat een item is, weet alleen de agent. |
| `itemsFailed` | int | ja (default 0) | Hoeveel items zijn afgekeurd of mislukt. |
| `rolledBack` | bool | ja (default false) | Of de transactie is teruggedraaid. |
| `trigger` | enum | ja | Waardoor deze run startte; zelfde waarden als `triggerKind`. |
| `errorType` | string | nee | Het volledige .NET-type van de uitzondering, als de run mislukte. Zie [de audiëntie van de foutvelden](#wie-leest-de-foutvelden-van-een-run). |
| `errorMessage` | string | nee | De boodschap van de uitzondering. Eén zin, één regel — **de klant leest dit.** |
| `version` | string | ja | De agentversie die deze run draaide. Zo is te zien of een fout met een uitrol samenhangt. |

Een run die op `running` blijft staan terwijl de hartslag doorloopt is zelf een signaal: het
proces leeft, maar de run is nooit afgerond.

#### Wie leest de foutvelden van een run

`errorMessage` en `errorType` staan **op de run en niet in `extra`**, en een run heeft geen
operator-only variant zoals een logregel die heeft. Ze hebben dus geen vangnet: wat je hier
schrijft kan op het scherm van de klant komen — het portaal zet het in de tooltip van de
resultaatbadge.

- **`errorMessage` wordt door de klant gelezen.** Dezelfde eisen als aan `msg`: één zin, in het
  Nederlands, geen bestandspaden of klassenamen. En dezelfde afdwinging — de bibliotheek knipt
  alles ná de eerste regelovergang eraf. Dat is geen luxe: `exception.Message` van een
  `CosmosException` is een halve pagina diagnostiek over meerdere regels. De volledige boodschap
  blijft bewaard in de bijbehorende `run.failed`-logregel, en die is operator-only: bij een
  uitzondering onder `extra._exception.message`, en bij een zelf opgegeven boodschap onder
  `extra.msgOverflow` — want ook de tekst van die logregel gaat langs de knip op `msg`.
- **`errorType` houdt zijn volledige typenaam**, inclusief naamruimte
  (`SoratusAgent.Sync.ValidationException`). Dat is opzet en het tegendeel van de regel hierboven,
  om één reden: voor de operator ís de naamruimte het nuttige deel — `Sync.ValidationException` is
  een ander defect dan `Mail.ValidationException`, en na inkorten zijn die twee niet meer te
  onderscheiden. Bij `errorMessage` blijft de volledige tekst elders bewaard, hier niet. Afkappen
  bij het schrijven zou dus informatie weggooien in plaats van verplaatsen.

Vul je `errorType` zelf via de tweede `Fail`-overload, zet er dan géén naamruimte in — een korte
aanduiding als `Http502` volstaat, en die is voor beide lezers goed.

> **Open punt.** Of `errorType` naar de klant geprojecteerd mag worden is een vraag voor het
> portaal en niet voor de schrijfkant. Zolang het klanttype het veld draagt, ziet een klant onze
> naamruimtestructuur. Gemeten: 7 van 112 runs hebben een `errorType`, alle drie de voorkomende
> waarden bevatten een naamruimte, en twee ervan staan op documenten van een echte klant.

### 3. Logregel — `LogRecord`

Eén document per regel. Plat en klein, want dit leest een mens die wil weten of er iets mis is.

| Veld | Type | Verplicht | Betekenis |
|---|---|---|---|
| `id` | string | ja | ULID. Oplopend in tijd, en stabiel — het portaal houdt de live tail hiermee bij zonder de tabel opnieuw op te bouwen. |
| `pk` | string | ja | Partitiesleutel `{agentName}\|{yyyy-MM-dd}`, op het tijdstip van de regel in UTC. |
| `ts` | string (tijd) | ja | Tijdstip van de regel. |
| `level` | enum | ja | `info` · `warn` · `error`. Drie waarden, geen zes. |
| `event` | string | ja | Puntgescheiden gebeurtenisnaam (`document.processed`, `api.retry`). |
| `msg` | string | ja | Eén zin in het Nederlands, leesbaar voor wie de code niet kent. **Eén regel — dit wordt afgedwongen, zie hieronder.** **De klant leest dit.** |
| `runId` | string | nee | De run waarbinnen deze regel viel. Leeg voor regels buiten een run, zoals bij het starten. |
| `extra` | object | nee | Vrije context, uitklapbaar op het scherm. Hier landt de structured-logging-state van `ILogger`, en hier hoort een stacktrace. **Operator-only.** |
| `customerId` | string | ja | De klant. |
| `agentName` | string | ja | De agent. |

`debug` en `trace` horen niet in dit contract. Die zijn voor de ontwikkelaar en gaan naar
Application Insights. Vijfhonderd debugregels per run maken het portaal niet informatiever maar
onleesbaar.

**Framework-categorieën komen pas vanaf `warn` door.** Alles onder `Microsoft.*`, `System.*` en
`Azure.*` wordt op `info` volledig weggefilterd; `warn` en `error` komen wél door. Dezelfde
redenering als bij debug en trace: op `info` vertelt de bibliotheek dingen over zichzelf, niet over
het werk van de klant.

Dit is niet theoretisch. Gemeten in de opslag schreef `Microsoft.Hosting.Lifetime` dit in `msg`,
en `msg` wordt door de klant gelezen:

```
Content root path: D:\SORATUS\Website\...\bin\Debug\net10.0\
```

Een absoluut bestandspad, op één regel — dus de knip hierboven helpt er niet tegen. En het komt
niet van een agentbouwer: het staat er bij élke agent die met een gewone host start.

Dat `Application started` hiermee verdwijnt kost niets. Dat feit staat beter gemodelleerd in het
registratiedocument, als `startedAt` en `lifecycle`: het portaal toont "draait sinds" daaruit, en
een herstart geeft een nieuwe `startedAt`. Een feit in een veld verslaat een regel die je moet zien
langskomen. Hetzelfde geldt voor `Hosting environment`, dat als `environment` in de registratie
staat.

`warn` en `error` blijven omdat een framework-melding dan over echt gedrag gaat.
`HttpsRedirectionMiddleware — Failed to determine the https port for redirect` is onschadelijk,
maar het is een echte melding en een operator hoort hem te kunnen vinden.

De toets is de **categorie**, niet de inhoud van het bericht — met het punt erbij, dus `Azure` en
`Azure.Identity` vallen eronder en een koppeling die je zelf `AzureKoppeling` noemt niet. Een
patroon in de tekst zou vandaag op een pad met `D:\` letten en morgen een pad met `/srv/` missen.
Dit wordt in de bibliotheek afgedwongen en niet met een instelbare logfilter: een contractregel die
een agent kan uitzetten is geen regel.

**Het is een lijst en geen patroon**, en dat is opzet. Het criterium is niet hoe een categorie
heet maar wat er logt: een bibliotheek die zijn eigen werking beschrijft. Dat is geen eigenschap
die je uit een naam kunt aflezen, dus er valt niets te matchen — je moet weten welke bibliotheek
het is. Een patroon zou bovendien de kant op glijden van "namen die op een framework lijken", en
dan filtert het de koppeling weg van een agentbouwer die zijn naamruimte ongelukkig heeft gekozen.

Wie er iets bij wil zetten, toetst daarop: **logt deze bibliotheek op `info` over zichzelf** —
endpoints, paden, versies, interne toestand — in plaats van over het werk van de klant? Zo ja, dan
hoort hij in de lijst. Is het de naam van een koppeling of een domein, dan niet, hoe technisch hij
ook klinkt.

`Azure.*` staat erbij omdat `Azure.Identity` op `info` endpoints en tenant-id's noemt. Dat dit in
`heartbeat-demo` al wegvalt via `"Azure": "Warning"` in `appsettings.json` was juist de reden om
het hier te zetten en niet daar: dat is per-agent configuratie, en precies wat een volgende
agentbouwer niet zet en niet hoeft te weten.

#### Wie leest wat: `msg` is voor de klant, `extra` is voor ons

Dit is geen stijladvies. Het is de enige grens die er op deze twee velden bestaat, en hij is niet
in code af te dwingen — dus hij staat hier.

- **`msg` wordt door de klant gelezen.** Het staat onverkort in de logtabel van het agentdetail,
  voor de klant net zo goed als voor de operator. Zet er dus **geen** interne details in: geen
  bestandspaden, geen klasse- of methodenamen, geen resource groups, geen endpoints of API-paden,
  geen sleutels of tokennamen, geen namen of id's van **andere** klanten. Schrijf één zin over wat
  er met het werk van *deze* klant is gebeurd.
- **`extra` is operator-only.** De klantweergave van een logregel draagt het veld niet — niet leeg,
  niet verborgen: het staat niet op het type. Dít is dus de plek voor de context die je bij een
  storing nodig hebt: de stacktrace, het endpoint dat 503 gaf, de payload die werd afgekeurd, het
  model dat traag was, de scope waarop het token is vernieuwd.

Dat `extra` operator-only is, is een besluit van het portaal en niet van deze bibliotheek. Het is
geen vrijbrief: schrijf er nog steeds geen persoonsgegevens in die er niet horen (logs zijn 30
dagen leesbaar), en geen gegevens van een andere klant dan die van de agent — een operator die naar
klant A kijkt, hoort daar geen naam van klant B te zien.

Waarom deze kant en niet aan de leeskant filteren: `extra` is vrije JSON en de sleutelnamen komen
van jou. Een blokkeerlijst op namen sluit niets af — vandaag `endpoint`, morgen `svcEndpoint`. De
enige plek waar de betekenis van een sleutel bekend is, is hier, bij het schrijven.

#### `msg` is één regel, en dat wordt afgedwongen

Dit is geen verzoek meer. `Soratus.Agents.Telemetry` **knipt `msg` af op de eerste
regelovergang** — `\n`, `\r\n` of een losse `\r`. Wat erachter stond verhuist naar de
gereserveerde sleutel `msgOverflow` in `extra`, en is daarmee operator-only. Achter de
overgebleven regel komt de markering `" … (ingekort)"`, zodat een lezer weet dat er meer was en
niet denkt dat de agent halverwege stopte.

De aanleiding was gemeten, niet bedacht. Bij een verificatie over negentien agents stond dit in
`msg` van `bakker-voorraad-sync / payload.dump`, en dus zichtbaar voor een klant:

```
   at SoratusAgent.Sync.Validators.StockLineValidator.Validate(…) in /src/Sync/Validators/StockLineValidator.cs:line …
   … zestien regels stacktrace met /src/-paden, klassenamen en regelnummers
```

**Waarom de regelovergang en niet een lengtegrens.** Een lengtegrens was het eerste voorstel en is
gemeten onbruikbaar. Over de 93 klantzichtbare logregels:

| | |
|---|---|
| regels met méér dan één regel in `msg` | 1 |
| regels met verdachte inhoud in de volledige `msg` | 1 |
| regels met verdachte inhoud in **alleen de eerste regel** | **0** |
| langste eerste regel | **1417 tekens**, legitiem Nederlands proza |

Elke grens tussen 200 en 500 verminkt dus een geldig bericht middenin, en elke grens boven 1417
laat de stacktrace er deels doorheen. Dat middengebied is het gevaarlijkst, want het lijkt de
veilige ruime keuze. De knip op de regelovergang haalt alle zestien stacktrace-regels weg en laat
de andere 92 regels ongemoeid: nul valse positieven, één ware positief.

De toets is **mechanisch**. Er wordt nooit gekeken of iets "op een stacktrace lijkt" — alleen of er
een regelafbreking staat. Een heuristiek op inhoud is precies wat morgen faalt. Dat de knip toch
werkt komt doordat hij samenvalt met de contractregel die er al staat: één zin, en een zin bevat
geen regelafbreking.

Er zit ook een hygiënegrens van 8000 tekens op, tegen één absurd lange ononderbroken regel. Die
staat ruim boven de gemeten 1417 en gaat in de praktijk nooit af. Slaat hij toch toe, dan wordt er
op een grafeemgrens geknipt — nooit midden in een surrogaatpaar of een samengestelde glyph, want
een afgekapte string die ongeldige UTF-16 oplevert is erger dan een lange.

**De knip zit op twee plekken.** Bij het schrijven, in de telemetriebibliotheek; en bij het
projecteren naar de klant in het portaal. Die tweede is nodig voor wat de eerste niet kan dekken:
de dertig dagen documenten die er al staan, een agent op een oudere bibliotheekversie, en een agent
die de bibliotheek helemaal niet gebruikt. Beide roepen dezelfde functie aan,
`MessageTruncation.Cut` in `Soratus.Agents.Contracts` — één definitie van "één zin", zodat de twee
kanten niet kunnen divergeren.

**Gereserveerde sleutels in `extra`.** Deze namen zijn van het contract; zet je ze zelf, dan
worden ze overschreven.

| Sleutel | Wat erin staat |
|---|---|
| `msgOverflow` | Alles ná de eerste regelovergang van `msg`, onveranderd, inclusief de oorspronkelijke regelafbrekingen. |
| `_exception` | Type, boodschap en stacktrace van een uitzondering. |
| `_category`, `_template`, `_scopes`, `_eventId` | Wat de `ILogger`-keten meelevert. |

Het portaal moet `msgOverflow` renderen. Doet het dat niet, dan is de stacktrace stil wég in plaats
van verplaatst — en dan hebben we bij een gefaalde run juist de informatie weggegooid die de
operator nodig heeft.

## Statusafleiding

**Status wordt nooit gepubliceerd.** Er is geen veld `status`, in geen van de drie documenten, en
er komt er ook geen. Een agent die om is kan niet melden dat hij om is; een zelfgemelde status is
precies onbetrouwbaar op het moment dat het ertoe doet.

Het portaal en de storingsmelder leiden status af met `AgentStatusCalculator.Calculate`. De eerste
regel die past, wint:

| # | Voorwaarde | Status | Rang |
|---|---|---|---|
| 1 | geen registratiedocument | `Unknown` | 0 |
| 2 | laatste afgeronde run heeft `result: failed` | `Failed` | 4 |
| 3 | `lifecycle` is `idleWaiting` of `stoppedCleanly` én de hartslag is vers | `Idle` | 1 |
| 4 | `now - lastHeartbeatAt` > 2 min | `Degraded` | 3 |
| 5 | overige | `Live` | 2 |

De volgorde loopt van ernstig naar mild, zodat de ernstigste waarheid wint. Een mislukte run bij
een stokkende hartslag levert `Failed` (rang 4) en niet `Degraded` (rang 3): er is aantoonbaar iets
misgegaan, en dat is een hardere mededeling dan "hij meldt zich niet". Omgekeerd kan een agent
zich met `idleWaiting` niet uit een mislukte run praten.

"Vers" in regel 3 is dezelfde grens als in regel 4: twee minuten. Een wachtende agent schrijft
namelijk wél hartslagen — die komen van de bibliotheek, niet van de werklus. Zwijgt hij toch, dan
is er iets met het proces zelf, en juist dan moet zijn eigen laatste mededeling ("ik wacht even")
hem niet langer groen houden dan verdiend. Gevolg, expliciet en gewenst: een agent die
`stoppedCleanly` meldde en daarna zweeg staat na twee minuten op `Degraded`. Hij ís immers weg.

De drempels staan op één plek, in `AgentStatusThresholds`:

| Drempel | Waarde | Waarvoor |
|---|---|---|
| `HeartbeatInterval` | 30 s | Hoe vaak de bibliotheek een hartslag schrijft. Ruim onder de degraded-drempel, zodat één gemiste schrijfactie nog geen storing is. |
| `Degraded` | 2 min | Vanaf deze stilte staat de agent op `Degraded`. |
| `Alert` | 10 min | Vanaf deze stilte mailt de storingsmelder over een degraded agent. |

Bij `Failed` mailt de melder direct: een mislukte run is een afgerond feit en wordt niet beter door
tien minuten te wachten. Bij `Unknown` mailt hij niet — we weten niets van die agent, dat is een
uitrolvraag en geen storing.

Scherm en storingsmelder gebruiken hiervoor letterlijk dezelfde functie,
`AgentStatusCalculator.ShouldAlert`. Dat is een harde eis: lopen ze uiteen, dan mailt de melder
over iets dat het scherm niet toont, of andersom.

`AgentStatusCalculator.SilenceFor` geeft de lengte van de stilte terug, zodat de melding kan
meeschalen van "meldt zich 3 minuten niet" naar "meldt zich 4 uur niet, vermoedelijk gestopt".
De status verandert daarmee niet — dat blijft `Degraded` — alleen de zin die de lezer krijgt.

Voor het overzichtsscherm zit dezelfde rekenregel voor het klantniveau in `Contracts`:
`AgentSeverity.From` reduceert één agent tot status plus laatste activiteit,
`CustomerSeverity.FromAgents` vat de agents van een klant samen tot de ernstigste status en het
jongste activiteitsmoment, en `CustomerSeverity.SeverityFirst` sorteert op ernst en dan op
recentheid. Een klant zonder agents komt op `Unknown` (rang 0) uit en staat dus onderaan; idle
tilt een klant nooit naar boven.

## Partitiesleutels

| Document | Partitiesleutel |
|---|---|
| Registratie | `{agentName}` |
| Run | `{agentName}\|{yyyy-MM-dd}` |
| Logregel | `{agentName}\|{yyyy-MM-dd}` |

De registratie is één document per agent dat telkens wordt overschreven. Het groeit niet, dus de
agentnaam is genoeg, en "geef mij de registratie van deze agent" is dan een puntlezing in één
partitie.

Runs en logregels groeien onbeperkt. Partitioneren op alleen de agentnaam laat één partitie
eindeloos groeien tot hij tegen de limiet loopt; partitioneren op de runId of het log-id spreidt
mooi, maar maakt "alle runs van deze agent vandaag" — precies de vraag die het scherm stelt — tot
een query over alle partities. De combinatie van naam en dag begrenst de partitie én houdt de vraag
van het scherm binnen één partitie. Logregels gebruiken dezelfde vorm als runs, zodat het
agentdetailscherm voor beide tabs dezelfde sleutel opbouwt.

De dag is de UTC-dag. Niet de lokale dag: die verschuift twee keer per jaar, en dan verspringt de
partitiegrens met hem mee.

## Retentie

| Document | Bewaartermijn |
|---|---|
| Logregels | 30 dagen |
| Runs | 400 dagen |
| Registratie | zolang de agent bestaat |

Dat zijn bewust twee getallen. Logregels zijn er om een storing te onderzoeken; die vraag stel je
binnen dagen, en daarna is het volume alleen nog kosten. Runs zijn er om een vraag over de
uitvoering te beantwoorden: "wat is er in mei gebeurd", "hoeveel facturen heeft die agent vorig
kwartaal verwerkt", "klopt deze factuurregel". Die vraag komt maanden later, soms bij een
jaarafsluiting, en de 400 dagen dekken een volledig jaar plus de tijd om erover te praten. Eén
getal voor beide zou dus of de logs te lang bewaren, of de runs te kort — en één van die twee
kost geld terwijl de ander een vraag onbeantwoordbaar maakt.

Een run bevat daarom ook alles wat je later nog nodig hebt zonder de logs: aantallen, duur,
foutsoort en -boodschap, en de versie die draaide.

## Wat de bouwer zelf doet

Vier dingen kan de bibliotheek niet voor je raden, en die kunnen niet automatisch:

- **Event-namen** (`event`). Alleen jij weet wat er gebeurde. Automatisch zou het de methodenaam
  worden, en die verandert bij een refactor — dan breekt elk filter en elke zoekopdracht die op de
  oude naam stond. Kies puntgescheiden namen van grof naar fijn (`document.processed`,
  `document.rejected`, `api.retry`) en houd ze stabiel; ze zijn onderdeel van het contract met de
  operator.
- **Aantal verwerkte items** (`itemsProcessed`, `itemsFailed`). Wat een item is, verschilt per
  agent: een factuur, een regel in een bestand, een bericht op een queue. Een getal dat de
  bibliotheek verzint — verwerkte berichten, iteraties van de lus — staat op het scherm naast het
  woord "verwerkt" en is dan gewoon onwaar.
- **Terugdraaien melden** (`rolledBack`). Het foutscherm vertelt de klant dat er geen halve stand
  is weggeschreven. Dat is een bewering over jouw transactiegrens, die de bibliotheek niet kan
  zien. Die bewering moet waar zijn, dus hij wordt gemeld en niet geraden.
- **Bewust wachten melden** (`lifecycle: idleWaiting`). Een leeg wachtinterval ziet er van buiten
  precies hetzelfde uit als een vastgelopen lus: in beide gevallen gebeurt er niets. Meld het als
  je bewust wacht, en zet het weer op `running` als je aan het werk gaat. Doe je dat niet, dan komt
  je agent er als `Live` op te staan; dat is niet fout, alleen minder informatief.

Verder: schrijf `msg` in het Nederlands en in één zin, gericht op iemand die de code niet kent, en
houd je aan de scheiding tussen `msg` en `extra` hierboven — `msg` leest de klant, `extra` alleen de
operator. Zet in geen van beide persoonsgegevens die niet in het portaal thuishoren; logs zijn 30
dagen leesbaar.

Dat "één zin" is geen advies meer: staat er een tweede regel in `msg`, dan knipt de bibliotheek hem
eraf en zet hem in `extra`. Je verliest niets, maar je bericht leest wel beter als je die knip niet
nodig hebt. Verwijs in `msg` ook niet naar `extra` ("zie stacktrace"): de klant kan daar niet bij,
en een verwijzing naar iets onzichtbaars is geen mededeling maar een raadsel.

## Wat automatisch gaat

Dit hoef je niet te schrijven; `Soratus.Agents.Telemetry` doet het:

- het registratiedocument aanmaken en bijwerken, inclusief `agentName`, `customerId`,
  `displayType`, `environment` en `contractVersion` uit de configuratie
- `version` uit de informational assembly version, `startedAt` uit de processtart
- de hartslag, elke 30 seconden, uit een eigen achtergrondlus die niet meelift op jouw werklus
- `lifecycle: running` bij het starten en `stoppedCleanly` bij een netjes afgesloten proces
- `nextRunAt` berekenen uit de cron-expressie waarop daadwerkelijk gepland wordt
- een run wegschrijven bij het starten (`result: running`) en bijwerken bij het afronden, met
  `finishedAt`, `durationMs` en `result`
- bij een uitzondering: `result: failed`, `errorType`, `errorMessage`, plus een logregel op
  `error` met de stacktrace in `extra`
- de runId meevoeren naar elke logregel binnen de run — je geeft hem nergens door
- id's en partitiesleutels opbouwen, ULID's genereren, tijdstempels in UTC zetten
- de structured-logging-state van `ILogger` in `extra` zetten — let op dat daar ook host- en
  frameworkvelden in kunnen belanden (gemeten: `ContentRoot`, `EnvName`, `_category`,
  `_template`); dat is een tweede reden dat `extra` operator-only is
- `msg` afknippen op de eerste regelovergang en de rest naar `msgOverflow` verhuizen, op elk
  schrijfpad: `AgentEvent`, een gewone `logger.LogInformation(...)`, en de foutboodschap van een
  uitzondering bij een mislukte run
- `debug` en `trace` uit dit contract houden en naar Application Insights sturen, en
  framework-categorieën (`Microsoft.*`, `System.*`) pas vanaf `warn` doorlaten

## Hoe voldoe ik hieraan

Neem `Soratus.Agents.Telemetry` op en registreer hem bij het opstarten; dan staat het
registratiedocument er en loopt de hartslag. Wikkel je werk per run in de scope die de bibliotheek
aanbiedt — daarbinnen worden de start- en eindschrijving, de foutafhandeling en het meevoeren van
de runId voor je gedaan. Zet zelf de vier dingen uit
[Wat de bouwer zelf doet](#wat-de-bouwer-zelf-doet): event-namen op je logregels, de aantallen op
de run, `rolledBack` als je hebt teruggedraaid, en `idleWaiting` als je bewust wacht. Log via
`ILogger`; de bibliotheek zorgt dat `info`, `warn` en `error` in dit contract terechtkomen.

`Soratus.Agents.Contracts` zelf heeft opzettelijk geen enkele NuGet-afhankelijkheid. Het project
wordt door de agents én door het portaal gebruikt, en een pakket dat aan beide kanten meekomt is
een pakket dat je aan beide kanten tegelijk moet bijwerken.

De precieze aanroepen, configuratiesleutels en voorbeelden staan bij
`Soratus.Agents.Telemetry`. Wijkt de bibliotheek af van dit document, dan is dit document leidend
en is de bibliotheek stuk.
