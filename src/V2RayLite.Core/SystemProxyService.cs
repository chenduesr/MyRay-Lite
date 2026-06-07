using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace V2RayLite.Core;

public sealed class SystemProxyService
{
    private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public void Enable(AppSettings settings)
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: true)
            ?? throw new InvalidOperationException("无法打开 Windows Internet Settings 注册表。");

        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
        key.SetValue("ProxyServer", $"http=127.0.0.1:{settings.HttpPort};https=127.0.0.1:{settings.HttpPort};socks=127.0.0.1:{settings.SocksPort}", RegistryValueKind.String);
        key.SetValue("ProxyOverride", "localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;192.168.*;<local>", RegistryValueKind.String);
        RefreshInternetOptions();
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: true);
        key?.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
        RefreshInternetOptions();
    }

    public void SetStartOnBoot(bool enabled, string appPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);

        if (enabled)
        {
            key.SetValue("V2RayLite", $"\"{appPath}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue("V2RayLite", throwOnMissingValue: false);
        }
    }

    private static void RefreshInternetOptions()
    {
        InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
    }

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
}
