using System.IO;
using System.Diagnostics;

namespace SteamNativeBrowser.Services;

/// <summary>保证 Steam 以 CEF 调试模式运行（必要时自动重启 Steam）。</summary>
public static class SteamCefController
{
    public const string LaunchArguments = "-cef-enable-debugging -dev";
    public const int StartupTimeoutSeconds = 120;

    public static async Task<CefEndpoint> EnsureAsync(
        string steamDirectory,
        bool allowRestart,
        Action<string> log,
        CancellationToken ct = default)
    {
        var existing = await SteamCef.ProbeAsync(SteamCef.DefaultPort, ct).ConfigureAwait(false);
        if (existing is not null && SteamCef.LooksLikeSteam(existing))
        {
            log($"CEF 调试端口已经就绪：{existing.Browser}");
            return existing;
        }

        if (existing is not null)
        {
            throw new InvalidOperationException(
                $"端口 {SteamCef.DefaultPort} 已被其他程序占用（{existing.Browser}），请先释放该端口。");
        }

        if (!allowRestart)
        {
            throw new InvalidOperationException(
                "Steam 未开启 CEF 调试端口，且已关闭“需要时自动重启 Steam”。请手动用调试参数启动 Steam，或打开该选项。");
        }

        var steamExe = Path.Combine(steamDirectory, "steam.exe");
        if (!File.Exists(steamExe))
        {
            throw new FileNotFoundException("指定目录下找不到 steam.exe。", steamExe);
        }

        // 低完整性 / 沙箱进程、或注册表键被高权限改写过时，普通权限启动 Steam 会触发“注册表不可写”。
        // 此时改为以管理员身份启动 Steam（UAC），只提权 Steam，不提权界面本身。
        var needsElevation = !ProcessIntegrity.CanWriteSteamRegistry();
        if (needsElevation)
        {
            log("当前权限无法写入 Steam 注册表，将以管理员身份启动 Steam（会弹出 UAC 授权，请点“是”）。");
        }

        if (SteamLocator.IsSteamRunning())
        {
            log("Steam 正在运行但未开启调试端口，正在请求 Steam 退出…");
            await ShutdownAsync(steamExe, log, ct).ConfigureAwait(false);
        }

        log($"正在以调试模式启动 Steam：steam.exe {LaunchArguments}");
        await StartSteamAsync(steamExe, steamDirectory, needsElevation).ConfigureAwait(false);

        log($"等待 CEF 调试端口（最多 {StartupTimeoutSeconds} 秒）…");
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(StartupTimeoutSeconds))
        {
            await Task.Delay(1000, ct).ConfigureAwait(false);

            var endpoint = await SteamCef.ProbeAsync(SteamCef.DefaultPort, ct).ConfigureAwait(false);
            if (endpoint is not null && SteamCef.LooksLikeSteam(endpoint))
            {
                log($"CEF 调试端口已就绪：{endpoint.Browser}");
                return endpoint;
            }
        }

        throw new TimeoutException(
            "等待 Steam CEF 调试端口超时。请确认 Steam 能正常启动并保持登录，然后重试。");
    }

    private static async Task StartSteamAsync(string steamExe, string workingDirectory, bool elevated)
    {
        var info = new ProcessStartInfo(steamExe, LaunchArguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
        };

        if (elevated)
        {
            info.Verb = "runas";
        }

        try
        {
            // runas 会等待用户在 UAC 弹窗上做出选择，放到后台线程避免阻塞界面。
            await Task.Run(() => Process.Start(info)).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException(
                "已取消管理员授权（UAC），无法以当前权限启动 Steam。请重试并在 UAC 弹窗中点击“是”，" +
                "或先在资源管理器里手动启动 Steam。");
        }
    }

    private static async Task ShutdownAsync(string steamExe, Action<string> log, CancellationToken ct)
    {
        try
        {
            var info = new ProcessStartInfo(steamExe, "-shutdown")
            {
                WorkingDirectory = Path.GetDirectoryName(steamExe) ?? ".",
                UseShellExecute = true,
            };
            Process.Start(info);
        }
        catch (Exception ex)
        {
            log("发送 Steam 关闭指令失败：" + ex.Message);
        }

        for (var i = 0; i < 25; i++)
        {
            await Task.Delay(1000, ct).ConfigureAwait(false);
            if (!SteamLocator.IsSteamRunning())
            {
                log("Steam 已退出。");
                return;
            }
        }

        log("Steam 未在预期时间内退出，正在结束进程…");
        foreach (var name in new[] { "steam", "steamwebhelper" })
        {
            foreach (var process in SafeProcesses(name))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // 可能权限不足
                }
            }
        }

        for (var i = 0; i < 10 && SteamLocator.IsSteamRunning(); i++)
        {
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
    }

    private static IEnumerable<Process> SafeProcesses(string name)
    {
        try
        {
            return Process.GetProcessesByName(name);
        }
        catch
        {
            return Array.Empty<Process>();
        }
    }
}

