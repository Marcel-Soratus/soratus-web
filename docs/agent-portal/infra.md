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

Alles daarbuiten in de uitvoer is een echte afwijking. Zoek hem uit voor je deployt.

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

## Wat per klant met de hand blijft

**Entra-objecten kunnen niet in ARM.** App-registraties, app-rollen, service principals,
federated credentials en rol-toewijzingen aan personen leven in Microsoft Graph, niet in
Azure Resource Manager. Bicep kan ze niet aanmaken en `what-if` ziet ze niet. Wat er staat,
staat in `infra/entra/` als JSON met het bijbehorende `az`-commando.

Concreet per klant met de hand:

| Wat | Waarom niet in Bicep |
|---|---|
| Klantgebruikers de rol `Klant` geven op de app-registratie `soratus-portal` | Graph, geen ARM. Zonder deze toewijzing komt de klant het portaal niet in — `appRoleAssignmentRequired` staat aan |
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
- **Diagnostic settings op de portaalresources.** Er zijn er nu nul. Dat is een gemis, geen
  besluit — zie hieronder.

## Twee dingen die nog niet goed staan

Vastgelegd omdat ze zichtbaar moeten blijven, niet omdat ze zo bedoeld zijn.

**1. Het portaal zelf heeft geen enkele diagnostic setting en geen Application Insights.**
Precies het gebrek dat we bij MBV hebben aangetroffen, nu in onze eigen twee dagen oude
omgeving. Er is geen Log Analytics workspace in `rg-soratus-prod`, dus er is ook geen plek
om naartoe af te voeren. Wat het vraagt: een `log-soratus-prod` en `appi-soratus-prod` in
`portal-rg.bicep`, plus diagnostic settings op de App Service, de Key Vault en Cosmos —
zoals `infra/klant/klant-rg.bicep` het voor een klant al doet. Bewust niet meegenomen
zolang Deel 1 de werkelijkheid moet vastleggen: die resources bestaan niet, en ze in de
template zetten haalt het bewijs onderuit dat de template met Azure overeenkomt.

**2. `keyVaultReferenceIdentity` staat op `SystemAssigned`.** De site heeft alleen een
user-assigned identity, dus er is geen system-assigned identity om mee te resolven. Zolang
de app zijn secrets zelf ophaalt via `DefaultAzureCredential` en `AZURE_CLIENT_ID` maakt dat
niets uit — en zo werkt het nu. Maar de eerste app-setting in de vorm
`@Microsoft.KeyVault(...)` valt stil, en de fout die je dan ziet zegt niets over de oorzaak.
Het is de ARM-standaardwaarde, niet iets wat iemand heeft gezet; daarom staat hij niet in de
template. Ga je Key Vault-referenties gebruiken, zet hem dan op de resource-id van
`id-soratus-portal`.

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
