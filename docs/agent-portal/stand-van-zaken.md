# Stand van zaken — 19 augustus 2026, einde dag

Werkdocument om morgen mee verder te gaan. Vervangbaar; dit is geen ontwerpdocument.

## Waar het staat

De solution bouwt in zijn geheel. Niets is gecommit — alle werk staat als lokale wijziging.

**Af en geverifieerd**

- `Soratus.Agents.Contracts` — de drie documenttypen en `AgentStatusCalculator`. 28 gedragscontroles gedraaid.
- `Soratus.Agents.Telemetry` — de bibliotheek. `builder.AddSoratusAgent()` is alles wat een agentbouwer schrijft.
- `agents/heartbeat-demo` — echt gedraaid tegen de echte Cosmos. Registratie, runs en logregels stonden erin.
- Azure: `app-soratus-portal-prod`, `id-soratus-portal`, `kv-soratus-prod`, `cosmos-soratus-prod`, DNS en certificaat. **portal.soratus.com antwoordt met 200 over TLS.**
- Entra: registratie `soratus-portal`, rollen `Operator` en `Klant`, `appRoleAssignmentRequired` aan.
- `tools/Soratus.Seed` — 19 agents, 105 runs, 126 logregels in Cosmos, in dezelfde vorm als de bibliotheek schrijft. Met `--keep-fresh` voor demo's en `--clean` om op te ruimen.
- Beide pipelines, met `paths`-filters zodat de site en het portaal elkaar niet meer deployen.
- De inlogknop op soratus.com. Site-tests: 32 groen.

**Half af — hier ging het licht uit**

| Onderwerp | Stand |
|---|---|
| `Soratus.Portal.Tests` | Testbestanden staan er, laatste toevoeging was de landingsroute. **Niet gedraaid, dus onbekend of ze slagen.** |
| Ernst per klant over productie | Besluit genomen, datalaag was ermee bezig. Niet af. |
| Sparkline-data | Agent meldde vlak voor het stoppen: klopt tegen de ruwe runs, kosten vallen mee. Vastleggen ontbreekt. |
| Contextuele klantnavigatie | Besluit genomen, opdracht uitgezet, **nog niets gebouwd**. |

## Wat morgen als eerste moet

1. **`dotnet test Soratus.Portal.Tests` draaien.** Alles hangt hieraan: de deploy-pijplijn draait niet zonder groene tests.
2. De drie half afgemaakte punten hierboven afronden.
3. Pas daarna committen en uitrollen.

## Wat Marcel zelf moet doen

- **De Operator-rol aan zichzelf toekennen**, anders komt hij het portaal niet in. Commando en JSON staan klaar in `infra/entra/`. Zonder dit is het portaal voor iedereen dicht, ook voor de eigenaar — dat is bedoeld gedrag van `appRoleAssignmentRequired`.

## Openstaande punten voor later

- **Vorm van de subnav.** §8 geeft er geen. Voorlopig besluit: gewone tekstlinks, actief in `--ink` gewicht 600, inactief `--ink-dim`, geen vulling en geen onderlijn — zodat hij niet lijkt op de tabs uit §8, die het agentdetail al gebruikt.
- **Zes menu-items leveren 404** tot hun schermen bestaan (fase 2 en verder). Bewust laten staan.
- `heartbeat-demo` wordt **niet** uitgerold. Hij heeft bewezen wat hij moest bewijzen; permanent draaien zou een vierde app op een plan zetten dat al krap zit.
- Twee §9-besluiten staan nog open: de Azure-uitsplitsing per dienst (fase 4) en de audittrail op urencorrecties (fase 3). Voor dat laatste ligt er een voorstel in `fase-0-afwijkingen.md`.
- **De sparkline steekt 8px in de kolomgoot.** Twaalf blokken van 5px met 2px ertussen is 82px, in een spoor van 74px. Bewust zo gelaten: de mockup doet exact hetzelfde met dezelfde goot van 10px, en er clipt niets. Wil je de kolom ooit zelfsluitend maken, dan is 82px het getal — maar dat verschuift elke volgende kolom, dus het is een beeldwijziging en geen bugfix.
- **Een staging slot voor het portaal.** Nu geldt: faalt de smoke test, dan is de deploy al gebeurd en staat er een stukke app die je met de hand moet terugrollen. Met een slot draait de test vóór de swap en bereikt een mislukte deploy nooit een gebruiker. `asp-soratus-prod` is P0v3, dus slots zijn beschikbaar. Kost één extra slot-instantie op hetzelfde plan.
- **`ShouldAlert` ontdubbelt niet, en dat is bewust.** Het is een zuivere vraag — "hoort hier een melding over" — en niet "hebben we die al gestuurd". Voor `Failed` levert dat elke aanroep `true`. De storingsmelder (fase 6) draait elke minuut, dus zonder ontdubbeling daar mailt hij zestig keer per uur over dezelfde mislukte run. Bouw die ontdubbeling in de melder, niet in de rekenregel: die moet puur blijven, want het scherm gebruikt hem ook.

## Twee fouten die alleen tegen echte data zichtbaar waren

Het noteren waard, omdat ze het patroon laten zien: lokaal groen betekent hier niets.

1. **Tijdstempels.** System.Text.Json schrijft een `DateTimeOffset` als `+00:00`, de opslag bevat `Z`. Cosmos vergelijkt die strings letterlijk, dus de cursor van de live tail matchte nooit — hij zou zijn nieuwste regel bij elke poll opnieuw getoond hebben.
2. **Koude start.** Het opzetten van de Cosmos-verbinding kostte bijna 8 seconden en viel binnen de time-out per klant. De eerste bezoeker na een herstart kreeg "7 klanten, 0 agents, alles onbereikbaar".
