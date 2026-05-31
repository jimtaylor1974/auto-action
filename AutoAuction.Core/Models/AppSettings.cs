namespace AutoAuction.Core.Models;

/// <summary>
/// General application settings, serialized to <c>settings.json</c> in the app root folder.
/// Plain data container (POCO) so it round-trips cleanly through System.Text.Json.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Default TradeMe bridge port (matches the roadmap's localhost:5999).</summary>
    public const int DefaultServerPort = 5999;

    /// <summary>Default OpenAI model used for listing generation.</summary>
    public const string DefaultOpenAiModel = "gpt-5.5";

    /// <summary>Port the local Chrome-extension bridge server listens on.</summary>
    public int ServerPort { get; set; } = DefaultServerPort;

    /// <summary>Whether the bridge server starts automatically when the app launches.</summary>
    public bool ServerAutoStart { get; set; } = true;

    /// <summary>
    /// OpenAI model id used for generation, e.g. "gpt-5.5". The API key is NOT stored here —
    /// it's held encrypted in the OS secret store (see <c>ISecretStore</c>).
    /// </summary>
    public string OpenAiModel { get; set; } = DefaultOpenAiModel;
}
