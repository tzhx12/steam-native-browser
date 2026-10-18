using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SteamNativeBrowser.WinUI.Services;
using Windows.UI;

namespace SteamNativeBrowser.WinUI.Pages;

public sealed partial class SettingsPage : Page
{
    private static readonly (string Hex, string Name)[] Palette =
    {
        ("#0067C0", "Windows 蓝"),
        ("#00B7C3", "青"),
        ("#107C10", "绿"),
        ("#8764B8", "紫"),
        ("#CA5010", "橙"),
        ("#D13438", "红"),
        ("#E3008C", "品红"),
        ("#69797E", "灰"),
    };

    private AccentMode _mode = AccentMode.System;
    private string _hex = "#0067C0";

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var settings = AppSettings.Load();
        _mode = settings.AccentMode;
        _hex = AccentService.TryParse(settings.AccentColor, out _) ? settings.AccentColor : "#0067C0";

        AccentModeRadios.SelectedIndex = _mode == AccentMode.Custom ? 1 : 0;
        PalettePanel.Visibility = _mode == AccentMode.Custom ? Visibility.Visible : Visibility.Collapsed;

        BuildPalette();
        UpdateCurrentText();
    }

    private void AccentModeRadios_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccentModeRadios.SelectedItem is not RadioButton { Tag: string tag })
        {
            return;
        }

        _mode = tag == "Custom" ? AccentMode.Custom : AccentMode.System;
        PalettePanel.Visibility = _mode == AccentMode.Custom ? Visibility.Visible : Visibility.Collapsed;
        UpdateCurrentText();
    }

    private void BuildPalette()
    {
        PalettePanel.Children.Clear();

        foreach (var (hex, name) in Palette)
        {
            if (!AccentService.TryParse(hex, out var color))
            {
                continue;
            }

            var selected = string.Equals(hex, _hex, StringComparison.OrdinalIgnoreCase);

            var grid = new Grid();
            grid.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 26,
                Height = 26,
                Fill = new SolidColorBrush(color),
            });

            if (selected)
            {
                grid.Children.Add(new FontIcon
                {
                    Glyph = "\uE73E",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            var button = new Button
            {
                Width = 40,
                Height = 40,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(20),
                Content = grid,
                Tag = hex,
            };
            ToolTipService.SetToolTip(button, name);
            button.Click += Color_Click;

            PalettePanel.Children.Add(button);
        }
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            _hex = hex;
            BuildPalette();
            UpdateCurrentText();
        }
    }

    private void UpdateCurrentText()
    {
        CurrentText.Text = _mode == AccentMode.Custom
            ? $"当前强调色：自定义 {_hex}"
            : "当前强调色：跟随系统";
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var settings = new AppSettings
        {
            AccentMode = _mode,
            AccentColor = _hex,
        };
        settings.Save();

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory,
            });
            Application.Current.Exit();
        }
        catch
        {
            // 重启失败时保留窗口
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is MainWindow window)
        {
            window.NavigateTo("about");
        }
    }
}
