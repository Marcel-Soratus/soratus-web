// Het agentportaal in rg-soratus-prod.
//
// Vastgelegd naar de werkelijke staat van 20 augustus 2026. Alles hier is met losse
// az-commando's gemaakt; deze template is de bron van waarheid geworden, niet de maker.
// Een `az deployment group what-if` hierop moet nul wijzigingen opleveren.
//
// LET OP — twee resources in deze resource group zijn NIET van het portaal en worden
// hier bewust alleen met `existing` aangehaald:
//   * asp-soratus-prod  draagt ook app-soratus-prod (marketingsite) en app-derdehelft
//   * soratus.com (DNS)  draagt de records van de marketingsite en de e-maildomeinen
// Ze herdefiniëren raakt live sites. Doe dat niet.

targetScope = 'resourceGroup'

@description('Regio voor de portaalresources.')
param location string = 'westeurope'

@description('Bestaand App Service Plan dat ook de marketingsite draagt. Wordt niet aangemaakt.')
param appServicePlanName string = 'asp-soratus-prod'

@description('Bestaande publieke DNS-zone. Wordt niet aangemaakt.')
param dnsZoneName string = 'soratus.com'

@description('Naam van de App Service van het portaal.')
param portalAppName string = 'app-soratus-portal-prod'

@description('Hostnaam waarop het portaal bereikbaar is.')
param portalHostName string = 'portal.soratus.com'

@description('User-assigned identity waarmee het portaal Key Vault en Cosmos benadert.')
param portalIdentityName string = 'id-soratus-portal'

// Principal-id van bovenstaande identity, als parameter en niet als
// portalIdentity.properties.principalId. Reden: what-if kan een reference() naar een
// resource die de template zelf aanmaakt niet uitrekenen, en meldt dan elke
// rolverlening als gewijzigd. Met een letterlijke waarde is what-if leesbaar.
// Een principal-id is geen geheim en verandert niet zolang de identity blijft staan.
// Bouw je van nul, geef dan de nieuwe waarde mee; de output onderaan noemt de echte.
@description('Principal-id (object-id) van de portal-identity.')
param portalIdentityPrincipalId string = 'e48ffac5-672c-4e2b-aab9-340871fb2d62'

// ---------------------------------------------------------------------------
// Mail: het maandoverzicht aan de klant en de storingsmelding aan Soratus
// ---------------------------------------------------------------------------
// Deze instellingen staan hier en niet als losse `az webapp config appsettings set`, en dat is
// geen voorkeur. De appSettings hieronder staan in deze template als vólledige array, en dat is
// in ARM een vervanging: elke met de hand gezette sleutel wordt door de volgende uitrol gewist.
// Dat is een stille storing van de ergste soort — je zet de configuratie, het werkt, en na de
// volgende uitrol zegt het portaal dat mailen niet is ingericht. Dat leest als een
// configuratiefout en niet als een uitrol die hem heeft opgegeten.
//
// Geen van deze waarden is een geheim: een ACS-endpoint is een adres en de authenticatie loopt
// via de managed identity, met een custom role die alleen lezen en schrijven mag — met opzet
// niet Contributor, want die geeft ListKeys erbij en is dan machtiger dan het geheim dat we
// juist wilden vermijden.

@description('Endpoint van de Communication Service waarlangs het portaal mailt.')
param mailEndpoint string = 'https://acs-soratus-prod.europe.communication.azure.com'

@description('Afzender. Moet een geverifieerd domein van de Communication Service zijn.')
param mailFromAddress string = 'DoNotReply@soratus.com'

@description('Antwoordadres op de mail aan de klant. Leeg laten om er geen te zetten.')
param mailReplyToAddress string = ''

@description('''
Proefdraaien: valideren en loggen, niets versturen. Staat standaard uit in deze template en
standaard áán in de code — die kant op, omdat een standaard-uit vlag een storing is die zich
voordoet als werkende functionaliteit. Hier moet hij dus expliciet uit, zodat er in de template
te zien staat dát er werkelijk gemaild wordt.
''')
param mailDryRun bool = false

