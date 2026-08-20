# Agent Portal — documentatie

Het Soratus Agent Portal is de ingelogde omgeving naast de marketingsite, op
**portal.soratus.com**. Twee rollen — klant en Soratus-operator — boven de geïsoleerde
Azure-omgevingen van klanten.

Het functioneel ontwerp staat in [`handoff/agent-portal/agent-portal-spec.md`](../../handoff/agent-portal/agent-portal-spec.md),
met een klikbare mockup ernaast. Deze map bevat wat er tijdens het bouwen is besloten.

| Document | Waarvoor |
|---|---|
| [`agent-contract.md`](agent-contract.md) | **Lees dit als je een agent bouwt.** De drie documenttypen, de veldbetekenissen, en wat je zelf moet zetten |
| [`fase-0-afwijkingen.md`](fase-0-afwijkingen.md) | Waar we van de spec afwijken, en waarom |
| [`deploy.md`](deploy.md) | Welke workflow wanneer draait, en hoe je terugrolt |
| [`infra.md`](infra.md) | De Bicep-templates, en hoe je een klantomgeving uitrolt |
| [`stand-van-zaken.md`](stand-van-zaken.md) | Werkdocument: wat er open staat |

## De onderdelen

| Project | Wat |
|---|---|
| `Soratus.Agents.Contracts` | De documenttypen en `AgentStatusCalculator`. Geen dependencies — agents én portaal gebruiken dezelfde types, dus ze kunnen niet uit elkaar lopen |
| `Soratus.Agents.Telemetry` | De bibliotheek die een agent aan het contract laat voldoen. Voor de bouwer is het één regel in `Program.cs` |
| `agents/heartbeat-demo` | Referentie-agent. Doet niets nuttigs, maar bewijst dat de keten werkt |
| `Soratus.Portal` | Het portaal zelf. Static SSR; interactieve eilanden komen in fase 1 |
| `Soratus.Portal.Tests` | Unit- en bUnit-tests, met de nadruk op zichtbaarheid per rol |
| `tools/Soratus.Seed` | **Tijdelijk.** Zet demodata in Cosmos in dezelfde vorm als de bibliotheek. Verdwijnt in fase 1 |

## Twee dingen die het ontwerp dragen

**Een agent publiceert zijn status niet.** Een agent die om is kan niet melden dat hij om is.
Agents publiceren feiten — hartslag, lifecycle, runresultaat — en het portaal leidt status af.
Geen document betekent geen status, dus het scherm kan structureel niet groen staan omdat het
niets weet.

**Autorisatie zit in het typesysteem, niet in een `if`.** Geen methode in de datalaag neemt een
losse klant-id aan; ze nemen allemaal een `CustomerScope`, en die kun je alleen krijgen van
`CustomerScopeResolver` nadat die je recht heeft gecontroleerd. De scope draagt bovendien de
opslaglocatie van díe klant mee. De verkeerde aanroep is niet fout — hij is niet te schrijven.
Een vergeten `if` compileert; een vergeten scope niet.

## Azure

Alles in `rg-soratus-prod`, subscription Pay-As-You-Go-SORATUS.

| Resource | Rol |
|---|---|
| `app-soratus-portal-prod` | het portaal, op het bestaande plan `asp-soratus-prod` |
| `id-soratus-portal` | user-assigned identity; leest telemetrie en kosten, verder niets |
| `kv-soratus-prod` | secrets, via Key Vault-referenties |
| `cosmos-soratus-prod` | telemetrie. Serverless, local auth uit, containers `agents` / `runs` / `logs` |

Entra-registratie `soratus-portal` met de rollen `Operator` en `Klant`. Toegang is
**expliciet**: `appRoleAssignmentRequired` staat aan, dus zonder toegewezen rol komt niemand
binnen — ook een tenantbeheerder niet.

## Lokaal draaien

Het portaal zit achter Entra. Gebruik het **https-profiel** (poort 7221), want alleen die
redirect-URI is geregistreerd:

```bash
dotnet run --project Soratus.Portal --launch-profile https
```

Zonder toegewezen app-rol krijg je een weigering van Entra. Dat is bedoeld gedrag.
