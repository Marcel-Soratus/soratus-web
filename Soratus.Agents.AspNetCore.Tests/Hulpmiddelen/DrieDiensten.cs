using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Soratus.Agents.AspNetCore.Tests.Hulpmiddelen;

/// <summary>
/// De drie diensten van de eerste echte klant, zo gemonteerd als het in zijn opstartcode staat.
/// </summary>
/// <remarks>
/// Bewust letterlijk het geval waarvoor dit gebouwd is: een chat tegen een boekhoudkoppeling, een
/// financieel overzicht en het inlezen van declaraties uit Excel. Plus één endpoint zonder agent,
/// want de vraag of dat endpoint géén agent oplevert is even belangrijk als de vraag of de drie
/// andere er wél een opleveren.
/// </remarks>
internal static class DrieDiensten
{
    internal const string Chat = "boekhoud-chat";
    internal const string Overzicht = "financieel-overzicht";
    internal const string Import = "declaraties-import";

    internal static void Monteer(WebApplication app)
    {
        app.MapPost("/api/chat", static () => Results.Ok("Dag."))
            .WithSoratusAgent(Chat, "Chat", "POST /api/chat");

        app.MapGet("/api/financieel", static () => Results.Ok(new { omzet = 1200 }))
            .WithSoratusAgent(Overzicht, "Rapportage", "GET /api/financieel");

        app.MapPost("/api/declaraties", static (HttpContext context) =>
            {
                // De enige regel die een bestaande handler erbij krijgt, en hij is optioneel.
                context.SoratusAgentRun()?.Processed(3);
                return Results.Ok();
            })
            .WithSoratusAgent(Import, "Document-intake", "POST /api/declaraties");

        // Geen agent: dit is geen dienst van de klant maar een controlepunt van het platform.
        app.MapGet("/healthz", static () => Results.Ok());
    }
}
