namespace AutoAuction.Core.Services;

/// <summary>
/// Defines the on-disk folder structure that acts as the application's database.
/// The folder a listing lives in dictates its state:
/// Inbox (raw photos) -> Drafts (work in progress) -> Listed (published).
/// </summary>
public interface IAppConfig
{
    /// <summary>Root data folder that contains the Inbox/Drafts/Listed sub-folders.</summary>
    string RootPath { get; }

    /// <summary>"1_Inbox" - raw photos are dropped here.</summary>
    string InboxPath { get; }

    /// <summary>"2_Drafts" - one sub-folder per work-in-progress listing.</summary>
    string DraftsPath { get; }

    /// <summary>"3_Listed" - listings that have been published.</summary>
    string ListedPath { get; }

    /// <summary>Full path to "settings.json" in the root data folder (general app settings).</summary>
    string SettingsPath { get; }

    /// <summary>Creates the root and all sub-folders if they do not already exist.</summary>
    void Initialize();
}
