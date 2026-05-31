namespace AutoAuction.Core.Services;

/// <inheritdoc />
public sealed class AppConfig : IAppConfig
{
    public const string InboxFolderName = "1_Inbox";
    public const string DraftsFolderName = "2_Drafts";
    public const string ListedFolderName = "3_Listed";

    public string RootPath { get; }
    public string InboxPath { get; }
    public string DraftsPath { get; }
    public string ListedPath { get; }

    /// <summary>
    /// Creates the configuration. When <paramref name="rootPath"/> is null the data folder
    /// defaults to "AutoAuction" under the user's Documents folder.
    /// </summary>
    public AppConfig(string? rootPath = null)
    {
        RootPath = rootPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AutoAuction");

        InboxPath = Path.Combine(RootPath, InboxFolderName);
        DraftsPath = Path.Combine(RootPath, DraftsFolderName);
        ListedPath = Path.Combine(RootPath, ListedFolderName);
    }

    public void Initialize()
    {
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(InboxPath);
        Directory.CreateDirectory(DraftsPath);
        Directory.CreateDirectory(ListedPath);
    }
}
