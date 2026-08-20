# Soratus.Seed

> **Tijdelijk.** Dit project bestaat alleen voor fase 0 en verdwijnt in fase 1. Zodra de echte
> agents bij echte klanten draaien, stoppen we met seeden, draaien we `--clean` en gooien we
> `tools/Soratus.Seed` en `tools/seed` weg. Er hoeft dan niets in het portaal te worden aangepast.

## Waarom dit bestaat

Het portaal krijgt **geen blijvende mocklaag**. Geen `SeedAgentTelemetryStore`, geen in-memory
implementatie naast de echte. Zo'n tweede bron is niet gratis: hij moet meegroeien met het
contract, hij loopt vroeg of laat uit de pas met de werkelijkheid, en dan bewijst een scherm dat
erop werkt niets meer over het echte geval.

In plaats daarvan zet dit gereedschap demodata **in de echte Cosmos, in exact dezelfde
documentvorm die `Soratus.Agents.Telemetry` zou schrijven**. Het portaal weet daardoor niet dat het
naar demodata kijkt en kán dat ook niet weten: het leest zijn normale bron met zijn normale query's.

Twee dingen volgen daaruit, en die zijn allebei bedoeld:

- De leescode van het portaal wordt nooit aangeraakt. Er is geen schakelaar, geen `if (seed)`, geen
  tweede registratie in de DI-container.
- In fase 1 valt er niets te vervangen. Er is geen migratie; er is alleen een `--clean` en een
  `git rm`.

Daarom schrijft dit gereedschap ook geen zelfgemaakte JSON. Het bouwt `AgentRegistration`,
`RunRecord` en `LogRecord` uit `Soratus.Agents.Contracts` en laat de Cosmos-SDK die serialiseren
met dezelfde opties als de telemetriebibliotheek. Loopt het contract, dan loopt dit mee — of het
compileert niet meer, en dat is precies de bedoeling.

**"Dezelfde documentvorm" is gemeten, niet aangenomen.** De veldverzamelingen van de geseede
documenten zijn in alle drie de containers naast die van `heartbeat-demo` gelegd — de agent die zijn
telemetrie wél via de bibliotheek schrijft:

| Container | Bibliotheek | Seed | Alleen bij één van de twee | Tijdvorm |
|---|---|---|---|---|
| `agents` | 1 doc | 19 docs | geen | 28 tekens, sluit op `Z` |
| `runs` | 10 docs | 105 docs | geen | 28 tekens, sluit op `Z` |
| `logs` | 98 docs | 121 docs | geen | 28 tekens, sluit op `Z` |

Geen enkel veld dat de één heeft en de ander niet, en dezelfde vaste tijdvorm aan beide kanten. De
enige verschillen die de vergelijking oplevert zijn velden die het contract als nullable bedoelt en
waar de ene kant toevallig een `null` heeft en de andere niet — `nextRunAt` bij een agent zonder
schema, `finishedAt` en `durationMs` bij de lopende run. Dat is het contract dat wordt uitgeoefend,
geen vormverschil.

Herhaal die meting als het contract of de serialisatie aan één van beide kanten verbouwd wordt. De
assertie bij het opstarten dekt dit **niet**: die toetst deze kopie tegen een vastgelegde vorm en
zou groen blijven als de bibliotheek verschuift. Zie de opmerking bij `SeedJson`.

## Draaien

Inloggen met een account dat `Cosmos DB Built-in Data Contributor` heeft op
`cosmos-soratus-prod`. Local auth staat uit op dat account: er zijn geen sleutels en er kan er ook
geen in de configuratie komen. Authenticatie loopt uitsluitend via `DefaultAzureCredential`.

