// Wat het portaal mag lezen in een klant-resource group.
//
// Twee rollen, beide alleen-lezen. Reader om te zien wat er staat en hoe het ervoor
// staat, Cost Management Reader om de kosten per klant te kunnen tonen. Het portaal
// heeft nergens schrijfrecht op een klantomgeving; dat is bewust en moet zo blijven.
//
// Aparte module omdat het bereik een ándere resource group is dan die van het portaal.
// Cross-resource-group rolverleningen kunnen niet in één resource-group-deployment;
// vandaar dat main.bicep op abonnementsniveau draait.

targetScope = 'resourceGroup'

@description('Principal-id (object-id) van de portal-identity, id-soratus-portal.')
param portalIdentityPrincipalId string

// Namen zijn GUID's. Bij bestaande, met de hand gemaakte verleningen geef je de
// bestaande GUID mee zodat what-if ze herkent. Laat je ze leeg, dan rekent de template
// een stabiele GUID uit op grond van bereik, rol en principal — dat is wat je wilt voor
// een nieuwe klant.
@description('Naam van de Reader-verlening. Leeg = automatisch bepalen.')
param readerAssignmentName string = ''

@description('Naam van de Cost Management Reader-verlening. Leeg = automatisch bepalen.')
param costReaderAssignmentName string = ''

var readerRoleId = 'acdd72a7-3385-48ef-bd42-f606fba81ae7'
var costManagementReaderRoleId = '72fafb9e-0641-4937-9268-a91bfd8191a3'

resource reader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: empty(readerAssignmentName)
    ? guid(resourceGroup().id, portalIdentityPrincipalId, readerRoleId)
    : readerAssignmentName
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', readerRoleId)
    principalId: portalIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource costReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: empty(costReaderAssignmentName)
    ? guid(resourceGroup().id, portalIdentityPrincipalId, costManagementReaderRoleId)
    : costReaderAssignmentName
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', costManagementReaderRoleId)
    principalId: portalIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}
