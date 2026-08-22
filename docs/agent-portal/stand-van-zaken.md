# Stand van zaken — 22 augustus 2026

Werkdocument om verder mee te gaan. Vervangbaar; dit is geen ontwerpdocument.

## Waar het werk stond aan het eind van 22 augustus

Eenentwintig commits. **1926 tests groen over vijf projecten**, nul waarschuwingen op een volledige
rebuild vanuit de wortel. Fase 0 t/m 4a staan uitgerold; fase 5 en 6 staan in de repo en zijn **niet
uitgerold**.

**Wat er vandaag bij is gekomen**, in drie commits die elk alleen staan (elk in een aparte worktree
gebouwd en getest):

- **De sprintweergave** (§3.4). Read-only uit Azure DevOps, met de collector achter de naad en het
  scherm dat alleen Cosmos leest. Zes toestanden waar zes handelingen bij horen, en de maand komt uit
  de **datums** van een iteratie en nooit uit een naam.
- **De supportdraad** met de AI-eerstelijn als naad die is gedeclareerd en niet geregistreerd. Vier
  sloten waarvan er geen één een instructie aan een model is; het scherpste is dat het antwoordtype
  nergens een tekstveld heeft.
- **Het platform meldt zichzelf** (fase 6). De kostencollector en de storingsmelder bestaan nu als
  agent in ons eigen overzicht, langs hetzelfde contract als de agents van een klant.
- En één commit die geen functionaliteit is: **één meetlaag voor de mutatierondes**.

### Vijf dingen die op een mens wachten

De vier van gisteren staan er nog, en er is één bij gekomen:

- **`id-soratus-portal` lid maken van de DevOps-organisatie `soratus`** — als service principal, met
  toegangsniveau Basic, en in de projectgroep **Readers** van `MBVApp4 MAUI`. Gemeten en niet
  vermoed: de identiteit zit in dezelfde tenant als de organisatie, maar een identiteitszoekopdracht
  geeft "No identities found" terwijl dezelfde zoekopdracht op een mens hem wél vindt. Zonder deze
  stap levert élke DevOps-aanroep een geweigerd verzoek op. Dat is een wijziging op
  organisatieniveau en valt buiten de twee resource groups waar wij mogen schrijven; het blok staat
  in punt 45.
- **Uitrollen.** Fase 5 en 6 staan in de repo en niet in Azure. Code en infra kunnen in willekeurige
  volgorde: de schakelaar voor de platformtelemetrie is de app-setting met de endpoint, en zolang die
  ontbreekt publiceert het portaal niets en blijft de interne klant naar de bestaande database
  kijken. Er is dus geen tussenstand.

### Wat de dag heeft geleerd

1. **Een mutatieronde is een aangekondigd venster.** Dit is de vierde manier waarop een meting hier
   kan liegen, en de gevaarlijkste van de vier: de andere drie laten een spoor in de uitvoer achter
   en deze niet. Een boom waarin een andere sessie een mutatie heeft staan, is van buiten niet van
   een boom met een defect te onderscheiden. Twee sessies hebben vandaag dezelfde drie rode tests aan
   elkaar toegewezen als bevinding; het waren er geen. Wat het verraadt is de **vorm** van het
   antwoord — een handvol tests die alle dezelfde ene regel dekken, en verder niets rood.
2. **Een assertie op de aanwezigheid van een teken in markup zegt alleen iets als dat teken daar
   uniek is.** Er stond een test op een kolom die groen bleef terwijl die kolom leeg raakte, omdat
   twee ándere kolommen hetzelfde streepje hebben. Twee streepjes dekten elkaars afwezigheid. Dit is
   niet te vinden door naar de test te kijken — hij was groen en hij las goed. Alleen een mutatie
   vindt hem, en niemand muteert een test die al groen is. De suite is hier **niet** op doorzocht.
3. **Een tweede laag die per constructie onbereikbaar is, is geen dode code.** De URL-escaping in de
   sprintlane is met geen enkele test te raken, omdat de validatie de tekens die iets veranderen al
   verbiedt. Hij blijft staan als vangnet voor de dag dat iemand die lijst versoepelt — en dát die
   lijst gemeten is, is de helft die het rechtvaardigt. Wie alleen "geen test" leest, haalt hem weg.