```bash
az login

# Kijken wat er zou gebeuren. Schrijft niets, verwijdert niets.
dotnet run --project tools/Soratus.Seed -- --dry-run

# Echt wegschrijven. Twee keer draaien mag: de eindtoestand is dezelfde.
dotnet run --project tools/Soratus.Seed

# Tellen wat er nu in de database staat.
dotnet run --project tools/Soratus.Seed -- --verify

# Alles weer opruimen wat dit gereedschap heeft gezet.
dotnet run --project tools/Soratus.Seed -- --clean
```

| Optie | Betekenis |
|---|---|
| `--dry-run` | Toon wat er zou gebeuren. Combineert met `--clean`. |
| `--clean` | Ruim de geseede documenten op. |
| `--verify` | Tel alleen wat er staat. |
| `--keep-fresh` | Seed en blijf daarna de hartslag verversen. Simulatie; zie hieronder. |
| `--interval <s>` | Rondetijd van `--keep-fresh`. Standaard 30 s. |
| `--endpoint <url>` | Cosmos-endpoint. Standaard `cosmos-soratus-prod`. |
| `--database <naam>` | Database. Standaard `telemetry`. |
| `--file <pad>` | Pad naar het bronbestand. Standaard `tools/seed/telemetry.json`. |

Endpoint, database en bestandspad kunnen ook uit `appsettings.json` naast het programma of uit de
omgevingsvariabelen `SORATUS_SEED_ENDPOINT`, `SORATUS_SEED_DATABASE` en `SORATUS_SEED_FILE` komen.
Een argument wint van een omgevingsvariabele, die wint van het bestand, dat wint van de standaard.

## De bron: `tools/seed/telemetry.json`

Zeven klanten met hun agents, runs en logregels, gewonnen uit het `DATA`-object van de mockup.
Twee dingen om te weten als je het bestand aanpast:

**Tijden zijn relatief.** Er staat `-11m` in plaats van een tijdstip. Dat wordt bij het seeden
omgerekend naar `nu − 11 minuten` en als UTC weggeschreven. Zou er een vast tijdstip staan, dan
meldde het portaal morgen dat elke agent al een dag zwijgt en stond alles op `degraded`. De vorm is
`[+|-]{getal}{eenheid}`, met `d`, `h`, `m` en `s`, bijvoorbeeld `-8m7s` of `+12d20h`. Het teken is
verplicht.

**Agentnamen zijn accountbreed uniek.** In de container `agents` is `agentName` tegelijk de
documentsleutel en de partitiesleutel, en het portaal leest een agent met een point read op die
naam. Klantagents dragen daarom de klant-slug als voorvoegsel (`vandijk-mail-triage`); de vijf
beheeragents van Soratus houden hun naam uit §4 van de spec.

**`msg` is één regel.** Het contract wil in `msg` één Nederlandse zin, leesbaar voor wie de code
niet kent — en een zin bevat geen regelovergang. De seeder handhaaft dat bij het wegschrijven: hij
knipt op de eerste regelovergang (`\n`, `\r\n` of een losse `\r`), zet ` … (ingekort)` achter wat
overblijft en schuift de rest naar `extra.msgOverflow`. Een *afsluitende* regelovergang is geen
overloop, dus die levert geen markering op.

**Die regel staat niet in dit project.** Hij staat in `MessageTruncation.Cut` in
`Soratus.Agents.Contracts` en wordt hier alleen aangeroepen; `ExtraOverflow.cs` doet niets anders
dan de overloop onder de gereserveerde sleutel opbergen. Dat is geen netheid maar ervaring: deze
seeder had de regel korte tijd zelf staan — nagebouwd op een met de telemetriebibliotheek
afgestemde vorm, met dezelfde constanten en dezelfde newline-regel — en toch week hij af. Bij een
dubbele knip plakte de kopie twee helften met een `\n` aan elkaar terwijl het contract één
aaneengesloten slice neemt, dus stond er in het origineel al een regelovergang op die plek, dan
kwam er één te veel. Drie schrijvers met dezelfde regel (bibliotheek, portaal, seeder) blijven niet
gelijk. Er staan hier daarom ook geen eigen constanten voor de markering, de sleutel of de
lengtegrens: twee namen met dezelfde waarde is hoe ze gaan verschillen.

