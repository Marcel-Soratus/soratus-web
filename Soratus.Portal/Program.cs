using System.Security.Claims;
using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Soratus.Portal.Alerts;
using Soratus.Portal.Api;
using Soratus.Portal.Components;
using Soratus.Portal.Data;
using Soratus.Portal.Mail;
using Soratus.Portal.Platform;
using Soratus.Portal.Security;
using Soratus.Portal.Sprints;
using Soratus.Portal.Support;
using Soratus.Portal.Views;
using Soratus.Support.FirstLine;

var builder = WebApplication.CreateBuilder(args);

// ── Authenticatie ────────────────────────────────────────────────────────────────────────────
// OpenID Connect tegen Entra ID, configuratie uit de AzureAd-sectie. Client-id en tenant-id staan
// gewoon in appsettings.json: dat zijn geen geheimen. Een client secret komt later uit Key Vault en
// staat nergens in een bestand.
builder.Services
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

// PostConfigure en niet Configure: dit moet als laatste over de opties heen, ná alles wat
// Microsoft.Identity.Web zelf instelt. Anders hangt het gedrag af van registratievolgorde.
builder.Services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    // Inkomende claims worden gemapt naar de lange WIF-schema-URI's, en dat is niet uit te
    // zetten via OpenIdConnectOptions.MapInboundClaims: Microsoft.Identity.Web zet zijn eigen
    // tokenhandler, waardoor die instelling geen effect heeft. Gemeten op een echt token uit
    // deze tenant kwamen deze claimnamen aan:
    //
    //   aio · name · preferred_username · rh · sid · uti
    //   http://schemas.microsoft.com/identity/claims/objectidentifier
    //   http://schemas.microsoft.com/identity/claims/tenantid
    //   http://schemas.microsoft.com/ws/2008/06/identity/claims/role
    //   http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier
    //
    // De rol komt dus binnen als ClaimTypes.Role, niet als "roles". Stond RoleClaimType op
    // "roles", dan zocht IsInRole naar een naam die na het mappen niet bestaat — en dan staat
    // elk rolbeleid stil dicht in plaats van luidruchtig kapot. Dat is precies wat er gebeurde.
    //
    // Deze regel staat er expliciet, ook al is het de standaardwaarde: stopt een toekomstige
    // versie met mappen, dan hoort dit hier aangepast te worden op basis van een nieuwe meting
    // en niet stilzwijgend te verschuiven.
    options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
    options.TokenValidationParameters.NameClaimType = "name";
});

// Naast de browsersessie een tweede aanmeldweg: een bearer-token voor de MCP-server soratus-uren.
// AddMicrosoftIdentityWebApi naast AddMicrosoftIdentityWebApp, met dezelfde AzureAd-sectie. Zie
// Api/HoursApi.cs — daar staat waarom dat een AddAuthentication() zonder schemanaam is, en welke
// claims er op dít pad gemeten zijn (niet dezelfde handler als hierboven, dus niet dezelfde aanname).
builder.Services.AddHoursApi(builder.Configuration);

// ── Autorisatie ──────────────────────────────────────────────────────────────────────────────
builder.Services.AddAuthorizationBuilder()
    // Beide operatorbeleiden krijgen hun rol-eis van dezelfde methode. Eén beleid voor het scherm en
    // de API kan niet — een beleid bindt ook het aanmeldschema, en dat is hier een cookie en daar een
    // bearer-token — maar de eis zelf hoort één keer te bestaan. Zie HoursApiPolicy.
    .AddPolicy(PortalPolicies.Operator, HoursApiPolicy.RequireOperator)
    .AddPolicy(HoursApiPolicy.OperatorBearer, HoursApiPolicy.RequireOperatorOnBearer)
    .AddPolicy(PortalPolicies.Customer, policy => policy.RequireRole(PortalRoles.Customer))

    // Standaard is alles geautoriseerd; anoniem is de uitzondering en moet expliciet worden
    // aangevraagd met AllowAnonymous. Een vergeten [Authorize] levert dan geen open pagina op.
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// De aan- en afmeldroutes van Microsoft.Identity.Web (/MicrosoftIdentity/Account/SignIn|SignOut).
builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI();

