# MCP-server `soratus-uren`

De koppeling waarmee uren vanuit Claude Code in het Soratus Agent Portal worden geboekt. §5 van
`agent-portal-spec.md` schrijft één tool voor, en één vaste regel eromheen:

> **alles wat een agent of koppeling inschiet landt als te fiatteren en telt pas mee in uren en
> facturatie na akkoord van Soratus.**

Dit document legt vast hoe die regel hier een eigenschap van het systeem is en niet een gewoonte,
en wat het portaal daarvoor moet aanbieden.

## De publieke vorm

```
uren_boeken({ klant, maand, uren, categorie, omschrijving })
```

| Parameter | Type | Eis |
|---|---|---|
| `klant` | string | Klantslug in kleine letters, zoals in `/klant/<slug>/…`. Niet de bedrijfsnaam |
| `maand` | string | `jjjj-MM`. Niet in de toekomst, niet vóór 2024 |
| `uren` | number | `> 0`, `≤ 16` per regel, maximaal twee decimalen |
| `categorie` | string | Een categorie die het portaal kent |
| `omschrijving` | string | Eén zin, 5–400 tekens, één regel. **De klant leest dit** |

**De tool heet `uren_boeken` en niet `uren.boeken`.** Dat is de enige afwijking van de letterlijke
notatie in §5, en hij kan niet anders. De Messages-API van Anthropic eist dat een toolnaam op
`^[a-zA-Z0-9_-]{1,64}$` past; Claude Code stuurt de naam met zijn eigen voorvoegsel mee
(`mcp__soratus-uren__uren_boeken`) en een punt levert daar een `400` op — bij **elke** prompt in de
sessie, niet pas bij het aanroepen van deze tool. De MCP-specificatie zelf verbiedt de punt niet,
dus dit is een clientgrens en geen protocolgrens; maar het is de client waar deze server voor
bestaat. `uren.boeken` staat in de titel en de beschrijving, zodat de tool op de naam uit de spec
vindbaar blijft. Er staat een test op de naam (`ToolvormTests`), want zonder die test valt dit pas
op nadat iemand de server heeft aangesloten en zijn hele sessie stuk is.

Er is geen tweede tool. Geen `uren.lezen`, geen `uren.fiatteren`. Fiatteren is een handeling van een
mens in het portaal en hoort niet automatiseerbaar te zijn vanaf de kant die inschiet — dan is de
vaste regel een formaliteit.

## Waar hij naartoe schrijft, en waarom niet naar Cosmos

**Eén `POST` naar het portaal.** Deze server praat niet met Cosmos.

Dat is niet de gemakkelijkste keuze — rechtstreeks naar Cosmos zou lokaal draaien eenvoudiger maken
en het portaal uit het pad halen — maar het is de enige keuze die klopt, om drie redenen die elk op
zichzelf voldoende zijn.

**1. Schrijfrecht op een urenregel ís schrijfrecht op de toegangsdocumenten.** Een urenregel staat
in de container `customers` van de database `platform`, in de partitie van de klant, naast het
klantdocument, het contract en de `access`-documenten. Die vier staan daar samen omdat een klant
aanmaken dan één atomaire batch is (zie `infra.md`, "Waarom één container en niet drie"). Een
dataplane-rol in Cosmos is niet fijner te scopen dan een container. Wie schrijfrecht op urenregels
krijgt, krijgt dus schrijfrecht op de documenten die bepalen wie het portaal in mag — en kan
zichzelf toegang verlenen. Dat is geen lek maar een rechtenverhoging, en hij zou nergens als
storing zichtbaar zijn.

**2. De vaste regel moet aan de schrijfkant staan.** Zou deze server rechtstreeks schrijven, dan is
"landt als te fiatteren" een constante in een programma op iemands laptop. Een gewijzigde build, een
verkeerde merge of een enthousiaste aanpassing zet er `approved` in, en dan telt er iets mee in de
facturatie waar niemand naar heeft gekeken. Achter een endpoint is de regel structureel: het
schrijfpad voor een koppeling heeft geen statusveld, en het portaal is de enige plek waar
`CustomerWriteScope` bestaat.

**3. Eén definitie van de documentvorm.** Rechtstreeks schrijven betekent een tweede kopie van het
`hourEntry`-document in dit project, die in lockstep met de datalaag moet meebewegen. In dit werk is
dat al drie keer gaan schuiven. Achter het endpoint bestaat de vorm één keer, in
`Soratus.Portal/Data`.