@description('''
Waar de storingsmeldingen naartoe gaan. Leeg betekent: geen ontvangers, en dan verstuurt de
melder niets — hij valt niet om, hij heeft niets om te doen. Dit is de enige weg waarlangs een
adres bij de melder komt; er is geen parameter waarin een klantadres past.
''')
param alertRecipients array = []

// De ontvangers van de storingsmelding, als PortalAlerts__Recipients__0, __1, … Dat is de vorm
// waarin de configuratiebinder een lijst leest uit platte sleutels. Als variabele en niet inline:
// Bicep staat een for-expressie niet toe binnen een concat. Een lege array levert géén sleutel op,
// en dan heeft de melder geen ontvangers en verstuurt hij niets — hij valt niet om.
// Een optioneel adres hoort te ONTBREKEN als het er niet is, en niet leeg te zijn.
//
// Dit heeft het portaal plat gelegd. De sleutel stond hier onvoorwaardelijk met een lege waarde,
// de configuratiebinder maakte daar "" van, en op dat veld staat een e-mailadresvalidatie — die
// een lege string afkeurt waar hij null doorlaat. Gevolg: een OptionsValidationException bij de
// eerste keer dat de mailinstellingen werden opgevraagd, en omdat dat in een achtergronddienst
// gebeurt legde die de hele host neer. De app was al gestart en /healthz had al 200 gegeven,
// want die raakt met opzet geen enkele afhankelijkheid.
//
// Dit is dezelfde fout als punt 15, nu in een template: leeg en afwezig zijn niet hetzelfde.
var replyToSetting = empty(mailReplyToAddress) ? [] : [
  {
    name: 'PortalMail__ReplyToAddress'
    value: mailReplyToAddress
  }
]

var alertRecipientSettings = [
  for (recipient, index) in alertRecipients: {
    name: 'PortalAlerts__Recipients__${index}'
    value: recipient
  }
]

param keyVaultName string = 'kv-soratus-prod'
param cosmosAccountName string = 'cosmos-soratus-prod'
param cosmosDatabaseName string = 'telemetry'

// Portaaleigen bedrijfsdata staat in een eigen database op hetzelfde account, en
// bewust NIET in `telemetry`. `telemetry` is de vorm die zich per klant herhaalt —
// straks één keer per klantaccount, zie de remarks op Soratus.Portal/Data/
// TelemetryLocation.cs. Wat daarin staat is klantscoped en verhuist mee met de klant.
// Klanten, contracten en toegangsregels zijn van Soratus, horen bij precies één
// omgeving en mogen zich niet vermenigvuldigen. Vandaar een tweede database.
@description('Database voor portaaleigen data: klanten, contracten, toegang.')
param platformDatabaseName string = 'platform'

@description('Entra-tenant van het abonnement.')
param tenantId string = subscription().tenantId

@description('Client-id van de app-registratie soratus-portal (Entra, met de hand beheerd).')
param entraClientId string = '6d1fae2b-eb90-4a5a-ae27-a1032e15ac58'

@description('Runtime van de App Service.')
param linuxFxVersion string = 'DOTNETCORE|10.0'

// De customDomainVerificationId van de site. Staat in de TXT-record asuid.portal en is
// per site vast; App Service genereert hem bij aanmaak en hij verandert niet.
param customDomainVerificationId string = 'B0A04C985681566759FB301CBA01F04F671B67DF1DFC266CEE176E9D9BB4BFB8'

// Namen van de rolverleningen. Met de hand gemaakt, dus willekeurige GUID's; hier
// letterlijk vastgelegd zodat de template overeenkomt met de werkelijkheid. Bij een
// herbouw van nul mag dit guid(...) worden — zie docs/agent-portal/infra.md.
param keyVaultRoleAssignmentName string = '10284461-491b-42f1-ba68-6e927eb27c3c'
param cosmosReaderAssignmentName string = '3ae107a3-d905-4e45-b66c-bf0c53af4717'

