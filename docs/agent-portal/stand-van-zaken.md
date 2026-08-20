# Stand van zaken — 20 augustus 2026

Werkdocument om verder mee te gaan. Vervangbaar; dit is geen ontwerpdocument.

## Fase 0 is af en werkt

Van eind tot eind bewezen op productie: aanmelden via Entra ID, rol herkend, echte
telemetrie uit Cosmos, klanten gesorteerd op ernst.

| | |
|---|---|
| soratus.com | live, met de inlogknop naar het portaal |
| portal.soratus.com | live achter Entra; `/healthz` 200 |
| Tests | 218 portaal, 32 site |
| Commits | tien, op `main` |

Wat er staat: het agentcontract, `Soratus.Agents.Telemetry`, de referentie-agent
`heartbeat-demo`, het portaal met overzicht en klantweergave, het seed-gereedschap, de
Bicep-templates voor het portaal én voor een klantomgeving, en twee gescheiden pipelines.

## Wat nog open staat

- **Het portaal heeft zelf geen diagnostic settings en geen Application Insights.** Precies
  het gebrek dat we in `fase-0-afwijkingen.md` §1 aan MBV verwijten, in onze eigen omgeving:
  App Service, Cosmos en Key Vault leveren alle drie een lege lijst, en er is geen Log
  Analytics workspace in `rg-soratus-prod`. Dit is met losse `az`-commando's opgezet en
  daarbij vergeten. De klant-blauwdruk (`infra/klant/`) heeft ze wél. Doe dit via
  `infra/portal/` met een `what-if` ernaast, niet met losse commando's.
- **`keyVaultReferenceIdentity` staat in Azure op `SystemAssigned`** terwijl de app alleen een
  user-assigned identity heeft. De template corrigeert dit al, dus een `what-if` op
  `infra/portal/` hoort één Modify te melden. Zolang er geen Key Vault-referentie in de
  app-settings staat merkt niemand het; de eerste wél valt stil met een foutmelding die niets
  over de oorzaak zegt.
- **De zeven klanten in `appsettings.json` zijn verzonnen**, inclusief de subscription-ids.
  Dat is de demodata uit de mockup en het punt van fase 0. Eruit zodra er echte omgevingen
  staan.
- **`UseHttpsRedirection` waarschuwt bij elke start.** In de nieuwe workspace staat
  `warn: HttpsRedirectionMiddleware — Failed to determine the https port for redirect`. Achter
  de App Service-proxy kan de middleware de poort niet vaststellen. Onschadelijk in de
  praktijk — `httpsOnly` staat aan op de App Service en de site is alleen via TLS bereikbaar —
  maar het is ruis bij elke start, en ruis is precies wat later een échte waarschuwing
  onzichtbaar maakt. Oplossen door de poort expliciet te zetten of de middleware achter de
  proxy over te slaan.
- **`deploy-portal` wacht niet op `ci-agents`, en dat is een bewuste keuze.** De stap
  `Test contractregels` in `deploy-portal.yml` draait `Soratus.Agents.Telemetry.Tests` mee, dus
  een kapotte `MessageTruncation.Cut` blokkeert de uitrol. Verdwijnt die stap ooit, dan valt de
  dekking niet helemaal weg — `ci-agents` staat op dezelfde paths en wordt op dezelfde push
  rood — maar dan is de uitrol al gebeurd. Koppelen met `workflow_run` zou dat dichten; niet
  gedaan omdat die trigger draait tegen de workflowdefinitie van de standaardbranch en lastig
  te debuggen is. Dezelfde vorm als het ontbrekende staging slot hieronder: de melding komt
  wel, maar erna. Een test die de YAML grep't is hier géén oplossing: die bewaakt het verkeerde
  (het is een ordeningsprobleem tussen twee workflows) en is stil te omzeilen, want wie de stap
  weghaalt haalt in dezelfde beweging die test weg.
- **De bibliotheek weert framework-logs nog niet.** Besloten, niet gebouwd:
  `Microsoft.Hosting.Lifetime` schrijft `Content root path: D:\SORATUS\Website\…` naar `msg`, op
  info-niveau en dus klantzichtbaar, bij elke agent die met een gewone host start. De knip helpt
  daar niet — het is één regel. Filter op categorie (`Microsoft.`, `System.`) en alleen op info;
  warn en error blijven, want een framework-waarschuwing is precies wat een operator wil zien.
  "Application started" verdwijnt daarmee uit het portaal, en dat kost niets: dat feit staat al
  in het registratiedocument als `startedAt` en `lifecycle`.
