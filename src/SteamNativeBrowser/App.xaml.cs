using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace SteamNativeBrowser;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamNativeBrowser",
        "startup.log");

    private static readonly string FallbackLogPath = Path.Combine(AppContext.BaseDirectory, "startup.log");

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log("AppDomain: " + args.ExceptionObject);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log("OnStartup");

        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
            Log("MainWindow shown");
        }
        catch (Exception ex)
        {
            Log("Startup failed: " + ex);
            MessageBox.Show(
                "程序启动失败：\n\n" + ex.Message + "\n\n日志：" + LogPath,
                "Steam 原生浏览器",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log("Dispatcher: " + e.Exception);
        MessageBox.Show(
            "程序遇到未处理的错误：\n\n" + e.Exception.Message,
            "Steam 原生浏览器",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    public static void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";

        foreach (var path in new[] { LogPath, FallbackLogPath })
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line);
                return;
            }
            catch
            {
                // 换下一个位置
            }
        }
    }
}
