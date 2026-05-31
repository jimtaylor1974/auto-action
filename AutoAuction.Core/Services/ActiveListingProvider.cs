namespace AutoAuction.Core.Services;

/// <summary>
/// Holds the folder path of the draft currently open in the editor. The local bridge
/// server reads this to know which listing the Chrome extension should fill, and reads
/// the listing fresh from disk so it always sees the latest auto-saved data.
/// </summary>
public interface IActiveListingProvider
{
    /// <summary>Folder path of the active draft, or null if no draft is open.</summary>
    string? ActiveFolderPath { get; set; }
}

/// <inheritdoc />
public sealed class ActiveListingProvider : IActiveListingProvider
{
    public string? ActiveFolderPath { get; set; }
}
