using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SteamNativeBrowser.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace SteamNativeBrowser.WinUI.Pages;

public sealed partial class HomePage : Page
{
    private static readonly Color Green = Color.FromArgb(255, 15, 123, 15);
    private static readonly Color Amber = Color.FromArgb(255, 157, 93, 0);
    private static readonly Color Red = Color.FromArgb(255, 196, 43, 28);
    private static readonly Color Gray = Color.FromArgb(255, 154, 154, 154);

    private readonly ObservableCollection<string> _log = new();
    private readonly DispatcherQueueTimer _statusTimer;

    private string? _steamPath;
    private bool _busy;
    private bool _refreshing;

    public HomePage()
    {
        InitializeComponent();
        LogItems.ItemsSource = _log;

        _statusTimer = DispatcherQueue.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromSeconds(3);
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync(false);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        try
        {
            CheckEnvironment();

            _steamPath ??= SteamLocator.FindSteamPath();
            UpdatePathUi();

            AppendLog(_steamPath is null
                ? "未自动找到 Steam，请点击“浏览…”手动指定安装目录。"
                : $"已定位 Steam：{_steamPath}");

            await RefreshStatusAsync(true);
            _statusTimer.Start();
        }
        catch (Exception ex)
        {
            AppendLog("初始化失败：" + ex.Message);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _statusTimer.Stop();
        base.OnNavigatedFrom(e);
    }

    // ================= 环境 / 状态 =================

    private void CheckEnvironment()
    {
        var rid = ProcessIntegrity.GetIntegrityRid();
        var canWriteRegistry = ProcessIntegrity.CanWriteSteamRegistry();
        var elevated = ProcessIntegrity.IsElevated();

        if (canWriteRegistry)
        {
            EnvInfoBar.IsOpen = false;
            return;
        }

        var level = rid.HasValue ? $"0x{rid.Value:X4}" : "未知";
        var levelName = rid is int value && value < 0x2000 ? "Low" : "Medium 或更高";

        EnvInfoBar.IsOpen = true;
        EnvInfoText.Text =
            $"完整性：{level}（{levelName}）· 注册表可写：否。启动 Steam 时会自动请求管理员权限（UAC），" +
            "请在弹窗中点“是”；也可以复制路径后用 Win+R 以普通权限重新运行本程序。";
        EnvInfoBar.ActionButton.IsEnabled = !elevated;

        AppendLog("警告：当前进程无法写入 Steam 注册表；启动 Steam 时将请求 UAC 提权。");
    }

    private async Task RefreshStatusAsync(bool verbose)
    {
        if (_busy || _refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            _steamPath ??= SteamLocator.FindSteamPath();
            UpdatePathUi();

            var running = SteamLocator.IsSteamRunning();
            var probe = await SteamCef.ProbeAsync();
            var cef = probe is not null && SteamCef.LooksLikeSteam(probe) ? probe : null;
            var portConflict = probe is not null && cef is null;

            var extensionCount = 0;
            if (cef is not null)
            {
                try
                {
                    var targets = await SteamCef.ListTargetsAsync();
                    extensionCount = targets.Count(t => t.Type == "service_worker");
                }
                catch
                {
                    // 忽略
                }
            }

            SetDot(SteamDot, running ? Green : Gray);
            SteamStateText.Text = running ? "运行中" : "未运行";

            if (cef is not null)
            {
                SetDot(CefDot, Green);
                CefStateText.Text = "已就绪";
                ToolTipService.SetToolTip(CefStateText, $"{cef.Browser} · 127.0.0.1:{cef.Port}");
            }
            else if (portConflict)
            {
                SetDot(CefDot, Red);
                CefStateText.Text = "端口占用";
            }
            else
            {
                SetDot(CefDot, running ? Amber : Gray);
                CefStateText.Text = "未开启";
            }

            SetDot(ExtDot, extensionCount > 0 ? Green : Gray);
            ExtStateText.Text = extensionCount > 0 ? $"{extensionCount} 个可用" : "无";

            if (verbose)
            {
                AppendLog(running ? "Steam 正在运行。" : "Steam 当前未运行。");

                if (cef is not null)
                {
                    AppendLog($"CEF 调试端口已就绪（{cef.Browser}）。");
                }
                else if (portConflict)
                {
                    AppendLog($"端口 {SteamCef.DefaultPort} 被其他程序占用，请先释放该端口。");
                }
                else
                {
                    AppendLog("CEF 调试端口未开启；点击“一键调出”时会自动处理。");
                }

                AppendLog(extensionCount > 0
                    ? $"检测到 {extensionCount} 个扩展后台，可使用“插件方式”。"
                    : "未检测到扩展后台，建议使用“内置方式”。");
            }
        }
        catch (Exception ex)
        {
            AppendLog("状态检测失败：" + ex.Message);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdatePathUi()
    {
        PathText.Text = string.IsNullOrEmpty(_steamPath) ? "未检测到 Steam" : _steamPath;
        ToolTipService.SetToolTip(PathText, _steamPath);
    }

    private static void SetDot(Microsoft.UI.Xaml.Shapes.Ellipse dot, Color color)
    {
        dot.Fill = new SolidColorBrush(color);
    }

    // ================= 操作 =================

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var url = NormalizeUrl(UrlBox.Text);
        if (url is null)
        {
            AppendLog("地址无效，请输入 http 或 https 链接。");
            return;
        }

        var maximized = MaximizeToggle.IsOn;
        var allowRestart = AutoRestartToggle.IsOn;
        var useExtension = GetSelectedMode() == "extension";

        SetBusy(true);
        try
        {
            var steamPath = _steamPath ?? SteamLocator.FindSteamPath();
            if (steamPath is null)
            {
                AppendLog("未找到 Steam。请先点击“浏览…”指定包含 steam.exe 的目录。");
                return;
            }

            _steamPath = steamPath;
            UpdatePathUi();

            var endpoint = await SteamCef.ProbeAsync();
            if (endpoint is null || !SteamCef.LooksLikeSteam(endpoint))
            {
                if (endpoint is not null)
                {
                    AppendLog($"端口 {SteamCef.DefaultPort} 被其他程序占用（{endpoint.Browser}），无法继续。");
                    return;
                }

                endpoint = await SteamCefController.EnsureAsync(steamPath, allowRestart, AppendLog);
            }
            else
            {
                AppendLog("CEF 调试端口已就绪，跳过启动步骤。");
            }

            var outcome = useExtension
                ? await SteamWindowLauncher.LaunchViaExtensionAsync(endpoint.Port, url, maximized, AppendLog)
                : await SteamWindowLauncher.LaunchBuiltInAsync(endpoint, url, maximized);

            if (!outcome.Success && useExtension)
            {
                AppendLog("插件方式未能创建窗口，自动改用内置方式重试…");
                outcome = await SteamWindowLauncher.LaunchBuiltInAsync(endpoint, url, maximized);
            }

            AppendLog(outcome.Message);
            if (!string.IsNullOrEmpty(outcome.Detail))
            {
                AppendLog("详情：" + outcome.Detail);
            }
        }
        catch (Exception ex)
        {
            AppendLog("失败：" + ex.Message);
        }
        finally
        {
            SetBusy(false);
            await RefreshStatusAsync(false);
        }
    }

    private async void DebugOnly_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var allowRestart = AutoRestartToggle.IsOn;
        SetBusy(true);
        try
        {
            var steamPath = _steamPath ?? SteamLocator.FindSteamPath();
            if (steamPath is null)
            {
                AppendLog("未找到 Steam。请先点击“浏览…”指定安装目录。");
                return;
            }

            var endpoint = await SteamCef.ProbeAsync();
            if (endpoint is null || !SteamCef.LooksLikeSteam(endpoint))
            {
                endpoint = await SteamCefController.EnsureAsync(steamPath, allowRestart, AppendLog);
            }
            else
            {
                AppendLog($"CEF 调试端口已就绪：{endpoint.Browser}");
            }

            AppendLog($"现在可以在浏览器打开 http://localhost:{endpoint.Port} 查看可调试页面。");
        }
        catch (Exception ex)
        {
            AppendLog("失败：" + ex.Message);
        }
        finally
        {
            SetBusy(false);
            await RefreshStatusAsync(false);
        }
    }

    private async void OpenPanel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri($"http://localhost:{SteamCef.DefaultPort}"));
            AppendLog("已在默认浏览器打开调试面板。");
        }
        catch (Exception ex)
        {
            AppendLog("无法打开调试面板：" + ex.Message);
        }
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
                ViewMode = PickerViewMode.List,
            };
            picker.FileTypeFilter.Add(".exe");

            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                _steamPath = Path.GetDirectoryName(file.Path);
                UpdatePathUi();
                AppendLog("已指定 Steam 目录：" + _steamPath);
                await RefreshStatusAsync(true);
            }
        }
        catch (Exception ex)
        {
            AppendLog("选择目录失败：" + ex.Message);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _steamPath = SteamLocator.FindSteamPath();
        UpdatePathUi();
        AppendLog("正在重新检测 Steam…");
        await RefreshStatusAsync(true);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_steamPath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_steamPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendLog("无法打开目录：" + ex.Message);
        }
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            var package = new DataPackage();
            package.SetText(path);
            Clipboard.SetContent(package);
            AppendLog("已复制程序路径：" + path);
        }
        catch (Exception ex)
        {
            AppendLog("复制失败：" + ex.Message);
        }
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(string.Join(Environment.NewLine, _log));
            Clipboard.SetContent(package);
        }
        catch
        {
            // 忽略
        }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => _log.Clear();

    private void Elevate_Click(object sender, RoutedEventArgs e)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            AppendLog("无法定位当前程序路径。");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory,
            });

            App.Current.Exit();
        }
        catch (Exception ex)
        {
            AppendLog("以管理员身份重启失败（可能取消了 UAC）：" + ex.Message);
        }
    }

    private void ModeSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        ModeDescriptionText.Text = GetSelectedMode() == "extension"
            ? "插件方式会调用已安装扩展的后台 Service Worker（chrome.windows.create），需要至少一个扩展在运行。"
            : "内置方式不需要任何插件，通过 DevTools 协议直接在 Steam 进程里创建窗口。";
    }

    private string GetSelectedMode() =>
        (ModeSelector.SelectedItem as SelectorBarItem)?.Tag as string ?? "builtin";

    // ================= 辅助 =================

    private void SetBusy(bool busy)
    {
        _busy = busy;

        LaunchButton.IsEnabled = !busy;
        DebugOnlyButton.IsEnabled = !busy;
        OpenPanelButton.IsEnabled = !busy;
        BrowseButton.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
        OpenFolderButton.IsEnabled = !busy;
        BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AppendLog(string message)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => AppendLog(message));
            return;
        }

        _log.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        if (_log.Count > 500)
        {
            _log.RemoveAt(0);
        }

        DispatcherQueue.TryEnqueue(() => LogScroll.ChangeView(null, LogScroll.ScrollableHeight, null));
    }

    private static string? NormalizeUrl(string? input)
    {
        var text = (input ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.ToString()
            : null;
    }
}
