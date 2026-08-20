# Deploy

Hoe de code van dit repository in productie komt. Twee workflows, één Azure-abonnement, geen
handmatige stappen.

## Welke workflow wanneer draait

| Workflow | Draait bij | Bouwt | Rolt uit naar |
|---|---|---|---|
| `.github/workflows/deploy.yml` | push naar `main` die `Soratus.Web/**`, `Soratus.Web.Tests/**`, `Soratus.slnx`, `NuGet.config` of de workflow zelf raakt | `Soratus.Web` + `Soratus.Web.Tests` | App Service `app-soratus-prod` (marketingsite) |
| `.github/workflows/deploy-portal.yml` | push naar `main` die `Soratus.Portal/**`, `Soratus.Portal.Tests/**`, `Soratus.Agents.Contracts/**`, `Soratus.slnx`, `NuGet.config` of de workflow zelf raakt | `Soratus.Agents.Contracts` + `Soratus.Portal` + `Soratus.Portal.Tests` | App Service `app-soratus-portal-prod` (agentportaal) |

Beide zijn ook met de hand te starten (`workflow_dispatch`), en beide bouwen bewust **per project**
in plaats van de hele solution. Een gebroken portaal mag de marketingsite niet tegenhouden, en
omgekeerd. `Soratus.slnx` en `NuGet.config` staan in beide filters omdat een wijziging daar allebei
kan raken.

De portalworkflow heeft twee jobs:

1. **build-test** — restore, build, `dotnet test` op `Soratus.Portal.Tests`, publish, en het
   publish-resultaat als artifact `portal-<sha>` (30 dagen bewaard).
2. **deploy** — `needs: build-test`, dus deployt alleen als de tests groen zijn. Haalt het artifact
   op, logt in met OIDC, rolt uit en doet een smoke test op `https://portal.soratus.com/healthz`
   (30 pogingen, 10 seconden ertussen, dus ongeveer vijf minuten).

`concurrency: deploy-portal` staat **zonder** `cancel-in-progress`. Deploys wachten op elkaar in
plaats van elkaar af te breken; een halverwege afgekapte App Service-deploy laat de app in
onbekende staat achter.

De deploy-job hangt aan GitHub-environment `production`. Zolang daar geen regels op staan deployt
hij door; zet er reviewers op en het wordt een approval gate, zonder wijziging aan de workflow.

## Secrets

| Secret | Waarvoor | Waar hij vandaan komt |
|---|---|---|
| `AZURE_CLIENT_ID` | client-id van `azure/login` | app-registratie `soratus-web-github-deploy` |
| `AZURE_TENANT_ID` | tenant van dezelfde login | Soratus-tenant |
| `AZURE_SUBSCRIPTION_ID` | doelabonnement | `Pay-As-You-Go-SORATUS` |

Alle drie zijn gezet door `setup-azure.ps1` en worden door beide workflows gedeeld. Er is **geen
publish profile** en er zijn geen wachtwoorden in het spel: de authenticatie loopt via OIDC. GitHub
wisselt een kortlevend token in tegen een Azure-token, op grond van een federated credential op
`repo:Marcel-Soratus/soratus-web:ref:refs/heads/main`. Die app-registratie heeft `Contributor` op
resource group `rg-soratus-prod` en nergens anders.

Gevolgen om te onthouden:

- Alleen `main` kan deployen. Een fork of feature branch krijgt geen token.
- Een nieuwe App Service binnen `rg-soratus-prod` heeft geen nieuw secret nodig.
- Een resource group of abonnement *buiten* `rg-soratus-prod` werkt niet zonder extra
  rolverlening — dat is een bewuste rem, geen omissie.

## Als een deploy faalt

Waar hij afbreekt, bepaalt wat er aan de hand is:

- **build of test rood** — er is niets uitgerold. Productie draait onveranderd. Repareren en
  opnieuw pushen.
- **`azure/login` rood** — het OIDC-vertrouwen klopt niet (verlopen credential, gewijzigde
  branchnaam, weggevallen rol). Nog steeds niets uitgerold. Controleer de federated credential en
  de rolverlening op `rg-soratus-prod`.
- **`azure/webapps-deploy` rood** — de uitrol is gedeeltelijk gebeurd. Behandelen als kapot:
  terugrollen, dan pas onderzoeken.
- **smoke test rood** — de nieuwe versie staat er wél op, maar `/healthz` geeft binnen vijf minuten
  geen 200. Dit is het normale geval voor "gebouwd, maar start niet". Terugrollen, dan de logs
  lezen: `az webapp log tail --name app-soratus-portal-prod --resource-group rg-soratus-prod`.

