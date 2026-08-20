// Productiewaarden voor het portaal. Dit is de staat zoals die op 20 augustus 2026
// in Azure staat; wijzig hier niets zonder een what-if te lezen.
using 'main.bicep'

param portalResourceGroupName = 'rg-soratus-prod'
param location = 'westeurope'

// Object-id van id-soratus-portal.
param portalIdentityPrincipalId = 'e48ffac5-672c-4e2b-aab9-340871fb2d62'

// Klant-resource groups waarin het portaal mag lezen. De GUID's zijn die van de
// bestaande, met de hand gemaakte verleningen. Bij een nieuwe klant laat je ze weg.
param customerScopes = [
  {
    resourceGroupName: 'MBV'
    readerAssignmentName: 'b928d8e8-c7dd-4c40-acb2-7e5ab33a335d'
    costReaderAssignmentName: '1569ae42-1d7b-41b5-8427-79eb2689ef6d'
  }
]
