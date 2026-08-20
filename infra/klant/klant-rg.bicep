// Blauwdruk voor één klantomgeving.
//
// Nog niet in gebruik. Vastgelegd voordat er een tweede klant bijkomt, zodat de vorm
// vaststaat en MBV er straks naartoe kan verhuizen.
//
// Wat hier anders is dan bij MBV, en waarom:
//   * eigen Log Analytics workspace per klant. MBV's Application Insights voert af naar
//     DefaultWorkspace-…-WEU, samen met PackCompany en AllSprinklers. Eén KQL-query en je
//     zit in de gegevens van een andere klant.
//   * purge protection aan op de Key Vault. Bij kv-mbv-keyvault-001 staat hij uit; een
//     verwijderde vault is daar definitief weg te gooien.
//   * diagnostic settings op alles wat ze kan leveren. MBV heeft er nul, dus er is geen
//     spoor van wie wat wanneer deed.
//   * disableLocalAuth op Cosmos. MBV gebruikt sleutels, en één daarvan staat als platte
//     app-setting naast een Key Vault die niets bevat.
//   * één Cosmos-account. MBV heeft mbv-dbaccount en mbv-dbaccount2, beide met een
//     database die MBV heet.

targetScope = 'resourceGroup'

@description('Klantcode, lowercase, kort. Zit in elke resourcenaam.')
@minLength(2)
@maxLength(12)
param klantcode string

@description('Weergavenaam van de klant. Alleen voor tags en het portaal.')
param klantnaam string

@description('Regio.')
param location string = 'westeurope'

@description('Omgeving. Zit in de resourcenamen.')
@allowed(['prod', 'acc', 'test'])
param omgeving string = 'prod'

@description('SKU van het App Service Plan. B1 volstaat voor een klant met een paar agents.')
@allowed(['B1', 'B2', 'B3', 'S1', 'P0v3', 'P1v3'])
param appServicePlanSku string = 'B1'

@description('Principal-id (object-id) van id-soratus-portal. Het portaal leest hiermee mee.')
param portalIdentityPrincipalId string

@description('''
De agents die voor deze klant draaien. Eén App Service per agent.
  naam            deel van de appnaam, en de agentnaam in de telemetrie
  weergavenaam    wat de klant in het portaal ziet
  schema          cron-expressie, leeg als de agent op een gebeurtenis draait
  trigger         Schedule | Event | Manual
  healthCheckPath standaard /healthz
''')
param agents array = []

@description('Bewaartermijn van de logs in Log Analytics, in dagen.')
@minValue(30)
@maxValue(730)
param logRetentionInDays int = 30

var k = toLower(klantcode)
var suffix = '${k}-${omgeving}'

var tags = {
  klant: klantnaam
  klantcode: k
  omgeving: omgeving
  beheer: 'bicep'
}

// TTL in seconden. null = geen verval.
var containers = [
  { name: 'agents', ttl: null } //   de registratie moet blijven staan
  { name: 'runs', ttl: 34560000 } // 400 dagen: ruim een jaar terugkijken
  { name: 'logs', ttl: 2592000 } //   30 dagen: logregels zijn er voor het onderzoek van nu
]

var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'
var cosmosDataContributorRoleId = '00000000-0000-0000-0000-000000000002'
var cosmosDataReaderRoleId = '00000000-0000-0000-0000-000000000001'

// ---------------------------------------------------------------------------
// Waarnemen
// ---------------------------------------------------------------------------

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${suffix}'
  location: location
  tags: tags
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

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${suffix}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    // Workspace-based. Klassieke Application Insights bestaat niet meer en zonder
    // deze verwijzing landt de telemetrie in een workspace die Azure zelf kiest.
    WorkspaceResourceId: workspace.id
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// ---------------------------------------------------------------------------
// Identity
// ---------------------------------------------------------------------------

// Eén identity voor alle agents van deze klant. Niet per agent: dan groeit het aantal
// rolverleningen met het aantal agents en wordt niemand er wijzer van. De grens die telt
// is die tussen klanten, en die valt hier samen met de resource group.
resource agentsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${k}-agents'
  location: location
  tags: tags
}

