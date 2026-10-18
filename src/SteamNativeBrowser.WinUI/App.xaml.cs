using Microsoft.UI.Xaml;
using SteamNativeBrowser.WinUI.Services;

namespace SteamNativeBrowser.WinUI;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AccentService.ApplyAtStartup();

        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
