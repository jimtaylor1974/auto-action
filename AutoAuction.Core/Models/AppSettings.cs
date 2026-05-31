using System.Collections.ObjectModel;

namespace AutoAuction.Core.Models;

/// <summary>
/// General application settings, serialized to <c>settings.json</c> in the app root folder.
/// Plain data container (POCO) so it round-trips cleanly through System.Text.Json.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Default TradeMe bridge port (matches the roadmap's localhost:5999).</summary>
    public const int DefaultServerPort = 5999;

    /// <summary>Port the local Chrome-extension bridge server listens on.</summary>
    public int ServerPort { get; set; } = DefaultServerPort;

    /// <summary>Whether the bridge server starts automatically when the app launches.</summary>
    public bool ServerAutoStart { get; set; } = true;

    /// <summary>Configured AI providers (provider name + API key). Stored as plaintext.</summary>
    public ObservableCollection<AiProviderConfig> AiProviders { get; set; } = new();

    /// <summary>Name of the provider currently selected for AI requests (matches an entry above).</summary>
    public string? ActiveProviderName { get; set; }

    /// <summary>Model id to use for the active provider, e.g. "gpt-4o" or "claude-sonnet-4-6".</summary>
    public string? ActiveModel { get; set; }
}
