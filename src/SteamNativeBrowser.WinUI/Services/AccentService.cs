using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace SteamNativeBrowser.WinUI.Services;

/// <summary>应用强调色：默认跟随系统，也可以使用自定义颜色。</summary>
public static class AccentService
{
    public static void ApplyAtStartup()
    {
        var settings = AppSettings.Load();
        if (settings.AccentMode == AccentMode.Custom && TryParse(settings.AccentColor, out var color))
        {
            Apply(color);
        }
    }

    public static Color GetSystemAccent()
    {
        try
        {
            var ui = new UISettings();
            return ui.GetColorValue(UIColorType.Accent);
        }
        catch
        {
            return Color.FromArgb(255, 0, 103, 192);
        }
    }

    public static void Apply(Color color)
    {
        var resources = Application.Current.Resources;
        resources["SystemAccentColor"] = color;
        resources["SystemAccentColorLight1"] = Blend(color, Colors.White, 0.18);
        resources["SystemAccentColorLight2"] = Blend(color, Colors.White, 0.36);
        resources["SystemAccentColorLight3"] = Blend(color, Colors.White, 0.54);
        resources["SystemAccentColorDark1"] = Blend(color, Colors.Black, 0.18);
        resources["SystemAccentColorDark2"] = Blend(color, Colors.Black, 0.34);
        resources["SystemAccentColorDark3"] = Blend(color, Colors.Black, 0.5);
    }

    public static bool TryParse(string? hex, out Color color)
    {
        color = Color.FromArgb(255, 0, 103, 192);
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var text = hex.Trim().TrimStart('#');
        if (text.Length != 6 || !uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var value))
        {
            return false;
        }

        color = Color.FromArgb(
            255,
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF));
        return true;
    }

    private static Color Blend(Color source, Color target, double ratio)
    {
        return Color.FromArgb(
            255,
            (byte)(source.R + (target.R - source.R) * ratio),
            (byte)(source.G + (target.G - source.G) * ratio),
            (byte)(source.B + (target.B - source.B) * ratio));
    }
}
