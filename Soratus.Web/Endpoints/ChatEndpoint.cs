using System.Text;
using System.Text.Json;
using Soratus.Web.Models;
using Soratus.Web.Services;

namespace Soratus.Web.Endpoints;

public static class ChatEndpoint
{
    public static void MapChatEndpoint(this WebApplication app)
    {
        app.MapPost("/api/chat", async (
                ChatRequest req,
                AzureOpenAIClient ai,
                SystemPromptBuilder prompt,
                HttpContext ctx,
                CancellationToken ct) =>
            {
                if (req.Messages is null || req.Messages.Count == 0)
                    return Results.BadRequest();

                ctx.Response.Headers["Content-Type"] = "text/event-stream";
                ctx.Response.Headers["Cache-Control"] = "no-cache";
                ctx.Response.Headers["X-Accel-Buffering"] = "no";

                var system = prompt.Build();
                await foreach (var chunk in ai.StreamAsync(system, req.Messages, ct))
                {
                    var line = $"data: {JsonSerializer.Serialize(new { delta = chunk, done = false })}\n\n";
                    await ctx.Response.WriteAsync(line, Encoding.UTF8, ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }

                await ctx.Response.WriteAsync("data: {\"delta\":\"\",\"done\":true}\n\n", Encoding.UTF8, ct);
                await ctx.Response.Body.FlushAsync(ct);
                return Results.Empty;
            })
            .WithName("Chat")
            .RequireRateLimiting("chat")
            .DisableAntiforgery();
    }
}
