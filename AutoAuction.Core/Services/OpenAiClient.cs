using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoAuction.Core.Models;
using AutoAuction.Core.Services.OpenAi;
using AutoAuction.Core.Services.Secrets;

namespace AutoAuction.Core.Services;

/// <summary>
/// Thin client over the OpenAI Responses API (<c>https://api.openai.com/v1/responses</c>),
/// called directly with <see cref="HttpClient"/> — no SDK. The model comes from settings; the
/// API key is held encrypted in the OS secret store. Listing-from-photos generation lands in
/// Phase 3; this is the seam it plugs into.
/// </summary>
public sealed class OpenAiClient
{
    /// <summary>Name the OpenAI API key is stored under in the <see cref="ISecretStore"/>.</summary>
    public const string ApiKeySecretName = "OpenAiApiKey";

    private const string ResponsesEndpoint = "https://api.openai.com/v1/responses";

    // One shared HttpClient for the app's lifetime (avoids socket exhaustion).
    private static readonly HttpClient Http = new();

    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;

    public OpenAiClient(ISettingsService settings, ISecretStore secrets)
    {
        _settings = settings;
        _secrets = secrets;
    }

    /// <summary>True when an OpenAI API key has been stored.</summary>
    public bool HasApiKey => _secrets.Has(ApiKeySecretName);

    /// <summary>Stores (or replaces) the API key, encrypted at rest.</summary>
    public void SetApiKey(string apiKey) => _secrets.Set(ApiKeySecretName, apiKey);

    /// <summary>Removes the stored API key.</summary>
    public void ClearApiKey() => _secrets.Delete(ApiKeySecretName);

    /// <summary>Sends a tiny prompt to validate the stored key + configured model.</summary>
    public Task<(bool Ok, string Message)> TestAsync()
        => TestAsync(_secrets.Get(ApiKeySecretName), _settings.Current.OpenAiModel);

    /// <summary>Sends a tiny prompt to validate the supplied key/model (used by the Settings test button).</summary>
    public async Task<(bool Ok, string Message)> TestAsync(string? apiKey, string? model)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return (false, "API key is empty.");

        try
        {
            var text = await CreateResponseAsync(apiKey, ModelOrDefault(model), "Reply with the single word: OK");
            return (true, string.IsNullOrWhiteSpace(text) ? "Connected." : $"Connected: {text.Trim()}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Sends a prompt using the stored API key and the configured model. Throws when no key
    /// is set. This is the entry point Phase 3 listing generation calls.
    /// </summary>
    public Task<string> CreateResponseAsync(string input, CancellationToken cancellationToken = default)
    {
        var apiKey = _secrets.Get(ApiKeySecretName)
            ?? throw new InvalidOperationException("No OpenAI API key has been configured.");
        return CreateResponseAsync(apiKey, ModelOrDefault(_settings.Current.OpenAiModel), input, cancellationToken);
    }

    /// <summary>
    /// POSTs a single text prompt to the Responses API and returns the concatenated output text.
    /// Throws on a non-success status (the message includes the API error body).
    /// </summary>
    public async Task<string> CreateResponseAsync(
        string apiKey, string model, string input, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ResponsesEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new ResponsesRequest(model, input));

        using var response = await Http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenAI returned {(int)response.StatusCode}: {body}");

        return ExtractOutputText(body);
    }

    /// <summary>
    /// Sends a Responses API request that may include images and function tools, and returns the
    /// parsed output items (function calls + any message text) plus the response id (for chaining
    /// the next turn via <c>previous_response_id</c>). Non-streaming. Throws on a non-success status.
    /// </summary>
    public async Task<OpenAiResponse> CreateResponseAsync(
        IReadOnlyList<InputItem> input,
        IReadOnlyList<FunctionTool>? tools,
        string? instructions,
        string? previousResponseId,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _secrets.Get(ApiKeySecretName)
            ?? throw new InvalidOperationException("No OpenAI API key has been configured.");

        var payload = new ResponsesToolRequest
        {
            Model = ModelOrDefault(_settings.Current.OpenAiModel),
            Instructions = instructions,
            Input = input,
            Tools = tools,
            ToolChoice = tools is {Count: > 0} ? "auto" : null,
            PreviousResponseId = previousResponseId
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, ResponsesEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(payload, options: OpenAiJson.Options);

        using var response = await Http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenAI returned {(int)response.StatusCode}: {body}");

        return ParseResponse(body);
    }

    /// <summary>Parses the response id and output items (function_call / message text).</summary>
    private static OpenAiResponse ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;

        var items = new List<OutputItem>();
        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                var type = item.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type == "function_call")
                {
                    items.Add(new FunctionCallItem(
                        item.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                        item.TryGetProperty("arguments", out var a) ? a.GetString() ?? string.Empty : string.Empty,
                        item.TryGetProperty("call_id", out var c) ? c.GetString() ?? string.Empty : string.Empty));
                }
                else if (type == "message" && item.TryGetProperty("content", out var content)
                                            && content.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder();
                    foreach (var part in content.EnumerateArray())
                    {
                        if (part.TryGetProperty("type", out var pt) && pt.GetString() == "output_text"
                            && part.TryGetProperty("text", out var text))
                        {
                            sb.Append(text.GetString());
                        }
                    }
                    items.Add(new MessageItem(sb.ToString()));
                }
            }
        }

        return new OpenAiResponse(id, items);
    }

    private static string ModelOrDefault(string? model)
        => string.IsNullOrWhiteSpace(model) ? AppSettings.DefaultOpenAiModel : model;

    /// <summary>
    /// Flattens a Responses API payload to plain text by concatenating every
    /// <c>output[].content[]</c> part of type <c>output_text</c>.
    /// </summary>
    private static string ExtractOutputText(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var type) && type.GetString() == "output_text"
                    && part.TryGetProperty("text", out var text))
                {
                    sb.Append(text.GetString());
                }
            }
        }

        return sb.ToString();
    }

    private sealed record ResponsesRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string Input);
}