// Nieuw, dus uit te rekenen in plaats van letterlijk. Leeg laten is wat je wilt.
@description('Naam van de schrijfrechtverlening op de platform-database. Leeg = uitrekenen.')
param cosmosPlatformWriterAssignmentName string = ''

@description('Log Analytics workspace voor het portaal. Eigen workspace, niet de gedeelde defaultworkspace.')
param workspaceName string = 'log-soratus-prod'

@description('Retentie van de workspace in dagen. Gelijk aan de logretentie van het portaal zelf.')
param logRetentionInDays int = 30

var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'
var cosmosDataReaderRoleId = '00000000-0000-0000-0000-000000000001'
var cosmosDataContributorRoleId = '00000000-0000-0000-0000-000000000002'

// ---------------------------------------------------------------------------
// Bestaand — niet aanmaken, niet wijzigen
// ---------------------------------------------------------------------------

resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' existing = {
  name: appServicePlanName
}

resource dnsZone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: dnsZoneName
}

// ---------------------------------------------------------------------------
// Waarnemen
//
// Dit ontbrak. Het portaal is met losse az-commando's opgezet en had daardoor
// nul diagnostic settings en geen Application Insights — precies het gebrek dat
// docs/agent-portal/fase-0-afwijkingen.md §1 aan de klantomgeving MBV verwijt.
// Een eigen workspace, niet de gedeelde DefaultWorkspace-...-WEU, want daarin
// staat nu de telemetrie van drie klanten door elkaar.
//
// Let op de vorm van de diagnostic settings hieronder: losse `category`-waarden
// en geen `categoryGroup`. Deze providers leveren geen categoryGroups, dus
// 'allLogs' of 'audit' wordt bij het uitrollen afgewezen.
// ---------------------------------------------------------------------------

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: workspaceName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: logRetentionInDays
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// Bewust géén Application Insights hier. De opdracht zegt "geen tracking of
// analytics"; platformlogs zijn operationeel en vallen daarbuiten, maar
// Application Insights op applicatieniveau legt elk verzoek vast inclusief de
// URL, en daarin staat de klant-slug. Dat is een besluit voor Marcel en geen
// implementatiedetail. Wat het praktische probleem oplost — "het portaal valt
// om, waar kijk ik dan" — zijn AppServiceAppLogs en AppServiceConsoleLogs
// hieronder, en die raken die grens niet.

// ---------------------------------------------------------------------------
// Identity
// ---------------------------------------------------------------------------

resource portalIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: portalIdentityName
  location: location
}

// ---------------------------------------------------------------------------
// Key Vault
// ---------------------------------------------------------------------------

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
  }
}

resource keyVaultSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: keyVaultRoleAssignmentName
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: portalIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------
// Cosmos DB — serverless, alleen Entra-auth
// ---------------------------------------------------------------------------

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = {
  name: cosmosAccountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    capabilities: [
      { name: 'EnableServerless' }
    ]
    consistencyPolicy: {
      // maxIntervalInSeconds en maxStalenessPrefix staan bij Session-consistentie
      // wel in de resource maar doen niets; daarom niet opgenomen.
      defaultConsistencyLevel: 'Session'
    }
    // Sleutels uit: het portaal en de agents komen binnen met Entra.
    disableLocalAuth: true
    enableAutomaticFailover: true
    enableMultipleWriteLocations: false
    publicNetworkAccess: 'Enabled'
    minimalTlsVersion: 'Tls12'
    networkAclBypass: 'None'
    defaultIdentity: 'FirstPartyIdentity'
    enablePerRegionPerPartitionAutoscale: false
    analyticalStorageConfiguration: {
      schemaType: 'WellDefined'
    }
    backupPolicy: {
      type: 'Periodic'
      periodicModeProperties: {
        backupIntervalInMinutes: 240
        backupRetentionIntervalInHours: 8
        backupStorageRedundancy: 'Geo'
      }
    }
  }
}

