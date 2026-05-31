using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoAuction.Core.Services.OpenAi;

// A minimal, non-streaming subset of the OpenAI Responses API wire format — enough to send a
// system prompt + an image + function tools, loop on tool calls, and read a structured result.
// Every field uses an explicit JsonPropertyName so JSON-schema property names are never rewritten
// by a naming policy. Null fields are omitted (see OpenAiJson.Options).

/// <summary>One content part of a user message: input_text or input_image.</summary>
public sealed class ContentPart
{
    [JsonPropertyName("type")] public string Type { get; init; } = "input_text";
    [JsonPropertyName("text")] public string? Text { get; init; }
    [JsonPropertyName("image_url")] public string? ImageUrl { get; init; }

    public static ContentPart TextPart(string text) => new() {Type = "input_text", Text = text};

    /// <summary><paramref name="dataUrl"/> is e.g. "data:image/jpeg;base64,...".</summary>
    public static ContentPart ImagePart(string dataUrl) => new() {Type = "input_image", ImageUrl = dataUrl};
}

/// <summary>An input item: a role message, or a function_call_output carrying a tool result.</summary>
public sealed class InputItem
{
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("role")] public string? Role { get; init; }
    [JsonPropertyName("content")] public List<ContentPart>? Content { get; init; }
    [JsonPropertyName("call_id")] public string? CallId { get; init; }
    [JsonPropertyName("output")] public string? Output { get; init; }

    public static InputItem UserMessage(IEnumerable<ContentPart> parts) =>
        new() {Type = "message", Role = "user", Content = parts.ToList()};

    public static InputItem FunctionOutput(string callId, string output) =>
        new() {Type = "function_call_output", CallId = callId, Output = output};
}

/// <summary>A function tool the model may call. Parameters is a raw JSON Schema element.</summary>
public sealed class FunctionTool
{
    [JsonPropertyName("type")] public string Type { get; init; } = "function";
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("parameters")] public JsonElement Parameters { get; init; }

    public static FunctionTool Create(string name, string description, object parametersSchema) => new()
    {
        Name = name,
        Description = description,
        // Serialize the schema on its own so the outer options never touch its property names.
        Parameters = JsonSerializer.SerializeToElement(parametersSchema)
    };
}

/// <summary>The request body POSTed to /v1/responses.</summary>
public sealed class ResponsesToolRequest
{
    [JsonPropertyName("model")] public string Model { get; init; } = string.Empty;
    [JsonPropertyName("instructions")] public string? Instructions { get; init; }
    [JsonPropertyName("input")] public IReadOnlyList<InputItem> Input { get; init; } = Array.Empty<InputItem>();
    [JsonPropertyName("tools")] public IReadOnlyList<FunctionTool>? Tools { get; init; }
    [JsonPropertyName("tool_choice")] public string? ToolChoice { get; init; }
    [JsonPropertyName("previous_response_id")] public string? PreviousResponseId { get; init; }
}

// ----- Parsed response (what the client returns to the generator) -----

public abstract record OutputItem;

/// <summary>The model asked to call a tool. Arguments is a JSON string.</summary>
public sealed record FunctionCallItem(string Name, string Arguments, string CallId) : OutputItem;

/// <summary>A plain assistant text message.</summary>
public sealed record MessageItem(string Text) : OutputItem;

public sealed record OpenAiResponse(string Id, IReadOnlyList<OutputItem> Output)
{
    public IEnumerable<FunctionCallItem> FunctionCalls => Output.OfType<FunctionCallItem>();
    public string? MessageText => Output.OfType<MessageItem>().Select(m => m.Text).FirstOrDefault();
}

internal static class OpenAiJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