- **Welke agentcode `payload.dump` schrijft.** De knip dekt het symptoom aan twee kanten; een
  agent die een externe respons in een logbericht dumpt is de oorzaak.
- **Een staging slot voor het portaal.** Nu geldt: faalt de smoke test, dan is de deploy al
  gebeurd en staat er een stukke app die je met de hand moet terugrollen. Met een slot draait
  de test vóór de swap. `asp-soratus-prod` is P0v3, dus slots kunnen.
- **`ShouldAlert` ontdubbelt niet, en dat is bewust.** Het is een zuivere vraag — "hoort hier
  een melding over" — en niet "hebben we die al gestuurd". Voor `Failed` levert dat elke
  aanroep `true`. De storingsmelder (fase 6) draait elke minuut, dus zonder ontdubbeling
  daar mailt hij zestig keer per uur over dezelfde mislukte run. Bouw die ontdubbeling in de
  melder, niet in de rekenregel: het scherm gebruikt hem ook.
- **De sparkline steekt 8px in de kolomgoot.** Twaalf blokken van 5px met 2px ertussen is
  82px, in een spoor van 74px. Bewust zo gelaten: de mockup doet exact hetzelfde en er clipt
  niets. Wil je de kolom zelfsluitend maken, dan is 82px het getal — maar dat verschuift elke
  volgende kolom, dus het is een beeldwijziging en geen bugfix.
- **`Sparkline` dwingt zelf niet af dat er twaalf blokken zijn.** Dat doet `PortalViews`. Een
  toekomstige aanroeper die minder blokken meegeeft wordt door niets tegengehouden.
- **Twintig losse "laatste afgeronde run"-queries in het overzicht**, één per agent. Bewuste
  keuze voor correctheid: een gezamenlijke tijdvensterquery mist de agent wiens laatste run
  buiten het venster viel, en juist die zou dan ten onrechte op live staan. Bij 20 agents is
  het 208 ms; richting 200 agents wordt het ~1300 RU per paginaweergave en is dit de eerste
  plek om naar te kijken.
- Twee §9-besluiten uit de spec staan nog open: de Azure-uitsplitsing per dienst (fase 4) en
  de audittrail op urencorrecties (fase 3). Voor dat laatste ligt er een voorstel in
  `fase-0-afwijkingen.md`.

## Eén valkuil om te onthouden

De rolclaim uit Entra komt **gemapt** binnen, als
`http://schemas.microsoft.com/ws/2008/06/identity/claims/role` en niet als `roles`. Dat
mappen is niet uit te zetten via `OpenIdConnectOptions.MapInboundClaims`, want
Microsoft.Identity.Web zet zijn eigen tokenhandler en die instelling heeft dan geen effect.

Stond `RoleClaimType` op `"roles"`, dan gaf `IsInRole` altijd `false` en stond elk rolbeleid
**stil dicht** — je komt binnen, maar zonder rol. Dat kostte twee deploys, waarvan de eerste
op een aanname berustte in plaats van een meting. De gemeten claimnamen staan als commentaar
in `Program.cs`.

Les voor volgende keer: bij een autorisatieprobleem eerst meten welke claims er werkelijk
aankomen, dan pas een oorzaak kiezen.

## Fase 1

Observability: het agentdetail met tabs Logs, Runs en Configuratie, filterchips met tellingen,
zoeken, uitklapbare JSON met een stacktrace die netjes afbreekt, en live tail.

Acceptatie uit de spec: een operator ziet binnen twee seconden of ergens iets mis is, en kan
de foutregel van een gefaalde run vinden.

Aandachtspunt dat nu al vastligt: de live tail moet zichzelf stoppen na 15 minuten en pauzeren
als het tabblad op de achtergrond staat, anders blijft een tabblad dat een uur openstaat
pollen. En de logtabel wordt niet gevirtualiseerd — `Virtualize` gaat uit van rijen met een
vaste hoogte, en uitklapbare JSON heeft dat niet.
