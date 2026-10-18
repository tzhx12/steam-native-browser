using System.Windows.Controls;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SteamNativeBrowser.Interop;
using SteamNativeBrowser.Services;

namespace SteamNativeBrowser;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    private string? _steamPath;
    private bool _busy;
    private bool _refreshing;

    public MainWindow()
    {
        InitializeComponent();
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync(false);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

        var dark = WindowEffects.IsDarkTheme();
        var mica = WindowEffects.TryEnableMica(hwnd, dark);

        if (!mica || dark)
        {
            var background = (Brush)FindResource("WindowBackgroundBrush");
            Background = background;
            RootSurface.Background = background;
        }

        UpdateMaximizeGlyph();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_GETMINMAXINFO)
        {
            WindowEffects.ClampToWorkArea(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    // ================= 生命周期 =================

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _steamPath = SteamLocator.FindSteamPath();
        UpdatePathUi();

        AppendLog(_steamPath is null
            ? "未自动找到 Steam，请点击“浏览…”手动指定安装目录。"
            : $"已定位 Steam：{_steamPath}");

        await RefreshStatusAsync(true);
        PageScroll.ScrollToTop();
        _statusTimer.Start();
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
            if (_steamPath is null)
            {
                _steamPath = SteamLocator.FindSteamPath();
                UpdatePathUi();
            }

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
                    // 忽略枚举失败
                }
            }

            UpdateStatusUi(running, cef, extensionCount, portConflict);

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
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdateStatusUi(bool running, CefEndpoint? cef, int extensionCount, bool portConflict)
    {
        var green = (Brush)FindResource("GreenBrush");
        var amber = (Brush)FindResource("AmberBrush");
        var gray = (Brush)FindResource("GrayDotBrush");

        SteamDot.Fill = running ? green : gray;
        SteamStateText.Text = running ? "运行中" : "未运行";

        if (cef is not null)
        {
            CefDot.Fill = green;
            CefStateText.Text = "已就绪";
            CefStateText.ToolTip = $"{cef.Browser} · 127.0.0.1:{cef.Port}";
        }
        else if (portConflict)
        {
            CefDot.Fill = (Brush)FindResource("RedBrush");
            CefStateText.Text = "端口被占用";
            CefStateText.ToolTip = null;
        }
        else
        {
            CefDot.Fill = running ? amber : gray;
            CefStateText.Text = "未开启";
            CefStateText.ToolTip = running ? "点击“一键调出”会自动重启 Steam 并开启调试端口。" : null;
        }

        ExtDot.Fill = extensionCount > 0 ? green : gray;
        ExtStateText.Text = extensionCount > 0 ? $"{extensionCount} 个可用" : "无";
    }

    private void UpdatePathUi()
    {
        PathText.Text = string.IsNullOrEmpty(_steamPath) ? "未检测到 Steam" : _steamPath;
        PathText.ToolTip = _steamPath;
        OpenFolderButton.IsEnabled = !string.IsNullOrEmpty(_steamPath);
    }

    // ================= 主要操作 =================

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

        var maximized = MaximizeToggle.IsChecked == true;
        var allowRestart = AutoRestartToggle.IsChecked == true;
        var useExtension = ModeExtension.IsChecked == true;

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

            if (!outcome.Success)
            {
                AppendLog("建议：切换到“内置方式”后重试。");
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

        var allowRestart = AutoRestartToggle.IsChecked == true;
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

    private void OpenPanel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo($"http://localhost:{SteamCef.DefaultPort}") { UseShellExecute = true });
            AppendLog("已在默认浏览器打开调试面板。");
        }
        catch (Exception ex)
        {
            AppendLog("无法打开调试面板：" + ex.Message);
        }
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 Steam 安装目录中的 steam.exe",
            Filter = "Steam (steam.exe)|steam.exe|可执行文件 (*.exe)|*.exe",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true)
        {
            _steamPath = Path.GetDirectoryName(dialog.FileName);
            UpdatePathUi();
            AppendLog("已指定 Steam 目录：" + _steamPath);
            await RefreshStatusAsync(true);
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

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogBox.Clear();

    /// <summary>阻止日志框追加文字时把整页滚动到底部。</summary>
    private void PageScroll_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.TargetObject is DependencyObject target && FindAncestor<TextBox>(target) is not null)
        {
            e.Handled = true;
        }
    }

    // ================= 窗口按钮 =================

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Window_StateChanged(object? sender, EventArgs e) => UpdateMaximizeGlyph();

    private void UpdateMaximizeGlyph()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "还原" : "最大化";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // 拖拽在某些状态下可能抛出，忽略
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    // ================= 辅助 =================

    private void SetBusy(bool busy)
    {
        _busy = busy;

        LaunchButton.IsEnabled = !busy;
        DebugOnlyButton.IsEnabled = !busy;
        OpenPanelButton.IsEnabled = !busy;
        BrowseButton.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
        OpenFolderButton.IsEnabled = !busy && !string.IsNullOrEmpty(_steamPath);
        ModeBuiltIn.IsEnabled = !busy;
        ModeExtension.IsEnabled = !busy;
        UrlBox.IsEnabled = !busy;
        MaximizeToggle.IsEnabled = !busy;
        AutoRestartToggle.IsEnabled = !busy;

        BusyLine.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Mouse.OverrideCursor = busy ? Cursors.AppStarting : null;
    }

    private void AppendLog(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AppendLog(message));
            return;
        }

        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
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