// ---------------------------------------------------------------------------
// Key Vault
// ---------------------------------------------------------------------------

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-${suffix}'
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    // RBAC in plaats van access policies. Access policies zijn niet te overzien
    // zodra er meer dan één principal is.
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    // Aan. Zonder purge protection is een verwijderde vault definitief weg te gooien
    // en verlies je onherroepelijk sleutels waarvan je niet wist dat ze in gebruik waren.
    // Let op: dit is niet terug te draaien.
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
  }
}

resource keyVaultDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'naar-eigen-workspace'
  scope: keyVault
  properties: {
    workspaceId: workspace.id
    // Losse categorieën, geen categoryGroup. Microsoft.KeyVault levert geen
    // categoryGroups (categoryGroups is null bij `az monitor diagnostic-settings
    // categories list`), dus 'audit' of 'allLogs' wordt afgewezen bij het uitrollen.
    logs: [
      {
        category: 'AuditEvent'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

resource agentsKeyVaultAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, agentsIdentity.id, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: agentsIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------
// Cosmos DB — zelfde vorm als cosmos-soratus-prod
// ---------------------------------------------------------------------------

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = {
  name: 'cosmos-${suffix}'
  location: location
  tags: tags
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
    // Serverless: agenttelemetrie is bursty en laag in volume. Provisioned throughput
    // kost hier een veelvoud voor niets.
    capabilities: [
      { name: 'EnableServerless' }
    ]
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    // Alleen Entra. Geen sleutels, dus ook geen sleutel die in een app-setting belandt.
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

resource telemetry 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' = {
  parent: cosmos
  name: 'telemetry'
  properties: {
    resource: {
      id: 'telemetry'
    }
  }
}

resource telemetryContainers 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = [
  for c in containers: {
    parent: telemetry
    name: c.name
    properties: {
      // `defaultTtl` moet ontbréken als er geen verval is, en niet null zijn. Een
      // expliciete null levert bij het uitrollen "One of the specified inputs is
      // invalid" op de container `agents`, en `what-if` ziet dat niet aankomen:
      // daar zijn "null" en "afwezig" niet van elkaar te onderscheiden. Vandaar
      // union() met een leeg object in plaats van `defaultTtl: c.ttl`. Dezelfde
      // constructie staat in infra/portal/portal-rg.bicep, om dezelfde reden.
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

// Dataplane-RBAC van Cosmos. Dit is NIET Microsoft.Authorization/roleAssignments:
// Reader of Contributor via Azure-RBAC geeft geen enkel recht op documenten.
resource agentsCosmosWrite 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = {
  parent: cosmos
  name: guid(cosmos.id, agentsIdentity.id, cosmosDataContributorRoleId)
  properties: {
    roleDefinitionId: resourceId(
      'Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions',
      cosmos.name,
      cosmosDataContributorRoleId
    )
    principalId: agentsIdentity.properties.principalId
    scope: cosmos.id
  }
}

resource portalCosmosRead 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = {
  parent: cosmos
  name: guid(cosmos.id, portalIdentityPrincipalId, cosmosDataReaderRoleId)
  properties: {
    roleDefinitionId: resourceId(
      'Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions',
      cosmos.name,
      cosmosDataReaderRoleId
    )
    principalId: portalIdentityPrincipalId
    scope: cosmos.id
  }
}

// ---------------------------------------------------------------------------
// App Service Plan en de agents
// ---------------------------------------------------------------------------

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'asp-${suffix}'
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: appServicePlanSku
  }
  properties: {
    // Linux. Verplicht bij kind: 'linux' en niet later te wijzigen.
    reserved: true
  }
}

resource planDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'naar-eigen-workspace'
  scope: plan
  properties: {
    workspaceId: workspace.id
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

resource agentApps 'Microsoft.Web/sites@2024-04-01' = [
  for a in agents: {
    name: 'app-${k}-${a.naam}-${omgeving}'
    location: location
    tags: union(tags, { agent: a.naam })
    kind: 'app,linux'
    identity: {
      // System-assigned voor de app zelf, plus de gedeelde agent-identity waaraan de
      // rechten op Cosmos en Key Vault hangen. Zonder de user-assigned zou elke app
      // eigen rolverleningen nodig hebben.
      type: 'SystemAssigned, UserAssigned'
      userAssignedIdentities: {
        '${agentsIdentity.id}': {}
      }
    }
    properties: {
      serverFarmId: plan.id
      reserved: true
      httpsOnly: true
      publicNetworkAccess: 'Enabled'
      siteConfig: {
        linuxFxVersion: 'DOTNETCORE|10.0'
        // Aan: een agent die in slaap valt mist zijn schema.
        alwaysOn: true
        healthCheckPath: a.?healthCheckPath ?? '/healthz'
        http20Enabled: true
        minTlsVersion: '1.2'
        scmMinTlsVersion: '1.2'
        ftpsState: 'Disabled'
        numberOfWorkers: 1
        appSettings: concat(
          [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: omgeving == 'prod' ? 'Production' : 'Staging'
            }
            {
              name: 'WEBSITE_RUN_FROM_PACKAGE'
              value: '1'
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: appInsights.properties.ConnectionString
            }
            {
              // Wijst DefaultAzureCredential naar de gedeelde agent-identity. Laat je
              // dit weg, dan pakt hij de system-assigned identity, die geen rechten heeft.
              name: 'AZURE_CLIENT_ID'
              value: agentsIdentity.properties.clientId
            }
            {
              name: 'KEYVAULT_URI'
              value: keyVault.properties.vaultUri
            }
            // Vanaf hier de configuratie die Soratus.Agents.Telemetry inleest.
            {
              name: 'SORATUS_CUSTOMER__ID'
              value: k
            }
            {
              name: 'SORATUS_TELEMETRY__ENDPOINT'
              value: cosmos.properties.documentEndpoint
            }
            {
              name: 'SORATUS_AGENT__NAME'
              value: a.naam
            }
            {
              name: 'SORATUS_AGENT__DISPLAY_TYPE'
              value: a.?weergavenaam ?? a.naam
            }
            {
              name: 'SORATUS_AGENT__ENVIRONMENT'
              value: omgeving
            }
            {
              name: 'SORATUS_AGENT__TRIGGER'
              value: a.?trigger ?? 'Schedule'
            }
            {
              name: 'SORATUS_AGENT__TIMEZONE'
              value: 'Europe/Amsterdam'
            }
          ],
          // Alleen meesturen als de agent een schema heeft. Een lege cron-expressie
          // is erger dan geen: de bibliotheek rekent er dan een volgende run uit die
          // nooit komt, en het portaal meldt hem als achterstallig.
          empty(a.?schema ?? '')
            ? []
            : [
                {
                  name: 'SORATUS_AGENT__SCHEDULE'
                  value: a.schema
                }
              ]
        )
      }
    }
  }
]

resource agentAppDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = [
  for (a, i) in agents: {
    name: 'naar-eigen-workspace'
    scope: agentApps[i]
    properties: {
      workspaceId: workspace.id
      // Ook hier losse categorieën: Microsoft.Web/sites kent geen categoryGroups.
      logs: [
        { category: 'AppServiceHTTPLogs', enabled: true }
        { category: 'AppServiceConsoleLogs', enabled: true }
        { category: 'AppServiceAppLogs', enabled: true }
        { category: 'AppServicePlatformLogs', enabled: true }
        { category: 'AppServiceAuditLogs', enabled: true }
        { category: 'AppServiceIPSecAuditLogs', enabled: true }
      ]
      metrics: [
        { category: 'AllMetrics', enabled: true }
      ]
    }
  }
]

// ---------------------------------------------------------------------------
// Wat het portaal in deze resource group mag
// ---------------------------------------------------------------------------

// Reader en Cost Management Reader op de hele resource group. Alleen-lezen; het portaal
// wijzigt nooit iets in een klantomgeving.
module portalLeesrecht '../modules/portal-leesrecht.bicep' = {
  name: 'portal-leesrecht-${k}'
  params: {
    portalIdentityPrincipalId: portalIdentityPrincipalId
  }
}

// ---------------------------------------------------------------------------

output cosmosEndpoint string = cosmos.properties.documentEndpoint
output keyVaultUri string = keyVault.properties.vaultUri
output workspaceId string = workspace.id
output appInsightsName string = appInsights.name
output agentsIdentityClientId string = agentsIdentity.properties.clientId
output agentsIdentityPrincipalId string = agentsIdentity.properties.principalId
output agentHostNames array = [for (a, i) in agents: agentApps[i].properties.defaultHostName]