Dat is nodig omdat dit gereedschap niet door die bibliotheek heen gaat. Zonder de knip zou de seed
het enige document in de database zijn met een .NET-stacktrace en onze `/src/`-paden in een veld
dat een klant wél ziet — `extra` is operator-only, `msg` niet. Bronpaden, endpoints en scopes
horen dus in `extra`. Bij elke run meldt de uitvoer hoeveel berichten er zijn geknipt en hoe lang
het langste bericht is; blijft er na de knip toch een regelovergang staan, dan stopt hij en schrijft
niets weg.

**`errorMessage` op een run is óók één regel, en daar weigert de seeder.** Dat veld is net zo
klantzichtbaar als `msg` — het portaal draagt het op de runrij en er is op runs géén
operator/klant-splitsing zoals op logregels — maar een `RunRecord` heeft geen `extra` om een
overloop in te bewaren. Knippen zou de rest dus weggooien in plaats van verplaatsen. Staat er een
meerregelige `errorMessage` in het bestand, dan stopt de seeder met de naam van de run erbij en
schrijft niets weg; kort de melding in tot één zin en zet de techniek in de `extra` van de
bijbehorende `run.failed`-logregel. Dat is bewust een ander besluit dan in de bibliotheek, die daar
zacht moet landen omdat een agent in productie niet mag omvallen over de vorm van een foutmelding.

**Er zijn twee fixtures voor de logtabel, en ze hebben elk een eigen rol.** Bij
`bakker-voorraad-sync`:

| Gebeurtenis | Wat het bewijst |
|---|---|
| `validation.summary` | Lang (~1600 tekens) maar op **één** regel, met een lange ononderbroken reeks artikelnummers zonder spaties, en zonder bronpaden. Moet de knip **ongeschonden overleven** en moet in de tabel netjes afbreken in plaats van de kolom breder te maken dan het venster. |
| `payload.dump` | **Meerregelig**, met stacktrace en bronpaden na de eerste regelovergang. Moet **geknipt** worden: in `msg` blijft één zin met de markering, de rest staat in `extra.msgOverflow`. |

Eén fixture kan die twee rollen niet spelen: zodra de knip bestaat, bewijst een meerregelige regel
niets meer over lange teksten die intact moeten blijven.

**Er zit één lopende run in.** `meijer-contractcheck` heeft een run met `result: running`, zonder
`durationMs` en dus zonder `finishedAt`. Dat is een echt geval dat het scherm moet aankunnen: de
kolommen duur en resultaat horen dan leeg te blijven en niet op "0 ms" te staan. `itemsProcessed`
is 0, want de bibliotheek schrijft dit document bij het starten weg en werkt het pas bij het
afronden bij. Voor de status telt hij niet mee — die kijkt alleen naar de laatste *afgeronde* run —
dus hij staat bewust bij een live agent en niet bij een degraded of failed.

Er wordt geen `ttl` in de documenten gezet. Retentie is een eigenschap van de container — 30 dagen
op `logs`, 400 op `runs` — en hoort bij de inrichting van de omgeving, niet bij een document.

## Hoe `--clean` seed van echt onderscheidt

**Op de agentnaam, en op niets anders.**

Het zou makkelijker zijn om elk seed-document een veldje `seed: true` mee te geven en daarop te
filteren. Dat is bewust niet gedaan: zo'n veld voert het onderscheid dat we juist willen vermijden
weer in, en daarmee een mocklaag door de achterdeur.

De regel is dus: de agentnamen in `telemetry.json` zijn de namen van agents die niet bestaan. Er
draait geen proces dat onder die naam telemetrie schrijft. Alles in de drie containers met zo'n
naam is dus door dit gereedschap gezet en mag weg. Documenten van een agent die *niet* in het
bestand staat worden nooit aangeraakt — ook niet als hij bij dezelfde klant hoort.

