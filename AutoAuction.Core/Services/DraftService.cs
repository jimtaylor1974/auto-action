using System.Text.Json;
using AutoAuction.Core.Models;
using ImageMagick;

namespace AutoAuction.Core.Services;

/// <summary>A draft folder on disk together with its deserialized listing data.</summary>
public sealed record DraftFolder(string FolderPath, ListingModel Listing);

/// <summary>
/// Reads and writes the file-system "database": the Inbox of raw photos, and the
/// Drafts folders that each contain a <c>listing.json</c> plus the listing's images.
/// </summary>
public interface IDraftService
{
    /// <summary>File name used for the serialized listing inside each draft folder.</summary>
    const string ListingFileName = "listing.json";

    /// <summary>Returns the full paths of all image files currently in the Inbox.</summary>
    IReadOnlyList<string> GetInboxImages();

    /// <summary>
    /// Copies external image files into the Inbox (used by drag-and-drop and the file picker).
    /// Non-image files are ignored. Returns the number of files actually imported.
    /// </summary>
    int ImportToInbox(IEnumerable<string> sourcePaths);

    /// <summary>Loads every draft folder and its listing, newest first.</summary>
    IReadOnlyList<DraftFolder> GetDrafts();

    /// <summary>
    /// Creates a new draft: generates a unique folder under Drafts, moves the given
    /// inbox images into it, and writes a default <c>listing.json</c>.
    /// </summary>
    DraftFolder CreateDraft(IEnumerable<string> inboxImagePaths);

    /// <summary>
    /// Discards a draft: moves its images back into the Inbox and deletes the draft folder
    /// (including its <c>listing.json</c>). Returns the number of images moved back.
    /// </summary>
    int DiscardDraft(string draftFolderPath);

    /// <summary>Loads the listing for a specific draft folder (creating a default if missing).</summary>
    ListingModel LoadListing(string draftFolderPath);

    /// <summary>Serializes the listing back to <c>listing.json</c> in its draft folder.</summary>
    void SaveListing(string draftFolderPath, ListingModel listing);
}

