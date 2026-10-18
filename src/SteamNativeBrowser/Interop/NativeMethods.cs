using System.Runtime.InteropServices;

namespace SteamNativeBrowser.Interop;

internal static class NativeMethods
{
    public const int WM_GETMINMAXINFO = 0x0024;

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public const int DWMWCP_ROUND = 2;
    public const int DWMSBT_MAINWINDOW = 2;

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT Reserved;
        public POINT MaxSize;
        public POINT MaxPosition;
        public POINT MaxTrackSize;
        public POINT MinTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int Size;
        public RECT Monitor;
        public RECT WorkArea;
        public uint Flags;
    }
}