4. **Drie kopieën van hetzelfde meetgereedschap hadden drie verschillende fouten.** Niemand had ze
   gevonden door het script te lezen; ze kwamen boven doordat het instrument valse uitkomsten gaf.

## Waar het werk stond aan het eind van 21 augustus

Zeventien commits, alles uitgerold. **1558 tests groen over vijf projecten**, nul waarschuwingen op
een volledige rebuild. Fase 0 t/m 4a staan; fase 5 en 6 zijn begonnen.

**Wat er vandaag bij is gekomen:** het urenendpoint waarop de MCP-server post, de Azure-scope als
eigen veld met de kostencollector erachter, het maandoverzicht per mail, de verzendlaag met de
storingsmelder als tweede aanroeper, de integratie waarmee een bestaande webapplicatie zich als
agent-host meldt, de telemetrie-opslag voor MBV, en de maandsprints op het DevOps-board van MBV.

**Waar je morgen mee begint.** Er staat werk in de boom dat niet is gecommit: de sessie die het
portaal zich als agent-host laat melden (het eerste stuk van fase 6). De build was schoon toen we
stopten. Lees haar rapport voordat je iets aanraakt — er staat één open ontwerpvraag in: of een
agent die op een klok draait binnen een webhost met de bestaande vorm is uit te drukken, of dat het
agentcontract iets mist. Die sessie heeft `Soratus.Agents.Telemetry` op zeven plekken aangeraakt en
`appsettings.json` gewijzigd; dat laatste bestand is vanuit de hoofdsessie niet leesbaar, dus dat
komt uit haar beschrijving.

Daarna: de sprintweergave op de maandsprints, en de supportpagina met de eerstelijnsagent. Die
laatste is nog niet ontworpen, en de moeilijke eis staat vast — hij mag niets verzinnen en moet
escaleren als hij het niet zeker weet.

### Vier dingen die op een mens wachten

- **Een adres voor de storingsmelding** (`PortalAlerts__Recipients__0`, als parameter in
  `infra/portal/`). Zonder dat kijkt de melder wel maar mailt hij niet.
- **De custom role op `acs-soratus-prod`** — het `az`-blok staat in punt 29.10, met de grens die
  erin genoemd staat: `az role definition create` is een schrijfactie op abonnementsniveau en valt
  buiten de twee resource groups waar wij mogen schrijven.
- **Hoe `Soratus.Agents.Telemetry` in een klantcodebase komt.** Dit blokkeert de hartslag bij MBV, en
  dus of MBV agents in het portaal krijgt. Vier uitwegen staan verderop; de makkelijkste — broncode
  meekopiëren — is wat punt 13 verbiedt.
- **De SnelStart-vraag** over de scope `orders:*`. Bepaalt of fase 4b bestaat.

### Wat de dag heeft geleerd, en het is drie keer dezelfde les

1. **`/healthz` bewijst niet dat het portaal staat.** Die controle raakt met opzet geen enkele
   afhankelijkheid, dus hij kan een kapotte configuratie of DI niet zien. Vandaag gaf hij 200 terwijl
   het portaal op het punt stond om te vallen, en de uitrolpijplijn deed dezelfde meting en meldde
   succes. Er staat nu een tweede smoke test op `/` die een 302 verwacht. Wil je weten of het
   portaal werkelijk staat: vraag `/` op.
2. **Wat vóór het einde van `StartAsync` moet zijn gebeurd, hoort in `StartAsync`.** Drie keer
   voorgekomen in de telemetriebibliotheek. Het lijf van `ExecuteAsync` van een `BackgroundService`
   is niet gegarandeerd gelopen als `StartAsync` terugkomt — gemeten op .NET 10, twee keer
   onafhankelijk. Eén keer verloor een kortlevende agent daardoor al zijn telemetrie, en één keer
   meldde hij zich helemaal niet.
3. **Meet de invariant en niet zijn gevolg**, als het gevolg van de planner afhangt. Een test op het
   gevolg bleef hier zes runs groen terwijl de fout er was. En kijk *welke* tests rood worden, niet
   hoeveel: ik heb bijna een correcte diagnose teruggedraaid omdat ik een aantal las in plaats van
   een lijst.

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

