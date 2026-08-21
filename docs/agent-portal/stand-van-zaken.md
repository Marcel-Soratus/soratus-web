# Stand van zaken — 20 augustus 2026

Werkdocument om verder mee te gaan. Vervangbaar; dit is geen ontwerpdocument.

## Waar het werk stond aan het eind van 20 augustus

Fase 0 tot en met 3 staan op `main` en zijn uitgerold. 1067 tests groen over vier
projecten (868 portaal, 92 MCP, 75 contractregels, 32 site), nul waarschuwingen op een
volledige rebuild.

**Waar je morgen mee begint, in deze volgorde:**

1. **Het portaalendpoint voor urenboekingen.** De MCP-server is af en getest maar kan
   nergens naartoe schrijven. Wat er moet komen staat verderop onder "Wat de MCP-server
   nog nodig heeft van het portaal" — endpoint, bearer-tokenvalidatie naast de
   browsersessie, categorievalidatie, en een eigen public client in Entra.
2. **`GenerateDocumentationFile` vastzetten.** Zie het besluit verderop. Meet eerst de
   projecten die nog nooit met die vlag zijn gemeten; daarna omzetten. De sequentie is
   het hele punt.
3. **Fase 4a**: kosten, opslag, uren boven bundel en het maandoverzicht mailen. 4b
   (SnelStart) wacht op de aanvraag — zet die in, want de doorlooptijd is weken.

**Twee losse bestanden in de werkboom, bewust niet gecommit.** `fix-date2.py` in de
repo-root is een wegwerpscript van een afgebroken sessie en kan weg. `tools/mutatie.py`
is een mutatietest-hulpmiddel dat waarde heeft — dat vraagt een blik voordat het meegaat,
want het is niet gereviewd.

**De tijdstempelmigratie is uitgevoerd.** Alle acht documenten in `platform/customers`
staan nu in de canonieke vorm; teruggemeten op nul afwijkingen, en de documenten zijn
volledig gecontroleerd (alle velden aanwezig, `changedAt` nog `null` — de migratie heeft
geen wijzigingsspoor verzonnen). Het verbod op `ORDER BY c.createdAt` blijft staan: dat
rust nu niet meer op de tijdvorm maar op de tie-break, want bij een gelijk moment moet de
sleutel de volgorde bepalen en dat kan een `ORDER BY` op één veld niet.

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
- **`actions/upload-artifact@v4` en `download-artifact@v4` staan op Node 20 en dat is afgeschaft.**
  GitHub dwingt ze nu al op Node 24 en meldt dat bij elke uitrol als annotatie. Werkt dus, maar
  het is een aangekondigde afloop en het is ruis bij elke run. Naar v5 in `deploy-portal.yml`.
  Let op dat het artefact daar niet alleen een tussenstap is maar ook het terugrolpakket.
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

## Drie besluiten van fase 3 die vastliggen

- **Urenregels staan in de container `customers`**, met `kind: "hourEntry"` en de klantslug als
  partitiesleutel. Niet in een eigen container: de splitsing van de telemetriecontainers rust op
  verschillende bewaartermijnen, en een urenregel verloopt net zo min als een contract. De bundel
  en de regels staan daarmee in dezelfde partitie.
- **Daarom schrijft de MCP-server `soratus-uren` niet zelf naar Cosmos, maar post hij naar het
  portaal.** Dat is een eis en geen voorkeur. Cosmos-dataplane-rollen zijn per container te
  scopen, dus zolang uren in `customers` staan kan geen tweede identiteit schrijfrecht op uren
  krijgen zónder ook schrijfrecht op de toegangsdocumenten — en wie daar een regel bijschrijft
  verleent zichzelf portaaltoegang. Dat is geen lek maar een rechtenverhoging, en die zou
  zichtbaar zijn als werkende functionaliteit in plaats van als storing. Hetzelfde patroon als
  waarom het portaal geen `AppRoleAssignment.ReadWrite.All` krijgt. Bijkomend voordeel: de regel
  "een boeking via de MCP-server is nooit gefiatteerd" staat op één plek in plaats van in elke
  schrijver. Moet uren ooit tóch naar een eigen container, dan is dat nu goedkoop — er staat nog
  geen enkele urenregel — en het is een Bicep-wijziging plus een rechtenafweging, geen bouwkeuze.