De smoke test is opzettelijk streng: liever een rode pijplijn dan een portaal dat er wel staat maar
niets doet.

## Met de hand terugrollen

Elke geslaagde run laat een deploybaar pakket achter. Terugrollen is dus: het pakket van de vorige
goede run opnieuw uitrollen.

```bash
# 1. Zoek de laatste goede run en pak zijn artifact.
gh run list --workflow deploy-portal.yml --repo Marcel-Soratus/soratus-web --limit 10
gh run download <run-id> --repo Marcel-Soratus/soratus-web --name portal-<sha> --dir ./rollback

# 2. Zip het en zet het terug.
cd rollback && zip -r ../rollback.zip . && cd ..
az account set --subscription 501a66d2-de54-4d4f-9f7c-1fbb55bec17f
az webapp deploy \
  --name app-soratus-portal-prod --resource-group rg-soratus-prod \
  --src-path rollback.zip --type zip

# 3. Controleer.
curl -i https://portal.soratus.com/healthz
```

Artifacts worden 30 dagen bewaard; ouder dan dat rol je terug door de betreffende commit opnieuw te
bouwen (`gh workflow run deploy-portal.yml --ref <tag>`). Tag daarom releases die ertoe doen.

## Een derde workflow voor de agents — nog niet

De agents (te beginnen met `heartbeat-demo`) krijgen een eigen uitrol per klant. Die workflow is
bewust **nog niet gebouwd**. De redenen, zodat de afweging later te herzien is:

- Er is nog geen agentcode in het repository (`agents/` bestaat niet) en nog geen doel-App Service.
- Elke klant draait in zijn eigen abonnement/resource group. De huidige app-registratie mag alleen
  in `rg-soratus-prod`. Een workflow die naar een klantomgeving wijst kan vandaag niet eens
  inloggen; eerst moet per klant een rolverlening (en waarschijnlijk een eigen federated credential)
  bestaan.
- Of agents op App Service of op Container Apps draaien staat nog niet vast — de portaalspecificatie
  gaat uit van Container Apps met Log Analytics. Dat verschil bepaalt de hele deploy-stap.
- De Cosmos DB waar de hartslag in landt is nog niet ingericht, dus de belangrijkste controle van
  die workflow is nu niet uitvoerbaar.

Een workflow die naar niet-bestaande resources wijst en die niemand kan draaien is een dode knop
die bij de eerste echte uitrol toch herschreven wordt. Zodra bovenstaande vier punten rond zijn,
hoort hij er wel te komen, met deze eisen:

- `workflow_dispatch` met invoer `agent` (naam, bijvoorbeeld `heartbeat-demo`) en `customer`
  (klantslug), waaruit doelresource en resource group worden afgeleid.
- Na de uitrol wacht de workflow tot **90 seconden** op een hartslag: het `AgentRegistration`-document
  van die agent in Cosmos DB moet een `lastHeartbeatAt` krijgen die ná het moment van uitrollen
  ligt. Blijft die uit, dan faalt de run.

Die laatste eis is de kern ervan. Zonder die controle is "ik was `Soratus.Agents.Telemetry`
vergeten" een agent die stilletjes ontbreekt in het portaal; mét die controle is het een rode
pijplijn.

## Wat er nog moet gebeuren voordat de portal-deploy kan draaien

1. App Service `app-soratus-portal-prod` aanmaken in `rg-soratus-prod` op plan `asp-soratus-prod`
   (West Europe), met dezelfde harding als de marketingsite: always-on, HTTP/2, TLS 1.2, FTPS uit,
   `ASPNETCORE_ENVIRONMENT=Production` en `WEBSITE_RUN_FROM_PACKAGE=1`.
2. Custom domein `portal.soratus.com` koppelen met certificaat. Zonder dat faalt de smoke test,
   ook al is de deploy geslaagd. Tijdelijk uitwijken kan door `HEALTH_URL` in de workflow op
   `https://app-soratus-portal-prod.azurewebsites.net/healthz` te zetten.
3. `Soratus.Portal.Tests` toevoegen. De workflow rekent erop; tot dat project bestaat faalt de
   build-test-job. Dat is bedoeld gedrag — een teststap die niets test, test niets.
4. Endpoint `/healthz` in `Soratus.Portal` publiceren, zonder authenticatie, zodat de smoke test
   erbij kan.