resource telemetry 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' = {
  parent: cosmos
  name: cosmosDatabaseName
  properties: {
    resource: {
      id: cosmosDatabaseName
    }
  }
}

// TTL in seconden. -1 = aan zonder verval, null/afwezig = uit.
var containers = [
  { name: 'agents', ttl: null } //   geen verval: de registratie moet blijven staan
  { name: 'runs', ttl: 34560000 } // 400 dagen
  { name: 'logs', ttl: 2592000 } //   30 dagen
]

resource cosmosContainers 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = [
  for c in containers: {
    parent: telemetry
    name: c.name
    properties: {
      // defaultTtl moet ontbréken als er geen verval is, niet null zijn. Een
      // expliciete null levert bij het uitrollen "One of the specified inputs is
      // invalid" op de container `agents`, en `what-if` ziet dat niet: daar zijn
      // "null" en "afwezig" niet van elkaar te onderscheiden. Vandaar union() met
      // een leeg object in plaats van defaultTtl: c.ttl.
      resource: union(
        {
          id: c.name
          partitionKey: {
            paths: ['/pk']
            kind: 'Hash'
          }
          conflictResolutionPolicy: {
            mode: 'LastWriterWins'
            conflictResolutionPath: '/_ts'
          }
          indexingPolicy: {
            indexingMode: 'consistent'
            automatic: true
            includedPaths: [
              { path: '/*' }
            ]
            excludedPaths: [
              { path: '/"_etag"/?' }
            ]
          }
        },
        c.ttl == null ? {} : { defaultTtl: c.ttl }
      )
    }
  }
]

// Dit is GEEN Microsoft.Authorization/roleAssignments. Cosmos heeft een eigen
// dataplane-RBAC; een Reader-rol via Microsoft.Authorization geeft geen leesrecht
// op documenten. Verwar de twee niet.
resource cosmosDataReader 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = {
  parent: cosmos
  name: cosmosReaderAssignmentName
  properties: {
    roleDefinitionId: resourceId(
      'Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions',
      cosmosAccountName,
      cosmosDataReaderRoleId
    )
    principalId: portalIdentityPrincipalId
    scope: cosmos.id
  }
}

// ---------------------------------------------------------------------------
// Cosmos DB — de platform-database: klanten, contracten, toegang
//
// Fase 2 vraagt dat een nieuwe klant zonder database-actie kan worden ingericht.
// Nu komt de klantenlijst uit `appsettings.json`, dus een klant toevoegen is een
// deploy. Deze database is waar die lijst naartoe gaat.
//
// Eén container en niet drie. De telemetriecontainers zijn per documenttype
// gesplitst omdat ze verschillende bewaartermijnen hebben; dát is de reden voor
// die splitsing en die reden geldt hier niet. Klant, contract en toegangsregel
// verlopen geen van drieën, horen bij dezelfde klant en worden samen gelezen. In
// één container met de klant-id als partitiesleutel is een klant één punt-lees en
// is een wijziging aan klant + contract + toegang één transactionele batch. Over
// drie containers is diezelfde wijziging drie schrijfacties zonder samenhang, en
// dan bestaat de toestand "contract bijgewerkt, toegang niet".
// ---------------------------------------------------------------------------

resource platform 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' = {
  parent: cosmos
  name: platformDatabaseName
  properties: {
    resource: {
      id: platformDatabaseName
    }
  }
}