- **`HourBalanceCalculator` blijft in `Soratus.Portal/Data/`** en gaat niet naar
  `Soratus.Agents.Contracts`. Die bibliotheek is het *agentcontract*; een urenregel is het
  tegendeel — Soratus-eigen administratie waar een agent per ontwerp niet bij mag. Hem daar
  zetten betekent het uurtarief en de marge in de agentbibliotheek. De verhuizing komt zodra er
  een **eerste lezer buiten `Soratus.Portal`** is; dan is een platformbibliotheek de juiste plek.
  Hij is puur en dependency-vrij gehouden, dus dat is een bestandsverplaatsing.

## Besloten en ingepland: `GenerateDocumentationFile` structureel aan

Dit portaal draagt zijn redenering in XML-documentatie. Dat is hier de dragende constructie en
geen versiering: waaróm een veld nullable is, waarom een klanttype iets níet heeft, waarom een
regel op één plek staat. Een `<see cref="..."/>` naar iets dat niet meer bestaat is dan geen
schoonheidsfoutje maar een aanwijzing naar een veld dat de volgende lezer gaat zoeken.

`GenerateDocumentationFile` is de enige vlag die dat controleert. Kosten: `NoWarn` voor CS1591
(ontbrekende documentatie op een publiek lid), anders regent het op elk lid en is de melding
onbruikbaar.

Eén sweep met die vlag leverde vier dode verwijzingen en tien half gedocumenteerde methoden op,
negen daarvan in code van één dag oud. Dat is het bewijs dat dit zonder controle verschuift.

**Stand nu, zelf nagemeten over alle vier de projecten: er is er nog precies één over** —
`Components/Pages/Klant/Uren.razor` 337, een `cref="FieldName"` die uit `NieuweKlant.razor`
is meegekomen. In `Uren.razor` heten die helpers `JudgeField`, `BookField` en `CorrectField`;
één cref naar één daarvan is dus misleidend, en de tekst hoort naar alle drie te verwijzen of
naar geen.

**De sequentie is het punt:** eerst die ene opruimen, dán de vlag om. Andersom breekt de
nul-waarschuwingen-eis op het moment dat de vlag aangaat, en dan is de vlag er binnen een dag
weer uit — zo sneuvelen zulke controles, en een tweede poging is daarna moeilijker te verkopen.
Aanzetten als de lanes leeg zijn, want een rode build op andermans werk blokkeert elke sessie
die op dat moment loopt.

**Grep niet op één waarschuwingscode.** Dat is hier twee keer misgegaan en het leverde de derde
telling van hetzelfde probleem op: eerst vier, toen twaalf, uiteindelijk één. Filter op
`warning CS` met een volledige rebuild en selecteer daarna. De vijf die hier tellen zijn CS1574
(dode cref), CS0419 (ambigue cref — een cref naar een methodenaam wordt dubbelzinnig zodra er
een tweede overload bijkomt), CS1573 (parameter zonder tag terwijl andere die wél hebben),
CS1584 en CS1580.

## Wat de MCP-server nog nodig heeft van het portaal

De server `soratus-uren` is gebouwd en beproefd in proefdraaimodus, maar hij kan nog nergens
naartoe schrijven. Wat er aan de portaalkant bij moet:

- **Het endpoint zelf**, met de vijf velden `klant`, `maand`, `uren`, `categorie` en
  `omschrijving` — en zonder `status`, `by`, `source` en de registratiedatum. Die zet het
  portaal, en `by` komt uit het token. Een aanroeper die zijn eigen `by` mag meesturen kan uren
  op naam van iemand anders boeken.
- **Validatie van de categorie achter dat endpoint**, met een afwijzing die de geldige waarden
  noemt. Dat is de enige plek waar die lijst hoort te staan: het voorstel voor een
  `GET /api/uren/metadata` is afgewezen, want een tweede plek die de lijst kent is nog steeds
  een tweede plek. De MCP-server stuurt de string door en de client leert de waarden uit de
  afwijzing.
- **Bearer-tokenvalidatie naast de OIDC-aanmelding.** Het portaal kent nu alleen een
  browsersessie; een aanroep met een token uit `DefaultAzureCredential` moet dezelfde rolgrens
  raken als het scherm.
