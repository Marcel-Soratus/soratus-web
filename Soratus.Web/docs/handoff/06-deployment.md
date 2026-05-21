# 06 · Deployment

Target: **Azure App Service for Linux, P1V3** in West Europe (`westeurope`). Reasons: it's the cheapest tier that supports always-on, SignalR, and 64-bit .NET 9; West Europe = NL = AVG-friendly + lowest latency for the audience.

## Resources

| Resource | SKU | Notes |
|---|---|---|
| Resource Group | `rg-soratus-prod` | `westeurope` |
| App Service Plan | `asp-soratus-prod` (P1V3, Linux) | Always On enabled |
| App Service | `app-soratus-prod` | .NET 9 runtime, HTTPS only |
| Application Insights | `appi-soratus-prod` | Workspace-based |
| Key Vault | `kv-soratus-prod` | Managed identity reference from App Service |
| Azure Front Door | `afd-soratus` | Edge + WAF (optional but recommended) |
| Custom domain | `soratus.com` | CNAME at registrar → App Service / Front Door |

If Front Door is skipped (budget): App Service has free managed certs and is fine on its own. Add Front Door once traffic or geographic spread justifies it.

## App settings (Azure portal → Configuration)

```
Anthropic__ApiKey         → Key Vault reference
Anthropic__Model          → claude-haiku-4-5
SendGrid__ApiKey          → Key Vault reference
ASPNETCORE_ENVIRONMENT    → Production
WEBSITE_RUN_FROM_PACKAGE  → 1
```

App settings with double-underscore (`__`) map onto the nested JSON sections (`Anthropic:ApiKey`).

## Deploy pipeline

GitHub Actions, `main` → prod. Single workflow file `.github/workflows/deploy.yml`:

```yaml
name: deploy

on:
  push:
    branches: [main]

permissions:
  id-token: write
  contents: read

jobs:
  build-test-deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 9.0.x

      - run: dotnet restore
      - run: dotnet build -c Release --no-restore
      - run: dotnet test -c Release --no-build
      - run: dotnet publish Soratus.Web/Soratus.Web.csproj -c Release -o ./publish

      - uses: azure/login@v2
        with:
          client-id:     ${{ secrets.AZURE_CLIENT_ID }}
          tenant-id:     ${{ secrets.AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.AZURE_SUB_ID }}

      - uses: azure/webapps-deploy@v3
        with:
          app-name: app-soratus-prod
          package: ./publish
```

Federated identity (OIDC) — no service-principal secrets in GitHub.

## Health probes

```csharp
app.MapGet("/healthz", () => Results.Ok(new { ok = true }));
app.MapGet("/readyz",  async (AnthropicClient c) => {
    try { await c.PingAsync(); return Results.Ok(new { ok = true }); }
    catch { return Results.StatusCode(503); }
});
```

App Service health check → `/healthz`. Front Door origin probe → `/readyz`.

## Logging

- Stdout → App Service log stream (default).
- Application Insights via OpenTelemetry; sampling at 25%.
- Don't log message contents from `/api/chat` — privacy. Log token counts, latency, model, status code only.

## Backup / disaster recovery

The app is stateless. No backup needed beyond what GitHub holds. Lead emails are the only persisted artefact and they live in the recipient's mailbox (mail provider's retention rules apply).

## Cost estimate (per month)

| Item | EUR |
|---|---:|
| App Service Plan P1V3 (Linux) | ~75 |
| Application Insights (low volume) | ~5 |
| Key Vault | ~1 |
| Azure Front Door (optional) | ~30 |
| **Total** | **~80–115** |

Plus Anthropic API spend: with ~1000 chat sessions / month averaging 2k tokens each on Haiku, < €25.

## Pre-launch checklist

- [ ] Custom domain bound, certificate auto-renew on.
- [ ] HSTS header (`Strict-Transport-Security: max-age=63072000; includeSubDomains; preload`).
- [ ] `Content-Security-Policy` set; allow `'self'` + `fonts.gstatic.com` + `fonts.googleapis.com` + `api.anthropic.com`.
- [ ] `Permissions-Policy: camera=(), microphone=(), geolocation=()` (we ask for none).
- [ ] Robots: `/robots.txt` allowing everything, sitemap at `/sitemap.xml`.
- [ ] OG image: `wwwroot/brand/og-image.png` exists and is < 300 KB.
- [ ] Lighthouse mobile ≥ 95 on every category.
- [ ] axe-core scan clean.
- [ ] No console errors in production build.
- [ ] Chat widget tested on Safari 17, Chrome 122, Firefox 125, mobile Safari iOS 17.
- [ ] Lead form delivers an email end-to-end in production.
- [ ] Anthropic key has spending limit set in the Anthropic console.

## Out of scope for v1

- A blog / press section. If marketing wants it, scaffold a separate route group and a CMS at that point — not now.
- Multilanguage (EN). The site is Dutch-only by design.
- A customer portal / login. Keep the app fully public until there's a product to portal into.
- Analytics. Add Plausible (self-hosted, EU) when needed; never GA4.
