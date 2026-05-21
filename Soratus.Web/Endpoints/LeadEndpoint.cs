using Soratus.Web.Models;
using Soratus.Web.Services;

namespace Soratus.Web.Endpoints;

public static class LeadEndpoint
{
    public static void MapLeadEndpoint(this WebApplication app)
    {
        app.MapPost("/api/lead", async (
                Lead lead,
                LeadSink sink,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(lead.Name) ||
                    string.IsNullOrWhiteSpace(lead.Phone) ||
                    !lead.Consent)
                    return Results.BadRequest(new { error = "Naam, telefoon en toestemming zijn verplicht." });

                await sink.SaveAsync(lead, ct);
                return Results.Accepted();
            })
            .WithName("Lead")
            .RequireRateLimiting("lead")
            .DisableAntiforgery();
    }
}