- **Een eigen public client in Entra met device-code**, en niet de Azure CLI-client vooraf
  autoriseren. Dat laatste zou elk script op de machine van een operator een token voor ons
  portaal kunnen geven; die persoon kan het via de browser ook, maar dan is de macht bereikbaar
  voor code die er niets mee te maken heeft en dat is niet te zien. De commando's komen als blok
  voor Marcel, zoals al het tenantwerk.

De tool heet `uren_boeken` en niet `uren.boeken` zoals §5 voorschrijft: een punt in een
toolnaam laat élke prompt in de sessie falen, niet alleen deze tool. Clientgrens, geen
protocolgrens, en er staat een test op de naam.

## Wat fase 4 nodig heeft van een mens

De haalbaarheid staat in `fase-4-haalbaarheid.md`. Fase 4 is geknipt: **4a** (kosten, opslag, uren
boven bundel, maandoverzicht mailen) kan gebouwd worden, **4b** (SnelStart) is geblokkeerd op
handelingen die niemand namens Marcel kan doen:

- **De SnelStart-koppeling aanvragen.** Online administratie, abonnement inZicht of inControle
  voor de Maatwerk-tegel, €250 eenmalig ex btw, en een certificeringsperiode van ongeveer twaalf
  dagen. Doorlooptijd weken. Zet dit los van elke bouwbeslissing in, want de tijd loopt dan
  parallel aan het werk.
- **Drie vragen aan `partner@snelstart.nl`.** De bepalende: vereist de scope `orders:*` een
  inHandel-abonnement? Dat is de scope van de veilige conceptroute; valt het antwoord verkeerd,
  dan bestaat die route voor ons niet en wordt 4b niet gebouwd. Daarnaast: welke scopes krijgt
  een maatwerksleutel en zijn ze te beperken, en wat zijn de echte rate limits.
- **Twee secrets in `kv-soratus-prod` zetten.** Dit wordt de eerste Key Vault-referentie van het
  portaal in productie. Bewijs dat pad eerst met een onschuldig secret, vóór er een
  boekhoudsleutel in gaat.
- **`Key Vault Reader` op de vaults** voor wie de namen moet kunnen lezen. `Owner` geeft geen
  data-plane recht; een poging tot `readMetadata` geeft Forbidden.
- **Kostenrecht.** `id-soratus-portal` heeft `Cost Management Reader` alleen op de resource group
  `MBV`. Een query op abonnementsniveau werkt maar mag niet, en zou het portaal de kosten van
  álles in dat abonnement geven — ook van klanten die niet van ons zijn. Er is geen tussenvorm.
  Klantomgevingen staan bovendien in meer dan één abonnement, dus "één query per dag" is in
  werkelijkheid één per abonnement.

Twee dingen die de acceptatie van §7 niet halen en die dus een tekstwijziging in de spec vragen:
"verzonden" is niet leesbaar uit SnelStart (`VerkoopfactuurModel` heeft geen statusveld) — noem het
**gefactureerd** — en de betaaldatum bestaat er niet, alleen `openstaandSaldo`. De echte datum kost
`boekhouden:read` op het hele grootboek voor één veld.

## Drie manieren waarop een meting vandaag loog

Alle drie gebeurd, alle drie kostten werk. Ze staan hier omdat ze niets met de code te maken
hebben en dus in geen enkele test te vangen zijn.

- **`dotnet test --no-build` ná het bouwen van één project meet tegen een oude assembly.** Dat
  heeft een correcte wijziging gekost: de reparatie werd teruggedraaid omdat de testrun hem niet
  zag. Bouw de solution, of laat `--no-build` weg.
- **`MSB3061 — the file is locked by: testhost`** komt van een testrun die tegelijk loopt en
  `bin/` vasthoudt. Dat verschijnt als waarschuwingen op een build waar er geen zijn: eerst
  twaalf, dan tien, dan drie, dan nul. Wie dat naast een nul-waarschuwingen-eis ziet gaat code
  repareren die niets mankeert. Meet opnieuw als er niets meer vergrendeld is.
- **Filter niet op één waarschuwingscode.** Dat leverde bij de documentatiecontrole drie keer een
  ander getal voor hetzelfde probleem op: eerst vier, toen twaalf, uiteindelijk één. En een dode
  cref náást een compilatiefout is een **gevolg** en geen oorzaak — de crefresolutie valt om zodra
  het project niet compileert. Wie hem dan "repareert" haalt een goede verwijzing weg. Eerst de
  compilatiefout, dan opnieuw meten, dán een cref aanraken.

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