// TTL in seconden, of null voor géén verval. Let op: `null` is hier de instructie
// aan de template en komt níet als null in de resource terecht — zie de union()
// hieronder. Contractdata mag niet verlopen; dat is het verschil met `logs`.
// `customers` en niet `klanten`: de drie bestaande containers heten `agents`, `runs`
// en `logs`, en de naam staat als constante in de code. Resourcenamen zijn hier
// Engels, documentatie en tests Nederlands.
var platformContainers = [
  { name: 'customers', ttl: null }
]

resource platformContainerResources 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = [
  for c in platformContainers: {
    parent: platform
    name: c.name
    properties: {
      // Zelfde valkuil als bij de telemetriecontainers hierboven, en de reden dat
      // hier een union() staat waar je `defaultTtl: c.ttl` zou verwachten: een
      // expliciete `defaultTtl: null` levert bij het uitrollen "One of the specified
      // inputs is invalid" op, en `what-if` ziet dat niet — daar zijn "null" en
      // "afwezig" niet van elkaar te onderscheiden. Een groene what-if is hier dus
      // geen bewijs. Het veld moet ontbreken.
      resource: union(
        {
          id: c.name
          partitionKey: {
            // Zelfde pad als de telemetriecontainers, zodat er één conventie is:
            // het document draagt zijn partitiesleutel in het veld `pk`. Hier is
            // dat de klant-id. Eén klant is daarmee één logische partitie.
            paths: ['/pk']
            kind: 'Hash'
          }
          conflictResolutionPolicy: {
            mode: 'LastWriterWins'
            conflictResolutionPath: '/_ts'
          }
          indexingPolicy: {
            // Alles indexeren. Bij een paar honderd documenten kost dat niets, en
            // de datalaag moet ook kunnen zoeken op e-mailadres — dat is een
            // cross-partition query en die heeft een index nodig, geen scan.
            indexingMode: 'consistent'
            automatic: true
            includedPaths: [
              { path: '/*' }
            ]
            excludedPaths: [
              { path: '/"_etag"/?' }
            ]
          }
        },
        c.ttl == null ? {} : { defaultTtl: c.ttl }
      )
    }
  }
]

// Schrijfrecht, en alleen hier. De scope is de platform-DATABASE en niet het
// account: het portaal moet contracten kunnen bijwerken, maar het mag geen
// telemetrie kunnen wijzigen of wissen. Telemetrie is het bewijsmateriaal waarop
// de statusweergave rust — schrijfrecht daarop maakt van "de agent heeft niets
// gepubliceerd" een uitspraak die het portaal zelf kan hebben veroorzaakt.
//
// Het accountbrede Data Reader hierboven blijft staan en wordt hier niet
// vervangen: dat is wat het portaal telemetrie laat lezen. Deze verlening komt
// ernaast; Cosmos telt data-plane rechten bij elkaar op.
//
// Er bestaat geen ingebouwde rol die alleen schrijft. Data Contributor omvat lezen,
// en dat is op deze scope precies wat nodig is.
resource cosmosPlatformWriter 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = {
  parent: cosmos
  name: empty(cosmosPlatformWriterAssignmentName)
    ? guid(cosmos.id, platformDatabaseName, portalIdentityPrincipalId, cosmosDataContributorRoleId)
    : cosmosPlatformWriterAssignmentName
  properties: {
    roleDefinitionId: resourceId(
      'Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions',
      cosmosAccountName,
      cosmosDataContributorRoleId
    )
    principalId: portalIdentityPrincipalId
    // Niet cosmos.id. Dit is de hele grens tussen "mag contracten bijwerken" en
    // "mag telemetrie overschrijven", en het is één regel.
    //
    // En niet platform.id. Een Cosmos data-plane rolverlening wil een dáta-plane
    // pad en geen ARM-resource-id: '/dbs/{db}' en niet '/sqlDatabases/{db}'. Met de
    // ARM-vorm faalt de uitrol op "Expected path segment [dbs] at position [0] but
    // found [sqlDatabases]" — en what-if ziet dat niet, want die valideert de vorm
    // van deze string niet. Tweede keer in dit werk dat een groene what-if een
    // falende uitrol opleverde; de eerste was defaultTtl: null.
    scope: '${cosmos.id}/dbs/${platformDatabaseName}'
  }
}

