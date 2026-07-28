using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.RateLimiting;
using Soratus.Web.Components;
using Soratus.Web.Endpoints;
using Soratus.Web.Models;
using Soratus.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<BrandOptions>(builder.Configuration.GetSection("Brand"));
builder.Services.Configure<CompanyOptions>(builder.Configuration.GetSection("Company"));
builder.Services.Configure<AzureOpenAIOptions>(builder.Configuration.GetSection("AzureOpenAI"));
builder.Services.Configure<AzureEmailOptions>(builder.Configuration.GetSection("AzureEmail"));

builder.Services.AddHttpClient<AzureOpenAIClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureOpenAIOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(opts.Endpoint))
        http.BaseAddress = new Uri(opts.Endpoint);
    http.Timeout = TimeSpan.FromSeconds(60);
})
.AddStandardResilienceHandler(o =>
{
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);
    o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(120);
});

builder.Services.AddSingleton<SystemPromptBuilder>();
builder.Services.AddScoped<LeadSink>();

builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter("lead", w =>
    {
        w.PermitLimit = 5;
        w.Window = TimeSpan.FromMinutes(1);
        w.QueueLimit = 0;
    });
    o.AddFixedWindowLimiter("chat", w =>
    {
        w.PermitLimit = 30;
        w.Window = TimeSpan.FromMinutes(1);
        w.QueueLimit = 0;
    });
    o.RejectionStatusCode = 429;
});

builder.Services.AddOpenApi();
builder.Services.AddResponseCompression();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

// Canonical-host redirect: www.soratus.com → soratus.com (301)
// (uvidai.com → soratus.com is handled by Namecheap URL forwarding at the
//  registrar, so it never reaches us.)
//
// Daarnaast de standaard Azure-hostnaam (app-soratus-prod.azurewebsites.net)
// → soratus.com. Die blijft naast het eigen domein bereikbaar en wordt continu
// door bots afgescand, wat de statistieken vervuilt en duplicate content geeft.
//
// Twee uitzonderingen, anders breken we iets:
//  - Deployment-slots. Daar wil je juist wél via de azurewebsites.net-URL kunnen
//    testen. WEBSITE_SLOT_NAME is "Production" in de productieslot en anders de
//    naam van de slot.
//  - /healthz. Azure's health check verwacht een 2xx en volgt geen redirect; een
//    301 zou de app als ongezond markeren.
var canonicalHost = new Uri(app.Configuration["Company:Url"] ?? "https://soratus.com").Host;
var slotName = Environment.GetEnvironmentVariable("WEBSITE_SLOT_NAME");
var isProductionSlot = string.IsNullOrEmpty(slotName)
    || slotName.Equals("Production", StringComparison.OrdinalIgnoreCase);

app.Use(async (context, next) =>
{
    var host = context.Request.Host.Host;

    if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
    {
        var target = new UriBuilder(context.Request.GetEncodedUrl())
        {
            Scheme = Uri.UriSchemeHttps,
            Host = host[4..],
            Port = -1
        };
        context.Response.Redirect(target.ToString(), permanent: true);
        return;
    }

    if (isProductionSlot
        && host.EndsWith(".azurewebsites.net", StringComparison.OrdinalIgnoreCase)
        && !context.Request.Path.StartsWithSegments("/healthz"))
    {
        var target = new UriBuilder(context.Request.GetEncodedUrl())
        {
            Scheme = Uri.UriSchemeHttps,
            Host = canonicalHost,
            Port = -1
        };
        context.Response.Redirect(target.ToString(), permanent: true);
        return;
    }

    await next();
});

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), interest-cohort=()";
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://www.clarity.ms https://*.clarity.ms; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data: https:; " +
        "connect-src 'self' wss: https://*.openai.azure.com https://*.clarity.ms https://c.bing.com; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self';";
    await next();
});

app.UseResponseCompression();
app.UseAntiforgery();
app.UseRateLimiter();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapChatEndpoint();
app.MapLeadEndpoint();

app.MapGet("/healthz", () => Results.Ok(new { ok = true }));
app.MapGet("/readyz", async (AzureOpenAIClient c, CancellationToken ct) =>
{
    var ok = await c.PingAsync(ct);
    return ok ? Results.Ok(new { ok = true }) : Results.StatusCode(503);
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Catch-all fallback: any path that didn't match a Razor route, endpoint,
// static file, or health probe gets a 301 to the homepage. Mostly catches
// inbound traffic on legacy Wix URLs that have been linked / indexed.
app.MapFallback(context =>
{
    context.Response.Redirect("/", permanent: true);
    return Task.CompletedTask;
});

app.Run();
