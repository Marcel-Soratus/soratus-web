using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Soratus.Portal.Components;
using Soratus.Portal.Data;
using Soratus.Portal.Security;
using Soratus.Portal.Views;

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
    // Zet het mappen van inkomende claims uit. Dat mappen is een erfenis van WIF: het doopt
    // JWT-claims om naar lange schema-URI's, waarbij `roles` bijvoorbeeld
    // `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` wordt. Met dat mappen aan
    // én RoleClaimType op "roles" zoekt IsInRole naar een claimnaam die na het omdopen niet
    // meer bestaat, en dan is elk rolbeleid stil dicht in plaats van luidruchtig kapot.
    //
    // Uit betekent: claims heten precies wat Entra ze noemt. Dat is deterministisch, en het
    // maakt de twee regels hieronder waar in plaats van afhankelijk van een mappingtabel.
    options.MapInboundClaims = false;

    options.TokenValidationParameters.RoleClaimType = ClaimConstants.Roles;
    options.TokenValidationParameters.NameClaimType = "name";
});

// ── Autorisatie ──────────────────────────────────────────────────────────────────────────────
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(PortalPolicies.Operator, policy => policy.RequireRole(PortalRoles.Operator))
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

// Warmt na het opstarten elke klantopslag op. Gemeten: koud kost het overzicht bijna acht
// seconden, warm ongeveer 200 ms — dat verschil hoort de eerste operator van de ochtend niet te
// betalen. Faalt de opwarming, dan wordt dat gelogd en draait het portaal gewoon door.
builder.Services.AddHostedService<TelemetryWarmup>();

builder.Services.AddSingleton<ICustomerDirectory, ConfigurationCustomerDirectory>();
builder.Services.AddScoped<ICustomerScopeResolver, CustomerScopeResolver>();

// Precies één implementatie. Geen seed-store, geen in-memory variant, geen tweede registratie:
// seed-data wordt door een apart consoleproject in dezelfde Cosmos gezet, in dezelfde
// documentvorm, en het portaal kan het verschil niet zien.
builder.Services.AddScoped<IAgentTelemetryStore, CosmosAgentTelemetryStore>();
builder.Services.AddScoped<IPortalViews, PortalViews>();

// ── Blazor ───────────────────────────────────────────────────────────────────────────────────
// Static SSR is de standaard. InteractiveServer is alleen beschikbaar als render mode voor de
// eilanden die later komen (live tail); de app als geheel wordt niet interactief.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
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

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
