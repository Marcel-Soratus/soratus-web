# Infrastructuur

De Azure-infrastructuur staat als Bicep in `infra/`. Alles is met de hand gemaakt en
daarna vastgelegd; de template is nu de bron van waarheid, niet de maker.

Abonnement: `Pay-As-You-Go-SORATUS`, `501a66d2-de54-4d4f-9f7c-1fbb55bec17f`. Zet hem
expliciet mee met `--subscription`; er staan meer abonnementen in de tenant.

## Welke template waarvoor is

| Bestand | Bereik | Waarvoor |
|---|---|---|
| `infra/portal/main.bicep` | abonnement | Het portaal in zijn geheel: `rg-soratus-prod` plus het leesrecht in klant-resource groups |
| `infra/portal/portal-rg.bicep` | resource group | Wat er in `rg-soratus-prod` staat. Ook los te gebruiken voor een `what-if` |
| `infra/portal/main.bicepparam` | — | De productiewaarden |
| `infra/klant/main.bicep` | abonnement | Een klantomgeving: maakt `rg-{k}-{omgeving}` en rolt de inhoud uit |
| `infra/klant/klant-rg.bicep` | resource group | De inhoud van een klantomgeving |
| `infra/klant/mbv.bicepparam` | — | Voorbeeld. **Niet uitgerold** — MBV staat nog in de oude, met de hand gemaakte vorm |
| `infra/modules/portal-leesrecht.bicep` | resource group | `Reader` + `Cost Management Reader` voor het portaal op één resource group |
| `infra/entra/` | — | Entra-objecten. Geen Bicep; zie [Wat met de hand blijft](#wat-per-klant-met-de-hand-blijft) |

## Lezen voor je schrijft

`what-if` laat zien wat een deploy zou doen zonder iets aan te raken. Gebruik hem altijd.

```bash
export MSYS_NO_PATHCONV=1   # Git Bash op Windows, anders verbouwt MSYS de resource-paden

az deployment group what-if \
  -g rg-soratus-prod \
  -f infra/portal/portal-rg.bicep \
  --subscription 501a66d2-de54-4d4f-9f7c-1fbb55bec17f
```

Dit hoort **geen** wijzigingen op te leveren, op twee uitzonderingen na die geen afwijking
zijn maar een beperking van `what-if` zelf:

- **`cosmos-soratus-prod` → `properties.sqlEndpoint` wordt verwijderd.** `sqlEndpoint` is
  alleen-lezen; ARM geeft hem terug maar er is geen schrijfbare tegenhanger, dus `what-if`
  ziet hem als "staat er wel, template heeft hem niet". Een deploy raakt hem niet aan.
- **`app-soratus-portal-prod` → zes `siteConfig`-waarden worden aangemaakt.** De GET op
  `Microsoft.Web/sites` geeft `siteConfig` niet volledig terug (`appSettings` komt er zelfs
  als `*******` uit), dus `what-if` heeft niets om mee te vergelijken en meldt alles als
  nieuw. De waarden in de template zijn nagelopen tegen `az webapp show` en gelijk:
  `ftpsState=Disabled`, `healthCheckPath=/healthz`, `minTlsVersion=1.2`,
  `scmMinTlsVersion=1.2`, `netFrameworkVersion=v4.0`, `localMySqlEnabled=false`.

- **Drie keer `naar-eigen-workspace` → `logAnalyticsDestinationType` wordt verwijderd, en
  `properties.logs` en `properties.metrics` worden als gewijzigde array gemeld.** ARM vult op een
  diagnostic setting standaardwaarden in (`AzureDiagnostics`, een `retentionPolicy` per categorie)
  die de template niet opschrijft, en vergelijkt vervolgens twee arrays waarvan de elementen niet
  in dezelfde vorm staan. De categorieën zelf zijn nagelopen en gelijk.

Alles daarbuiten in de uitvoer is een echte afwijking. Zoek hem uit voor je deployt.

**En: een groene what-if is geen bewijs.** Zie [Een veld dat moet
ontbreken](#een-veld-dat-moet-ontbreken) — dat is geen theorie maar de reden dat een uitrol
halverwege is gefaald op een template waar what-if niets van vond.

## Een nieuwe klant uitrollen

1. Maak een parameterbestand naar het voorbeeld van `infra/klant/mbv.bicepparam`. Zet de
   klantcode lowercase; die zit in elke resourcenaam.
2. Lees de what-if:

```bash
export MSYS_NO_PATHCONV=1

az deployment sub what-if \
  --name klant-<code> --location westeurope \
  --subscription 501a66d2-de54-4d4f-9f7c-1fbb55bec17f \
  --template-file infra/klant/main.bicep \
  --parameters infra/klant/<code>.bicepparam
```

3. Klopt hij, wissel `what-if` voor `create`. Verder verandert er niets aan het commando.
4. Doe daarna de handmatige stappen hieronder. Zonder die stappen draait er niets.

Naamgeving, met `{k}` als lowercase klantcode: `rg-{k}-prod`, `asp-{k}-prod`,
`app-{k}-{agent}-prod`, `cosmos-{k}-prod`, `kv-{k}-prod`, `log-{k}-prod`, `appi-{k}-prod`,
`id-{k}-agents`.

Het portaal moet ná het uitrollen ook leesrecht krijgen. Dat regelt de klanttemplate zelf
(Cosmos Data Reader, `Reader` en `Cost Management Reader`), dus je hoeft er niets voor te
doen — maar zet de nieuwe resource group wél in `customerScopes` in
`infra/portal/main.bicepparam`, zonder GUID's. Dan staat het leesrecht op twee plekken
vastgelegd en blijft het staan als de klanttemplate ooit opnieuw draait.

## De platform-database: wat van Soratus zelf is

Fase 2 vraagt dat een nieuwe klant zonder database-actie kan worden ingericht. Nu komt de
klantenlijst uit `appsettings.json` en is een klant toevoegen dus een deploy. Die lijst gaat naar
Cosmos, en wel naar een **eigen database naast `telemetry`** op hetzelfde account:

| | |
|---|---|
| Account | `cosmos-soratus-prod` (bestaand) |
| Database | `platform` |
| Container | `customers`, partitiesleutelpad `/pk`, pk = klantslug |
| TTL | **geen.** Contractdata mag niet verlopen |

**Waarom niet in `telemetry`.** `telemetry` is de vorm die zich per klant herháált — straks één keer
per klantaccount, zie de remarks op `Soratus.Portal/Data/TelemetryLocation.cs`. Wat daarin staat is
klantscoped en verhuist mee met de klant. Klanten, contracten en toegangsregels zijn van Soratus,
horen bij precies één omgeving en mogen zich niet vermenigvuldigen. Een vierde container in
`telemetry` zou bij de eerste klant met een eigen account zes keer bestaan, waarvan vijf keer leeg.

**Waarom geen eigen account in een `rg-soratus-platform`.** Dat is het einddoel, maar nog niet nu.
Het enige wat een apart account vandaag extra oplevert is dat het portaal geen schrijfrecht op het
telemetrieaccount hoeft te hebben — en dat is al opgelost, want de schrijfrol staat op de
database en niet op het account. Wat het wél kost is een tweede endpoint, een tweede backupbeleid
en een tweede set rolverleningen. Het moment om te verhuizen is wanneer de eerste klant een eigen
Cosmos-account krijgt: dan is `cosmos-soratus-prod` niet langer "het account" maar het account van
de interne klant, en hoort de platformdata daar niet meer bij in.

**Waarom één container en niet drie.** De telemetriecontainers zijn per documenttype gesplitst
omdat ze verschillende bewaartermijnen hebben. Die reden geldt hier niet: klant, contract en
toegangsregel verlopen geen van drieën. In één container met de klantslug als partitiesleutel is
een klant één punt-lees en is klant + contract + toegang wijzigen één transactionele batch. Over
drie containers is dat drie losse schrijfacties, en dan bestaat de toestand "contract bijgewerkt,
toegang niet".

Nagemeten tegen de echte container, en dat is de reden dat het bij die keuze blijft: een klant
aanmaken (klant + contract + twee toegangen in één batch) kost **39 RU**, een contract wijzigen 11,
een point read 1,0, en het hele overzicht bij zeven klanten 6,1. Het account is serverless, dus dat
is wat je betaalt. Belangrijker dan de getallen is wat een gedwongen botsing liet zien: de batch gaf
**424 Failed Dependency** op bewerking 0 en 409 op bewerking 1, en het document van bewerking 0 was
er daarna níet. De batch is dus echt atomair en een halve klant kan niet bestaan — dat is de eigenschap
waar de keuze voor één container op rust, en die is nu bewezen in plaats van aangenomen.

Twee dingen die je in die container zult zien en die geen rommel zijn:

- **Eén document met pk `$portal` en id `bootstrap`.** De markering dat de eenmalige migratie van de
  zeven klanten uit `appsettings.json` heeft gelopen. Een klantslug kan niet met `$` beginnen, dus
  die partitie botst nooit met een klant. Het portaal schrijft hem; de template niet.
- **Er staat geen veld in dat de Entra-kant bijhoudt.** Geen `invitedAt`, geen `entraActive`. Het
  portaal nodigt niet uit, dus zou het dat veld nooit vullen: het zou voor altijd "wacht op
  uitnodiging" blijven zeggen, ook nadat iemand het had gedaan — een onwaarheid met een tijdstempel
  eronder. En een gekopieerde Entra-toestand in Cosmos blijft dat probleem, alleen met een
  verversingsinterval eromheen. De toestand is daarom **afgeleid op het moment van renderen** en
  heeft drie waarden en niet twee: onbekend, actief, ontbrekend. "Onbekend" en "niet uitgenodigd"
  zijn twee verschillende mededelingen, en een `bool` kan er maar één van doen.
- **Toegang intrekken is een harde verwijdering**, geen `revokedAt`-vlag. "Wie mag hierbij" is
  daarmee de *aanwezigheid* van een document: een vergeten filter op een vlag verleent toegang, een
  ontbrekend document kan dat niet. De prijs is dat er geen spoor van een ingetrokken toegang
  achterblijft. Dat is dezelfde audittrail-vraag die §9 van de spec nog open heeft; valt dat besluit,
  dan is een **eigen** container het antwoord (audit heeft een eigen bewaartermijn en hoort niet in
  een container zonder TTL). Nu bewust niet leeg vooruit gebouwd.

Zoeken op e-mailadres — "welke klanten mag dit adres zien" — is een cross-partition query, en geen
tweede document met het adres als partitiesleutel. Twee documenten zijn twee waarheden over wie
toegang heeft, en Cosmos kent geen transactie over partitiesleutels heen; grant zou dan tweefasig
worden en halverwege kunnen stoppen. Daarom staat de indexeringspolicy op alles indexeren.

### De schrijfrol, en waarom hij op de database staat

```bicep
scope: platform.id      // NIET cosmos.id
```

Dat is de hele grens tussen "mag contracten bijwerken" en "mag telemetrie overschrijven", en het is
één regel. Het portaal houdt zijn bestaande **accountbrede `Cosmos DB Built-in Data Reader`** — dat
is waarmee het telemetrie leest — en krijgt er **`Cosmos DB Built-in Data Contributor` op
`dbs/platform`** naast. Cosmos telt data-plane rechten bij elkaar op, dus dat is precies genoeg.

Een ingebouwde rol die alléén schrijft bestaat niet; Data Contributor omvat lezen, en op deze scope
is dat geen probleem. Wat wél een probleem zou zijn is diezelfde rol op accountniveau: dan kan het
portaal `logs`, `runs` en `agents` wijzigen en wissen. Telemetrie is het bewijsmateriaal waarop de
statusweergave rust — schrijfrecht daarop maakt van "de agent heeft niets gepubliceerd" een uitspraak
die het portaal zélf kan hebben veroorzaakt.

Dit is een **data-plane** rolverlening
(`Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments`) en geen
`Microsoft.Authorization/roleAssignments`. Zie [Cosmos-RBAC](#cosmos-rbac-twee-dingen-die-op-elkaar-lijken).

Uitgerold en nagemeten. Zo hoort het eruit te zien:

```bash
az cosmosdb sql role assignment list -g rg-soratus-prod -a cosmos-soratus-prod \
  --query "[].{principal:principalId, rol:roleDefinitionId, scope:scope}"
```

| principal | rol | scope |
|---|---|---|
| `id-soratus-portal` | `…0001` Data Reader | het account |
| `id-soratus-portal` | `…0002` Data Contributor | `…/dbs/platform` |
| Marcel (mens, voor `tools/Soratus.Seed`) | `…0002` Data Contributor | het account |

Die derde regel is een mens en geen infrastructuur — zie [Wat bewust niet in Bicep
staat](#wat-bewust-niet-in-bicep-staat). Komt er een vierde regel met Data Contributor op **het
account** voor een service, dan is dat de afwijking om op te zoeken.

Wil je de verlening los zetten in plaats van de template te draaien:

```bash
export MSYS_NO_PATHCONV=1   # anders verbouwt MSYS de --scope

az cosmosdb sql role assignment create \
  --account-name cosmos-soratus-prod -g rg-soratus-prod \
  --subscription 501a66d2-de54-4d4f-9f7c-1fbb55bec17f \
  --role-definition-id 00000000-0000-0000-0000-000000000002 \
  --principal-id e48ffac5-672c-4e2b-aab9-340871fb2d62 \
  --scope "/dbs/platform"
```

Zolang die verlening er niet is, blijft lezen werken en faalt alleen het schrijfpad — met een 403 en
verder niets. De datalaag geeft daar een eigen tekst bij; verwacht geen foutmelding van Cosmos die
de oorzaak noemt.

## Toegang per e-mailadres, en wat dat aan privilege kost

§3.5 van de spec zegt: een operator geeft toegang per e-mailadres en trekt die in, en dat loopt via
Entra ID. Klantgebruikers worden B2B-gasten in de SORATUS-tenant met een app-roltoewijzing op de
registratie `soratus-portal`; `appRoleAssignmentRequired` staat aan, dus zonder toewijzing komt
niemand binnen.

**Het portaal doet dat niet zelf, en dat is een besluit en geen tekortkoming.** Er bestaat precies
één Graph-permissie waarmee een app een app-rol kan toekennen —
`AppRoleAssignment.ReadWrite.All` — en die is niet tot één applicatie te beperken. Microsofts eigen
waarschuwing erbij zegt dat een app ermee "additional privileges to itself, other applications, or
any user" kan geven, op elke API inclusief Microsoft Graph. Een gecompromitteerd portaal kan
daarmee dus niet alleen de rol `Operator` uitdelen — die alle klanten ziet — maar zichzelf
Graph-rollen toekennen en van daaruit de tenant overnemen. En in deze tenant hangt het abonnement
met álle klantomgevingen aan diezelfde tenant.

Wat het portaal in fase 2 wél doet:

1. **De toegang vastleggen** in `platform/customers`. Daarvoor is geen enkele Graph-permissie nodig
   en dáármee is de acceptatie gehaald: een nieuwe klant inrichten kost geen database-actie. De eis
   zegt "zonder database-actie", niet "zonder menselijke handeling".
2. **Zeggen dat het de Entra-kant niet kan zien.** Vandaag heeft het portaal geen enkele
   Graph-permissie, dus staat de Entra-toestand van elke toegangsregel op `Unknown` en zegt het
   scherm dat ook. Wil je dat het portaal het écht kan controleren, dan is `Application.Read.All`
   genoeg — alleen-lezen, geen waarschuwing van Microsoft, geen escalatieroute.
3. **De twee commando's tonen** die de uitnodiging afmaken, met het e-mailadres er al in.

Dat is dezelfde vorm als de knop "Nieuwe klant" in fase 0: het portaal legt vast en zegt eerlijk wat
het niet doet. Verdedigbaar zolang de knop niet liegt.

### De commando's die met de hand blijven

Per klantgebruiker twee stappen. Zet het abonnement er niet bij — dit is Graph, niet ARM.

```bash
# 1. Nodig de gast uit. Kent nog geen enkel recht toe: de gast krijgt een gastaccount
#    in de tenant en komt zonder stap 2 het portaal niet in.
az rest --method POST \
  --uri "https://graph.microsoft.com/v1.0/invitations" \
  --headers "Content-Type=application/json" \
  --body '{
    "invitedUserEmailAddress": "iemand@klant.nl",
    "invitedUserDisplayName": "Naam Achternaam",
    "invitedUserType": "Guest",
    "inviteRedirectUrl": "https://portal.soratus.com",
    "sendInvitationMessage": true
  }'
# Bewaar de `invitedUser.id` uit het antwoord; dat is de principalId van stap 2.
```

```bash
# 2. Ken de rol `Klant` toe op de service principal van soratus-portal. Dít is de stap
#    die toegang geeft. Wissel appRoleId voor e9290944-a9f0-4390-a69d-fb4ab0e5b7e0 om
#    iemand `Operator` te maken — dat is Soratus-breed inzicht in alle klanten.
az rest --method POST \
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/9008de5c-1b6a-45cb-ad7a-a8f9badf866f/appRoleAssignedTo" \
  --headers "Content-Type=application/json" \
  --body '{
    "principalId": "<invitedUser.id uit stap 1>",
    "resourceId": "9008de5c-1b6a-45cb-ad7a-a8f9badf866f",
    "appRoleId": "c254bf6c-f40b-4d9f-b27b-7ba473dd82dd"
  }'
```

```bash
# Intrekken. Zoek eerst de id van de toewijzing op; die is niet te raden.
az rest --method GET \
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/9008de5c-1b6a-45cb-ad7a-a8f9badf866f/appRoleAssignedTo?\$select=id,principalDisplayName,appRoleId"

az rest --method DELETE \
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/9008de5c-1b6a-45cb-ad7a-a8f9badf866f/appRoleAssignedTo/<id>"
```

Het intrekken van de app-roltoewijzing is genoeg om iemand buiten te houden; het gastaccount
opruimen is een aparte handeling.

**Let bij het lezen op `appRoleId` `00000000-0000-0000-0000-000000000000`.** Dat is geen rol maar
"default access": een toewijzing zónder rol, die Entra aanmaakt als iemand aan de app wordt gekoppeld
zonder een rol te kiezen. Met `appRoleAssignmentRequired` aan is dat genoeg om **binnen** te komen,
maar er komt geen rolclaim mee — dus je bent aangemeld en elk rolbeleid staat stil dicht. Precies de
toestand die in fase 0 twee deploys kostte, nu met een andere oorzaak. Er staat er op dit moment één
op de tenant (naast een echte `Operator`-toewijzing voor dezelfde persoon, dus onschadelijk). Wie
toewijzingen uitleest om te tonen wie toegang heeft, moet deze waarde apart behandelen en niet als
onbekende rol wegfilteren.

Voor de leesrol van punt 2 — die geeft het portaal géén recht om iets te wijzigen:

```bash
# Application.Read.All als application permission op de managed identity id-soratus-portal.
# Alleen-lezen op app-registraties en hun roltoewijzingen. Vereist toestemming van een
# beheerder, en dat is de bedoeling.
az rest --method POST \
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/e48ffac5-672c-4e2b-aab9-340871fb2d62/appRoleAssignments" \
  --headers "Content-Type=application/json" \
  --body '{
    "principalId": "e48ffac5-672c-4e2b-aab9-340871fb2d62",
    "resourceId": "ebf9778f-daff-452f-bb3e-d98ba0505ecc",
    "appRoleId": "9a5d68dd-52b0-4cc2-bd40-abcf44ac3a30"
  }'
```

`ebf9778f-daff-452f-bb3e-d98ba0505ecc` is de service principal van Microsoft Graph in deze tenant.
Let op dat de Graph-permissies aan de **managed identity** hangen en niet aan de registratie
`soratus-portal`: die registratie heeft geen enkele credential en dient alleen om gebruikers aan te
melden. Het portaal praat met Azure als `id-soratus-portal`, en dat is een ander object.

### Wil je het tóch automatisch, dan is dit de rangorde

Niet gebouwd. Vastgelegd zodat het besluit een keer bewust valt en niet per ongeluk.

| Variant | Permissie | Wat een aanvaller ermee kan |
|---|---|---|
| **Vastleggen + mens nodigt uit** (nu) | geen, plus `Application.Read.All` om te controleren | niets in Entra |
| Groep → app-rol | `User.Invite.All` + `GroupMember.ReadWrite.All` | iemand in willekeurige groepen zetten. **Geen** route naar Graph-rollen, dus geen tenantovername. Vereist Entra ID P1 (aanwezig: `AAD_PREMIUM`) en één groep per app-rol, éénmalig door een beheerder aan de rol gekoppeld |
| Access packages | rol *Access package assignment manager* i.p.v. een permissie | begrensd tot één access package. De enige variant waarvoor Microsoft least-privilege app-only gedocumenteerd ondersteunt, maar vereist Entra ID Governance — **niet** in deze tenant |
| Direct `appRoleAssignedTo` | `AppRoleAssignment.ReadWrite.All` | de rol `Operator` uitdelen, en van daaruit de tenant. **Niet doen** |

Twee dingen om te weten als de groepsvariant ooit wordt gekozen: gebruik **nooit** een
role-assignable groep (`isAssignableToRole`), want lidmaatschap daarvan beheren vereist
`RoleManagement.ReadWrite.Directory` en dat trekt de escalatie alsnog binnen. En op dit moment hangt
er in het abonnement geen enkele Azure-rolverlening aan een groep, dus groepslidmaatschap opent nu
niets in Azure — dat is een eigenschap van vandaag en geen garantie van morgen.

Terzijde, uit dezelfde ronde: `allowInvitesFrom` staat tenantbreed op `everyone`, dus iedere
member én iedere gast mag gasten uitnodigen. Dat is de Microsoft-standaard en geen keuze die iemand
hier heeft gemaakt. `adminsAndGuestInviters` is de nette waarde. Het remt een service principal met
`User.Invite.All` níet af — het remt mensen af.

## Wat per klant met de hand blijft

**Entra-objecten kunnen niet in ARM.** App-registraties, app-rollen, service principals,
federated credentials en rol-toewijzingen aan personen leven in Microsoft Graph, niet in
Azure Resource Manager. Bicep kan ze niet aanmaken en `what-if` ziet ze niet. Wat er staat,
staat in `infra/entra/` als JSON met het bijbehorende `az`-commando.

Concreet per klant met de hand:

| Wat | Waarom niet in Bicep |
|---|---|
| Klantgebruikers uitnodigen en de rol `Klant` geven op `soratus-portal` | Graph, geen ARM. Zonder die toewijzing komt de klant het portaal niet in — `appRoleAssignmentRequired` staat aan. Commando's en de reden dat het portaal dit **niet** zelf doet: [Toegang per e-mailadres](#toegang-per-e-mailadres-en-wat-dat-aan-privilege-kost) |
| Secrets in de Key Vault zetten | Een secret in een template is een secret in Git. De vault wordt leeg uitgerold |
| Federated credential voor de deploy-pipeline van de agents | Graph, geen ARM |
| Toestemming van de klant op zijn eigen systemen (Microsoft 365, boekhoudpakket) | Buiten Azure |

## Wat bewust niet in Bicep staat

- **`asp-soratus-prod` en de DNS-zone `soratus.com`.** Beide bestonden al en dragen ook de
  marketingsite en `app-derdehelft`. De template haalt ze aan met `existing` en definieert
  ze niet. Wie ze wél herdefinieert kan soratus.com raken, en die site is live.
- **`app-soratus-prod`, `app-derdehelft`, `aoai-soratus-prod`, `acs-soratus-prod` en de
  bijbehorende certificaten.** Staan in dezelfde resource group maar horen niet bij het
  portaal. `what-if` meldt ze als `Ignore`; dat is goed.
- **Resource groups.** `infra/portal/main.bicep` maakt `rg-soratus-prod` niet aan. Een
  resource group die een template kan aanmaken, kan een template ook opruimen.
  `infra/klant/main.bicep` maakt er wél een, omdat een klantomgeving van niets begint.
- **Secrets, app-registraties en alles wat in Entra leeft.** Zie hierboven.
- **De rol `Cosmos DB Built-in Data Contributor` die op `cosmos-soratus-prod` aan een
  persoon is gegeven.** Dat is een mens die met `tools/Soratus.Seed` werkt, geen
  infrastructuur. Zet je hem in de template, dan staat er straks een naam in Git die er niet
  meer werkt. Zie ook de afwijkingen hieronder.
- **Application Insights op het portaal.** De workspace en de diagnostic settings staan er wel.
  Application Insights niet: dat legt elk verzoek vast inclusief de URL, en daarin staat de
  klant-slug. Dat is een besluit voor Marcel en geen implementatiedetail. Wat het praktische
  probleem oplost — "het portaal valt om, waar kijk ik dan" — zijn `AppServiceAppLogs` en
  `AppServiceConsoleLogs`, en die raken die grens niet.

## Twee dingen die niet goed stonden, en nu wel

Blijven staan als aantekening, want de reden waarom ze fout stonden is leerzamer dan de fix.

**1. Het portaal had geen enkele diagnostic setting en geen Log Analytics workspace** — precies
het gebrek dat `fase-0-afwijkingen.md` §1 aan de klantomgeving MBV verwijt, in onze eigen twee
dagen oude omgeving. Opgelost: `log-soratus-prod` staat er, met `naar-eigen-workspace` op de
App Service, Cosmos en de Key Vault. Dat het kon gebeuren komt doordat de omgeving met losse
`az`-commando's is opgezet; de klant-blauwdruk had ze vanaf de eerste regel wél.

**2. `keyVaultReferenceIdentity` stond op `SystemAssigned`** terwijl de site alleen een
user-assigned identity heeft. Staat nu op de resource-id van `id-soratus-portal`. Het was de
ARM-standaardwaarde en niet iets wat iemand had gezet — het soort afwijking dat niemand merkt tot
de eerste app-setting in de vorm `@Microsoft.KeyVault(...)` stil wegvalt met een fout die niets
over de oorzaak zegt.

## Een veld dat moet ontbreken

De duurste les uit deze templates, en de reden dat een groene `what-if` geen bewijs is.

Op een Cosmos-container betekent `defaultTtl: -1` "TTL aan, geen verval" en een **afwezig**
`defaultTtl` "uit". Een expliciete `null` is géén van beide: die levert bij het uitrollen
`One of the specified inputs is invalid` op. De uitrol is daar halverwege op gefaald, op de
container `agents`, na een schone what-if.

**Waarom `what-if` dat niet kan zien**, en dit is het hele mechanisme: de GET op een container
geeft een niet-gezette `defaultTtl` terug als `null`. Kijk maar:

```bash
az cosmosdb sql container show -g rg-soratus-prod -a cosmos-soratus-prod \
  -d platform -n customers --query "resource.defaultTtl"     # → null
```

Die `null` betekent hier "staat uit" en niet "staat op null". `what-if` vergelijkt de voorgestelde
body met precies die GET, dus template-null en werkelijk-afwezig zien er voor hem identiek uit — en
identiek betekent "geen wijziging", dus groen. De **PUT**-validatie is strenger dan de GET-weergave,
en dat verschil valt buiten wat `what-if` kan weten. Een groene what-if is hier dus geen bewijs maar
een bevestiging dat er niets te zien is.

Daarom staat er in `portal-rg.bicep` en `klant-rg.bicep` een `union()` waar je
`defaultTtl: c.ttl` zou verwachten:

```bicep
resource: union(
  { id: c.name, partitionKey: { paths: ['/pk'], kind: 'Hash' }, /* … */ },
  c.ttl == null ? {} : { defaultTtl: c.ttl }
)
```

De controle die je vóór een uitrol kunt doen, en de enige die dit vangt:

```bash
az bicep build --file infra/portal/portal-rg.bicep --stdout | grep 'defaultTtl": null'
```

Nul treffers. Wat je overhoudt is een `if(equals(..., null()), createObject(), …)` in de
expressie — het veld wordt dus weggelaten en niet op null gezet.

Containers zonder TTL: `agents` (de registratie moet blijven staan) en `customers`
(contractdata mag niet verlopen).

## Namen van rolverleningen

Een rolverlening heet in ARM naar een GUID. Bij de bestaande, met de hand gemaakte
verleningen staan die GUID's letterlijk in `portal-rg.bicep` en `main.bicepparam`, want
anders meldt `what-if` ze als nieuw en zou een deploy een tweede, identieke verlening
aanmaken. In de klanttemplate worden ze uitgerekend met `guid(bereik, principal, rol)`: dat
is stabiel over deploys heen en is wat je wilt als je van nul begint.

Hetzelfde geldt voor `portalIdentityPrincipalId`. Dat is een parameter met een letterlijke
waarde en niet `portalIdentity.properties.principalId`, omdat `what-if` een `reference()`
naar een resource die de template zelf aanmaakt niet kan uitrekenen en dan élke rolverlening
als gewijzigd meldt. Een principal-id is geen geheim. Bouw je van nul, geef dan de nieuwe
waarde mee; de output van de template noemt de echte.

## Cosmos-RBAC: twee dingen die op elkaar lijken

De meest gemaakte fout in dit soort templates. Cosmos heeft twéé
rechtenstelsels, en het ene geeft geen recht in het andere:

- `Microsoft.Authorization/roleAssignments` — de gewone Azure-RBAC. Geeft recht op het
  **account**: zien dat het bestaat, instellingen lezen, sleutels ophalen. `Reader` hierop
  geeft **geen** enkel recht op een document.
- `Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments` — het dataplane van Cosmos.
  Hier zitten `Cosmos DB Built-in Data Reader` (`…0001`) en `Data Contributor` (`…0002`).
  Dit is wat een agent nodig heeft om te schrijven en het portaal om te lezen.

Omdat `disableLocalAuth: true` staat, is er geen sleutel om op terug te vallen. Vergeet je
de `sqlRoleAssignment`, dan krijgt de agent een 403 bij de eerste schrijfpoging en niet
eerder — de verbinding komt wel op.

**En het `scope`-veld doet écht iets.** Een data-plane verlening kan op het account staan, op één
database (`…/dbs/platform`) of op één container (`…/dbs/platform/colls/customers`). Cosmos telt de
verleningen van een principal bij elkaar op. Dat is wat het portaal accountbreed laat lézen en
alleen in `platform` laat schríjven, en het is één regel in de template:

```bicep
scope: platform.id      // niet cosmos.id
```

Een `Data Contributor` op accountniveau ziet er in een template precies zo uit en geeft het portaal
schrijfrecht op alle telemetrie. Kijk bij een review dus niet alleen naar de rol, maar naar de
scope.
