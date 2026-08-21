// ---------------------------------------------------------------------------
// Telemetrie-opslag voor MBV — een aanvulling, niet de blauwdruk.
//
// Waarom dit bestand bestaat naast klant-rg.bicep: MBV is met de hand gebouwd en heeft al
// een App Service-plan, een Key Vault, drie web-apps en twee Cosmos-accounts. De blauwdruk
// maakt die allemaal opnieuw, dus die uitrollen levert een tweede, lege omgeving náást de
// bestaande — dat staat ook met zoveel woorden in mbv.bicepparam. Wat MBV mist is precies
// één ding: een plek waar de agents hun telemetrie kwijt kunnen, in de vorm die het portaal
// leest.
//
// Waarom niet in cosmos-soratus-prod, en waarom ook niet in hun eigen mbv-dbaccount:
//
//   - Agents moeten telemetrie *wegschrijven*. Cosmos-dataplane-rollen zijn per container
//     of per database te scopen en nooit per partitie. In gedeelde containers in ons
//     account kan elke klantagent dus in elke klantpartitie schrijven. Met een database per
//     klant is dat wél te scopen, maar dan staat het schrijfrecht van een klantagent in
//     hetzelfde account als `platform` — de autorisatiebron van het portaal — met alleen een
//     correct gespelde scope-string ertussen. Juist daarop is hier al één uitrol stukgelopen.
//   - Hun bestaande accounts bevatten hun applicatiegegevens in container `mbv`, en op
//     beide staat local auth aan: er kunnen dus accountsleutels bestaan. Een gelekte
//     agentsleutel raakt dan hun eigen gegevens.
//   - En de kosten horen op hun rekening. Fase 4a factureert per resource group; telemetrie
//     in ons account is onze kostenpost en staat onzichtbaar op onze marge.
//
// De workspace hoort erbij en is geen extra. Punt 1 van de afwijkingennotitie verwijt MBV
// dat er geen diagnostic settings staan; een Cosmos-account toevoegen zonder diagnostiek is
// datzelfde gebrek nog een keer maken, met onze naam eronder.
// ---------------------------------------------------------------------------

targetScope = 'resourceGroup'

@description('Klantcode, kleine letters. Bepaalt de resourcenamen.')
param klantcode string = 'mbv'

@description('Weergavenaam van de klant, alleen voor tags.')
param klantnaam string = 'MBV'

param location string = 'westeurope'

@description('prod of test. Zit in elke resourcenaam.')
param omgeving string = 'prod'

@description('''
Object-id van de identiteit waaronder de agents draaien. Voor MBV is dat de system-assigned
identity van wa-mbv-api-001: de drie agents zijn diensten binnen die app en geen eigen
processen, dus er is geen aparte agent-identiteit zoals in de blauwdruk.
''')
param agentsPrincipalId string

@description('Object-id van id-soratus-portal. Krijgt leesrecht op de telemetrie.')
param portalIdentityPrincipalId string

@description('Bewaartermijn van de workspace in dagen.')
param logRetentionInDays int = 30

var suffix = '${toLower(klantcode)}-${omgeving}'

var tags = {
  klant: klantnaam
  omgeving: omgeving
  beheer: 'soratus'
  bron: 'infra/klant/mbv-telemetrie.bicep'
}

// Dezelfde drie containers en dezelfde bewaartermijnen als de blauwdruk. Wijkt dit ooit af,
// dan leest het portaal bij de ene klant iets anders dan bij de andere.
var containers = [
  { name: 'agents', ttl: null } //   de registratie moet blijven staan
  { name: 'runs', ttl: 34560000 } // 400 dagen: ruim een jaar terugkijken
  { name: 'logs', ttl: 2592000 } //   30 dagen: logregels zijn er voor het onderzoek van nu
]

var cosmosDataContributorRoleId = '00000000-0000-0000-0000-000000000002'
var cosmosDataReaderRoleId = '00000000-0000-0000-0000-000000000001'

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
    // Serverless: agenttelemetrie is bursty en laag in volume. Provisioned throughput kost
    // hier een veelvoud voor niets.
    capabilities: [
      { name: 'EnableServerless' }
    ]
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    // Alleen Entra. Geen sleutels, dus ook geen sleutel die in een app-setting belandt —
    // anders dan op mbv-dbaccount en mbv-dbaccount2, waar local auth aan staat.
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
      // `defaultTtl` moet ontbréken als er geen verval is, en niet null zijn. Een expliciete
      // null levert bij het uitrollen "One of the specified inputs is invalid" op de
      // container `agents`, en `what-if` ziet dat niet aankomen: daar zijn "null" en
      // "afwezig" niet van elkaar te onderscheiden. Vandaar union() met een leeg object.
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

// Dataplane-RBAC van Cosmos. Dit is NIET Microsoft.Authorization/roleAssignments: Reader of
// Contributor via Azure-RBAC geeft geen enkel recht op documenten.
resource agentsCosmosWrite 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = {
  parent: cosmos
  name: guid(cosmos.id, agentsPrincipalId, cosmosDataContributorRoleId)
  properties: {
    roleDefinitionId: resourceId(
      'Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions',
      cosmos.name,
      cosmosDataContributorRoleId
    )
    principalId: agentsPrincipalId
    // Op de database en niet op het account. Vandaag staat er alleen telemetrie in, dus het
    // verschil is nul — maar komt er ooit een tweede database bij, dan heeft deze identiteit
    // daar zonder deze regel automatisch recht op. Let op de vorm: dit is een dataplane-pad
    // en geen ARM-resource-id, en een ARM-id levert hier een uitrolfout op die what-if niet
    // aankomen ziet.
    scope: '${cosmos.id}/dbs/${telemetry.name}'
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
    // Op de database en niet op het account. Vandaag staat er alleen telemetrie in, dus het
    // verschil is nul — maar komt er ooit een tweede database bij, dan heeft deze identiteit
    // daar zonder deze regel automatisch recht op. Let op de vorm: dit is een dataplane-pad
    // en geen ARM-resource-id, en een ARM-id levert hier een uitrolfout op die what-if niet
    // aankomen ziet.
    scope: '${cosmos.id}/dbs/${telemetry.name}'
  }
}

@description('Zet dit in het klantdocument van MBV als telemetryEndpoint.')
output cosmosEndpoint string = cosmos.properties.documentEndpoint

@description('Zet dit in het klantdocument van MBV als telemetryDatabase.')
output telemetryDatabase string = telemetry.name

output workspaceId string = workspace.id
