// Een klantomgeving uitrollen: resource group plus alles erin.
//
// Eén resource group per klant, in hetzelfde abonnement. De resource group is de grens:
// wat erin staat hoort bij die klant en niets anders, kosten zijn per group te lezen, en
// het leesrecht van het portaal hangt eraan en niet aan het abonnement.
//
// Uitrollen:
//   az deployment sub what-if \
//     --name klant-mbv --location westeurope \
//     --subscription 501a66d2-de54-4d4f-9f7c-1fbb55bec17f \
//     --template-file infra/klant/main.bicep \
//     --parameters infra/klant/mbv.bicepparam
//
// Lees de what-if. Klopt hij, wissel `what-if` voor `create`.

targetScope = 'subscription'

@description('Klantcode, lowercase, kort. Zit in elke resourcenaam.')
@minLength(2)
@maxLength(12)
param klantcode string

@description('Weergavenaam van de klant.')
param klantnaam string

@description('Regio.')
param location string = 'westeurope'

@allowed(['prod', 'acc', 'test'])
param omgeving string = 'prod'

@allowed(['B1', 'B2', 'B3', 'S1', 'P0v3', 'P1v3'])
param appServicePlanSku string = 'B1'

@description('Principal-id (object-id) van id-soratus-portal.')
param portalIdentityPrincipalId string

@description('De agents die voor deze klant draaien. Zie klant-rg.bicep voor de velden.')
param agents array = []

@minValue(30)
@maxValue(730)
param logRetentionInDays int = 30

var k = toLower(klantcode)

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${k}-${omgeving}'
  location: location
  tags: {
    klant: klantnaam
    klantcode: k
    omgeving: omgeving
    beheer: 'bicep'
  }
}

module klant 'klant-rg.bicep' = {
  name: 'klant-${k}-${omgeving}'
  scope: rg
  params: {
    klantcode: k
    klantnaam: klantnaam
    location: location
    omgeving: omgeving
    appServicePlanSku: appServicePlanSku
    portalIdentityPrincipalId: portalIdentityPrincipalId
    agents: agents
    logRetentionInDays: logRetentionInDays
  }
}

output resourceGroupName string = rg.name
output cosmosEndpoint string = klant.outputs.cosmosEndpoint
output keyVaultUri string = klant.outputs.keyVaultUri
output agentsIdentityClientId string = klant.outputs.agentsIdentityClientId
output agentHostNames array = klant.outputs.agentHostNames
