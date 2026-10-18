using System.Windows;
using Microsoft.Win32;

namespace SteamNativeBrowser.Interop;

internal static class WindowEffects
{
    public static bool IsDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryEnableMica(IntPtr hwnd, bool dark)
    {
        try
        {
            var corner = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(
                hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

            var darkMode = dark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(
                hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

            var backdrop = NativeMethods.DWMSBT_MAINWINDOW;
            return NativeMethods.DwmSetWindowAttribute(
                hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void ClampToWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        var info = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new NativeMethods.MONITORINFO
        {
            Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>(),
        };

        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var work = monitorInfo.WorkArea;
        var full = monitorInfo.Monitor;

        info.MaxPosition.X = work.Left - full.Left;
        info.MaxPosition.Y = work.Top - full.Top;
        info.MaxSize.X = work.Right - work.Left;
        info.MaxSize.Y = work.Bottom - work.Top;
        info.MaxTrackSize.X = info.MaxSize.X;
        info.MaxTrackSize.Y = info.MaxSize.Y;

        System.Runtime.InteropServices.Marshal.StructureToPtr(info, lParam, false);
    }
}
