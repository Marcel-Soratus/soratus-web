namespace Soratus.Web.Models;

/// <summary>
/// Discriminated event emitted by <c>AzureOpenAIClient.StreamChatAsync</c>.
/// The chat widget pattern-matches on the concrete type to drive UI state.
/// </summary>
public abstract record StreamEvent;

/// <summary>A chunk of assistant-visible text. Append to the streaming bubble.</summary>
public sealed record TextDelta(string Text) : StreamEvent;

/// <summary>Model has begun emitting a tool-call. Use to flip UI into "noteren" mode.</summary>
public sealed record ToolCallStarted(string CallId, string ToolName) : StreamEvent;

/// <summary>A fragment of the JSON-encoded arguments string. Accumulate before parsing.</summary>
public sealed record ToolCallArgsDelta(string CallId, string JsonFragment) : StreamEvent;

/// <summary>
/// The model has finished a tool-call. <paramref name="ArgumentsJson"/> is the complete,
/// concatenated arguments string ready to deserialize.
/// </summary>
public sealed record ToolCallReady(string CallId, string ToolName, string ArgumentsJson) : StreamEvent;
