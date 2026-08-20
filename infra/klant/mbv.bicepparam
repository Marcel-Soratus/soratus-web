// Voorbeeld: MBV in de nieuwe vorm.
//
// NIET UITGEROLD. MBV staat nu in resource group `MBV`, met de hand gemaakt. Dit bestand
// laat zien hoe die omgeving eruit zou zien als hij hier vandaan kwam, en dient als
// voorbeeld voor de volgende klant. Uitrollen maakt een tweede, lege omgeving naast de
// bestaande — de verhuizing zelf is werk met een gegevensmigratie erin, geen deploy.
using 'main.bicep'

param klantcode = 'mbv'
param klantnaam = 'MBV'
param location = 'westeurope'
param omgeving = 'prod'

// B1 volstaat voor drie agents. Er is geen always-on-limiet op Basic en geen reden voor
// Premium zolang er niets aan hangt dat schaalt.
param appServicePlanSku = 'B1'

// Object-id van id-soratus-portal in rg-soratus-prod. Hiermee krijgt het portaal
// leesrecht op deze omgeving: Reader, Cost Management Reader en Cosmos Data Reader.
param portalIdentityPrincipalId = 'e48ffac5-672c-4e2b-aab9-340871fb2d62'

param agents = [
  {
    naam: 'declaraties'
    weergavenaam: 'Declaratiecontrole'
    schema: '0 7 * * *'
    trigger: 'Schedule'
  }
  {
    naam: 'urenherinnering'
    weergavenaam: 'Urenherinnering'
    schema: '0 16 * * 5'
    trigger: 'Schedule'
  }
  {
    // Geen schema: draait op een webhook. Laat `schema` dan weg, anders rekent de
    // bibliotheek een volgende run uit die nooit komt.
    naam: 'inbox'
    weergavenaam: 'Postvakverwerking'
    trigger: 'Event'
  }
]

param logRetentionInDays = 30