**Wat het kost, eerlijk.** Deze server werkt niet zonder een bereikbaar portaal met een API die
bearer-tokens aanneemt. Dat is portaalwerk dat op het moment van schrijven nog niet bestaat (zie
[Wat het portaal moet bouwen](#wat-het-portaal-moet-bouwen)). Tot die tijd is de server te draaien
in **proefdraaimodus**: hij valideert en toont wat hij zou versturen, en zegt in de eerste regel van
elk antwoord dat er niets is geboekt. Een 404 op het endpoint levert geen vage netwerkfout op maar de
melding dat het endpoint er nog niet is, met een verwijzing naar dit document.

## Autorisatie

Deze server draait als lokaal proces in Claude Code op iemands machine. Er is geen `CustomerScope`
buiten het portaal, en er komt er ook geen: die grens hangt aan een Entra-token en aan de
klantenlijst in `platform`, en beide zijn portaaleigendom.

De keten is daarom:

| Laag | Wat het doet | Waar het staat |
|---|---|---|
| Device-code op een eigen public client | Haalt een token op de identiteit van de **persoon** achter Claude Code | deze server |
| App-rol `Operator` | Het portaal weigert alles wat die rol niet heeft | portaal |
| Klantenlijst in `platform` | Het portaal weigert een klant die niet bestaat | portaal |
| `SORATUS_UREN__KLANTEN` | Beperkt deze installatie tot een paar klanten | deze server, **geen veiligheidsgrens** |

**Geen sleutel, geen client secret, geen service-identiteit.** Het token is van de persoon die boekt.
Dat levert drie dingen op: "geboekt door" is een echt antwoord in plaats van "de MCP-server", toegang
intrekken is één handeling in Entra in plaats van een geheim opsporen op onbekend hoeveel machines,
en de autorisatie is letterlijk dezelfde als op het scherm — §2 zegt dat uren boeken operatorwerk is,
en dat wordt hier niet nog een keer, anders, uitgedacht.

**`SORATUS_UREN__KLANTEN` is expliciet geen grens.** Die lijst staat op de machine van de aanroeper en
is door de aanroeper te wijzigen. De melding bij een afwijzing zegt dat ook ("dat is een grens op deze
machine, niet die van het portaal"), want een operator die hier een garantie in leest, leest iets wat
er niet is. Waarvoor hij wél is: hij beperkt de schade van een verkeerd getypte of verkeerd geraden
slug, en van een klantnaam die uit een gelezen bestand of een webpagina in een gesprek is beland. Een
tool die voor elke klant kan boeken is een ander risico dan een die aan één omgeving hangt, en op een
ontwikkelmachine is dat verschil gratis.

### Een eigen public client, en expliciet geen `DefaultAzureCredential`

Het eerste ontwerp gebruikte `DefaultAzureCredential`, dat op een ontwikkelmachine de aanmelding van
de Azure CLI oppakt. Dat werkt alleen als je de CLI-client (`04b07795-8ddb-461a-bbee-02f9e1bf7b46`)
vooraf autoriseert op onze API — en **dan kan elk script dat op die machine met
`DefaultAzureCredential` werkt een token voor het portaal krijgen en uren wegschrijven.**

Het gaat daarbij niet om nieuwe macht: die persoon is al operator en kan het via de browser ook. Het
gaat erom dat de macht dan bereikbaar is voor code die er niets mee te maken heeft, en dat dat niet te
zien is. Een schrijfpad naar facturatiegegevens hoort een expliciete stap te hebben. Hetzelfde patroon
als waarom het portaal geen `AppRoleAssignment.ReadWrite.All` krijgt.

Daarom: een eigen public-client-registratie met device-code-flow, en **`DefaultAzureCredential` staat
er ook niet als terugvaloptie.** Dat laatste is het interessante deel. Zou hij als vangnet blijven
staan, dan heropent hij de gesloten route stil: er verandert niets aan het gedrag tot iemand ooit de
CLI-client alsnog autoriseert, en dán is het gat er zonder dat er een regel code is gewijzigd. Er
staat een test op die in het gecompileerde bestand kijkt of `DefaultAzureCredential` of
`AzureCliCredential` ergens wordt aangehaald (`AanmeldpadTests`).

**Aanmelden is een eigen commando en gebeurt nooit tijdens een tool-aanroep.** Een
device-code-instructie zou op stdout moeten — het JSON-RPC-kanaal — en op stderr ziet de aanroeper hem
niet; dan hangt de tool tot de tijdslimiet zonder dat iemand weet waarop hij wacht. In MCP-modus staat
`DisableAutomaticAuthentication` daarom aan: geen aanmelding levert een leesbare melding op die naar
`soratus-uren aanmelden` verwijst.

```bash
soratus-uren aanmelden     # eenmalig, device-code; bewaart de aanmelding
soratus-uren controleer    # haalt een echt token en zegt wat erin staat, boekt niets
```

`controleer` bestaat omdat het tokenpad anders het enige stuk is dat nooit gedraaid heeft: de
proefdraaimodus slaat het over en het endpoint bestaat nog niet, dus de eerste echte boeking zou
tegelijk de eerste aanmeldpoging zijn. Het drukt `aud`, `appid`, de gebruiker, `scp` en de rollen af
— **nooit het token zelf** — en zegt met zoveel woorden dat het portaal een 403 gaat geven als er geen
`roles`-claim in zit. Dat is precies de valkuil uit `stand-van-zaken.md`: een toewijzing zonder rol
(`appRoleId 00000000-…`) laat je wél binnen maar levert geen rolclaim, en dan staat elk rolbeleid stil
dicht.

De bewaarde aanmelding staat in `%LOCALAPPDATA%\soratus-uren\aanmelding.json`. Dat bestand is **geen
token en geen geheim**: het draagt de gebruikersnaam, de tenant en de account-id, zodat de credential
weet welk account hij in de door het besturingssysteem versleutelde tokencache moet zoeken. De tokens
staan in die cache; `UnsafeAllowUnencryptedStorage` staat uit en hoort uit te blijven — een tokencache
voor dit schrijfpad die onversleuteld op schijf staat, is een credential in rust.

### De Entra-registratie, als blok om uit te voeren

Tenantniveau, dus dit doet Marcel. Zet het abonnement er **niet** bij: dit is Graph, geen ARM.

```bash
export MSYS_NO_PATHCONV=1   # Git Bash op Windows, anders verbouwt MSYS de Graph-paden
```

**1. De public client aanmaken.** Levert de `appId` op die in `SORATUS_UREN__CLIENT_ID` komt.
`--is-fallback-public-client true` is wat device-code mogelijk maakt; zonder die vlag weigert Entra
de flow met een melding over een ontbrekend client secret.

```bash
az ad app create \
  --display-name "soratus-uren" \
  --sign-in-audience AzureADMyOrg \
  --is-fallback-public-client true \
  --public-client-redirect-uris "http://localhost" \
  --query "{appId:appId, objectId:id}"
```

Verwacht: een object met `appId` en `objectId`. Bewaar beide.

**2. De service principal aanmaken.** Zonder dit object kan de tenant geen toestemming vastleggen.

```bash
az ad sp create --id <appId-uit-stap-1> --query "{id:id, appId:appId}"
```

Verwacht: een object met een `id`. **Staat er `already in use` of `already exists`, dan is dat geen
fout** — de principal bestond al, bijvoorbeeld omdat stap 1 eerder is gedraaid. Ga door.

**3. De object-id van de portaal-registratie opzoeken.** Die heb je nodig voor stap 4 en 5, en hij is
niet dezelfde als de service-principal-id uit `infra.md`.

```bash
az ad app list --display-name "soratus-portal" --query "[].{naam:displayName, appId:appId, objectId:id}" -o table
```

Verwacht: één regel. Staan er meer, kies op `appId` en niet op naam.

**4. De scope blootstellen op de portaal-API.** Hier gaat het mis met `az ad app update --set`: die
werkt niet op subeigenschappen van `api`, en inline JSON met quoting is in Git Bash al eerder
stukgelopen. Daarom een Graph `PATCH` met de payload **in een bestand**.

> **Let op: een `PATCH` op `api.oauth2PermissionScopes` vervangt de hele collectie.** Staan er al
> scopes op `soratus-portal`, zet die dan mee in dit bestand. Nakijken met
> `az rest --method GET --uri "https://graph.microsoft.com/v1.0/applications/<objectId>?\$select=api,identifierUris"`.

```bash
SCOPE_ID=$(cat /proc/sys/kernel/random/uuid)   # of: python -c "import uuid;print(uuid.uuid4())"
echo "Scope-id: $SCOPE_ID"                     # bewaar deze; stap 5 en 6 hebben hem nodig

cat > /tmp/uren-scope.json <<JSON
{
  "identifierUris": ["api://soratus-portal"],
  "api": {
    "oauth2PermissionScopes": [
      {
        "id": "$SCOPE_ID",
        "value": "Uren.Boeken",
        "type": "User",
        "isEnabled": true,
        "adminConsentDisplayName": "Uren boeken in het portaal",
        "adminConsentDescription": "Staat de aanroeper toe uren te boeken als te fiatteren regel.",
        "userConsentDisplayName": "Uren boeken",
        "userConsentDescription": "Boekt uren die Soratus daarna moet fiatteren."
      }
    ]
  }
}
JSON

az rest --method PATCH \
  --uri "https://graph.microsoft.com/v1.0/applications/<objectId-uit-stap-3>" \
  --headers "Content-Type=application/json" \
  --body @/tmp/uren-scope.json
```

Verwacht: **geen uitvoer.** Een `PATCH` op Graph geeft `204 No Content` bij succes. Uitvoer betekent
hier dus een fout.

**5. De permissie declareren op `soratus-uren`.** Dit is de stap die je zou overslaan, en dan faalt
het aanmelden met een melding over ontbrekende scopes. `/.default` betekent "alles waarvoor deze
client statisch toestemming heeft" — staat de permissie niet op de client, dan is dat niets.

```bash
az ad app permission add \
  --id <appId-uit-stap-1> \
  --api <appId-van-soratus-portal-uit-stap-3> \
  --api-permissions "$SCOPE_ID=Scope"
```

Verwacht: een waarschuwing dat je nog toestemming moet geven. Dat doet stap 6.

**6. De client vooraf autoriseren op de API.** Hiermee is er geen toestemmingsvraag meer voor de
gebruiker, en de autorisatie is begrensd tot déze client op déze scope — het tegendeel van een
tenantbrede permissie.

> Zelfde waarschuwing als bij stap 4: dit vervangt de hele `preAuthorizedApplications`-collectie.

```bash
cat > /tmp/uren-preauth.json <<JSON
{
  "api": {
    "preAuthorizedApplications": [
      {
        "appId": "<appId-uit-stap-1>",
        "delegatedPermissionIds": ["$SCOPE_ID"]
      }
    ]
  }
}
JSON

az rest --method PATCH \
  --uri "https://graph.microsoft.com/v1.0/applications/<objectId-uit-stap-3>" \
  --headers "Content-Type=application/json" \
  --body @/tmp/uren-preauth.json
```

Verwacht: geen uitvoer.

**7. Controleren, vanaf de machine waar de server komt te draaien.**

```bash
export SORATUS_UREN__PORTAL=https://portal.soratus.com
export SORATUS_UREN__SCOPE=api://soratus-portal/.default
export SORATUS_UREN__CLIENT_ID=<appId-uit-stap-1>
export SORATUS_UREN__TENANT_ID=<tenant-id>

dotnet run --project Soratus.Mcp.Uren -- aanmelden
dotnet run --project Soratus.Mcp.Uren -- controleer
```

Verwacht bij `controleer`: `aud` gelijk aan de portaal-appId of `api://soratus-portal`, en
`Rollen: Operator`. Staat er `Rollen: (geen)`, dan mist de app-roltoewijzing `Operator` op
`soratus-portal` — zie `infra.md`, "De commando's die met de hand blijven". Afsluitcode 0 betekent
bruikbaar, 1 betekent niet.

**Opruimen na afloop:** `rm /tmp/uren-scope.json /tmp/uren-preauth.json`. Er staat geen geheim in,
maar er staat wel de tenantstructuur in.

## Validatie aan de bron

Er gaat niets de deur uit dat later iemand moet opruimen. Dezelfde regel die `tools/Soratus.Seed`
volgt bij een manifestfout: melden en niets schrijven.

Wat hier wordt geweigerd, met de reden:

- **Een bedrijfsnaam in plaats van een slug.** De plausibelste vergissing van een taalmodel: het kent
  "Bakker Techniek B.V." uit het gesprek en niet `bakker` uit de URL. De melding zegt waar de slug
  staat.
- **Een maand die geen maand is** (`augustus`, `08-2026`, `2026-8`, `2026-13`), **een maand in de
  toekomst** (uren die niet zijn gewerkt kunnen niet worden geboekt) en **een jaartal vóór 2024**
  (dat vangt `2016-08` voor `2026-08`, met een voorstel erbij).
- **Nul of negatieve uren.** Een correctie naar beneden is portaalwerk; zie het voorstel in
  `fase-0-afwijkingen.md` over de categorie `Correctie`.
- **Meer dan 16 uur op één regel.** Dezelfde waarde als `HourLimits.MaximumPerEntry` in het portaal,
  en dáár wordt hij afgedwongen; deze controle bestaat alleen om hem uit te leggen vóór er een
  netwerkaanroep aan te pas komt. Meer dan twee werkdagen op één regel is meestal een cijfer te veel;
  klopt het toch, dan gaat het in meerdere regels. Er is met opzet **geen** grens op wat een maand mag
  optellen: een drukke maand is ongebruikelijk maar niet onmogelijk, en een grens die je daar tegenkomt
  wordt omzeild door de uren over twee maanden te verdelen — waarna de administratie verkeerd staat in
  plaats van dat de invoer geweigerd wordt.

  Hier stond eerst 200, gekozen op de gedachte dat een werkmaand ruwweg 168 uur is. Dat is het
  verkeerde vergelijk: deze grens geldt per regel en niet per maand. Het gevolg was geen lek maar een
  band waarin deze server doorliet en het portaal weigerde, met een foutmelding die pas na een
  netwerkronde kwam — precies wat deze controle hoort te voorkomen.
- **Meer dan twee decimalen.** Er wordt **niet** stil afgerond. Stil afronden verandert een bedrag
  zonder dat iemand het heeft gezien.
- **Een lege of meerregelige omschrijving.** De klant leest dit veld op zijn specificatie. Dezelfde
  eisen als aan `msg` in het agentcontract: één zin, geen paden, geen klassenamen, geen andere klant.

**Waarom hier geweigerd en niet afgeknipt, anders dan in het agentcontract.** De bibliotheek knipt
`msg` af omdat de schrijver een achtergrondproces is dat niet kan worden gevraagd het over te doen, en
omdat de overloop naar `extra` kan verhuizen en dus alleen van plaats verandert. Hier zit er een
aanroeper aan de andere kant die het meteen kan herstellen, en een urenregel heeft geen veld om de
rest in te bewaren. Knippen zou hier informatie weggooien in plaats van verplaatsen — precies de
asymmetrie die `fase-0-afwijkingen.md` §14 bij `errorType` beschrijft.

**Alle fouten komen in één keer terug**, niet alleen de eerste. Drie keer heen en weer voor drie
fouten in dezelfde aanroep kost drie keer een mens die wacht.

### De categorielijst staat nul keer in dit project, ook niet opgehaald

Deze server kent de categorieën niet en valideert er niet op. Hij stuurt de string door en geeft de
afwijzing van het portaal ongewijzigd terug. Dat gold ook voor de vraag of een klant bestáát.

Het eerste ontwerp haalde de lijst op via een `GET /api/uren/metadata`, zodat de afwijzing hier al
leesbaar kon zijn. Dat is verlaten: de juiste zorg was "geen kopie", maar de oplossing daarvoor is niet
een tweede plek die de lijst kent — het is nul plekken. Valideren op categorie hoort achter het
endpoint, want dat is de enige plek waar het een *eigenschap* is in plaats van een *afspraak*. Het
portaal is eigenaar via `HourCategories.Bookable` en `HourCategories.IsBookable`.

**De statische lijst in de toolbeschrijving mag wél blijven, en dat is geen inconsistentie.** Het
verschil is wat achterlopen kost:

| | Beschrijving | Validatie |
|---|---|---|
| Lijst loopt achter | een taalmodel gokt een oude naam en krijgt een afwijzing die de goede namen noemt | een geldige boeking wordt geweigerd, of een afgeschafte categorie wordt doorgelaten |
| Herstelt zichzelf | ja, binnen één ronde | nee |
| Kost | één extra ronde | het verkeerde antwoord, met gezag |

Een beschrijving die achterloopt is hinderlijk. Een validatie die achterloopt geeft het verkeerde
antwoord met gezag. Daarom staat `Ontwikkeling, Beheer, Support of Advies` in de parameterbeschrijving
mét de zin "dit is een voorbeeld en geen gezag — het portaal beslist", en staat er nergens in dit
project een `if` op die namen. `ValidatieTests.DeCategorielijstStaatNietInDitProject` legt dat vast:
`Correctie`, `ontwikkeling` en een verzonnen naam gaan alle drie door de lokale toets heen.

Wat hier wél wordt getoetst is de **vorm**: een tekst van meer dan 60 tekens of met een regelovergang
is geen categorie maar een omschrijving in het verkeerde veld, en dat hoeft geen netwerkverzoek te
kosten.

Een echte gedeelde bibliotheek komt er wel, later en om een andere reden: de rekenregels voor uren en
bundel krijgen in fase 4 een tweede lezer. Dat is een aparte stap na fase 3 en niets waar deze server
op wacht.

## Wat de aanroeper terugkrijgt

Claude Code toont dit aan een mens, en die mens besluit op grond daarvan of hij nog iets moet doen.
Daarom staat in elk geslaagd antwoord twee dingen: wat er is vastgelegd, én dat het nog gefiatteerd
moet worden.

```
Vastgelegd als TE FIATTEREN. Nog niet meegeteld.

  klant        bakker
  maand        2026-08
  uren         3,5 u
  categorie    Ontwikkeling
  omschrijving Koppeling met de voorraadservice afgemaakt.
  bron         mcp
  geboekt door Claude Code — Marcel
  status       te fiatteren (pending)
  regel        hourEntry-mcp-01K9

Deze regel telt NIET mee in het maandtotaal en NIET in de facturatie. Het maandtotaal is de som van
de gefiatteerde regels; een operator van Soratus moet deze regel in het portaal eerst fiatteren. Zeg
dat tegen degene voor wie je boekt — de boeking is hiermee niet af.

Fiatteren: https://portal.soratus.com/klant/bakker/uren?maand=2026-08
```

Er zijn vijf uitkomsten, en het verschil tussen de laatste twee is de reden dat het geen `bool` is:

| Uitkomst | Eerste regel | `isError` |
|---|---|---|
| Vastgelegd | `Vastgelegd als TE FIATTEREN. Nog niet meegeteld.` | nee |
| Proefdraai | `PROEFDRAAI — er is NIETS geboekt…` | nee |
| Geweigerd | `NIET geboekt. …er is niets vastgelegd` | ja |
| Onbereikbaar, zeker niets geland | `NIET geboekt. Er is niets vastgelegd.` | ja |
| Onbereikbaar, mogelijk wél geland | `ONBEKEND of er geboekt is.` | ja |

Die laatste is het geval waar de neiging om "mislukt" te zeggen het sterkst is en waar dat de duurste
gok is: bij een tijdslimiet of een `5xx` kan het verzoek zijn aangekomen en alleen het antwoord zijn
weggevallen. Zegt de melding dan "mislukt", dan probeert de aanroeper het opnieuw en staat er twee
keer hetzelfde. De melding zegt daarom dat het onbekend is en verwijst naar het portaal.

**En één uitkomst die er hopelijk nooit is.** Geeft het portaal een `2xx` terug met een status die
niet `pending` is, dan wordt dat **niet** als geslaagde boeking gemeld maar als `LET OP`, met de
melding dat §5 gebroken is en dat de regel moet worden nagekeken vóór er gefactureerd wordt. Dat is
het gevaarlijkste moment: een boeker die denkt dat er nog een mens naar kijkt terwijl het bedrag al
meetelt.

Er staat een test op de woorden in deze meldingen (`MeldingTests`). Grof, en precies grof genoeg: de
fout die voorkomen moet worden is dat iemand de melding later "korter" maakt en de waarschuwing als
eerste sneuvelt, want die is de langste regel.

### De stand staat ook als veld, niet alleen in de tekst

Een aanroeper die alleen naar `isError` kijkt, ziet bij een geslaagde boeking `false` en kan daaruit
concluderen dat het klaar is. Daarom draagt elk toolresultaat naast de tekst een
`structuredContent` met dezelfde mededeling als veld:

```json
{
  "outcome": "booked",
  "recorded": true,
  "approvalStatus": "pending",
  "requiresSoratusApproval": true,
  "countsTowardMonthTotal": false,
  "entryId": "hourEntry-mcp-01K9",
  "customer": "bakker",
  "month": "2026-08",
  "hours": 3.5,
  "reviewUrl": "https://portal.soratus.com/klant/bakker/uren?maand=2026-08",
  "reasons": []
}
```

Drie eigenschappen daarvan zijn opzet:

- **`requiresSoratusApproval` staat op élke uitkomst op `true`**, ook bij een afwijzing. Het zegt iets
  over de tool en niet over deze aanroep; zou het per geval wisselen, dan moet een lezer per geval
  nadenken.
- **`recorded` heeft drie waarden.** `null` betekent "niet vast te stellen", bij een tijdslimiet of een
  `5xx`. Dezelfde reden als bij de drie Entra-toestanden en bij een ontbrekend contractbedrag (§15):
  een waarde die "onbekend" moet kunnen uitdrukken, kan dat niet met een `bool`.
- **`countsTowardMonthTotal` is `null` bij `suspect`**, niet `false`. Als de status niet `pending` is,
  kan de regel al meetellen, en `false` zou daar de gevaarlijkste van de twee onwaarheden zijn.

Tekst en stand komen uit dezelfde `BookingOutcome`, dus ze kunnen niet uiteenlopen. Er is bewust geen
`outputSchema` gedeclareerd: dan bestaat er een tweede beschrijving van deze vorm die met de eerste uit
de pas kan lopen, en de winst — schemavalidatie aan de clientkant — weegt daar niet tegenop bij één
tool.

## Wat het portaal moet bouwen

Twee endpoints. Beide achter `Authorization: Bearer` met de app-rol `Operator`; een klantrol krijgt
`403`.

### `POST /api/uren`

```json
{
  "cid": "bakker",
  "month": "2026-08",
  "hours": 3.5,
  "category": "Ontwikkeling",
  "note": "Koppeling met de voorraadservice afgemaakt."
}
```

**Vijf velden, en niet meer.** Geen `status`, geen `by`, geen `source`, geen `createdAt` en geen
`createdBy`. Die worden door
het portaal gezet:

- `status` altijd `pending` — dat is de vaste regel, en het schrijfpad voor een koppeling hoort er
  geen veld voor te hebben. Niet "op `pending` vastgezet met een test eromheen": afwezig. Dezelfde
  vorm als `CustomerLogLine` zonder `extra` en `CustomerRunRow` zonder `errorType`.
- `by` uit het bearer-token. De enige betrouwbare bron; zou de aanroeper het meesturen, dan kan hij
  op naam van iemand anders boeken.
- `source` altijd `mcp` (§6).
- `createdAt` het moment van vastleggen, canoniek UTC. **Er is geen veld `date`** — dat is §20 van
  `fase-0-afwijkingen.md`, en het is verder gegaan dan "we spreken af hoe we het lezen": een MCP-boeking
  heeft geen werkdatum (§5 geeft de tool geen datumparameter), dus het veld zou een kalenderdag-duplicaat
  van `createdAt` zijn, op een grovere korrel en in een andere tijdzone. Twee velden over hetzelfde
  moment kunnen uiteen gaan lopen. De werkperiode zit in `month`, en de specificatie toont de
  Nederlandse dag uit `createdAt` onder de kop **Geboekt** — niet "Datum", want dat woord belooft de
  werkdatum.
- `createdBy` de koppeling die de regel wegschreef, naast `by` voor de mens die het werk deed. Twee
  velden en niet één: met één veld is "wie heeft dit in de opslag gezet" onbeantwoordbaar, en dat is de
  vraag bij een factuurdiscussie. Voor een MCP-regel is `by` de operator uit het token en `createdBy`
  deze koppeling.

Antwoord `201` met de vastgelegde regel, inclusief `id`, `status`, `source` en `by`. Deze server
controleert `status` en `source` op dat antwoord.

Bij een afwijzing `400` of `422` met `application/problem+json`. De uitbreidingen `categories` en
`customers` worden gelezen en aan de aanroeper doorgegeven — dat is precies de kennis die het portaal
heeft en deze server niet:

```json
{
  "title": "Ongeldige boeking",
  "detail": "De categorie 'Koffie' bestaat niet.",
  "categories": ["Ontwikkeling", "Beheer", "Support", "Advies"]
}
```

**Dat is het enige endpoint.** Er is bewust geen tweede om de categorie- of klantenlijst op te halen;
zie [De categorielijst](#de-categorielijst-staat-nul-keer-in-dit-project-ook-niet-opgehaald). Gebruik
bij een afwijzing op categorie `HourCategories.IsBookable` en zet de geldige waarden in de
`categories`-uitbreiding van het antwoord — dan komen ze bij de aanroeper terecht zonder dat ze ergens
anders zijn overgeschreven.

### Wat de portaalkant nog moet regelen

- **Bearer-tokenvalidatie naast de OIDC-aanmelding** van de Blazor-app
  (`AddMicrosoftIdentityWebApi` naast `AddMicrosoftIdentityWebApp`), met een beleid dat de app-rol
  `Operator` eist.
- **Het endpoint zelf.** `IPortalHoursStore` heeft vandaag geen schrijfmethode voor een koppeling, en
  dat is opzet.
- **Een bewijstype voor een aanroeper die geen mens is** — en dit is de echte ontbrekende schakel, geen
  formaliteit. `CustomerWriteScope` betekent "operator die naar déze klant kijkt". Deze server is dat
  niet: er is geen klant waar hij naar kijkt en geen scherm waar hij op staat. Zolang dat type niet
  bestaat, is de enige manier om deze POST te laten landen hem te laten dóen alsof hij een operator is
  — **en dan kan hij ook fiatteren, en is de vaste regel uit §5 weg.** Wie het endpoint bouwt, bouwt
  dus eerst dat type: een schrijfrecht dat "boeken als te fiatteren" toestaat en "fiatteren" niet.
  Gemeld door `f3-datalaag` bij het bouwen van de datalaag.

## Instellen

```bash
export SORATUS_UREN__PORTAL=https://portal.soratus.com
export SORATUS_UREN__SCOPE=api://soratus-portal/.default
export SORATUS_UREN__CLIENT_ID=<appId van de registratie soratus-uren>
export SORATUS_UREN__TENANT_ID=<tenant-id>
export SORATUS_UREN__KLANTEN=bakker,vandijk     # optioneel, geen veiligheidsgrens
export SORATUS_UREN__TIMEOUT_SECONDEN=30        # optioneel
export SORATUS_UREN__DROOGLOOP=true             # optioneel: valideren, niets boeken

soratus-uren aanmelden      # eenmalig
soratus-uren controleer     # nakijken wat er in het token staat
```

Met `DROOGLOOP=true` zijn `SCOPE`, `CLIENT_ID` en `TENANT_ID` niet nodig — dan wordt er geen token
opgehaald. Dat is de stand om de validatie en de meldingen te bekijken zonder Entra; het is
uitdrukkelijk **niet** de stand waarin het aanmeldpad is beproefd, en daarvoor is `controleer`.

In `.mcp.json` of `~/.claude.json`:

```json
{
  "mcpServers": {
    "soratus-uren": {
      "command": "dotnet",
      "args": ["run", "--project", "D:/SORATUS/Website/Soratus.Mcp.Uren", "--no-build"],
      "env": {
        "SORATUS_UREN__PORTAL": "https://portal.soratus.com",
        "SORATUS_UREN__SCOPE": "api://soratus-portal/.default",
        "SORATUS_UREN__CLIENT_ID": "<appId>",
        "SORATUS_UREN__TENANT_ID": "<tenant-id>"
      }
    }
  }
}
```

Ontbreekt er verplichte configuratie, dan valt de server bij het opstarten om met de sleutel in de
melding op **stderr**. Dat is opzet: bij een MCP-server ziet de aanroeper van een half opgestarte
server alleen dát de tool er niet is, nooit waarom.

**Alle logging gaat naar stderr, ook `Trace`.** Stdout is het JSON-RPC-kanaal van de stdio-transport;
één regel logging erop maakt de stroom onleesbaar en de client verbreekt de verbinding met een
parsefout die niets over de oorzaak zegt.

## Waarom het officiële SDK en niet zelf

`ModelContextProtocol` 2.2.0, onderhouden door Microsoft en de MCP-organisatie, geen preview meer.
Het protocol zelf implementeren zou betekenen dat wij de JSON-RPC-kaders, de initialisatie-handshake
en de versieonderhandeling onderhouden voor één tool met vijf parameters — en dat elke
protocolwijziging aan onze kant landt. Het SDK levert daarbij het JSON-schema van de tool uit de
parametertypes en `[Description]`, dus de beschrijving die een taalmodel leest en de validatie die de
server doet komen uit één bron.

Eén gevolg om te weten: **het SDK gebruikt de C#-parameternaam letterlijk als veldnaam in het
schema.** De parameternamen zijn daarom Nederlands (`klant`, `maand`, `uren`, `categorie`,
`omschrijving`), en dat is geen afwijking van de conventie "Engelse identifiers" — dit zijn geen
identifiers die wij kiezen, maar de publieke vorm die §5 vastlegt. Er staat een test op
(`ToolvormTests`), zodat een refactor die ze hernoemt de vorm uit de spec niet stil verandert.

## Wat er open blijft

### Geen idempotentiesleutel, en dat is een bewuste opening

**Plak dit niet dicht met een sleutel over de inhoud.** Dat is de reparatie die zich aanbiedt en hij
blokkeert het verkeerde: twee blokken van een uur op dezelfde dag, in dezelfde categorie, met dezelfde
omschrijving is legitiem werk. Een sleutel over `cid|month|hours|category|note` weigert dat, en dan
faalt de tool op precies de boeking die klopt. Een sleutel uit het JSON-RPC-verzoek helpt ook niet: een
herhaling van de client heeft een nieuwe id, dus hij herkent niets.

Wat de opening aanvaardbaar maakt is de vaste regel zelf. Een dubbele regel landt op `pending` en kan
dus **niet ongezien op een factuur komen** — iemand moet hem eerst fiatteren, en een dubbele regel in
een lijst die je toch al regel voor regel nakijkt is een correctie voor een mens en geen boekhoudkundige
fout. Dat is zwakker dan een `409`, en het is waar §5 voor bestaat.

De tool is overeenkomstig als **niet-idempotent** aangemerkt (`Idempotent = false`), zodat een client
die op die annotatie afgaat niet stil opnieuw probeert. En de melding bij een tijdslimiet zegt met
zoveel woorden dat een tweede poging een tweede regel oplevert.

Wil je het tóch dicht, dan is de weg een `externalId` die de **aanroeper** meegeeft — en dat is een
zesde parameter, dus een wijziging van §5, en geen implementatiedetail.

### Overig dat open blijft

- **De rekenregels voor uren en bundel** krijgen in fase 4 een tweede lezer en horen dan in een
  gedeelde bibliotheek. Aparte stap na fase 3; deze server wacht er niet op.
- **Een bewijstype voor een niet-menselijke aanroeper** bestaat nog niet. Zie
  [Wat de portaalkant nog moet regelen](#wat-de-portaalkant-nog-moet-regelen) — dit is de zwaarste van
  de openstaande punten, want zonder dat type is de enige manier om deze POST te laten landen hem als
  operator te laten optreden, en dan kan hij fiatteren.
## Wat er wel en niet beproefd is

Eerlijk uitgesplitst, want "er zijn tests" is geen antwoord op "heeft het gelopen".

| Onderdeel | Status |
|---|---|
| Validatie, meldingen, machineleesbare stand, configuratie, toolvorm | 92 tests |
| Het lezen van portaalantwoorden (`201`/`400`/`403`/`404`/`5xx`/onleesbaar/verkeerde status) | tests met een stub-handler |
| De stdio-handshake | handmatig: `initialize` → `tools/list` → `tools/call`, met en zonder fouten in de invoer |
| **Het tokenpad tot Entra** | handmatig gedraaid: `controleer` zonder aanmelding geeft de juiste melding, en `aanmelden` bereikt Entra en geeft de `AADSTS`-code door |
| Het tokenpad met een **echte** registratie | **niet** — de registratie bestaat nog niet |
| Een boeking die daadwerkelijk landt | **niet** — het endpoint bestaat nog niet |

Die vierde regel is er omdat hij bijna ontbrak. De proefdraaimodus slaat het tokenpad volledig over —
`PortalTokenHandler` wordt dan niet eens geregistreerd — dus in de enige stand die zonder portaal kon
draaien, was de aanmelding het enige stuk dat nooit had gelopen. En dat is precies het stuk dat bij de
eerste echte poging faalt. Daarom bestaat `controleer` als eigen commando, en daarom is de melding van
Azure.Identity uitgepakt tot de binnenste uitzondering: die van de buitenste is in de praktijk
`DeviceCodeCredential authentication failed:` met niets erachter, en de bruikbare regel
(`AADSTS90002: Tenant … not found`) zit eronder. Gemeten met een verzonnen client- en tenant-id.

## Wat deze server bewust niet heeft

Voor wie hem later uitbreidt: dit is er niet, en dat is een besluit.

- **Geen `CosmosClient`, geen connection string, geen dataplane-rolverlening.** Niet in code, niet in
  configuratie, en ook niet als afgeschermde optie voor later. Zie
  [Waar hij naartoe schrijft](#waar-hij-naartoe-schrijft-en-waarom-niet-naar-cosmos): een halve
  Cosmos-route die "voor als het endpoint er nog niet is" blijft staan, is precies de route waarlangs
  de rechtenverhoging alsnog binnenkomt.
- **Geen statusveld op het verzoek.** De vaste regel wordt in het portaal afgedwongen, achter het
  endpoint. Wat deze server erbij doet — controleren dat het antwoord op `pending` staat — is een
  tweede slot en geen vervanging: het kan een gebroken regel *melden*, niet *voorkomen*.
- **Geen tool om te fiatteren of af te wijzen.** Dat is een handeling van een mens in het portaal. Zou
  de kant die inschiet ook kunnen fiatteren, dan is de vaste regel een formaliteit.
