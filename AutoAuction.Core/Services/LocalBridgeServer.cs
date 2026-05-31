using System.Net;
using System.Text;
using System.Text.Json;

namespace AutoAuction.Core.Services;

/// <summary>
/// Tiny localhost HTTP server that exposes the active draft to the Chrome extension bridge.
/// Built on <see cref="HttpListener"/> so it needs no extra dependencies and runs cross-platform.
///
/// Endpoints (CORS-allowed for the TradeMe origin):
///   GET /api/drafts/active         -> the active draft's listing.json
///   GET /api/drafts/active/images  -> the active draft's images as base64
/// </summary>
public sealed class LocalBridgeServer : IDisposable
{
    private const string AllowedOrigin = "https://www.trademe.co.nz";

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif"
    };

    private readonly IActiveListingProvider _activeListing;

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }
    public int Port { get; private set; }

    /// <summary>Last error encountered while starting or serving (shown in the UI). Null when healthy.</summary>
    public string? LastError { get; private set; }

    /// <summary>Raised when the running state changes, so the UI can refresh its indicator.</summary>
    public event Action? StateChanged;

    public LocalBridgeServer(IActiveListingProvider activeListing)
    {
        _activeListing = activeListing;
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
        response.AddHeader("Access-Control-Allow-Methods", "GET, OPTIONS");
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
            default:
                WriteStatus(response, 404, "{\"error\":\"not found\"}");
                break;
        }
    }

    private void WriteActiveListing(HttpListenerResponse response)
    {
        var folder = _activeListing.ActiveFolderPath;
        var jsonPath = folder is null ? null : Path.Combine(folder, IDraftService.ListingFileName);

        if (jsonPath is null || !File.Exists(jsonPath))
        {
            WriteStatus(response, 404, "{\"error\":\"no active draft\"}");
            return;
        }

        // Serve the file as-is so the extension always gets the latest auto-saved data.
        var json = File.ReadAllText(jsonPath);
        WriteJson(response, 200, json);
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
