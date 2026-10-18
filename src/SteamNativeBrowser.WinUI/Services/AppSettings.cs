using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamNativeBrowser.WinUI.Services;

public enum AccentMode
{
    System = 0,
    Custom = 1,
}

public sealed class AppSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AccentMode AccentMode { get; set; } = AccentMode.System;

    public string AccentColor { get; set; } = "#0067C0";

    private static string SettingsPath
    {
        get
        {
            var local = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SteamNativeBrowser",
                "settings.json");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(local)!);
                return local;
            }
            catch
            {
                return Path.Combine(AppContext.BaseDirectory, "settings.json");
            }
        }
    }

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            var path = SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 设置保存失败不阻塞使用
        }
    }
}