// ── Configuratie ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddOptions<PortalTelemetryOptions>()
    .Bind(builder.Configuration.GetSection(PortalTelemetryOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<PortalCustomerOptions>()
    .Bind(builder.Configuration.GetSection(PortalCustomerOptions.SectionName));

// Waar de telemetrie van het platform zélf staat. Geen ValidateOnStart, om dezelfde reden als bij
// PortalData: een verkeerd ingerichte telemetrie is een inrichtingsfout, en een inrichtingsfout die
// het opstarten tegenhoudt neemt /healthz mee.
//
// Deze sectie voedt twee kanten en dat is met opzet: de publicatiekant hieronder, en de leeskant via
// CustomerDirectory — die de interne beheerklant hierheen laat wijzen in plaats van naar
// Telemetry:Database. Twee configuraties zouden de toestand toestaan waarin het portaal netjes
// publiceert in een database waar het scherm niet kijkt, en dat levert geen fout op maar een leeg
// overzicht.
builder.Services.AddOptions<PlatformTelemetryOptions>()
    .Bind(builder.Configuration.GetSection(PlatformTelemetryOptions.SectionName))
    .ValidateDataAnnotations();

// De portaaleigen opslag: klanten, contracten en toegang. Een andere opslag dan de telemetrie, en
// dat is geen detail — zie PortalDataLocation. Geen ValidateOnStart op de endpoint: een lege
// endpoint is een inrichtingsfout die het portaal moet overleven, want anders neemt hij /healthz mee.
builder.Services.AddOptions<PortalDataOptions>()
    .Bind(builder.Configuration.GetSection(PortalDataOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ── Telemetrie en autorisatiebronnen ─────────────────────────────────────────────────────────
// De klok loopt via TimeProvider en niet via DateTimeOffset.UtcNow, zodat een drempel van twee
// minuten te testen is zonder twee minuten te wachten. Dezelfde afspraak als in
// Soratus.Agents.Contracts, waar geen enkele methode zelf de klok leest.
builder.Services.AddSingleton(TimeProvider.System);

// Eén credential voor alle Cosmos-accounts. DefaultAzureCredential pakt de user-assigned managed
// identity op uit AZURE_CLIENT_ID; lokaal valt hij terug op de Azure CLI of Visual Studio. Er is
// nergens een accountsleutel, en die kan er ook niet zijn: op de accounts staat local auth uit.
builder.Services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());

// Eén CosmosClient per account-endpoint, gedeeld zolang de app draait. Zie CosmosClientCache.
builder.Services.AddSingleton<CosmosClientCache>();
builder.Services.AddSingleton<CosmosContainerProvider>();

// ── Het portaal als agent-host (§4, fase 6) ──────────────────────────────────────────────────
// Hier meldt het platform zichzelf: de kostencollector en de storingsmelder worden agents in ons
// eigen overzicht, met een registratie, een hartslag en een run per tik van hun klok.
//
// Ná de TokenCredential en de TimeProvider hierboven, en dat is geen stijl: de bibliotheek zet die
// twee met TryAddSingleton neer, dus in deze volgorde hergebruikt zij de credential en de klok van
// het portaal. Andersom zou er een tweede DefaultAzureCredential staan.
//
// De uitkomst wordt ná Build() gelogd en niet hier: er is op dit punt nog geen logger. Dat het een
// uitkomst is en geen stilte is het punt — een portaal dat zijn eigen agents niet publiceert hoort
// dat te zeggen, want anders is een leeg beheeroverzicht niet van een kapotte inrichting te
// onderscheiden. Deze aanroep werpt niet; zie PlatformAgents.AddSoratusPlatformAgents voor waarom
// dat hier andersom is dan in de bibliotheek zelf.
var platformAgents = builder.AddSoratusPlatformAgents();

// Warmt na het opstarten elke klantopslag op. Gemeten: koud kost het overzicht bijna acht
// seconden, warm ongeveer 200 ms — dat verschil hoort de eerste operator van de ochtend niet te
// betalen. Faalt de opwarming, dan wordt dat gelogd en draait het portaal gewoon door.
builder.Services.AddHostedService<TelemetryWarmup>();

// De klantenlijst staat als momentopname in het geheugen en wordt door PortalDirectoryRefresh
// vervangen zodra de opslag is gelezen. Eén registratie voor beide, want de refresh moet bij de
// internal Replace kunnen en de rest van het portaal ziet alleen de interface. Twee registraties
// zouden twee lijsten opleveren, en dan ververst de ene wel en de andere niet.
builder.Services.AddSingleton<CustomerDirectory>();
builder.Services.AddSingleton<ICustomerDirectory>(services => services.GetRequiredService<CustomerDirectory>());
builder.Services.AddScoped<ICustomerScopeResolver, CustomerScopeResolver>();

// De portaaleigen store. Singleton en niet scoped, omdat PortalDirectoryRefresh hem nodig heeft en
// een hosted service geen scoped afhankelijkheid kan krijgen. Hij houdt geen staat vast.
builder.Services.AddSingleton<CosmosPortalDataStore>();
builder.Services.AddSingleton<IPortalDataStore>(services => services.GetRequiredService<CosmosPortalDataStore>());

// Migreert de klantenlijst één keer naar de opslag en houdt hem daarna bij.
//
// Let op de samenloop met TelemetryWarmup: die warmt de opslagen op die de klantenlijst op dát
// moment noemt, en dat is bij een koude start nog de configuratielijst. Staat er in de opslag een
// klant met een ándere telemetrie-endpoint, dan betaalt de eerste lezer van die klant de
// opstartkost. Vandaag wijst alles naar hetzelfde account, dus het verschil is nul; het staat hier
// omdat het opvalt zodra dat niet meer zo is.
builder.Services.AddHostedService<PortalDirectoryRefresh>();

// Precies één implementatie. Geen seed-store, geen in-memory variant, geen tweede registratie:
// seed-data wordt door een apart consoleproject in dezelfde Cosmos gezet, in dezelfde
// documentvorm, en het portaal kan het verschil niet zien.
//
// Twee regels en niet één, en dat is dezelfde constructie als bij CosmosPortalDataStore: de
// storingsmelder is een achtergronddienst en die kan geen scoped afhankelijkheid krijgen, dus de
// concrete klasse staat er als singleton bij en de interface wijst naar diezelfde instantie. Met twee
// losse registraties zouden er twee instanties zijn — onschuldig zolang de klasse geen staat heeft,
// en precies het soort stilzwijgende verdubbeling dat later een tweede moment oplevert.
//
// De melder gebruikt de scopevrije ScanAsync op de concrete klasse; zie AgentScanTarget voor waarom
// dat pad bestaat en welke test hem tot één aanroeper beperkt.
builder.Services.AddSingleton<CosmosAgentTelemetryStore>();
builder.Services.AddScoped<IAgentTelemetryStore>(services => services.GetRequiredService<CosmosAgentTelemetryStore>());

// Eén PortalViews achter twee interfaces, en dus ook één registratie waar beide naar wijzen. Met
// twee AddScoped-regels zou een pagina die IPortalViews en IAgentDetailViews beide injecteert twee
// instanties krijgen — onschuldig zolang de klasse geen staat heeft, maar precies het soort
// stilzwijgende verdubbeling dat later een tweede moment oplevert.
builder.Services.AddScoped<PortalViews>();
builder.Services.AddScoped<IPortalViews>(services => services.GetRequiredService<PortalViews>());
builder.Services.AddScoped<IAgentDetailViews>(services => services.GetRequiredService<PortalViews>());

// Het contractscherm heeft zijn eigen bouwer, want het leest een andere opslag. Zie IContractViews:
// één klasse die twee opslagen bedient wordt de plek waar het ene met het andere wordt gemengd.
builder.Services.AddScoped<IContractViews, ContractViews>();

// De urenopslag. Scoped en niet singleton, anders dan CosmosPortalDataStore: die is singleton omdat
// PortalDirectoryRefresh (een hosted service) hem nodig heeft en geen scoped afhankelijkheid kan
// krijgen. Voor uren bestaat die aanleiding niet, en dan is scoped de standaard.
//
// Een eigen interface naast IPortalDataStore, en niet een paar methoden erbij. Zie IPortalHoursStore:
// die interface is de autorisatiebron van het portaal, en een pagina die uren boekt hoort niet
// hetzelfde bewijs in handen te hebben als een pagina die toegang uitdeelt.
builder.Services.AddScoped<IPortalHoursStore, CosmosPortalHoursStore>();

// Het urenscherm heeft zijn eigen bouwer, om dezelfde reden als het contractscherm. Hij leest de
// urenregels én het contract — dat laatste voor precies één getal, de bundel, want een saldo bestaat
// niet zonder bundel en de bundel staat in het contract.
builder.Services.AddScoped<IHourViews, HourViews>();

// De kostenopslag en de facturatieweergave. Eén registratie per interface en geen constructie met
// een gedeelde instantie zoals bij PortalViews: beide implementaties zijn internal sealed en hangen
// achter precies één interface, dus er is geen tweede interface die dezelfde instantie moet zien.
//
// Dit is niet uit te stellen tot na het scherm: /klant/{slug}/facturatie valt door zijn @page-route
// automatisch onder de reflectietests die élke pagina renderen. Zonder deze twee regels bestaat de
// pagina wél en valt hij dus onder dat vangnet, maar geeft hij een DI-fout in plaats van markup — en
// dan is de melding "kan IBillingViews niet oplossen" in plaats van iets over facturatie.
builder.Services.AddScoped<IPortalCostsStore, CosmosPortalCostsStore>();
builder.Services.AddScoped<IBillingViews, BillingViews>();

// ── Maandoverzicht per mail (§3.7) ────────────────────────────────────────────────────────────
// De mailinstellingen mogen leeg zijn en er staat géén ValidateOnStart op, om dezelfde reden als bij
// PortalDataOptions: een ontbrekende endpoint is een inrichtingsfout, en een inrichtingsfout die het
// opstarten tegenhoudt neemt /healthz mee en rolt daarmee de uitrol terug. Het scherm meldt het.
builder.Services.AddOptions<PortalMailOptions>()
    .Bind(builder.Configuration.GetSection(PortalMailOptions.SectionName))
    .ValidateDataAnnotations();

// De verzendlaag. Singleton en zonder staat; hij maakt zijn EmailClient per verzending. Hij leunt op
// de TokenCredential die hierboven al staat — dezelfde managed identity als voor Cosmos, met een
// custom role op de Communication Service en niet Contributor: die geeft ListKeys erbij en is dan
// machtiger dan het geheim dat we juist wilden vermijden.
//
// Twee aanroepers: het maandoverzicht (naar de klant) en de storingsmelder (naar Soratus). Dat is met
// opzet zo gebouwd — een gedeelde laag met één gebruiker bewijst niets. Wat erin zit is de
// verzendsemantiek: drie uitkomsten, 4xx als niet-verstuurd inclusief 429, al het andere onbekend,
// geen herhaling uit onbekend, en een proefdraaimodus die standaard aan staat.
builder.Services.AddSingleton<IMailOutbox, AcsMailOutbox>();

// De verzendbevestigingen staan in de container customers, naast klant, contract en urenregels.
// Scoped, net als IPortalHoursStore en om dezelfde reden: geen hosted service heeft hem nodig, en dan
// is scoped de standaard.
builder.Services.AddScoped<IStatementStore, CosmosStatementStore>();
builder.Services.AddScoped<IStatementViews, StatementViews>();

// Deze twee regels komen en gaan samen, en dat is geen stijlvoorkeur.
//
// MonthlyStatementService hangt aan IMonthlyStatementFigures. Staat de dienst er zonder de naad, dan
// start het portaal niet: ValidateOnBuild staat in Development aan, dus een onvervulbare AddScoped
// maakt WebApplicationBuilder.Build() onmogelijk. Gemeten toen dat gebeurde: het nam alle 26 tests
// van het urenendpoint mee, met een melding die naar dit bestand wees en niet naar de mailkant.
//
// Staat de naad er zonder de dienst, dan is er een bedragenbron waar niets langskomt. Er is een test
// die op precies die twee gebroken tussenstanden rood wordt en op beide eindstanden groen blijft.
//
// Er lag een tussenoplossing klaar — een plaatshouder achter de naad, zodat het portaal zou starten
// tot de echte implementatie er was. Afgewezen, en de reden is het bewaren waard: die plaatshouder
// antwoordt "niets gemeten", en dat is niet te onderscheiden van een echte "niets gemeten". Verdwijnt
// de echte registratie ooit bij een hernoeming of een merge, dan start de app gewoon door en wordt er
// stil nooit gemaild, met een reden die op het operatorscherm plausibel oogt. Een storing die zich
// voordoet als werkende functionaliteit — en een test die controleert of de container volledig is,
// zou er groen op staan. Zie punt 29.11.
builder.Services.AddScoped<IMonthlyStatementFigures, BillingStatementFigures>();
builder.Services.AddScoped<MonthlyStatementService>();

// ── De kostencollector (§3.7, fase 4a) ───────────────────────────────────────────────────────
// Geen ValidateOnStart, om dezelfde reden als bij PortalData en PortalMail: een verkeerd ingestelde
// collector is een inrichtingsfout, en een inrichtingsfout die het opstarten tegenhoudt neemt
// /healthz mee en rolt daarmee de uitrol terug.
builder.Services.AddOptions<AzureCostOptions>()
    .Bind(builder.Configuration.GetSection(AzureCostOptions.SectionName))
    .ValidateDataAnnotations();

// Een benoemde HttpClient en geen typed client. De collector is een achtergronddienst en leeft zolang
// het portaal draait; een geïnjecteerde HttpClient zou daarmee jaren dezelfde handler vasthouden en
// een DNS-wijziging van management.azure.com niet meer volgen. AzureCostClient vraagt de fabriek per
// aanroep om een verse client. Dezelfde afweging als bij AcsMailOutbox.
builder.Services.AddHttpClient(AzureCostClient.HttpClientName);
builder.Services.AddSingleton<IAzureCostClient, AzureCostClient>();

// Singleton, want AzureCostCollector is een hosted service en die kan geen scoped afhankelijkheid
// krijgen. Een eigen interface naast IPortalCostsStore en niet twee methoden daar: elke methode van
// die interface vraagt een scope, en de collector heeft geen mens en dus geen scope.
builder.Services.AddSingleton<IAzureCostCollectorStore, CosmosAzureCostCollectorStore>();

// De collector draait niet in Development, en dat staat hier in code in plaats van als vlag in
// appsettings.Development.json.
//
// Waarom niet als configuratie: die vlag staat met opzet standaard áán (een standaard-uit vlag is
// een storing die zich voordoet als werkende functionaliteit), dus hem uitzetten vraagt een regel in
// een bestand die iemand kan vergeten of bij een merge kan verliezen. En de prijs van vergeten is
// niet klein: een lokale run roept om 04:00 UTC Cost Management aan met de identiteit van de
// ontwikkelaar, uit precies dezelfde emmer waarin de collector in productie meet — en die emmer
// hangt gemeten aan de aanroeper en niet aan de scope.
//
// Waarom niet als omgevingscontrole binnen de dienst zelf: dan is hij lokaal helemaal niet meer te
// draaien. Zo is hij er in Development gewoon niet, wat in de container te zien is, en wie hem
// bewust lokaal wil draaien haalt deze voorwaarde hier weg en ziet daarbij waarom hij er stond.
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<AzureCostCollector>();
}

// ── Support (§3.8) ────────────────────────────────────────────────────────────────────────────
// De draad staat in de container customers, naast klant, contract, urenregels en de
// verzendbevestigingen. Scoped, net als IPortalHoursStore en IStatementStore en om dezelfde reden:
// geen hosted service heeft hem nodig, en dan is scoped de standaard.
builder.Services.AddScoped<ISupportStore, CosmosSupportStore>();
builder.Services.AddScoped<ISupportViews, SupportProjection>();

// Het schrijfpad van de klantkant. Concrete klasse en geen interface: er is één schrijfpad en het
// heeft geen tweede implementatie, en een interface met één implementatie die nergens wordt
// vervangen is een laag zonder werk.
builder.Services.AddScoped<SupportDesk>();

// En hier staat met opzet GEEN registratie van ISupportFirstLine.
//
// Dat is punt 29 toegepast: daar is een plaatshouder achter een naad afgewezen omdat hij "niets
// gemeten" antwoordt en dat niet te onderscheiden is van een echte "niets gemeten". Hier zou een
// plaatshouder altijd escaleren, en dat is niet te onderscheiden van een eerstelijn die het niet
// weet — een storing die zich voordoet als werkende functionaliteit.
//
// SupportDesk haalt de naad daarom met GetService op en niet met GetRequiredService, en de
// afwezigheid is een eigen toestand met een eigen tekst op het scherm. Een klant leest dan dat een
// mens antwoordt, en dat is waar. Zie punt 46.

// ── De AI-eerstelijn (§3.8, fase 5) ──────────────────────────────────────────────────────────
// Ná de TokenCredential hierboven, en dat is geen stijl: de kiezer vraagt hem om een token voor
// aoai-soratus-prod, met dezelfde managed identity als voor Cosmos en de Communication Service. Er is
// nergens een api-key en er is ook geen veld om er een in te zetten.
//
// Deze aanroep bindt PortalFirstLine en registreert de kiezer dan en slechts dan als er een endpoint
// én een deployment staat, Enabled aan is, en dit geen Development is. Geen ValidateOnStart en geen
// uitzondering, om dezelfde reden als bij PortalData, PortalMail, PortalCosts en PortalAlerts: een
// inrichtingsfout die het opstarten tegenhoudt neemt /healthz mee en rolt de uitrol terug.
var firstLine = builder.AddSoratusFirstLine();

// En hier zit de schakelaar. Staat de eerstelijn niet aan, dan is er geen ISupportFirstLine, en dan
// leest de klant op het supportscherm dat een mens antwoordt — en dat is waar. Dat is punt 46.9
// letterlijk: een geregistreerde eerstelijn die niets vraagt zou op elke vraag escaleren terwijl het
// scherm zegt dat er een agent meekijkt, en dat is een storing die zich voordoet als werkende
// functionaliteit.
//
// De standaard is uit, net zoals PortalMail:DryRun standaard aan staat en om dezelfde reden: een
// aanroep aan een taalmodel kost geld en gaat naar een externe dienst, en de onveilige stand hoort
// iets te zijn dat iemand aanzet. Waarom hij niet in Development draait staat in FirstLineOptions en
// het is een andere reden dan bij de collectors: geen vervuiling van klantgegevens, maar capaciteit
// van productie die opgaat aan een lokale klik, en klantgegevens die vanaf een laptop het proces
// verlaten.
//
// Scoped, net als ISupportStore, ISupportViews en SupportDesk: dit hangt aan één vraag van één mens.
if (firstLine.IsReady)
{
    builder.Services.AddScoped<ISupportFirstLine, ChoosingFirstLine>();
}

// ── De sprintcollector (§3.4, fase 5) ────────────────────────────────────────────────────────
// Geen ValidateOnStart, om dezelfde reden als bij PortalData, PortalMail en PortalCosts.
builder.Services.AddOptions<SprintOptions>()
    .Bind(builder.Configuration.GetSection(SprintOptions.SectionName))
    .ValidateDataAnnotations();

// Een benoemde HttpClient en geen typed client, om dezelfde reden als bij AzureCostClient: de
// collector is een achtergronddienst en leeft zolang het portaal draait, dus een geïnjecteerde
// HttpClient houdt jaren dezelfde handler vast en volgt een DNS-wijziging van dev.azure.com niet.
builder.Services.AddHttpClient(DevOpsSprintClient.HttpClientName);
builder.Services.AddSingleton<IDevOpsSprintClient, DevOpsSprintClient>();

// Singleton, want SprintCollector is een hosted service en die kan geen scoped afhankelijkheid
// krijgen. Een eigen interface naast IPortalSprintStore en niet twee methoden daar: elke methode
// van die interface vraagt een scope, en de collector heeft geen mens en dus geen scope.
builder.Services.AddSingleton<ISprintCollectorStore, CosmosSprintCollectorStore>();

// De leeskant van het scherm. Scoped, net als IPortalCostsStore en om dezelfde reden.
//
// Het scherm roept DevOps niet aan; het leest uitsluitend wat de collector heeft weggeschreven,
// precies zoals het facturatiescherm Cost Management niet aanroept bij het renderen. §3.4 vraagt
// het tijdstip van laatste ophalen op het scherm, en bij een ophaling per paginaweergave is dat
// tijdstip altijd "nu" en zegt het niets — de spec vraagt met dat ene veld om een momentopname.
// Er is dus geen codepad waarlangs een paginaweergave aan dev.azure.com komt.
builder.Services.AddScoped<IPortalSprintStore, CosmosPortalSprintStore>();
builder.Services.AddScoped<ISprintViews, SprintViews>();

// De collector draait niet in Development, en dat staat hier in code in plaats van als vlag in
// appsettings.Development.json — dezelfde afweging als bij AzureCostCollector, met een reden die
// een graad zwaarder is. Die collector *leest* bij Azure; deze schrijft sprintdocumenten in de
// partitie van een echte klant. Een ontwikkelmachine die aan blijft staan vult dus de opslag van
// een klant met wat het DevOps-token van die ontwikkelaar mocht ophalen — vervuiling van
// klantgegevens vanaf een laptop, en niet alleen te veel meten uit dezelfde emmer.
//
// Wie hem bewust lokaal wil draaien haalt deze voorwaarde weg en ziet daarbij waarom hij er stond.
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<SprintCollector>();
}

// ── De storingsmelder (§4, fase 6) ───────────────────────────────────────────────────────────
// Geen ValidateOnStart, om dezelfde reden als bij PortalData, PortalMail en PortalCosts: een verkeerd
// ingestelde melder is een inrichtingsfout, en een inrichtingsfout die het opstarten tegenhoudt neemt
// /healthz mee en rolt daarmee de uitrol terug. Een lege ontvangerslijst is dus geen startfout maar
// een error-regel bij elke ronde — zie AgentAlertOptions.Recipients.
builder.Services.AddOptions<AgentAlertOptions>()
    .Bind(builder.Configuration.GetSection(AgentAlertOptions.SectionName))
    .ValidateDataAnnotations();

// Beide singleton, want AgentFaultAlerter is een hosted service en die kan geen scoped afhankelijkheid
// krijgen. De bron leunt op de concrete CosmosAgentTelemetryStore die hierboven als singleton staat —
// zie AgentScanTarget voor waarom die scopevrije weg bestaat en welke test hem tot één aanroeper
// beperkt.
builder.Services.AddSingleton<IAgentFaultSource, TelemetryAgentFaultSource>();
builder.Services.AddSingleton<IAgentAlertStore, CosmosAgentAlertStore>();

// De melder draait niet in Development, en dat staat hier in code in plaats van als vlag in
// appsettings.Development.json. Dezelfde afweging als bij AzureCostCollector, met een eigen reden: een
// lokale run zou elke minuut de telemetrieopslag van élke echte klant bevragen. Dat mailt niets — de
// proefdraaimodus staat standaard aan — maar het kost wel RU's op de opslag van een klant, en dat is
// niets wat een ontwikkelmachine hoort te doen zonder dat iemand het heeft aangezet.
//
// Wie hem bewust lokaal wil draaien haalt deze voorwaarde weg en ziet daarbij waarom hij er stond.
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<AgentFaultAlerter>();
}

