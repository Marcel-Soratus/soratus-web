namespace Soratus.Web.Models;

/// <summary>
/// A single turn in the chat history.
/// <list type="bullet">
///   <item><c>Role = "user" | "assistant" | "tool"</c></item>
///   <item><c>"tool"</c> turns hold the JSON result of a tool execution and reference the call via <see cref="ToolCallId"/>.</item>
///   <item><c>"assistant"</c> turns may carry one or more <see cref="ToolCalls"/> that the model emitted on its previous round.</item>
/// </list>
/// </summary>
public sealed record ChatTurn(string Role, string Content)
{
    public string Html { get; set; } = Content;

    /// <summary>Set for assistant turns that contain function-call invocations.</summary>
    public IReadOnlyList<PendingToolCall>? ToolCalls { get; set; }

    /// <summary>Set for tool-result turns — links back to the assistant <c>tool_calls[i].id</c>.</summary>
    public string? ToolCallId { get; set; }

    /// <summary>Set for tool-result turns — the function name that was called.</summary>
    public string? ToolName { get; set; }
}

/// <summary>
/// A tool/function call as emitted by the LLM on an assistant turn.
/// Mirrors the OpenAI Chat Completions schema.
/// </summary>
public sealed record PendingToolCall(string Id, string Name, string ArgumentsJson);

public sealed record ChatRequest(List<ChatTurn>? Messages);

public sealed record Lead(
    string Name,
    string Company,
    string Phone,
    string? Email,
    string? Note,
    bool Consent);

public sealed record Testimonial(string Quote, string Highlight, string Role, string Industry, string Chip, string AvatarStyle);

public sealed record ClientLogo(string Name, string Sub, string Badge, int StyleIndex);

public sealed record BrancheItem(string Number, string Title, string Hint);
