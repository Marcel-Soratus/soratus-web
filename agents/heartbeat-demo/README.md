# heartbeat-demo

De referentie-agent van `Soratus.Agents.Telemetry`. Hij doet niets nuttigs: elke minuut pakt hij
een verzonnen batch facturen op, verwerkt er een paar, keurt er soms een af, en ongeveer één op
de tien runs valt om op een echte uitzondering met een teruggedraaide transactie.

Zijn nut is tweeledig. Hij bewijst dat de bibliotheek werkt, en hij geeft het portaal vanaf dag
één een echte bron in plaats van nagebootste data.

De hele aansluiting op het contract is één regel in `Program.cs`:

```csharp
builder.AddSoratusAgent<HeartbeatDemoAgent>();
```

Alles wat het portaal nodig heeft — registratie, hartslag, levenscyclus, runs, logregels, de
volgende geplande run — komt daaruit. `HeartbeatDemoAgent` bevat alleen het werk zelf.

## Lokaal draaien

```bash
dotnet run --project agents/heartbeat-demo
```

Het profiel in `Properties/launchSettings.json` zet alle omgevingsvariabelen al goed, dus dit
werkt zonder verdere instellingen. Je hebt wel een Azure-login nodig, want de verbinding met
Cosmos loopt via `DefaultAzureCredential`:

```bash
az login
```

Zonder rechten op het Cosmos-account draait de agent gewoon door — hij meldt één keer dat de
telemetrie niet weggeschreven kan worden en gaat verder. Dat is opzet: een storing bij ons mag
het werk van een klant niet stilleggen.

## Omgevingsvariabelen

| Variabele | Verplicht | Voorbeeld | Wat het doet |
|---|---|---|---|
| `SORATUS_CUSTOMER__ID` | ja | `soratus` | De klant, als slug. Zonder deze werpt `AddSoratusAgent()` bij het opstarten. |
| `SORATUS_TELEMETRY__ENDPOINT` | ja | `https://cosmos-soratus-prod.documents.azure.com:443/` | Alleen de URL. Een connection string met sleutel wordt geweigerd. |
| `SORATUS_AGENT__NAME` | nee | `heartbeat-demo` | Valt terug op de assemblynaam in kleine letters, en die is hier al `heartbeat-demo`. |
| `SORATUS_AGENT__SCHEDULE` | nee | `* * * * *` | Cron, vijf velden of zes (met seconden). Hieruit volgt ook `nextRunAt` in het portaal. |
| `SORATUS_AGENT__TIMEZONE` | nee | `Europe/Amsterdam` | De tijdzone waarin de cron-expressie wordt uitgelegd. Standaard UTC. |
| `SORATUS_AGENT__DISPLAY_TYPE` | nee | `Referentie` | De typekolom in het portaal. Standaard afgeleid van de agentnaam. |
| `SORATUS_AGENT__TRIGGER_DETAIL` | nee | `Elke minuut` | Toelichting op de trigger, alleen presentatie. |
| `SORATUS_AGENT__ENVIRONMENT` | in Azure ja | `prod` · `acc` · `dev` | Standaard afgeleid uit `DOTNET_ENVIRONMENT`. De klantweergave toont alleen `prod`. |
| `DOTNET_ENVIRONMENT` | nee | `Development` | Bepaalt de standaardomgeving hierboven. |

Het gedrag van de demo zelf staat in `appsettings.json` onder `HeartbeatDemo` en is
herhaalbaar — er zit geen `Random` zonder seed in:

| Sleutel | Standaard | Wat het doet |
|---|---|---|
| `Seed` | `20260819` | Basis van de toevalsgenerator. Samen met het minuutnummer van de run bepaalt dit precies welke runs falen. |
| `FailureRate` | `10` | Eén op de zoveel runs mislukt. `0` zet falen uit. |
| `LongLineRate` | `7` | Eén op de zoveel runs bevat een héél lange logregel. |

Wil je alle bijzondere gevallen meteen zien, zet dan `HeartbeatDemo__FailureRate=1` en
`HeartbeatDemo__LongLineRate=1`.

Let op bij uitrollen: draait de agent in Azure zonder expliciete `SORATUS_AGENT__ENVIRONMENT`
en met een `DOTNET_ENVIRONMENT` die niet `Production` of `Staging` is, dan valt hij bij het
opstarten om met een duidelijke melding. Dat is opzet — stilletjes op `dev` blijven staan zou
de agent uit de klantweergave laten verdwijnen zonder dat iemand iets ziet.

Alle tijdstempels gaan als UTC de opslag in, in de vaste vorm
`yyyy-MM-ddTHH:mm:ss.fffffffZ`. `SORATUS_AGENT__TIMEZONE` bepaalt alleen wanneer de cron loopt,
niet wat er gepubliceerd wordt.

## Wat je in het portaal moet zien

**In het overzicht** een agent `heartbeat-demo` bij de klant `soratus`, type `Referentie`,
versie `1.0.0-demo`, met een hartslag die niet ouder wordt dan een halve minuut. De status
wisselt vanzelf tussen `live` en `failed`, want ongeveer elke tiende run valt om.

**Bij volgende run** een tijdstip dat precies op het volgende hele minuut valt en meeloopt zodra
een run afgerond is. Dat is geen losse beschrijving: het is hetzelfde moment waarop de planner
zelf gaat draaien.

**Op het tabblad Runs** elke minuut een regel. `ok` met een handvol verwerkte items, af en toe
`failed` met `rolledBack` op waar, en ongeveer één op de acht runs `skipped`, omdat de demo dan
een lege batch aantreft. Een lopende run staat op `running` tot hij klaar is.

Bij een lege batch meldt de agent zelf `lifecycle: idleWaiting` — het enige stuk levenscyclus
dat de bibliotheek niet kan afleiden. Het portaal mag daar `idle` van maken, en dat is geen
storing.

**Op het tabblad Logs** alle drie de niveaus, allemaal met dezelfde `runId`:

- `info` — `batch.started`, `document.processed`, `batch.empty`
- `warn` — `api.retry`
- `error` — `document.rejected` en, bij een omgevallen run, `run.failed`

De `error`-regels zijn uit te klappen naar de volledige JSON, met de stacktrace onder
`_exception.stackTrace`. Bij `document.processed` zit onder `extra` het veld `docId` — dat komt
letterlijk uit `logger.AgentEvent("document.processed", …, new { docId })`.

Ongeveer elke zevende run staat er een `payload.dump`-regel van een paar duizend tekens tussen.
Die is er om te controleren dat de logtabel netjes afbreekt in plaats van uit te lopen.

**Wat je níet moet zien** zijn `debug`- en `trace`-regels. Die worden door de bibliotheek
volledig weggefilterd; ze horen in Application Insights en niet in een scherm dat een operator
's ochtends opent om te zien of er iets stuk is.

## Afsluiten

Bij een nette afsluiting (`Ctrl+C`, of een uitrol) schrijft de bibliotheek nog één
registratiedocument met `lifecycle: stoppedCleanly` en draait de logbuffer leeg, met een korte
timeout. Crasht het proces, dan komt dat document er niet en blijft de laatste hartslag staan —
dan is de agent in het portaal terecht `degraded` en niet "netjes gestopt".
