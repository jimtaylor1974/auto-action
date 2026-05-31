using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutoAuction.Core.Services;

/// <summary>
/// Tiny localhost HTTP server that exposes the active draft to the Chrome extension bridge.
/// Built on <see cref="HttpListener"/> so it needs no extra dependencies and runs cross-platform.
///
/// Endpoints (CORS-allowed for the TradeMe origin):
///   GET  /api/drafts/active         -> the active draft's listing (+ CategoryPath names)
///   GET  /api/drafts/active/images  -> the active draft's images as base64
///   POST /api/drafts/active/listed  -> { listingId, listingUrl }: mark the active draft Listed
/// </summary>
public sealed class LocalBridgeServer : IDisposable
{
    private const string AllowedOrigin = "https://www.trademe.co.nz";

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif"
    };

    private readonly IActiveListingProvider _activeListing;
    private readonly IDraftService _draftService;
    private readonly ICategoryService _categories;

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }
    public int Port { get; private set; }

    /// <summary>Last error encountered while starting or serving (shown in the UI). Null when healthy.</summary>
    public string? LastError { get; private set; }

    /// <summary>Raised when the running state changes, so the UI can refresh its indicator.</summary>
    public event Action? StateChanged;

    /// <summary>Raised after the extension reports a published listing (the draft is now Listed).</summary>
    public event Action<DraftFolder>? ListingPublished;

    public LocalBridgeServer(
        IActiveListingProvider activeListing,
        IDraftService draftService,
        ICategoryService categories)
    {
        _activeListing = activeListing;
        _draftService = draftService;
        _categories = categories;
    }

    public string Url => $"http://localhost:{Port}";

    public void Start(int port)
    {
        if (IsRunning && Port == port)
            return;

        Stop();

        try
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            listener.Start();

            _listener = listener;
            _cts = new CancellationTokenSource();
            Port = port;
            IsRunning = true;
            LastError = null;

            _ = ListenLoopAsync(listener, _cts.Token);
        }
        catch (Exception ex)
        {
            IsRunning = false;
            LastError = ex.Message;
        }

        StateChanged?.Invoke();
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
        }
        catch
        {
            // Ignore shutdown races; we're tearing down anyway.
        }
        finally
        {
            _listener = null;
            _cts = null;
            if (IsRunning)
            {
                IsRunning = false;
                StateChanged?.Invoke();
            }
        }
    }

    public void Restart(int port) => Start(port);

    private async Task ListenLoopAsync(HttpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception)
            {
                // Listener stopped/disposed - exit the loop quietly.
                break;
            }

            // Handle each request without blocking the accept loop.
            _ = Task.Run(() => HandleRequestSafely(context));
        }
    }

    private void HandleRequestSafely(HttpListenerContext context)
    {
        try
        {
            HandleRequest(context);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            TryWriteStatus(context, 500);
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // CORS headers on every response so the extension (on trademe.co.nz) can call us.
        response.AddHeader("Access-Control-Allow-Origin", AllowedOrigin);
        response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

        if (string.Equals(request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = 204;
            response.Close();
            return;
        }

        var path = request.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;

        switch (path)
        {
            case "/api/drafts/active":
                WriteActiveListing(response);
                break;
            case "/api/drafts/active/images":
                WriteActiveImages(response);
                break;
            case "/api/drafts/active/listed":
                HandleListed(request, response);
                break;
            default:
                WriteStatus(response, 404, "{\"error\":\"not found\"}");
                break;
        }
    }

    private void WriteActiveListing(HttpListenerResponse response)
    {
        var folder = _activeListing.ActiveFolderPath;

        if (folder is null || !Directory.Exists(folder))
        {
            WriteStatus(response, 404, "{\"error\":\"no active draft\"}");
            return;
        }

        // Load the latest listing and enrich it with the category name path so the extension can
        // drive the TradeMe category picker (which is name-based) from the stored category number.
        var listing = _draftService.LoadListing(folder);
        var node = JsonSerializer.SerializeToNode(listing)!.AsObject();
        var segments = _categories.GetPathSegments(listing.CategoryId);
        node["CategoryPath"] = new JsonArray(segments.Select(s => JsonValue.Create(s)).ToArray<JsonNode?>());

        WriteJson(response, 200, node.ToJsonString());
    }

    /// <summary>POST: the extension reports a published listing; mark the active draft Listed.</summary>
    private void HandleListed(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (!string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            WriteStatus(response, 405, "{\"error\":\"method not allowed\"}");
            return;
        }

        var folder = _activeListing.ActiveFolderPath;
        if (folder is null || !Directory.Exists(folder))
        {
            WriteStatus(response, 404, "{\"error\":\"no active draft\"}");
            return;
        }

        string? listingId = null, listingUrl = null;
        try
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
            using var doc = JsonDocument.Parse(reader.ReadToEnd());
            var root = doc.RootElement;
            if (root.TryGetProperty("listingId", out var id)) listingId = id.GetString();
            if (root.TryGetProperty("listingUrl", out var url)) listingUrl = url.GetString();
        }
        catch (JsonException)
        {
            WriteStatus(response, 400, "{\"error\":\"invalid json\"}");
            return;
        }

        DraftFolder listed;
        try
        {
            listed = _draftService.MarkListed(folder);
        }
        catch (Exception ex)
        {
            WriteStatus(response, 500, JsonSerializer.Serialize(new {error = ex.Message}));
            return;
        }

        listed.Listing.TradeMeListingId = listingId;
        listed.Listing.TradeMeListingUrl = listingUrl;
        _draftService.SaveListing(listed.FolderPath, listed.Listing);

        // The draft has moved to 3_Listed; it's no longer the active draft.
        _activeListing.ActiveFolderPath = null;
        ListingPublished?.Invoke(listed);

        WriteStatus(response, 200, "{\"ok\":true}");
    }

    private void WriteActiveImages(HttpListenerResponse response)
    {
        var folder = _activeListing.ActiveFolderPath;

        if (folder is null || !Directory.Exists(folder))
        {
            WriteStatus(response, 404, "{\"error\":\"no active draft\"}");
            return;
        }

        var images = Directory.EnumerateFiles(folder)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(f => new
            {
                fileName = Path.GetFileName(f),
                contentType = ContentTypeFor(f),
                base64 = Convert.ToBase64String(File.ReadAllBytes(f))
            })
            .ToList();

        var json = JsonSerializer.Serialize(images);
        WriteJson(response, 200, json);
    }

    private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".gif" => "image/gif",
        _ => "image/jpeg"
    };

    private static void WriteJson(HttpListenerResponse response, int status, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        response.StatusCode = status;
        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.Close();
    }

    private static void WriteStatus(HttpListenerResponse response, int status, string json)
        => WriteJson(response, status, json);

    private static void TryWriteStatus(HttpListenerContext context, int status)
    {
        try
        {
            context.Response.StatusCode = status;
            context.Response.Close();
        }
        catch
        {
            // Response may already be closed.
        }
    }

    public void Dispose() => Stop();
}
