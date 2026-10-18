using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace SteamNativeBrowser.Services;

/// <summary>检测当前进程的完整性级别与 Steam 注册表可写性。</summary>
internal static class ProcessIntegrity
{
    private const int TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private const int SecurityMandatoryMediumRid = 0x2000;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation,
        int tokenInformationLength, out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthorityIndex);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenMandatoryLabel
    {
        public SidAndAttributes Label;
    }

    /// <summary>当前进程是否低于 Medium（例如 Low 完整性沙箱）。</summary>
    public static bool IsBelowMediumIntegrity()
    {
        var token = IntPtr.Zero;
        var buffer = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token))
            {
                return false;
            }

            GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out var size);
            if (size <= 0)
            {
                return false;
            }

            buffer = Marshal.AllocHGlobal(size);
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out _))
            {
                return false;
            }

            var label = Marshal.PtrToStructure<TokenMandatoryLabel>(buffer);
            var countPointer = GetSidSubAuthorityCount(label.Label.Sid);
            var count = Marshal.ReadByte(countPointer);
            var subAuthority = GetSidSubAuthority(label.Label.Sid, (uint)(count - 1));
            var rid = Marshal.ReadInt32(subAuthority);

            return rid < SecurityMandatoryMediumRid;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }

            if (token != IntPtr.Zero)
            {
                CloseHandle(token);
            }
        }
    }

    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>真正写一次测试值，确认 Steam 注册表当前可写。</summary>
    public static bool CanWriteSteamRegistry()
    {
        const string testValue = "__SteamNativeBrowserWriteTest";

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", writable: true);
            if (key is null)
            {
                return false;
            }

            key.SetValue(testValue, 1);
            key.DeleteValue(testValue, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
