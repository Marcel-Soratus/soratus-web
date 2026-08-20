// Het agentportaal, in zijn geheel.
//
// Draait op abonnementsniveau omdat het portaal leesrecht nodig heeft in resource groups
// van klanten — en dat kan niet vanuit een resource-group-deployment.
//
// Deze template maakt GEEN resource groups aan. rg-soratus-prod bestaat en draagt ook de
// marketingsite; laat het aanmaken en verwijderen daarvan met de hand gebeuren.
//
//   az deployment sub what-if \
//     --name portal --location westeurope \
//     --subscription 501a66d2-de54-4d4f-9f7c-1fbb55bec17f \
//     --template-file infra/portal/main.bicep \
//     --parameters infra/portal/main.bicepparam
//
// Wissel `what-if` voor `create` om echt uit te rollen. Lees de what-if eerst.

targetScope = 'subscription'

@description('Resource group van het portaal. Bestaat al en wordt hier niet aangemaakt.')
param portalResourceGroupName string = 'rg-soratus-prod'

@description('Regio.')
param location string = 'westeurope'

@description('Principal-id van id-soratus-portal. Zie de comment in portal-rg.bicep.')
param portalIdentityPrincipalId string = 'e48ffac5-672c-4e2b-aab9-340871fb2d62'

@description('''
Resource groups van klanten waarin het portaal mag lezen. Per klant de naam, en de
GUID's van de bestaande rolverleningen als die met de hand zijn gemaakt (leeg laten
voor een nieuwe klant, dan rekent de template ze uit).
''')
param customerScopes array = [
  {
    resourceGroupName: 'MBV'
    readerAssignmentName: 'b928d8e8-c7dd-4c40-acb2-7e5ab33a335d'
    costReaderAssignmentName: '1569ae42-1d7b-41b5-8427-79eb2689ef6d'
  }
]

module portal 'portal-rg.bicep' = {
  name: 'portal-rg'
  scope: resourceGroup(portalResourceGroupName)
  params: {
    location: location
    portalIdentityPrincipalId: portalIdentityPrincipalId
  }
}

module customerGrants '../modules/portal-leesrecht.bicep' = [
  for c in customerScopes: {
    name: 'grants-${toLower(c.resourceGroupName)}'
    scope: resourceGroup(c.resourceGroupName)
    params: {
      portalIdentityPrincipalId: portalIdentityPrincipalId
      readerAssignmentName: c.?readerAssignmentName ?? ''
      costReaderAssignmentName: c.?costReaderAssignmentName ?? ''
    }
  }
]

output portalIdentityPrincipalId string = portal.outputs.portalIdentityPrincipalId
output portalHostName string = portal.outputs.portalDefaultHostName
output cosmosEndpoint string = portal.outputs.cosmosEndpoint
