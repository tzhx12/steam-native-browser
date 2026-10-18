using System.Text.Json;

namespace SteamNativeBrowser.Services;

public sealed record LaunchOutcome(bool Success, string Message, string? Detail = null);

/// <summary>两种调出方式：内置（DevTools Target.createTarget）与插件（扩展 Service Worker）。</summary>
public static class SteamWindowLauncher
{
    public static async Task<LaunchOutcome> LaunchBuiltInAsync(
        CefEndpoint endpoint,
        string url,
        bool maximized,
        CancellationToken ct = default)
    {
        await using var client = await DevToolsClient
            .ConnectAsync(endpoint.BrowserWebSocketUrl, ct)
            .ConfigureAwait(false);

        var result = await client.SendAsync("Target.createTarget", new Dictionary<string, object?>
        {
            ["url"] = url,
            ["newWindow"] = true,
            ["windowState"] = maximized ? "maximized" : "normal",
        }, ct).ConfigureAwait(false);

        var targetId = result.ValueKind == JsonValueKind.Object && result.TryGetProperty("targetId", out var id)
            ? id.GetString()
            : null;

        return new LaunchOutcome(
            true,
            "已通过内置方式在 Steam 进程内创建原生 Chromium 窗口。",
            targetId);
    }

    public static async Task<LaunchOutcome> LaunchViaExtensionAsync(
        int port,
        string url,
        bool maximized,
        Action<string> log,
        CancellationToken ct = default)
    {
        var targets = await SteamCef.ListTargetsAsync(port, ct).ConfigureAwait(false);
        var workers = targets.Where(t => t.Type == "service_worker").ToList();

        if (workers.Count == 0)
        {
            log("暂未发现扩展后台，尝试唤醒…");
            await TryWakeExtensionsAsync(targets, log, ct).ConfigureAwait(false);
            targets = await SteamCef.ListTargetsAsync(port, ct).ConfigureAwait(false);
            workers = targets.Where(t => t.Type == "service_worker").ToList();
        }

        if (workers.Count == 0)
        {
            return new LaunchOutcome(
                false,
                "没有可用的扩展后台（Service Worker）。MV3 扩展的后台可能处于休眠状态；请改用“内置方式”，或先在 Steam 里打开一次扩展。");
        }

        log($"发现 {workers.Count} 个扩展后台，逐个尝试…");

        foreach (var worker in workers)
        {
            try
            {
                await using var client = await DevToolsClient
                    .ConnectAsync(worker.WebSocketDebuggerUrl, ct)
                    .ConfigureAwait(false);

                const string probeExpression =
                    "(function(){try{const m=chrome.runtime.getManifest();" +
                    "return JSON.stringify({name:m.name,windows:typeof chrome.windows!=='undefined'&&typeof chrome.windows.create==='function'});" +
                    "}catch(e){return 'ERR:'+e.message;}})()";

                var probe = await client.EvaluateAsync(probeExpression, false, ct).ConfigureAwait(false);

                if (string.IsNullOrEmpty(probe) || probe.StartsWith("ERR:", StringComparison.Ordinal))
                {
                    log($"跳过 {worker.Title}：{probe}");
                    continue;
                }

                using var probeDocument = JsonDocument.Parse(probe);
                var probeRoot = probeDocument.RootElement;

                var extensionName = probeRoot.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString() ?? worker.Title
                    : worker.Title;

                var supportsWindows = probeRoot.TryGetProperty("windows", out var windowsElement) &&
                                      windowsElement.ValueKind == JsonValueKind.True;

                if (!supportsWindows)
                {
                    log($"跳过 {extensionName}：该扩展不支持 chrome.windows.create。");
                    continue;
                }

                var state = maximized ? "maximized" : "normal";
                var expression =
                    $"chrome.windows.create({{url:{JsonSerializer.Serialize(url)},type:'normal',state:'{state}'}})" +
                    ".then(w=>JSON.stringify({id:w.id,state:w.state})).catch(e=>'ERR:'+e.message)";

                var result = await client.EvaluateAsync(expression, true, ct).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(result) && result.StartsWith("ERR:", StringComparison.Ordinal))
                {
                    log($"{extensionName} 调用失败：{result[4..]}");
                    continue;
                }

                return new LaunchOutcome(
                    true,
                    $"已通过扩展「{extensionName}」的 Service Worker 创建窗口。",
                    result);
            }
            catch (Exception ex)
            {
                log($"扩展 {worker.Title} 调用异常：{ex.Message}");
            }
        }

        return new LaunchOutcome(false, "找到了扩展后台，但没有任何一个成功创建窗口。请改用“内置方式”。");
    }

    private static async Task TryWakeExtensionsAsync(
        IReadOnlyList<CefTarget> targets,
        Action<string> log,
        CancellationToken ct)
    {
        var pages = targets
            .Where(t => t.Type == "page" && t.Url.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (pages.Count == 0)
        {
            return;
        }

        const string wakeExpression =
            "(function(){try{const p=chrome.runtime.sendMessage({type:'__steam_native_browser_wake__'});" +
            "if(p&&p.catch)p.catch(()=>{});}catch(e){}return 'ok';})()";

        foreach (var page in pages)
        {
            try
            {
                await using var client = await DevToolsClient
                    .ConnectAsync(page.WebSocketDebuggerUrl, ct)
                    .ConfigureAwait(false);
                await client.EvaluateAsync(wakeExpression, false, ct).ConfigureAwait(false);
            }
            catch
            {
                // 唤醒失败可以忽略
            }
        }

        log("已向扩展页面发送唤醒请求。");
        await Task.Delay(1200, ct).ConfigureAwait(false);
    }
}