/// <inheritdoc />
public sealed class DraftService : IDraftService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif"
    };

    // Formats that aren't natively displayable by Avalonia, so they are converted to JPG on import.
    private static readonly HashSet<string> ConvertibleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".heic", ".heif"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly IAppConfig _config;

    public DraftService(IAppConfig config)
    {
        _config = config;
    }

    public IReadOnlyList<string> GetInboxImages()
    {
        if (!Directory.Exists(_config.InboxPath))
            return Array.Empty<string>();

        return Directory.EnumerateFiles(_config.InboxPath)
            .Where(IsImage)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public int ImportToInbox(IEnumerable<string> sourcePaths)
    {
        Directory.CreateDirectory(_config.InboxPath);

        var imported = 0;
        foreach (var sourcePath in sourcePaths)
        {
            if (!File.Exists(sourcePath))
                continue;

            if (IsConvertible(sourcePath))
            {
                if (TryConvertToInbox(sourcePath))
                    imported++;
            }
            else if (IsImage(sourcePath))
            {
                var destPath = GetUniqueDestination(_config.InboxPath, Path.GetFileName(sourcePath));
                File.Copy(sourcePath, destPath);
                imported++;
            }
        }

        return imported;
    }

    /// <summary>
    /// Converts a HEIC/HEIF file to JPG and writes it into the Inbox.
    /// Returns false (rather than throwing) if the file cannot be decoded.
    /// </summary>
    private bool TryConvertToInbox(string sourcePath)
    {
        try
        {
            var jpgName = Path.GetFileNameWithoutExtension(sourcePath) + ".jpg";
            var destPath = GetUniqueDestination(_config.InboxPath, jpgName);

            using var image = new MagickImage(sourcePath);

            // Phone photos store rotation in EXIF rather than the pixels. Bake the rotation
            // into the pixels (and clear the now-redundant EXIF orientation) so the JPG
            // displays upright everywhere.
            image.AutoOrient();

            image.Format = MagickFormat.Jpeg;
            image.Write(destPath);
            return true;
        }
        catch (MagickException)
        {
            return false;
        }
    }

    public IReadOnlyList<DraftFolder> GetDrafts()
    {
        if (!Directory.Exists(_config.DraftsPath))
            return Array.Empty<DraftFolder>();

        var drafts = new List<DraftFolder>();
        foreach (var folder in Directory.EnumerateDirectories(_config.DraftsPath))
        {
            var listing = LoadListing(folder);
            drafts.Add(new DraftFolder(folder, listing));
        }

        return drafts
            .OrderByDescending(d => d.Listing.CreatedUtc)
            .ToList();
    }

    public DraftFolder CreateDraft(IEnumerable<string> inboxImagePaths)
    {
        var listing = new ListingModel();
        var folder = Path.Combine(_config.DraftsPath, listing.Id);
        Directory.CreateDirectory(folder);

        foreach (var sourcePath in inboxImagePaths)
        {
            if (!File.Exists(sourcePath))
                continue;

            var destPath = GetUniqueDestination(folder, Path.GetFileName(sourcePath));
            File.Move(sourcePath, destPath);
            listing.LocalImagePaths.Add(Path.GetFileName(destPath));
        }

        SaveListing(folder, listing);
        return new DraftFolder(folder, listing);
    }

    public int DiscardDraft(string draftFolderPath)
    {
        if (!Directory.Exists(draftFolderPath))
            return 0;

        Directory.CreateDirectory(_config.InboxPath);

        var moved = 0;
        foreach (var file in Directory.EnumerateFiles(draftFolderPath))
        {
            if (!IsImage(file))
                continue;

            var destPath = GetUniqueDestination(_config.InboxPath, Path.GetFileName(file));
            File.Move(file, destPath);
            moved++;
        }

        // Remove the folder and everything left in it (listing.json, any stray files).
        Directory.Delete(draftFolderPath, recursive: true);
        return moved;
    }

    public ListingModel LoadListing(string draftFolderPath)
    {
        var jsonPath = Path.Combine(draftFolderPath, IDraftService.ListingFileName);
        if (File.Exists(jsonPath))
        {
            try
            {
                var json = File.ReadAllText(jsonPath);
                var model = JsonSerializer.Deserialize<ListingModel>(json, JsonOptions);
                if (model is not null)
                {
                    // The folder name is the authoritative id even if the file disagrees.
                    model.Id = Path.GetFileName(draftFolderPath);
                    return model;
                }
            }
            catch (JsonException)
            {
                // Corrupt or partially written file - fall through to a fresh default
                // so the draft is still editable rather than crashing the app.
            }
        }

        var fresh = new ListingModel { Id = Path.GetFileName(draftFolderPath) };
        return fresh;
    }

    public void SaveListing(string draftFolderPath, ListingModel listing)
    {
        Directory.CreateDirectory(draftFolderPath);
        var jsonPath = Path.Combine(draftFolderPath, IDraftService.ListingFileName);

        // Write to a temp file then move into place so a crash mid-write can never
        // leave a half-written listing.json.
        var tempPath = jsonPath + ".tmp";
        var json = JsonSerializer.Serialize(listing, JsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, jsonPath, overwrite: true);
    }

    private static bool IsImage(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path));

    private static bool IsConvertible(string path) =>
        ConvertibleExtensions.Contains(Path.GetExtension(path));

    private static string GetUniqueDestination(string folder, string fileName)
    {
        var dest = Path.Combine(folder, fileName);
        if (!File.Exists(dest))
            return dest;

        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var counter = 1;
        do
        {
            dest = Path.Combine(folder, $"{name}_{counter}{ext}");
            counter++;
        }
        while (File.Exists(dest));

        return dest;
    }
}