## MBV — de eerste echte klant

Aangemaakt via `/klanten/nieuw` door een mens, en dat is meteen het bewijs van de acceptatie
van fase 2: de container ging van 8 naar 11 documenten, dus klant, contract en toegangen zijn
in één transactie weggeschreven. De tijdstempel die dat opleverde staat in de canonieke vorm
met een afsluitende `Z` — de reparatie van punt 25, bewezen door een echte handeling in
productie in plaats van door een test.

**Wat MBV werkelijk is, gemeten en niet aangenomen.** Geen verzameling achtergrondagents maar
een webapplicatie: drie App Services op één Premium-plan met Always On aan, twee Cosmos-accounts
met hun applicatiegegevens, een Key Vault, een AI Foundry-project en een CIAM-directory. In de
codebase (`D:\soratus\mbv`, .NET 10) staat **geen enkele achtergronddienst**. De drie agents zijn
endpoints: `/api/declaraties/agent`, `/api/jaarverslag/chat` en `/api/jaarverslag/snapshot`.
`MBV.SftpCheck` is géén vierde agent — dat is een ontwikkelaarshulpmiddel dat je met de hand
start om een SFTP-verbinding te controleren.

**Telemetrie-opslag staat en is nagemeten:** `cosmos-mbv-prod`, database `telemetry`, drie
containers met de bewaartermijnen van de blauwdruk, local auth uit, en beide dataplane-rollen
op de database in plaats van het account. Het klantdocument wijst erheen.

### Wat MBV nog nodig heeft van een mens

- **Een distributiekanaal voor `Soratus.Agents.Telemetry`.** Dit is de blokkade voor de hartslag
  en hij is nieuw: onze bibliotheken zijn geen NuGet-pakket, en de `nuget.config` van MBV wist
  bewust alle overgeërfde feeds en laat alleen nuget.org toe. Vier uitwegen — publiek op
  nuget.org, een privéfeed op Azure Artifacts, GitHub Packages met een token in hun pijplijn, of
  de broncode meekopiëren. Dat laatste is wat punt 13 verbiedt: zo liep de knipregel drie keer
  uit elkaar. Het raakt hun bouwpijplijn, dus het is geen technische keuze alleen.
- **Toestemming om in hun repo te werken en om die app opnieuw uit te rollen.** Code schrijven is
  één ding; een productie-app van een klant deployen is een ander. De integratie wordt daarom
  eerst in ónze repo gebouwd, zodat de wijziging bij MBV een paar regels plus een pakketreferentie
  is.
- **Twee bevindingen in hun omgeving die niets met ons werk te maken hebben**, gemeld omdat ze in
  de omgeving staan die we nu aansluiten: op `mbv-dbaccount` én `mbv-dbaccount2` staat **local
  auth aan**, dus er kunnen accountsleutels bestaan naast hun applicatiegegevens. En twee accounts
  met elk één container `mbv` ziet uit als een overblijfsel.
- **Always On moet aan blijven.** Gaat hij uit, dan laadt Azure de app na twintig minuten stilte
  uit, stopt de hartslag, en meldt het portaal alarm terwijl er niets aan de hand is. Een
  instelling buiten de code die de betekenis van de code omdraait.

## DevOps: maandsprints voor MBV, en één regel die vastligt

Board: organisatie `soratus`, project **MBVApp4 MAUI**, team **MBVApp4 MAUI Team**. DevOps is
leidend en het portaal schrijft nooit terug — dat staat al in §3.4 en het blijft zo.

Er stonden drie generieke iteraties (`Iteration 1` t/m `3`) **zonder datums**. Dat was stil
kapot: de teaminstelling staat op `@currentIteration`, en die wordt door datums bepaald, dus er
was helemaal geen huidige sprint. Er zijn nu vijf maandsprints aangemaakt en aan het team
toegewezen — `2026-08 Augustus` t/m `2026-12 December`, met de kalendermaand als periode — en
augustus is daarmee de huidige. De drie oude iteraties en hun werkitems zijn niet aangeraakt;
die items verplaatsen is een beslissing van een mens.