// ── Blazor ───────────────────────────────────────────────────────────────────────────────────
// Static SSR is de standaard. InteractiveServer is alleen beschikbaar als render mode voor de
// eilanden die later komen (live tail); de app als geheel wordt niet interactief.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();

// De doelpoort van UseHttpsRedirection, expliciet. Zonder dit meldt de middleware bij élke start
// "Failed to determine the https port for redirect" en stuurt hij niets door: achter de
// App Service-proxy luistert Kestrel alleen op HTTP, dus er is geen HTTPS-poort om af te leiden.
//
// Het gevolg was onschadelijk — httpsOnly staat aan op de App Service, dus de site is toch alleen
// via TLS bereikbaar — maar het is een waarschuwing bij elke start, en ruis is precies wat later
// een échte waarschuwing onzichtbaar maakt. De middleware weghalen zou ook werken en is
// verleidelijker; dat maakt de TLS-afdwinging alleen een eigenschap van een instelling in Azure
// die niemand in deze code ziet staan.
//
// Niet in Development: daar luistert Kestrel zelf op een HTTPS-poort en vindt de middleware hem.
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddHttpsRedirection(options => options.HttpsPort = 443);
}

var app = builder.Build();

// Wat er van het aansluiten als agent-host terecht is gekomen. Hier en niet in de opstartcode: daar
// bestaat de logger nog niet. Eén regel per start, met de reden erbij.
app.Logger.Log(platformAgents.Level, "{Explanation}", platformAgents.Explanation);

