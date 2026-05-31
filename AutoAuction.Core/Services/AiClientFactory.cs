using AutoAuction.Core.Models;
using LlmTornado;
using LlmTornado.Chat.Models;
using LlmTornado.Code;

namespace AutoAuction.Core.Services;

/// <summary>
/// Builds a provider-agnostic <see cref="TornadoApi"/> (LlmTornado) from the configured AI
/// providers, and validates individual keys. Actual listing generation is a later phase;
/// this is the seam it plugs into.
/// </summary>
public sealed class AiClientFactory
{
    private readonly ISettingsService _settings;

    public AiClientFactory(ISettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>True when an active provider is selected and has a non-empty key.</summary>
    public bool HasActiveProvider
    {
        get
        {
            var s = _settings.Current;
            return !string.IsNullOrWhiteSpace(s.ActiveProviderName)
                && s.AiProviders.Any(p => p.Provider == s.ActiveProviderName
                                          && !string.IsNullOrWhiteSpace(p.ApiKey));
        }
    }

    /// <summary>
    /// Builds a Tornado client holding every configured provider key (provider is auto-selected
    /// by model at call time). Returns null when no usable provider is configured.
    /// </summary>
    public TornadoApi? CreateApi()
    {
        var auths = new List<ProviderAuthentication>();
        foreach (var p in _settings.Current.AiProviders)
        {
            if (!string.IsNullOrWhiteSpace(p.ApiKey) && TryParseProvider(p.Provider, out var prov))
                auths.Add(new ProviderAuthentication(prov, p.ApiKey));
        }

        return auths.Count == 0 ? null : new TornadoApi(auths);
    }

    /// <summary>Sends a tiny prompt to validate a provider/key. Returns success + a message.</summary>
    public async Task<(bool Ok, string Message)> TestAsync(AiProviderConfig config)
    {
        if (!TryParseProvider(config.Provider, out var prov))
            return (false, $"Unknown provider '{config.Provider}'.");
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            return (false, "API key is empty.");

        try
        {
            var api = new TornadoApi(new List<ProviderAuthentication>
            {
                new(prov, config.ApiKey)
            });

            var response = await api.Chat.CreateConversation(DefaultModelFor(prov))
                .AppendUserInput("Reply with the single word: OK")
                .GetResponse();

            return (true, string.IsNullOrWhiteSpace(response) ? "Connected." : $"Connected: {response.Trim()}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>The set of provider names we surface in the Settings dropdown.</summary>
    public static IReadOnlyList<string> KnownProviders { get; } = new[]
    {
        nameof(LLmProviders.OpenAi),
        nameof(LLmProviders.Anthropic),
        nameof(LLmProviders.Google),
        nameof(LLmProviders.Cohere),
        nameof(LLmProviders.Groq),
        nameof(LLmProviders.Mistral),
        nameof(LLmProviders.DeepSeek),
        nameof(LLmProviders.XAi)
    };

    private static bool TryParseProvider(string provider, out LLmProviders result)
        => Enum.TryParse(provider, ignoreCase: true, out result) && Enum.IsDefined(result);

    private static ChatModel DefaultModelFor(LLmProviders provider) => provider switch
    {
        LLmProviders.Anthropic => ChatModel.Anthropic.Claude46.Sonnet,
        _ => ChatModel.OpenAi.Gpt4.O
    };
}
