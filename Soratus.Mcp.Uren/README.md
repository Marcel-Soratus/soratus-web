# `soratus-uren`

MCP-server waarmee uren vanuit Claude Code in het Soratus Agent Portal worden geboekt.

Eén tool:

```
uren_boeken({ klant, maand, uren, categorie, omschrijving })
```

**De regel landt altijd als te fiatteren.** Deze server kan geen gefiatteerde urenregel schrijven —
niet omdat er een waarde op `pending` is vastgezet, maar omdat het verzoek geen statusveld heeft. Zie
`HourBookingRequest`.

## Draaien

```bash
export SORATUS_UREN__PORTAL=https://portal.soratus.com
export SORATUS_UREN__SCOPE=api://soratus-portal/.default
export SORATUS_UREN__CLIENT_ID=<appId van de registratie soratus-uren>
export SORATUS_UREN__TENANT_ID=<tenant-id>

dotnet run --project Soratus.Mcp.Uren -- aanmelden    # eenmalig, device-code
dotnet run --project Soratus.Mcp.Uren -- controleer   # nakijken wat er in het token staat
dotnet run --project Soratus.Mcp.Uren                 # de MCP-server zelf
```

De aanmelding loopt via een eigen public client met device-code en **niet** via
`DefaultAzureCredential` — ook niet als terugvaloptie. Zie `UrenCredentials.cs`.

Zonder portaal, om te valideren zonder te boeken:

```bash
export SORATUS_UREN__PORTAL=https://portal.soratus.com
export SORATUS_UREN__DROOGLOOP=true
dotnet run --project Soratus.Mcp.Uren
```

Alle instellingen, de aansluiting in `.mcp.json`, het endpointcontract dat het portaal moet bouwen en
de afwegingen achter dit ontwerp staan in
[`docs/agent-portal/mcp-uren.md`](../docs/agent-portal/mcp-uren.md).

## Waar de stukken zitten

| Bestand | Wat er in staat |
|---|---|
| `Program.cs` | Host, stdio-transport, logging naar **stderr** (stdout is het JSON-RPC-kanaal) |
| `UrenTools.cs` | De tool zelf, met de beschrijving die een taalmodel leest |
| `HourBookingContract.cs` | De vorm op de draad. Hier staat waarom er geen `status` op het verzoek zit |
| `HourBookingValidation.cs` | Wat wordt geweigerd voordat er iets de deur uit gaat |
| `PortalUrenClient.cs` | De `POST`, en hoe elk antwoord van het portaal wordt gelezen |
| `BookingReport.cs` | De tekst die de aanroeper terugkrijgt |
| `BookingOutcome.cs` | De vijf uitkomsten, als gesloten hiërarchie |
| `BookingState.cs` | Dezelfde uitkomst als machineleesbare stand, zodat `isError: false` niet als "klaar" te lezen is |
| `UrenCredentials.cs` | De aanmelding, en waarom `DefaultAzureCredential` er niet staat |
| `SignInCommand.cs` | `aanmelden` en `controleer` — het tokenpad, buiten de MCP-modus |

## Testen

```bash
dotnet test Soratus.Mcp.Uren.Tests
```

Bouw met `--no-incremental` als je op waarschuwingen let: een incrementele build meldt
`0 Warning(s)` zonder te compileren.
