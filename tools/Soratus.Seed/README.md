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
