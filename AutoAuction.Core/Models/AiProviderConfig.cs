using ReactiveUI;

namespace AutoAuction.Core.Models;

/// <summary>
/// One AI provider credential: the LlmTornado provider name (e.g. "OpenAi", "Anthropic")
/// and its API key. ReactiveObject so the Settings grid can edit it inline.
/// </summary>
public class AiProviderConfig : ReactiveObject
{
    private string _provider = string.Empty;
    public string Provider
    {
        get => _provider;
        set => this.RaiseAndSetIfChanged(ref _provider, value);
    }

    private string _apiKey = string.Empty;
    public string ApiKey
    {
        get => _apiKey;
        set => this.RaiseAndSetIfChanged(ref _apiKey, value);
    }
}