// ---------------------------------------------------------------------------
// App Service
// ---------------------------------------------------------------------------

resource portalApp 'Microsoft.Web/sites@2024-04-01' = {
  name: portalAppName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${portalIdentity.id}': {}
    }
  }
  properties: {
    serverFarmId: appServicePlan.id
    reserved: true
    httpsOnly: true
    clientAffinityEnabled: true
    publicNetworkAccess: 'Enabled'

    // Deze regel is een bewuste correctie op de werkelijkheid en niet een
    // vastlegging ervan: in Azure staat dit veld op 'SystemAssigned', de
    // ARM-standaard, terwijl deze app alleen een user-assigned identity heeft.
    // Zolang er geen Key Vault-referentie in de app-settings staat merkt niemand
    // dat, maar de eerste @Microsoft.KeyVault(...)-instelling valt dan stil met
    // een foutmelding die niets over de oorzaak zegt. Een what-if op deze
    // template hoort hier dus één Modify te melden; dat is de fix, geen drift.
    keyVaultReferenceIdentity: portalIdentity.id
    siteConfig: {
      linuxFxVersion: linuxFxVersion
      alwaysOn: true
      healthCheckPath: '/healthz'
      http20Enabled: true
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      ftpsState: 'Disabled'
      numberOfWorkers: 1
      // Zinloos op Linux, maar de site heeft deze waarde staan en de ARM-standaard
      // is v4.6. Expliciet, zodat een deploy hem niet omzet.
      netFrameworkVersion: 'v4.0'
      // concat van twee lijsten: de vaste sleutels, en de ontvangers van de storingsmelding als
      // genummerde sleutels. Let op dat deze hele array een vervánging is in ARM — met de hand
      // gezette app-settings verdwijnen bij de volgende uitrol. Wat het portaal nodig heeft, hoort
      // dus hier te staan en niet in een los commando.
      appSettings: concat([
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
        {
          name: 'AzureAd__Instance'
          value: environment().authentication.loginEndpoint
        }
        {
          name: 'AzureAd__TenantId'
          value: tenantId
        }
        {
          name: 'AzureAd__ClientId'
          value: entraClientId
        }
        {
          name: 'AzureAd__CallbackPath'
          value: '/signin-oidc'
        }
        {
          // Zonder deze pakt DefaultAzureCredential de system-assigned identity,
          // die de site niet heeft. Dan valt zowel Key Vault als Cosmos weg.
          name: 'AZURE_CLIENT_ID'
          value: portalIdentity.properties.clientId
        }
        {
          name: 'PortalMail__Endpoint'
          value: mailEndpoint
        }
        {
          name: 'PortalMail__FromAddress'
          value: mailFromAddress
        }
        {
          name: 'PortalMail__DryRun'
          value: string(mailDryRun)
        }
        {
          name: 'PortalMail__PortalBaseUri'
          value: 'https://${portalHostName}'
        }
      ], replyToSetting, alertRecipientSettings)
    }
  }
}

// ---------------------------------------------------------------------------
// DNS — records in de bestaande zone
// ---------------------------------------------------------------------------

var portalRecordName = replace(portalHostName, '.${dnsZoneName}', '')

resource portalCname 'Microsoft.Network/dnsZones/CNAME@2018-05-01' = {
  parent: dnsZone
  name: portalRecordName
  properties: {
    TTL: 3600
    CNAMERecord: {
      cname: '${portalAppName}.azurewebsites.net'
    }
  }
}

// Bewijst aan App Service dat wij de hostnaam bezitten. Moet er staan vóór de
// hostname binding en moet blijven staan, ook daarna.
resource portalAsuid 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
  parent: dnsZone
  name: 'asuid.${portalRecordName}'
  properties: {
    TTL: 3600
    TXTRecords: [
      {
        value: [customDomainVerificationId]
      }
    ]
  }
}