**Het portaal leidt de maand af uit de datums van een iteratie en nooit uit de naam.** De naam is
voor mensen; `2026-08 Augustus` hernoemen naar `Augustus` mag de facturatiemaand niet verschuiven.
Dit is dezelfde klasse fout als de resourcegroep die in een weergavetekst stond: een tikfout daar
levert bij Cost Management een geslaagd leeg antwoord op, en dat wordt € 0,00 op een factuur.

En let op: **DevOps laat de tijd van een iteratiedatum vallen.** Er is `31 augustus 23:59:59`
verstuurd en `31 augustus 00:00:00` opgeslagen. Het zijn dus datums en geen momenten, en het
portaal hoort ze zo te behandelen.

## De verzendlaag voor mail moet geëxtraheerd worden

Nog geen nieuwe functionaliteit maar een extractie, en het moment is nu. Er zijn twee plekken die
zelf een `EmailClient` bouwen en `SendAsync` aanroepen — `Soratus.Web/Services/LeadSink.cs` voor
terugbelverzoeken en `Soratus.Portal/Mail/StatementMailSender.cs` voor het maandoverzicht — en de
storingsmelder van fase 6 wordt de derde. Drie kopieën van één handeling is precies wat de
knipregel ons heeft gekost (punt 13).

De asymmetrie maakt het scherper: de site authenticeert met een **connection string** (het geheim
dat in platte tekst als app-setting staat) en het portaal met een **managed identity** met een
custom role die alleen Read en Write mag — met opzet niet Contributor, want die geeft `ListKeys`
erbij en is dan machtiger dan het geheim dat je wilde vermijden. Eén verzendlaag is ook hoe dat
één keer wordt opgelost in plaats van twee keer.

Wat in die laag hoort is de verzendsemantiek, niet een wrapper: **drie uitkomsten en geen twee**
(verzonden / niet verzonden / onbekend), `4xx` als niet-verzonden inclusief een 429 omdat
throttling "niet aangenomen" betekent, al het andere als onbekend, géén retry uit onbekend — daar
komt een mens aan te pas, want een mail is niet terug te halen — en een proefdraaimodus die
standaard aan staat. Per doel verschilt alleen de opmaak en de ontvanger.

**Bouw hem met de storingsmelder als tweede aanroeper**, niet los: een gedeelde laag met één
gebruiker bewijst niets. En let bij die melder op de val die al vastligt: `ShouldAlert` ontdubbelt
met opzet niet, dus een melder die elke minuut draait mailt zestig keer per uur over dezelfde
storing. Die ontdubbeling hoort in de melder en niet in de rekenregel, want het scherm gebruikt
die regel ook.

## Vier manieren waarop een meting loog

Alle vier gebeurd, alle vier kostten werk. Ze staan hier omdat ze niets met de code te maken
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
- **Een mutatieronde van een andere sessie is een venster waarin de productiecode met opzet kapot
  is.** Dit is de vierde, hij is er op 22 augustus bijgekomen, en hij is de gevaarlijkste van de vier
  — de andere drie hebben een teken in de uitvoer en deze niet. Van buiten is een boom onder mutatie
  niet van een boom met een defect te onderscheiden. Het is gebeurd: tijdens één mutatie stonden er
  drie tests rood, ze zijn als bevinding aan een lane toegewezen, en er was niets kapot. Wat het
  verraadt is de **vorm** van het antwoord: drie tests die alle drie precies één regel dekken en
  verder niets rood. Nagemeten na een volledige rebuild stonden ze groen en stond de weggehaalde
  regel er weer. Vandaar de coördinatieregel: **een mutatieronde is een aangekondigd venster** —
  melden bij start en bij einde, en wie erin meet gooit de meting weg. Dezelfde val zit in het
  mutatiescript zelf: dat meldde bij eenentwintig mutaties "compileert niet" met een compileerfout
  uit een ánder project, dus een resultaat zonder meting. Een script dat muteert hoort te controleren
  of de compileerfout ín het gemuteerde bestand staat. Zie punt 45 van
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
