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
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection("Anthropic"));
builder.Services.Configure<SendGridOptions>(builder.Configuration.GetSection("SendGrid"));

builder.Services.AddHttpClient<AnthropicClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AnthropicOptions>>().Value;
    http.BaseAddress = new Uri(opts.BaseUrl);
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

// Canonical-host redirect:
//   uvidai.com / www.uvidai.com  → soratus.com  (301)
//   www.soratus.com               → soratus.com  (301)
const string canonicalHost = "soratus.com";
var redirectFromHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "uvidai.com",
    "www.uvidai.com",
    "www.soratus.com"
};

app.Use(async (context, next) =>
{
    var host = context.Request.Host.Host;
    if (redirectFromHosts.Contains(host))
    {
        var target = new UriBuilder(context.Request.GetEncodedUrl())
        {
            Host = canonicalHost,
            Port = context.Request.IsHttps ? 443 : 80
        };
        // UriBuilder includes default ports verbosely; strip them
        var url = target.Uri.ToString();
        context.Response.Redirect(url, permanent: true);
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
        "connect-src 'self' wss: https://api.anthropic.com https://*.clarity.ms https://c.bing.com; " +
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
app.MapGet("/readyz", async (AnthropicClient c, CancellationToken ct) =>
{
    var ok = await c.PingAsync(ct);
    return ok ? Results.Ok(new { ok = true }) : Results.StatusCode(503);
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();