// ---------------------------------------------------------------------------
// Aangepast domein en certificaat
// ---------------------------------------------------------------------------

// De ketting: eerst de binding zonder TLS (anders bestaat het certificaat nog niet),
// dan het App Service Managed Certificate, dan de binding opnieuw met SNI erop.
// Dit is de standaard-omweg; ARM kan de kringverwijzing niet anders oplossen.

resource hostNameBinding 'Microsoft.Web/sites/hostNameBindings@2024-04-01' = {
  parent: portalApp
  name: portalHostName
  properties: {
    hostNameType: 'Verified'
    siteName: portalAppName
    sslState: 'SniEnabled'
    thumbprint: managedCertificate.properties.thumbprint
  }
  dependsOn: [
    portalCname
    portalAsuid
  ]
}

resource managedCertificate 'Microsoft.Web/certificates@2024-04-01' = {
  name: portalHostName
  location: location
  properties: {
    canonicalName: portalHostName
    serverFarmId: appServicePlan.id
  }
  dependsOn: [
    portalCname
    portalAsuid
  ]
}

// ---------------------------------------------------------------------------
// Diagnostic settings
//
// Bewust niet op het App Service Plan: asp-soratus-prod draagt ook de live
// marketingsite en app-derdehelft. Een instelling daarop raakt dus resources
// buiten het portaal, en dat hoort een eigen besluit te zijn.
// ---------------------------------------------------------------------------

resource portalAppDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'naar-eigen-workspace'
  scope: portalApp
  properties: {
    workspaceId: workspace.id
    logs: [
      // HTTP-verkeer en de console- en applicatielogs. Dat laatste is wat je
      // nodig hebt als het portaal een uitzondering gooit.
      { category: 'AppServiceHTTPLogs', enabled: true }
      { category: 'AppServiceConsoleLogs', enabled: true }
      { category: 'AppServiceAppLogs', enabled: true }
      { category: 'AppServicePlatformLogs', enabled: true }
      // Aanmeldstromen. Dit is de categorie waarmee het rolclaim-probleem uit
      // fase 0 zichtbaar zou zijn geweest zonder een tijdelijke diagnose op een
      // pagina te zetten.
      { category: 'AppServiceAuthenticationLogs', enabled: true }
      { category: 'AppServiceAuditLogs', enabled: true }
      { category: 'AppServiceIPSecAuditLogs', enabled: true }
    ]
    metrics: [
      { category: 'AllMetrics', enabled: true }
    ]
  }
}

resource cosmosDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'naar-eigen-workspace'
  scope: cosmos
  properties: {
    workspaceId: workspace.id
    logs: [
      { category: 'DataPlaneRequests', enabled: true }
      { category: 'ControlPlaneRequests', enabled: true }
      { category: 'QueryRuntimeStatistics', enabled: true }
    ]
  }
}

resource keyVaultDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'naar-eigen-workspace'
  scope: keyVault
  properties: {
    workspaceId: workspace.id
    logs: [
      { category: 'AuditEvent', enabled: true }
    ]
    metrics: [
      { category: 'AllMetrics', enabled: true }
    ]
  }
}

// ---------------------------------------------------------------------------

@description('Principal-id van de portal-identity. Nodig als parameter bij elke klantomgeving.')
output portalIdentityPrincipalId string = portalIdentity.properties.principalId

@description('Client-id van de portal-identity.')
output portalIdentityClientId string = portalIdentity.properties.clientId

output portalDefaultHostName string = portalApp.properties.defaultHostName
output cosmosEndpoint string = cosmos.properties.documentEndpoint
output keyVaultUri string = keyVault.properties.vaultUri

@description('Database met de portaaleigen data. Hoort in de configuratiesectie Platform.')
output platformDatabaseName string = platform.name