// Wat er van het aansluiten van de eerstelijn terecht is gekomen. Hier en niet in de opstartcode:
// daar bestaat de logger nog niet. Eén regel per start, met de reden erbij — een eerstelijn die niet
// is aangesloten hoort dat te zeggen, want anders is een supportscherm zonder AI-antwoorden niet van
// een kapotte inrichting te onderscheiden.
app.Logger.Log(firstLine.Level, "{Explanation}", firstLine.Explanation);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// De foutpagina's zijn voor de browser en niet voor de API, en dat is gemeten en geen voorzorg.
// UseStatusCodePagesWithReExecute voert het oorspronkelijke verzoek opnieuw uit op /not-found — met
// dezelfde methode en hetzelfde lichaam — en een POST met JSON op een Razor-pagina levert daar
// "The request has an incorrect Content-type." op met status 400. Omdat er dan al een lichaam is
// geschreven, kan de oorspronkelijke code niet meer worden teruggezet: een aanroep zonder token op
// /api/uren kwam als 400 met platte tekst terug in plaats van als 401. De MCP-server leest dat als
// "NIET geboekt" met een onbekende reden, waar hij "er is geen geldige aanmelding" hoort te zeggen —
// en dan zoekt een operator de fout in zijn boeking in plaats van in zijn aanmelding.
//
// Onder /api blijft de lege 401/403 dus staan zoals de autorisatiemiddleware hem schreef, met de
// WWW-Authenticate-kop erop. Voor de browser verandert er niets.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Statische bestanden moeten anoniem bereikbaar blijven. Met een fallback-beleid vallen ze anders
// óók onder de aanmeldplicht, en dan heeft de aanmeldpagina van Entra geen stylesheet.
app.MapStaticAssets().AllowAnonymous();

