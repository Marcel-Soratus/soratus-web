# Fase 4 — haalbaarheid

Onderzoek, geen bouwopdracht. Vraag: kan fase 4 (kosten en facturatie, spec §3.7 en §7)
gebouwd worden, en wat is daarvoor nodig?

**Antwoord: niet als één fase.** Het kostendeel en de mail kunnen nu; het SnelStart-deel niet.
Er is vandaag geen SnelStart-koppeling voor de administratie van Soratus, en die is niet in een
middag te regelen: het vraagt een aanvraag, een abonnementsvoorwaarde, €250 en een
certificeringsperiode van ongeveer twaalf dagen bij een leverancier die zelf schrijft dat hij
terughoudend is met nieuwe koppelingen.

Het goede nieuws staat in [§4](#4-wat-concept-in-snelstart-werkelijk-is): de veiligheidsaanname
van §3.7 — de agent maakt alleen een concept, versturen doet Soratus zelf — is in SnelStart geen
afspraak maar een **eigenschap van de API**. Er is een route waarop het onmogelijk is om per
ongeluk iets definitief in een boekhouding te zetten. Dat is beter dan gehoopt.

Aanbeveling: knip fase 4 in **4a** (kosten, opslag, uren boven bundel, maandoverzicht mailen —
nu te bouwen) en **4b** (SnelStart — pas beginnen als de blokkades hieronder weg zijn). Voorstel
in [§9](#9-ontwerpvoorstel--4a-nu-4b-later).

---

## 1. Wat blokkeert — handelingen voor Marcel

Dit zijn geen implementatiedetails. Zolang deze niet gedaan zijn, is 4b niet te bouwen en 4a maar
half.

### B1 · De SnelStart-koppeling bestaat niet, en de doorlooptijd is weken

Er is in deze repo, in `kv-soratus-prod`, in de app-settings van het portaal en in de app-settings
van de marketingsite **geen enkele verwijzing naar SnelStart**. Er is geen sleutel, geen
abonnementsgegeven, geen configuratie. Wat er wél is, is leeswerk voor een klant — zie
[B4](#b4--ik-kan-niet-zien-wat-er-in-de-key-vaults-staat-ontbrekend-recht).

Wat de aanvraag volgens de openbare documentatie van SnelStart kost:

| | |
|---|---|
| Administratie | Moet **online** zijn. Een lokale administratie kan niet koppelen |
| Abonnement | De tegel **Maatwerk** (de route voor eigen bouw) werkt met **inZicht** of **inControle**. Tijdens een gratis proefperiode kan er géén koppeling worden gemaakt |
| Eenmalig | **€ 250 ex btw** per afgegeven permanente sleutel |
| Procedure | Aanmelden → goedkeuring → developertoegang + ontwikkelsleutel → bouwen → certificering aanvragen → **± 12 dagen** certificeringsperiode waarin SnelStart meekijkt |
| Houding leverancier | SnelStart schrijft zelf dat het terughoudend is met nieuwe productiekoppelingen, elke aanvraag individueel beoordeelt, en dat aanvragen kunnen worden afgewezen |
| Contact | `partner@snelstart.nl`, aanvraagformulier via `snelstart.nl/api` |

**Handeling:** de aanvraag doen. Dit is de kritieke pad-stap en hij loopt buiten ons om. Doe hem
los van, en vóór, elke bouwbeslissing.

### B2 · Drie vragen aan SnelStart die het ontwerp bepalen

Deze drie zijn niet uit openbare documentatie te halen en veranderen de bouw als het antwoord
tegenvalt. Stel ze bij de aanvraag, niet erna.

1. **Vereist de scope `orders:*` een inHandel-abonnement, of komt hij mee met inZicht/inControle?**
   Dit is de belangrijkste vraag van dit hele rapport. `orders:*` is de scope van de
   **conceptroute** (§4). Komt die niet mee, dan bestaat de veilige route voor ons niet en valt
   fase 4b terug op een route die direct boekt — en dan bouwen we hem niet.
2. **Welke scopes krijgt een maatwerksleutel, en kan die tot `orders:read` + `orders:write`
   worden beperkt?** De API kent twintig scopes netjes gesplitst per domein, maar ik heb nergens
   gedocumenteerd gevonden dat de klant of de ontwikkelaar ze per koppeling zelf kiest, en het
   token-endpoint kent geen `scope`-parameter — je kunt dus bij het inwisselen niet
   down-scopen. Als de maatwerktegel ruim is, is één gelekt sleutelpaar volledige toegang tot de
   administratie: alle relaties met IBAN en incassomachtigingen, alle boekingen, bankafschriften,
   btw-aangiftes, en het recht om te boeken en te verwijderen.
3. **Wat zijn de echte rate limits?** Niet in de spec (nul treffers op `429`, `throttl`,
   `Retry-After`, `X-RateLimit`). De cijfers die rondgaan (500.000 calls per week; 500/min en
   5.000/uur voor productie) komen van derden en één ervan is uit 2020. De autoritatieve bron is
   de `/certificering`-pagina achter de portallogin.

### B3 · Twee secrets die een mens moet zetten

De API vraagt twee onafhankelijke geheimen die bij élke aanroep samen meegaan:

| Wat | Waarvoor | Blast radius bij lek |
|---|---|---|
| **Subscription key** (`Ocp-Apim-Subscription-Key`) | identificeert Soratus als ontwikkelaar | gedeeld over al onze koppelingen. Dit is de gevoeligste van de twee |
| **Koppelsleutel** (clientkey) | identificeert één administratie | begrensd tot díe administratie, tot hij wordt ingetrokken |

Ze reizen samen in elke request, dus in de praktijk lekken ze samen. De koppelsleutel wordt door
een mens uit SnelStart Web gekopieerd (Koppelingen → Maatwerk → koppeling instellen → akkoord →
sleutel kopiëren) en **moet na het terugzetten van een back-up van de administratie opnieuw
worden geactiveerd** — de sleutel is niet overdraagbaar. Dat is een operationele valkuil die een
stille storing oplevert: de agent krijgt een 401 en niemand weet waarom.

**Handeling:** twee secrets in `kv-soratus-prod`. Voorgestelde namen, zodat de code er nu al
naar kan wijzen zonder dat er iets bestaat:

```
snelstart-subscription-key
snelstart-clientkey-soratus
```

De tweede heeft de administratie in de naam, want er komt er één per administratie. Zet ze niet
in een app-setting: dit wordt de **eerste Key Vault-referentie** van het portaal in productie, en
dat pad is nog nooit gelopen. `keyVaultReferenceIdentity` en `Key Vault Secrets User` staan
inmiddels goed (`infra.md`), maar "staat goed" en "is bewezen" zijn twee dingen. Bewijs dat pad
met een onschuldig secret vóór er een boekhoudsleutel in gaat.

### B4 · Ik kan niet zien wat er in de Key Vaults staat (ontbrekend recht)

Zoals gevraagd: geen waarden, alleen namen. Ik heb geen namen, want ik kan de lijst niet lezen.

```
az keyvault secret list --vault-name kv-soratus-prod
az keyvault secret list --vault-name kv-mbv-keyvault-001
→ (Forbidden) ForbiddenByRbac
  Action: 'Microsoft.KeyVault/vaults/secrets/readMetadata/action'
  Assignment: (not found)
```

Beide vaults staan op `enableRbacAuthorization: true`. Marcel is `Owner` én
`User Access Administrator` op het abonnement, en **dat geeft geen enkel data-plane recht op een
secret** — dat is precies de scheiding die in `infra.md` bij Cosmos ook staat, hier bij Key Vault.
Ik meld dit als ontbrekend recht en niet als "er staat niets".

**Handeling, als je wilt dat ik dit kan controleren:** ken `Key Vault Reader` toe (alleen
`secrets/readMetadata` — namen wél, waarden níet). `Key Vault Secrets User` kan ook, maar die
geeft er `getSecret` bij en dat is meer dan nodig om een lijst met namen te lezen.

Wie nu al kan kijken zonder iets te wijzigen: **Dennis heeft `Key Vault Secrets Officer` op
`kv-mbv-keyvault-001`.**

**En de nuance die belangrijker is dan het recht.** In de resource group `MBV` staat een AI
Foundry-project `mbv-foundry/proj-chat-snelstart`, aangemaakt op 21 juli 2026, en de beide
SnelStart-cases op de site (`snelstart-jaarverslag-agent`, `snelstart-declaraties-matchen`) zijn
allebei **lezend**: "leest de administratie rechtstreeks uit", "de betalingen komen rechtstreeks
uit SnelStart". Dus:

- Er is aantoonbare **lees**ervaring met SnelStart. Er is geen aanwijzing voor **schrijf**ervaring.
- Ligt er een koppelsleutel in `kv-mbv-keyvault-001`, dan is dat de sleutel van **de administratie
  van MBV**. Fase 4 factureert onze klanten in **onze eigen** administratie. Dat is een andere
  administratie, dus een andere koppelsleutel, en die bestaat niet. Een gevonden sleutel bij MBV
  is dus geen goed nieuws voor fase 4 — hij is niet de sleutel die we nodig hebben, en hem
  hiervoor gebruiken zou betekenen dat we onze facturen in de boekhouding van een klant zetten.

### B5 · Kostenrecht: het portaal kan vandaag maar één klant zien

Gemeten. `id-soratus-portal` heeft in het abonnement Pay-As-You-Go-SORATUS precies drie
verleningen:

| Rol | Scope |
|---|---|
| `Key Vault Secrets User` | `kv-soratus-prod` |
| `Cost Management Reader` | resource group `MBV` |
| `Reader` | resource group `MBV` |

Dus: **geen `Cost Management Reader` op abonnementsniveau.** De aanpak "één dagelijkse
subscription-scope query met grouping op resource group" werkt technisch (bewezen, zie §2), maar
het portaal mag hem vandaag niet uitvoeren. Dat is een rolverlening en dus een besluit.

En er zit een prijs aan. Zo'n query levert alles op wat in dat abonnement staat:

```
allsprinklerservice          € 36,38
defaultresourcegroup-weu     €  1,31
mbv                          € 36,39
packcompany                  € 58,24
rg-derdehelft-marcel-dev     €  0,30
rg-soratus-prod              € 36,79
```

`Cost Management Reader` op het abonnement geeft het portaal dus de kosten van álles, ook van
wat geen klantomgeving is. Er is geen tussenvorm: Cost Management kent geen "deze resource groups
wel, die niet".

Twee wegen, en dit is de keuze:

| | Recht | Kosten |
|---|---|---|
| **Per klant-RG** (nu) | wat er al is, uitgerold door `infra/klant/` | één query per klant per dag. Bij zeven klanten loopt dat tegen de throttling van §2 aan en moet het gespreid worden |
| **Subscription-scope** | één nieuwe verlening per abonnement | één query per dag voor alle klanten. Maar het portaal ziet ook kosten die niet van klanten zijn |

**En let op iets dat het ontwerp raakt: klantomgevingen staan in meer dan één abonnement.** Naast
Pay-As-You-Go-SORATUS is er een abonnement `Klanten` (`66ad59e7-…`) met daarin `PackCompany`.
"Één query per dag" is dus in werkelijkheid "één query per abonnement per dag", en de
rolverlening moet in elk abonnement staan waar een klant in leeft. Dat is nu al twee.

### B6 · Een open §9-besluit dat het scherm bepaalt

Spec §9 laat nog open: *mag de klant het Azure-verbruik per dienst zien, of alleen het door te
belasten totaal?* Nu staat er "alleen totaal" en zegt §2 dat de uitsplitsing operator-only is.
`stand-van-zaken.md` noemt dit al als openstaand. Dit blokkeert 4a niet — bouw het operator-only
zoals §2 zegt — maar het besluit hoort te vallen vóór het scherm er staat, want het omdraaien is
later een zichtbaarheidswijziging en geen CSS-aanpassing.

---

## 2. Wat er wél kan — Azure Cost Management, opnieuw gemeten

De eerdere conclusie was: leesbaar op resource-group-scope, minder dan 24 uur vertraging, maar
het throttelt hard met 429 zonder `Retry-After`. Dat is grotendeels bevestigd en op twee punten
**bijgesteld**. Alle getallen hieronder zijn van 20 augustus 2026, `api-version=2023-11-01`,
scope `resourceGroups/MBV`.

### Wat klopt

**Lezen op RG-scope werkt.** `POST .../providers/Microsoft.CostManagement/query`, grouping op
`ServiceName`, MonthToDate, in 3–5 seconden:

```
Azure App Service   € 36,3616
Azure Cosmos DB     €  0,0296
Bandwidth           €  0,0000
Key Vault           €  0,0002
Microsoft Entra     €  0,0000
```

**De data is verser dan 24 uur.** Met `granularity: Daily` stond om 16:17 UTC op de 20e het
volgende: dag 1 t/m 19 elk € 1,878 (een stabiele, complete dag), en dag 20 € 0,704 — ongeveer
37% van een dag, dus de boeking loopt op dat moment tot circa 09:00 UTC. De vertraging is dus
eerder zeven tot tien uur dan een etmaal.

**Caching is verplicht, niet netjes.** Zie hieronder.

### Bijstelling 1 — er is wél een wachthint, maar niet altijd, en niet onder de naam `Retry-After`

Bij tien snelle aanroepen achter elkaar kwamen tien 429's, elk met deze twee headers:

```
x-ms-ratelimit-microsoft.costmanagement-entity-retry-after: 26 … 17
x-ms-ratelimit-microsoft.costmanagement-clienttype-retry-after: 14 … 6
```

Ze tellen af, dus het is een echte hint. Een gewone `Retry-After` is er nooit. Maar — en dit is
de reden dat je er niet blind op mag leunen — bij een volgende meting op laag tempo kwamen zes
429's waarvan **vier zonder enige hintheader**, en één met de waarde `1`, wat aantoonbaar te kort
was. Conclusie: lees de header als hij er is, maar bouw een eigen backoff die niet omvalt als hij
ontbreekt.

Nog een header die vals gerustheid geeft:
`x-ms-ratelimit-remaining-subscription-resource-requests` bleef de hele meting op **1099** staan,
óók op de 429's. Dat is de ARM-teller, niet de Cost Management-teller. Wie daarop monitort ziet
nooit dat hij tegen de limiet aanloopt.

### Bijstelling 2 — het budget is veel kleiner dan "hard throttelen" suggereert

Na vier minuten stilte, één aanroep per elf seconden, tien keer:

```
16:24:40 #1  200      16:25:39 #6  429  (hint 1)
16:24:52 #2  200      16:25:49 #7  429  (geen hint)
16:25:03 #3  429      16:26:01 #8  429  (geen hint)
16:25:15 #4  200      16:26:12 #9  429  (geen hint)
16:25:27 #5  429      16:26:23 #10 200
```

**Vijf van tien mislukt op één aanroep per elf seconden.** Dat is geen piekprobleem dat je met
spreiden oplost; dat is een budget waarin één query per klant per dag al krap is als je ze niet
uit elkaar trekt.

En het budget is **niet per scope**. Een query tegen een héél ander abonnement kreeg een 429
terwijl de meting hierboven tegen Pay-As-You-Go liep, en in een controlemeting (twee minuten
stilte, dan afwisselend twee abonnementen met twee seconden ertussen) viel de vijfde aanroep om.
De naam van de header — `clienttype-retry-after` — past daarbij: de emmer hangt aan de aanroeper,
niet aan de resource. Meer abonnementen erbij maakt het dus niet ruimer.

### Nieuw en het gevaarlijkst — een 404 die "probeer opnieuw" betekent

Twee keer in ongeveer vijfentwintig aanroepen, op een request die er vlak ervoor en vlak erna
gewoon 200 op gaf:

```
HTTP 404
{"error":{"code":"NotFound","message":"GtmDimensionDataProvider.GetAzureSubscriptionsById
returns null or empty list for id: 501a66d2-… (Request ID: …)"}}
```

Dit is een tijdelijke backendfout die zich voordoet als een 404. Een normale client behandelt 404
als "bestaat niet" of "geen gegevens" en rendert € 0,00. **Op een factuur is € 0,00 geen lege
waarde maar een verkeerd bedrag.** Dit is de belangrijkste nieuwe bevinding van dit onderzoek en
hij heeft een harde ontwerpconsequentie: zie §9, regel "geen bedrag is niet nul".

### Wat de spec hier verkeerd aanneemt

§3.7 noemt de diensten met naam: Container Apps, Azure OpenAI, Storage, Log Analytics, Key Vault.
De werkelijke `ServiceName`-waarden uit de API zijn Azure's eigen namen, en die zijn anders
("Azure App Service", "Azure Cosmos DB", "Bandwidth", "Microsoft Entra"). Ook staat er in de echte
uitvoer een dienst met € 0,00 waar de spec hem niet verwacht. De uitsplitsing moet dus **komen uit
wat de API teruggeeft** en niet uit een vaste lijst in de code; een vaste lijst laat op de dag dat
er een dienst bijkomt stilletjes geld weg.

Terzijde: de bedragen komen in EUR terug en zijn exclusief btw. `ActualCost` is wat we willen —
`AmortizedCost` gaat pas iets betekenen als er reserveringen worden gekocht, en die zijn er niet.

---

## 3. Wat er wél kan — de mail loopt vandaag al

Dit is de makkelijkste helft van fase 4, en hij is bijna klaar. §5 van de spec noemt SendGrid;
de werkelijkheid is Azure Communication Services, en die werkt.

**Er staat werkende code in deze repo.** `Soratus.Web/Services/LeadSink.cs` verstuurt met
`Azure.Communication.Email` (`EmailClient`, `EmailMessage`, `WaitUntil.Started`) de
terugbelaanvragen van de marketingsite. Dat pad is bewezen in productie.

**De resources staan er en het domein is geverifieerd.**

| | |
|---|---|
| `acs-soratus-prod` | `rg-soratus-prod`, dataLocation `europe`, gekoppeld aan het domein hieronder |
| `acs-email-soratus-prod/soratus.com` | `domainManagement: CustomerManaged` |
| Verificatie | `Domain` **Verified**, `SPF` **Verified**, `DKIM` **Verified**, `DKIM2` **Verified** |
| DMARC | ACS meldt `NotStarted`, maar in de DNS-zone `soratus.com` staat wél `_dmarc`: `v=DMARC1; p=none; rua=mailto:hallo@soratus.com; …`. ACS heeft hem alleen nooit gevalideerd |
| Afzender | **één**: `DoNotReply@soratus.com` |

**Wat er nog nodig is, en het is weinig:**

1. **Afzenderkeuze.** Er is nu alleen `DoNotReply`. Een maandoverzicht van
   `DoNotReply@soratus.com` is verdedigbaar maar niet vriendelijk, en een extra afzenderadres
   toevoegen kan pas ná een quotaverhoging — de knop staat in de portal uit zolang het sendlimiet
   op de standaardwaarde staat. Wil je `facturatie@soratus.com`, dan is dat een supportverzoek.
2. **Quota.** Standaard 30 per minuut en 100 per uur. Voor één maandoverzicht per klant is dat
   ruim voldoende; dit is geen blokkade.
3. **Authenticatie, en hier is een keuze te maken.** De site gebruikt een connection string, en
   die staat als **platte app-setting** `AzureEmail__ConnectionString` op `app-soratus-prod` — geen
   Key Vault-referentie. Voor het portaal is er een beter alternatief: managed identity. Gemeten
   in de provider zijn dit de operaties, en let op de laatste kolom:

   ```
   Microsoft.Communication/CommunicationServices/Read              dataAction=False
   Microsoft.Communication/CommunicationServices/Write             dataAction=False
   Microsoft.Communication/CommunicationServices/ListKeys/action   dataAction=False
   Microsoft.Communication/CommunicationServices/RegenerateKey/action
   ```

   Mail versturen met een identity vraagt `Read` + `Write`, en dat zijn **control-plane**-acties.
   Microsofts eigen voorbeeld zegt: `Contributor`, of een custom role met precies die twee. Neem
   de custom role. `Contributor` geeft er `ListKeys` bij — dus het recht om de connection string
   op te halen — en `Delete`. Dan heb je een identity die machtiger is dan het geheim dat je
   ermee wilde vermijden. De ingebouwde rol `Communication and Email Service Owner` is
   beheerrecht en niet wat je zoekt.

Dit deel van fase 4 is dus geen onderzoeksvraag meer maar bouwwerk: een custom role, een
rolverlening, en de mailtekst.

---

## 4. Wat "concept" in SnelStart werkelijk is

Dit was de eerste weegvraag: is "concept" in SnelStart een echte, veilige toestand, of betekent
het daar iets anders dan wij aannemen? Ik heb hiervoor niet op een samenvatting vertrouwd maar de
officiële OpenAPI-spec zelf gedownload en nagelopen (`SnelStart B2B-Api v2`, 63 paths, server
`https://b2bapi.snelstart.nl/v2`).

### Er is geen POST op verkoopfacturen

```
/verkoopfacturen          → GET
/verkoopfacturen/{id}     → GET
/verkoopfacturen/{id}/ubl → GET
```

Een verkoopfactuur is in SnelStart geen ding dat je aanmaakt. Hij ontstáát. Er zijn twee routes
die tot een factuur leiden, en het verschil ertussen is het hele antwoord.

### Route A — `POST /verkoopboekingen`: onherroepelijk

Scope `boekhouden:write`. Verplichte velden `factuurnummer`, `klant`, `boekingsregels`. Er is
**geen status-, concept- of boekingsstatusveld**. De doorslaggevende regel staat in de
omschrijving van `factuurdatum`:

> "De datum van de factuur, dit is ook de datum waarop de verkoopboeking wordt geboekt."

Een verkoopboeking is dus per definitie een geboekte mutatie in de boekhouding op het moment van
de POST. Het veld `markering` is geen workflow — de spec noemt het "verdient speciale aandacht, in
SnelStart wordt dit visueel benadrukt". Een vlaggetje.

**Deze route is voor §3.7 de verkeerde.**

### Route B — `POST /verkooporders`: een echt conceptstadium, en de laatste stap kan de API niet zetten

Scope `orders:write`. Verplicht alleen `relatie` en `datum`. Twee statusvelden, en dit is de
letterlijke tekst uit de spec bij `procesStatus`:

> "DocumentStatus van de order. Als deze niet is opgegeven wordt de default waarde Order gebruikt.
> **Contantbon en Factuur zijn niet beschikbaar**"

De enum is `Order | Offerte | Bevestiging | Werkbon | Pakbon | Afhaalbon | Contantbon | Factuur`.
Dat is precies de ladder van concept naar definitief. En `Factuur` — de eindtoestand — is via de
API verboden. Niet op één plek, maar op **alle drie** de plekken waar het veld voorkomt: bij
`POST /verkooporders`, bij `POST /offertes`, en bij `PUT /verkooporders/{id}/ProcesStatus`.

Dat betekent iets sterker dan "wij zullen het niet doen":

> **Er bestaat geen codepad — ook niet bij een bug, ook niet bij een verkeerd samengestelde
> request — waarmee onze agent een factuur definitief maakt of verstuurt.** De mens in SnelStart
> Web zet de laatste stap en kiest daarbij de verzendwijze.

Dat is de veiligheidseigenschap van §3.7, niet als afspraak maar als eigenschap van het systeem.
En `DELETE /verkooporders/{id}` bestaat, dus een per ongeluk aangemaakt concept is schoon op te
ruimen — bij een verkoopboeking heb je dan al geboekt.

Aanvullend: `POST /offertes` bestaat ook, met dezelfde restrictie. Nog verder van de boekhouding
af. Voor een maandfactuur is een order de juistere vorm.

| | Route A `verkoopboekingen` | Route B `verkooporders` |
|---|---|---|
| Scope | `boekhouden:write` | `orders:write` |
| Resultaat | geboekte mutatie | order met `procesStatus: Order` |
| Concept mogelijk | nee | **ja** |
| Laatste stap door | niemand, is al geboekt | **een mens in SnelStart Web** |
| Terugdraaien | `DELETE`, maar je hebt geboekt | `DELETE` van een concept |
| Terugzoeken op eigen kenmerk | **geen collectie-GET, geen `$filter`** | `GET /verkooporders?$filter=…` |

Die laatste rij is voor idempotentie beslissend en staat in §6.

### Wat hier nog onbekend is

- Of `orders:*` een inHandel-abonnement vereist. Zie B2, vraag 1. Als dit tegenvalt, valt route B
  weg en daarmee fase 4b.
- Wat er in de UI precies gebeurt als een mens een via-de-API aangemaakte order factureert. Dat is
  gedrag, niet documentatie, en alleen vast te stellen in een testadministratie.
- Of een verkooporderregel een `artikel` móet hebben. `VerkooporderRegelModel` heeft
  `artikel`, `omschrijving`, `stuksprijs`, `aantal`, `kortingsPercentage`, `totaal` en niets is
  formeel verplicht — maar de omschrijving valt terug op die van het artikel, en btw hangt in
  SnelStart aan de artikelomzetgroep. Waarschijnlijk moeten er twee artikelen bestaan
  ("Azure-doorbelasting", "Uren boven bundel"). Dat is dan een handmatige stap per administratie
  en een vraag voor de testadministratie.

---

## 5. Lezen en schrijven: verschillende scopes, één sleutel

De tweede weegvraag: de acceptatie vraagt ook lezen uit SnelStart (verstuurd? betaald?). Vragen
lezen en schrijven dezelfde toegang?

**Technisch nee, praktisch ja.**

De API kent twintig scopes, gesplitst per domein en per richting:
`artikelen`, `bankieren`, `boekhouden`, `btwaangiftes`, `documenten`, `kas`, `memoriaal`,
`orders`, `relaties`, `settings` — elk `:read` en `:write`. Gemeten op de endpoints die wij nodig
hebben:

```
GET  /verkoopfacturen                orders:read     ($skip, $top, $filter)
GET  /verkooporders                  orders:read     ($skip, $top, $filter)
POST /verkooporders                  orders:write
GET  /relaties                       relaties:read   ($skip, $top, $filter)
```

Lezen en schrijven zijn dus echt gescheiden, en de API handhaaft het met een 403 als de scope
ontbreekt. Maar: **er is één koppelsleutel per administratie**, de scopes liggen bij het
activeren van de koppeling vast, en het token-endpoint kent geen `scope`-parameter. Je kunt dus
geen aparte leessleutel voor de betaalstatus en schrijfsleutel voor het concept maken. Eén geheim,
en dat geheim kan alles wat de koppeling mag.

Wat dat kost aan ontwerp: de scheiding "lezen mag altijd, schrijven mag alleen op de 1e" is
**onze** grens en niet die van SnelStart. Hij hoort dus in onze code te zitten op een plek waar
hij niet per ongeluk weg te halen is — de vorm die het portaal al gebruikt voor autorisatie
(`CustomerScope`: de verkeerde aanroep is niet fout maar niet te schrijven) is hier de juiste vorm.
Eén cliënttype dat leest, een ander dat schrijft, en de schrijver alleen bereikbaar vanuit de
factuur-agent.

### En één scope die duurder is dan hij lijkt

Zie §7: de exacte betaaldatum staat niet in `VerkoopfactuurModel`. Wie hem wil, moet naar
`GET /grootboekmutaties` — en dat is `boekhouden:read`, de hele grootboekadministratie. Dat is
een aanzienlijk bredere scope dan `orders:read` voor één datumveld. Mijn advies staat in §7.

---

## 6. Idempotentie — het ontwerp

De agent draait `0 6 1 * *`. Een retry die een tweede concept aanmaakt is een waarschijnlijke
storing. Eerst wat de API biedt, dan wat wij moeten doen.

### Wat SnelStart biedt: niets

Ik heb dit programmatisch nagelopen over alle 63 paths van de spec: **er is geen enkele
header-parameter gedefinieerd, op geen enkel endpoint.** Geen `Idempotency-Key`, geen
`X-Request-Id`, nul treffers op `idempoten*`. Een herhaalde POST maakt een tweede order aan.
Punt.

Wat SnelStart wél biedt is de mogelijkheid om je eigen kenmerk mee te geven en er later op te
zoeken — en dat is bruikbaar, maar het is een controle en geen garantie:

| Veld op `VerkoopOrderModel` | Bruikbaar als | Let op |
|---|---|---|
| `orderreferentie` | terugzoeksleutel | **klantzichtbaar** — "wordt in de e-factuur en in de factuur als PDF opgenomen". Geen interne guid |
| `memo` | interne correlatie | lijkt niet klantzichtbaar. Of je erop kunt `$filter`en is niet vastgesteld |
| `nummer` | ordernummer | integer, door SnelStart beheerd |
| `betalingskenmerk`, `omschrijving` | vrij | |
| `extraHoofdVelden[]` | — | de spec markeert dit als **`[experimenteel]`** en sjabloonafhankelijk. Niet op bouwen |

Voor route A (`verkoopboekingen`) is terugzoeken bovendien praktisch onmogelijk: er is geen
collectie-GET en `GET /relaties/{id}/verkoopboekingen` heeft géén `$filter`, `$top` of `$skip` —
alleen het pad-id. Nog een reden dat route B de juiste is.

### Wat wij moeten doen: drie lagen, waarvan één echt sluit

**Laag 1 — een claim in Cosmos, vóór de POST. Dit is de enige laag die transactioneel dekt.**

Fase 3 heeft hier al de vorm voor gezet, en de reden staat letterlijk in
`Soratus.Portal/Data/PortalDocuments.cs` bij `PortalDocumentIds.HourEntry`:

> "Een urenregel wordt geld: een dubbel weggeschreven regel is een dubbel gefactureerd uur. Met
> een herleidbare id levert een herhaalde schrijfactie een 409 op in plaats van een tweede regel."

Precies hetzelfde geldt hier, alleen zwaarder: een factuur *is* geld. Dus een factuurdocument met
een **afgeleide id**, één per klant per maand, in de container `customers` bij de partitie van die
klant:

```
id = "invoice-{maand}"        bijv. invoice-2026-08
pk = klantslug
kind = "invoice"
```

De agent schrijft dat document **vóór** de aanroep naar SnelStart, met een `CreateItemAsync` (geen
upsert). Bestaat het al, dan geeft Cosmos een 409 en stopt de agent voor die klant. Dat is
dezelfde eigenschap die `infra.md` bij de klant-batch heeft gemeten en die daar bewezen bleek —
409 op een botsing en het document is er daarna niet.

Het document heeft dan een toestand nodig, en die is drieledig en niet tweeledig:

| Toestand | Betekenis | Wat de agent bij de volgende run doet |
|---|---|---|
| `claimed` | claim gezet, SnelStart-aanroep nog niet afgerond | **niets, en melden.** Zie de gaten hieronder |
| `drafted` | order aangemaakt, guid bekend | niets. Klaar |
| `abandoned` | een mens heeft vastgesteld dat er geen order is | opnieuw proberen mag |

Twee toestanden zou hier niet kunnen: "niet klaar" moet onderscheiden zijn van "onbekend of het
gelukt is", want de eerste mag je overdoen en de tweede niet. Dezelfde afweging als bij de
Entra-toestand in `infra.md`: drie waarden, niet twee, omdat een `bool` er maar één van kan doen.

**Laag 2 — zoeken vóór schrijven.** Deterministische `orderreferentie`, bijvoorbeeld
`Soratus 2026-08` (klantzichtbaar, dus leesbaar houden — geen guid), en vóór de POST:

```
GET /verkooporders?$filter=orderreferentie eq 'Soratus 2026-08'
```

Dit is een controle en geen slot — tussen de GET en de POST kan alles gebeuren — maar hij vangt
het geval dat laag 1 niet kan zien: een order die er wél is terwijl onze claim op `claimed` staat.

**Laag 3 — het gat dat blijft, en wat je ermee doet.** POST verstuurd, antwoord verloren
(timeout, herstart van de container). Dan staat de claim op `claimed` en is er misschien een
order. Er is geen manier om dit atomair te sluiten, want de twee systemen delen geen transactie.

De regel moet daarom zijn: **bij `claimed` probeert de agent het niet opnieuw.** Hij doet laag 2
nog één keer, en kan hij het niet vaststellen, dan stopt hij, zet de run op `failed` met een
begrijpelijke reden, en dit is een van de weinige gevallen waar het portaal een mens iets moet
vragen. Een tweede concept aanmaken is een boekhoudkundige fout; een dag later factureren is dat
niet.

Dat dit überhaupt herstelbaar is, is opnieuw route B: `DELETE /verkooporders/{id}` haalt een dubbel
concept weg. Bij route A zou hetzelfde scenario twee geboekte facturen opleveren.

### Het cronmoment is verkeerd

`0 6 1 * *` factureert de vorige maand op de 1e om 06:00. Uit §2: de Cost Management-data loopt
zeven tot tien uur achter. Om 06:00 op de 1e is de laatste dag van de vorige maand dus mogelijk
nog niet volledig geboekt — en een factuur met een halve dag Azure erin is stil verkeerd, want
niemand ziet het aan het bedrag.

Twee oplossingen, en de eerste is beter:

1. **De agent controleert de volledigheid** voor hij een concept maakt: staat er voor de laatste
   dag van de maand een bedrag dat in de lijn ligt van de dagen ervoor? Zo niet, dan geen concept
   maar een nette afbreking en een nieuwe poging later. Dat vangt óók de 404 uit §2 en de dag dat
   Microsoft de boeking langer laat wachten.
2. **Later draaien**, bijvoorbeeld `0 6 3 * *`. Simpel, maar het lost het onderliggende probleem
   niet op — het verplaatst alleen de gok.

Wat je in geen geval wil is een agent die factureert op wat er toevallig in de cache stond.

---

## 7. Wat de acceptatie van §3.7 niet haalt

De acceptatie is: *één factuur per maand met Azure plus extra uren, en in het portaal is per maand
te zien of die verstuurd en betaald is.* Het eerste deel kan. Het tweede deel niet helemaal, en
dat is een spec-afwijking en geen implementatiedetail.

`VerkoopfactuurModel` heeft precies deze velden — er is **geen statusveld en geen betaaldatum**:

```
factuurnummer, factuurDatum, vervalDatum, factuurBedrag,
openstaandSaldo   ("Het openstaand saldo … Deze wordt alleen bij uitlezen gevuld")
relatie, verkoopBoeking, verkoopOrders[], modifiedOn, id, uri
```

§3.7 vraagt vier statussen en een betaaldatum. Wat daarvan afleidbaar is:

| Status uit §3.7 | Afleidbaar? | Waaruit |
|---|---|---|
| **Concept** | ja | er is een verkooporder, en `verkoopfactuur` erop is nog leeg |
| **Verzonden** | **nee** | er is geen veld dat zegt of de factuur is gemaild. Alleen "er ís een verkoopfactuur" is leesbaar |
| **Openstaand (vervallen)** | ja | `openstaandSaldo > 0` en `vervalDatum` in het verleden |
| **Betaald** | ja | `openstaandSaldo == 0` |
| **Betaaldatum** (§6 `Invoice.paidAt`) | **nee**, niet met `orders:read` | zie hieronder |

Twee dingen om te besluiten:

**"Verzonden" bestaat niet — noem het "Gefactureerd".** Wat we kunnen weten is dat een mens de
order tot factuur heeft gemaakt. Of hij hem daarna gemaild heeft, weet SnelStart wel maar de API
niet. Een label "Verzonden" boven een gegeven dat "gefactureerd" betekent is precies het soort
onwaarheid met een tijdstempel eronder dat `infra.md` bij de Entra-toestand afwijst. Zet er
"Gefactureerd" en het tijdstip van ophalen, of laat de status weg.

**De betaaldatum kost een veel bredere scope.** Er is geen `betaald`- of `voldaan`-veld in de hele
API — ik heb alle schema's doorzocht. De echte datum staat in
`GET /grootboekmutaties` (OData, met `datum`, `factuurNummer`, `debet`, `credit`), en dat is
`boekhouden:read`: de hele grootboekadministratie voor één datumveld.

Voorstel: doe dat **niet**, en zeg in het scherm de waarheid. Het portaal poll't dagelijks
`openstaandSaldo` en legt vast wanneer het de kentering zág:

> Betaald · gezien op 14-09-2026

Dat is een eerlijke mededeling met een bekende onnauwkeurigheid van maximaal één dag, en het houdt
de scope op `orders:read`. Wil Marcel de exacte datum, dan is dat een aparte afweging: één veld
tegen leesrecht op het hele grootboek. §6 (`Invoice.paidAt`) hoeft daar niet voor te wijzigen —
alleen de betekenis wordt "voor het eerst gezien als betaald op".

### Twee gaten in het datamodel van §6

- **`Contract` heeft geen SnelStart-relatie.** Een verkooporder vereist `relatie`, een guid. Elke
  klant moet dus als relatie in ónze SnelStart-administratie bestaan én die guid moet bij het
  contract vastliggen. `GET /relaties?$filter=…` kan hem opzoeken, maar een naam is geen sleutel:
  twee klanten met dezelfde naam of een naam die iemand aanpast, en je factureert de verkeerde.
  Leg de guid vast, met een veld erbij dat zegt wanneer hij is gecontroleerd.
- **`Invoice` heeft geen SnelStart-verwijzing.** Er moet een plek zijn voor het order-guid en
  later het factuur-guid, anders is de koppeling tussen ons document en het hunne alleen via het
  factuurnummer te maken — en dat nummer geeft SnelStart pas bij het factureren uit.

### Nog één ding dat niet zeker is

Ik heb niet kunnen vaststellen of een factuur die via route B (verkooporder → mens factureert)
ontstaat, ook daadwerkelijk terugkomt in `GET /verkoopfacturen` met een gevuld `openstaandSaldo`.
Het datamodel zegt van wel (`VerkoopfactuurModel.verkoopOrders[]` bestaat, en op de order staat
`verkoopfactuur`), en de scopes kloppen — schrijven en lezen zitten allebei in `orders`. Maar dat
is een testvraag en geen documentatievraag. **Dit is precies wat je in een testadministratie
bewijst voordat je er een fase op bouwt.**

---

## 8. Wat ik niet heb kunnen vaststellen

Expliciet, want gissen is hier duurder dan niet weten.

1. **Wat er in `kv-soratus-prod` en `kv-mbv-keyvault-001` staat.** Ontbrekend recht, zie B4. Ik
   heb geen namen genoemd omdat ik ze niet heb.
2. **Of `orders:*` inHandel vereist.** Bepaalt of fase 4b bestaat. B2 vraag 1.
3. **Welke scopes een maatwerksleutel meekrijgt** en of ze te beperken zijn. Veiligheidskritisch.
   B2 vraag 2.
4. **De echte rate limits van de SnelStart-API.** Niet gedocumenteerd in de spec. B2 vraag 3.
5. **De exacte tokenlevensduur.** Dat `expires_in` bestaat is zeker; de waarde (waarschijnlijk een
   uur) komt van derden. Bouw refresh op de waarde uit de respons, niet op een aanname.
6. **De base-URL van de SnelStart-testomgeving.** Dat er een testomgeving is staat vast
   (`b2bapi-tst.snelstart.nl`); het adres voor API-calls niet.
7. **Of een via de API aangemaakte order als verkoopfactuur terugkomt** met `openstaandSaldo`.
   §7.
8. **Of een verkooporderregel een artikel moet hebben**, en dus of er artikelen aangemaakt moeten
   worden. §4.
9. **Of de laatste dag van een maand om 06:00 op de 1e volledig in Cost Management staat.** Ik heb
   gemeten dat een volledige vorige dag om 16:17 UTC compleet was; over 06:00 zegt dat niets. §6.
10. **Of de OpenAPI-spec die ik heb nagelopen exact de huidige productiespec is.** Het is een
    export van het officiële developer portal (OpenAPI + WADL + JSON, de drie APIM-exportformaten,
    met APIM-gegenereerde schemanamen) uit een publieke repo, geen primaire bron. De inhoud is
    intern consistent en klopt met twee onafhankelijke open-source clients, maar bevestig de
    kritieke punten — vooral de restrictie op `procesStatus` — zodra er portaltoegang is.

Nog een waarschuwing uit dit onderzoek: er staan op GitHub SnelStart-clients die endpoints
**verzinnen** (`POST /v2/connect/token` met `grant_type=clientcredentials`, een `betaalstatus`-veld
op verkoopboekingen). Die bestaan niet. Dat is code die nooit tegen de echte API heeft gelopen.
Wie voorbeeldcode zoekt: `iwd-nl/snelstart-php` is de betrouwbaarste referentie.

---

## 9. Ontwerpvoorstel — 4a nu, 4b later

### Fase 4a — nu te bouwen

Geen SnelStart. Levert een portaal dat per maand toont **wat het zou factureren**, en dat de klant
per mail informeert. Dat is het grootste deel van §3.7 en het hele kostendeel van §7.

| Onderdeel | Vorm |
|---|---|
| `kosten-collector` | dagelijks 04:00, één query per abonnement met grouping op `ResourceGroupName` én `ServiceName`, resultaat weggeschreven per klant per maand |
| Cache | 6–12 uur, en de cache is de **bron voor het scherm**, niet een versnelling. Het portaal roept Cost Management nooit synchroon aan bij het renderen — dat zou de emmer uit §2 leegtrekken bij elke pageview |
| Beheeropslag | uit het contract; het veld `opslag` bestaat al op `ContractDocument` |
| Extra uren boven bundel | uren × `uurTarief`, op de som van de **gefiatteerde** regels. Hangt aan fase 3 |
| Maandoverzicht mailen | ACS met managed identity + custom role (§3) |
| Blokkade | B5 (kostenrecht) en B6 (het §9-besluit over zichtbaarheid per dienst) |

Vier regels die uit de metingen volgen en niet onderhandelbaar zijn:

1. **Geen bedrag is niet nul.** Een 429, een timeout, of die 404 uit §2 mag nooit tot € 0,00
   leiden. Het scherm toont de vorige waarde met het tijdstip erbij, of "onbekend" — nooit een
   bedrag dat niet gemeten is. Dit is dezelfde regel als "geen document betekent geen status" uit
   de README, en om dezelfde reden.
2. **Een 429 is geen mislukte run.** Bij de gemeten uitvalskans van vijf op tien zou de collector
   permanent amber staan en zou de storingsmelder van fase 6 gaan mailen over een gezonde agent.
   Log hem als `warn` met `api.retry` — de vorm die de seed-data al gebruikt — en laat de run
   slagen zolang er voor élke klant een bruikbaar bedrag is, uit deze run of uit de cache.
3. **De dienstuitsplitsing komt uit de API, niet uit een lijst.** Zie §2.
4. **Lees `x-ms-ratelimit-microsoft.costmanagement-entity-retry-after` als hij er is en heb een
   eigen backoff voor als hij er niet is.** En monitor niet op
   `x-ms-ratelimit-remaining-subscription-resource-requests`; die staat altijd op 1099.

### Fase 4b — SnelStart, geblokkeerd

Begin hier niet aan voordat B1 t/m B3 gedaan zijn **en** er een testadministratie is waarin de
conceptroute van eind tot eind is aangetoond. Concreet: een verkooporder aangemaakt via de API,
door een mens gefactureerd in SnelStart Web, en daarna teruggelezen met een gevuld
`openstaandSaldo`. Zolang dat niet is gezien, is het ontwerp uit §4 een goed onderbouwde
verwachting en geen feit.

De vorm, als het zover is:

```
1. claim schrijven in Cosmos            (CreateItemAsync, id = invoice-{maand}) → 409 = klaar
2. GET /verkooporders?$filter=orderreferentie eq '…'   → bestaat al = claim bijwerken, klaar
3. POST /verkooporders  procesStatus: "Order"          → order-guid
4. claim bijwerken naar drafted, met de guid
5. dagelijks: GET /verkoopfacturen?$filter=…           → gefactureerd / openstaand / betaald
```

Stap 1 vóór stap 3. Nooit andersom, want dat is precies de volgorde waarin een dubbele factuur
ontstaat.

En één gedragsregel bovenop de techniek: de spec (§5) zegt *alles wat een agent inschiet landt als
te fiatteren*. Voor uren betekent dat een fiatteringsstroom in het portaal. Voor de factuur
betekent het dat de conceptorder in SnelStart blijft staan tot een mens hem factureert — en dat is
wat route B ons cadeau doet. Bouw daar geen tweede fiatteringsstroom in het portaal bovenop; dat
zou een tweede waarheid zijn over dezelfde vraag.

---

## 10. Conclusie

**Fase 4 is niet in één keer te bouwen, en dat is geen tegenvaller maar een goed moment om te
knippen.**

- **4a — kosten, opslag, uren boven bundel, maandoverzicht mailen: nu te bouwen.** Cost Management
  is opnieuw gemeten en werkt, met scherpere grenzen dan eerder gedacht (§2) en één failure mode
  die je moet kennen voor je begint (de 404). De mail loopt vandaag al in de marketingsite. Wat
  ontbreekt is één rolverleningsbesluit (B5) en één §9-besluit (B6).
- **4b — SnelStart: geblokkeerd op handelingen van Marcel, niet op ontwerp.** Het ontwerp is
  helder en het is veiliger dan de spec durfde aannemen: de conceptroute is geen afspraak maar een
  hard gehandhaafde restrictie in de API. Maar er is geen koppeling, geen sleutel, geen
  abonnementsbevestiging, en de doorlooptijd is weken.

Concreet advies: maak fase 3 af, bouw daarna 4a, en zet vandaag de SnelStart-aanvraag in met de
drie vragen uit B2 erbij. Dan loopt de doorlooptijd terwijl er gewerkt wordt, en beginnen we aan
4b op het moment dat we het antwoord hebben in plaats van erop te hopen.

Bouw 4b niet tegen de spec-export uit §8. Bouw hem tegen een testadministratie.
