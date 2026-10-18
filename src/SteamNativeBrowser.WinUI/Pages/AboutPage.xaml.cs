using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SteamNativeBrowser.WinUI.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"版本 {version?.ToString(3) ?? "2.0.0"} · .NET 8 · WinUI 3";
        SystemInfoText.Text = $"{RuntimeInformation.OSDescription} · {RuntimeInformation.FrameworkDescription} · Windows App SDK 1.7";
    }

    private async void ShowLicense_Click(object sender, RoutedEventArgs e)
    {
        string text;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "LICENSE");
            text = File.Exists(path) ? File.ReadAllText(path) : "MIT License\n\nCopyright (c) 2026 SteamNativeLauncher contributors";
        }
        catch
        {
            text = "MIT License\n\nCopyright (c) 2026 SteamNativeLauncher contributors";
        }

        var dialog = new ContentDialog
        {
            Title = "MIT License",
            CloseButtonText = "关闭",
            XamlRoot = XamlRoot,
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                Content = new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
                    FontSize = 12,
                    IsTextSelectionEnabled = true,
                },
            },
        };

        await dialog.ShowAsync();
    }
}
