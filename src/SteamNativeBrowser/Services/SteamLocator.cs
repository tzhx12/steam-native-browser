using System.IO;
using System.Diagnostics;
using Microsoft.Win32;

namespace SteamNativeBrowser.Services;

/// <summary>负责找到本机 Steam 安装目录，并判断 Steam 是否在运行。</summary>
public static class SteamLocator
{
    public static string? FindSteamPath()
    {
        foreach (var candidate in EnumerateCandidates())
        {
            var path = Normalize(candidate);
            if (path is null)
            {
                continue;
            }

            try
            {
                if (File.Exists(Path.Combine(path, "steam.exe")))
                {
                    return path;
                }
            }
            catch
            {
                // 忽略无法访问的路径
            }
        }

        return null;
    }

    public static bool IsSteamRunning()
    {
        try
        {
            return Process.GetProcessesByName("steam").Length > 0 ||
                   Process.GetProcessesByName("steamwebhelper").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static string? Normalize(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var value = candidate.Trim().Trim('"').Replace('/', '\\');
        try
        {
            return Path.GetFullPath(value).TrimEnd('\\');
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string?> EnumerateCandidates()
    {
        yield return ReadRegistry(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        yield return ReadRegistry(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        yield return ReadRegistry(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");

        foreach (var process in SafeProcesses("steam"))
        {
            string? fileName = null;
            try
            {
                fileName = process.MainModule?.FileName;
            }
            catch
            {
                // 进程可能受保护或已退出
            }

            if (!string.IsNullOrEmpty(fileName))
            {
                yield return Path.GetDirectoryName(fileName);
            }
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        if (!string.IsNullOrEmpty(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "Steam");
        }

        if (!string.IsNullOrEmpty(programFiles))
        {
            yield return Path.Combine(programFiles, "Steam");
        }

        foreach (var drive in SafeDrives())
        {
            yield return Path.Combine(drive.RootDirectory.FullName, "Steam");
            yield return Path.Combine(drive.RootDirectory.FullName, "Games", "Steam");
            yield return Path.Combine(drive.RootDirectory.FullName, "SteamLibrary");
        }
    }

    private static string? ReadRegistry(RegistryKey root, string subKey, string valueName)
    {
        try
        {
            using var key = root.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
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

    private static IEnumerable<DriveInfo> SafeDrives()
    {
        try
        {
            return DriveInfo.GetDrives().Where(d =>
            {
                try
                {
                    return d.IsReady && d.DriveType == DriveType.Fixed;
                }
                catch
                {
                    return false;
                }
            });
        }
        catch
        {
            return Array.Empty<DriveInfo>();
        }
    }
}