Daar bovenop ligt een tweede, onafhankelijke grendel: **`heartbeat-demo` wordt nooit aangeraakt.**
Die naam staat in `SeedPlanner.ProtectedAgents`. Hij mag niet in `telemetry.json` voorkomen — dan
weigert het gereedschap te starten — en hij wordt bij elke verwijderactie nog eens apart
overgeslagen. Dat document is de bewijsregel dat het portaal op echte telemetrie werkt; het
overschrijven ervan zou precies dat bewijs weggooien.

De keerzijde, expliciet: hernoem je een agent in het bestand, dan valt de oude naam buiten het
bereik en blijven zijn documenten staan. Draai dan eerst `--clean` met het oude bestand.

## De data veroudert — draai hem opnieuw voor een demo

Status is een afgeleide van de hartslag: `AgentStatusCalculator` zet een agent op `degraded` zodra
hij langer dan twee minuten zwijgt. Een echte agent klopt elke 30 seconden door; een seed-document
staat stil. **Ongeveer twee minuten na het seeden staan dus alle geseede agents op `degraded`**, en
daarmee verdwijnt het verschil tussen live, degraded en idle waar de demodata het juist om te doen
is.

Dat is geen fout, dat is het systeem dat werkt: een agent die niet draait ís stil, en het portaal
hoort dat te tonen. Zou seed-data eeuwig "live" blijven, dan zat precies de leugen in de data die
deze hele opzet moet voorkomen. Het is dus ook niet opgelost door een hartslag in de toekomst te
zetten — dan toont het scherm "laatste activiteit over twee uur".

Voor demo's en voor het bouwen van de schermen is stilstaande data wel onwerkbaar. Daarvoor is er
`--keep-fresh`:

```bash
dotnet run --project tools/Soratus.Seed -- --keep-fresh
dotnet run --project tools/Soratus.Seed -- --keep-fresh --interval 20
```

Die seedt eerst normaal en blijft daarna draaien. Elke 30 seconden (instelbaar, en het moet ruim
onder de degraded-drempel van 120 seconden blijven — anders weigert hij) schrijft hij **alleen
`lastHeartbeatAt` van de registraties** opnieuw weg. Runs en logregels blijven staan en worden dus
gewoon ouder; dat hoort zo, want een run die tien minuten geleden liep is tien minuten geleden
gelopen.

Elke agent houdt daarbij zijn eigen afstand tot nu, zoals die in `telemetry.json` staat. Wie daar
`-12s` heeft blijft twaalf seconden geleden gezien en dus **live**; wie `-8m7s` heeft blijft acht
minuten stil en dus **degraded**; **failed** komt uit de laatste afgeronde run en verandert
sowieso niet. De statusmatrix blijft dus staan zoals hij bedoeld is in plaats van na twee minuten
in één kleur te vallen.

> **Dit is simulatie en het meldt dat ook, elke ronde, in de uitvoer.** Er draait geen enkele echte
> agent; er wordt alleen een hartslag nagebootst. Het is een demohulpstuk voor fase 0. Zodra er in
> fase 1 echte agents per klant draaien is het overbodig en verdwijnt het samen met de rest van dit
> project. Laat het nooit ergens ongemerkt doorlopen — na Ctrl+C veroudert de data weer, en dat is
> de eerlijke toestand.

## Idempotent

Een gewone run schrijft eerst alles uit het bestand weg en verwijdert daarna wat er van een
eerdere seed nog over is en er nu niet meer bij hoort. Twee keer draaien geeft daarom dezelfde
eindtoestand en nooit dubbele documenten. Registraties en runs hebben een vaste sleutel en worden
gewoon overschreven; logregels krijgen een nieuwe ULID omdat hun tijdstip meeschuift, en de vorige
lichting wordt in dezelfde run opgeruimd.
