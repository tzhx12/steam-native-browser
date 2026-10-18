using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace SteamNativeBrowser.Services;

/// <summary>极简 Chrome DevTools Protocol 客户端：单连接、顺序请求。</summary>
public sealed class DevToolsClient : IAsyncDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly ClientWebSocket _socket;
    private int _nextId;

    private DevToolsClient(ClientWebSocket socket) => _socket = socket;

    public static async Task<DevToolsClient> ConnectAsync(string webSocketUrl, CancellationToken ct = default)
    {
        var socket = new ClientWebSocket();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await socket.ConnectAsync(new Uri(webSocketUrl), cts.Token).ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new DevToolsClient(socket);
    }

    public async Task<JsonElement> SendAsync(string method, object? parameters = null, CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _nextId);

        var message = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["method"] = method,
        };
        if (parameters is not null)
        {
            message["params"] = parameters;
        }

        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        using (var sendCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            sendCts.CancelAfter(RequestTimeout);
            await _socket
                .SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, sendCts.Token)
                .ConfigureAwait(false);
        }

        var buffer = new byte[32 * 1024];
        using var frame = new MemoryStream();
        while (true)
        {
            using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            receiveCts.CancelAfter(RequestTimeout);

            var result = await _socket
                .ReceiveAsync(new ArraySegment<byte>(buffer), receiveCts.Token)
                .ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new IOException("DevTools 连接已被对端关闭。");
            }

            frame.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            var text = Encoding.UTF8.GetString(frame.ToArray());
            frame.SetLength(0);

            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            if (!root.TryGetProperty("id", out var idElement) ||
                !idElement.TryGetInt32(out var replyId) ||
                replyId != id)
            {
                continue; // 事件通知，忽略
            }

            if (root.TryGetProperty("error", out var error))
            {
                var errorMessage = error.TryGetProperty("message", out var m) ? m.GetString() : error.ToString();
                throw new InvalidOperationException(errorMessage ?? "未知的 DevTools 错误。");
            }

            return root.TryGetProperty("result", out var resultElement) ? resultElement.Clone() : default;
        }
    }

    public async Task<string?> EvaluateAsync(string expression, bool awaitPromise = false, CancellationToken ct = default)
    {
        var result = await SendAsync("Runtime.evaluate", new Dictionary<string, object?>
        {
            ["expression"] = expression,
            ["returnByValue"] = true,
            ["awaitPromise"] = awaitPromise,
            ["userGesture"] = true,
        }, ct).ConfigureAwait(false);

        if (result.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (result.TryGetProperty("exceptionDetails", out var details))
        {
            if (details.TryGetProperty("exception", out var exception) &&
                exception.TryGetProperty("description", out var description))
            {
                return "ERR:" + description.GetString();
            }

            return "ERR:" + (details.TryGetProperty("text", out var text) ? text.GetString() : "unknown");
        }

        if (result.TryGetProperty("result", out var inner) &&
            inner.TryGetProperty("value", out var value))
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => value.ToString(),
            };
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cts.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            // 关闭失败无所谓
        }
        finally
        {
            _socket.Dispose();
        }
    }
}