// De gezondheidscontrole waar de uitrolpijplijn op wacht. Anoniem, en met opzet nietszeggend: geen
// versie, geen omgeving, geen databasestatus. Wie hier langskomt hoort te weten of het proces
// antwoordt, en verder niets. Hij raakt Cosmos ook niet aan — een portaal dat draait terwijl één
// klantopslag hapert is gezond, en een gezondheidscontrole die dat anders beoordeelt rolt bij elke
// hapering de vorige versie terug.
app.MapGet("/healthz", () => Results.Text("ok", "text/plain"))
    .AllowAnonymous()
    .ExcludeFromDescription();

app.MapControllers();

// POST /api/uren — het schrijfpad van de MCP-server soratus-uren. Staat hier, ná UseAntiforgery, en
// dat is geen omissie: zie de opmerkingen bij HoursApi voor de meting waarom dit endpoint geen
// antiforgery-token nodig heeft en er ook niet op stuk loopt.
app.MapHoursApi();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>
/// Het toegangspunt van het portaal, expliciet publiek gemaakt.
/// </summary>
/// <remarks>
/// Bestaat alleen zodat een test deze app kan <em>starten</em> in plaats van de wiring erboven na te
/// bouwen. Een bestand met top-level statements levert een <c>internal</c> klasse op, en
/// <c>WebApplicationFactory&lt;Program&gt;</c> kan die niet als typeargument van een publieke fixture
/// dragen. Dit is de gedocumenteerde vorm daarvoor.
///
/// Waarom dat de moeite waard is: de dingen die hierboven gemeten moeten worden — dat het rolbeleid op
/// het bearer-schema staat, dat de rolclaim gemapt aankomt, dat de antiforgery-middleware een POST met
/// JSON doorlaat — zijn eigenschappen van deze registratie en deze middlewareorde. Een test die die
/// orde zelf opbouwt, meet zijn eigen kopie.
/// </remarks>
public partial class Program;
