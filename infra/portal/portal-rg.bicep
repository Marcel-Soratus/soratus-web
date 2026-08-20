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

param keyVaultName string = 'kv-soratus-prod'
param cosmosAccountName string = 'cosmos-soratus-prod'
param cosmosDatabaseName string = 'telemetry'

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

var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'
var cosmosDataReaderRoleId = '00000000-0000-0000-0000-000000000001'

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
      resource: {
        id: c.name
        partitionKey: {
          paths: ['/pk']
          kind: 'Hash'
        }
        defaultTtl: c.ttl
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
      }
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
      appSettings: [
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
      ]
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

@description('Principal-id van de portal-identity. Nodig als parameter bij elke klantomgeving.')
output portalIdentityPrincipalId string = portalIdentity.properties.principalId

@description('Client-id van de portal-identity.')
output portalIdentityClientId string = portalIdentity.properties.clientId

output portalDefaultHostName string = portalApp.properties.defaultHostName
output cosmosEndpoint string = cosmos.properties.documentEndpoint
output keyVaultUri string = keyVault.properties.vaultUri
