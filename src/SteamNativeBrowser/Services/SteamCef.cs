using System.Net.Http;
using System.Text.Json;

namespace SteamNativeBrowser.Services;

public sealed record CefEndpoint(int Port, string Browser, string UserAgent, string BrowserWebSocketUrl);

public sealed record CefTarget(string Id, string Type, string Title, string Url, string WebSocketDebuggerUrl);

/// <summary>Steam 内置 CEF 的远程调试端点（默认 127.0.0.1:8080）。</summary>
public static class SteamCef
{
    public const int DefaultPort = 8080;

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static async Task<CefEndpoint?> ProbeAsync(int port = DefaultPort, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(1500));

            var json = await Http
                .GetStringAsync($"http://127.0.0.1:{port}/json/version", cts.Token)
                .ConfigureAwait(false);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var browser = root.TryGetProperty("Browser", out var b) ? b.GetString() ?? string.Empty : string.Empty;
            var userAgent = root.TryGetProperty("User-Agent", out var u) ? u.GetString() ?? string.Empty : string.Empty;
            var wsUrl = root.TryGetProperty("webSocketDebuggerUrl", out var w) ? w.GetString() ?? string.Empty : string.Empty;

            if (!browser.StartsWith("Chrome/", StringComparison.OrdinalIgnoreCase) || wsUrl.Length == 0)
            {
                return null;
            }

            return new CefEndpoint(port, browser, userAgent, wsUrl);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>判断 8080 上跑的确实是 Steam 的 CEF，而不是别的本地服务。</summary>
    public static bool LooksLikeSteam(CefEndpoint endpoint) =>
        endpoint.UserAgent.Contains("Valve Steam Client", StringComparison.OrdinalIgnoreCase) ||
        endpoint.UserAgent.Contains("Steam", StringComparison.OrdinalIgnoreCase);

    public static async Task<IReadOnlyList<CefTarget>> ListTargetsAsync(
        int port = DefaultPort,
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        var json = await Http
            .GetStringAsync($"http://127.0.0.1:{port}/json/list", cts.Token)
            .ConfigureAwait(false);

        using var document = JsonDocument.Parse(json);
        var targets = new List<CefTarget>();

        foreach (var item in document.RootElement.EnumerateArray())
        {
            var wsUrl = item.TryGetProperty("webSocketDebuggerUrl", out var w) ? w.GetString() : null;
            if (string.IsNullOrEmpty(wsUrl))
            {
                continue;
            }

            targets.Add(new CefTarget(
                item.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                item.TryGetProperty("type", out var type) ? type.GetString() ?? string.Empty : string.Empty,
                item.TryGetProperty("title", out var title) ? title.GetString() ?? string.Empty : string.Empty,
                item.TryGetProperty("url", out var url) ? url.GetString() ?? string.Empty : string.Empty,
                wsUrl));
        }

        return targets;
    }
}

