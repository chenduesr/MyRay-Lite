using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace V2RayLite.Core;

public sealed class SystemProxyService
{
    private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "V2RayLite";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly AppPaths _paths;
    private readonly AppLogService _log;
    private readonly object _sync = new();

    public SystemProxyService(AppPaths paths, AppLogService log)
    {
        _paths = paths;
        _log = log;
    }

    public void Enable(AppSettings settings)
    {
        lock (_sync)
        {
            _paths.Ensure();
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: true)
                ?? throw new InvalidOperationException("无法打开 Windows Internet Settings 注册表。");

            var proxyServer = BuildProxyServer(settings);
            var snapshot = LoadBackup() ?? CaptureSnapshot(key, proxyServer);
            if (!File.Exists(_paths.SystemProxyBackupFile))
            {
                SaveBackup(snapshot);
            }

            try
            {
                key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                key.SetValue("ProxyServer", proxyServer, RegistryValueKind.String);
                key.SetValue(
                    "ProxyOverride",
                    "localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;192.168.*;<local>",
                    RegistryValueKind.String);
                RefreshInternetOptions();
            }
            catch
            {
                RestoreSnapshot(key, snapshot);
                DeleteBackup();
                RefreshInternetOptions();
                throw;
            }
        }
    }

    public void Disable()
    {
        lock (_sync)
        {
            var snapshot = LoadBackup();
            if (snapshot is null)
            {
                return;
            }

            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: true);
            if (key is null)
            {
                return;
            }

            var currentServer = key.GetValue("ProxyServer") as string;
            if (string.Equals(currentServer, snapshot.AppliedProxyServer, StringComparison.OrdinalIgnoreCase))
            {
                RestoreSnapshot(key, snapshot);
                RefreshInternetOptions();
                _log.Info("已恢复启用 MyRay Lite 前的系统代理设置。");
            }
            else
            {
                _log.Warn("检测到系统代理已被其他程序修改，跳过恢复以避免覆盖用户设置。");
            }

            DeleteBackup();
        }
    }

    public void RestoreStaleProxyIfNeeded()
    {
        lock (_sync)
        {
            var snapshot = LoadBackup();
            if (snapshot is null)
            {
                return;
            }

            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: true);
            if (key is null)
            {
                return;
            }

            var currentServer = key.GetValue("ProxyServer") as string;
            if (string.Equals(currentServer, snapshot.AppliedProxyServer, StringComparison.OrdinalIgnoreCase))
            {
                RestoreSnapshot(key, snapshot);
                RefreshInternetOptions();
                _log.Warn("检测到上次异常退出遗留的系统代理，已恢复原设置。");
            }

            DeleteBackup();
        }
    }

    public void SetStartOnBoot(bool enabled, string appPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);

        if (enabled)
        {
            key.SetValue(RunValueName, $"\"{appPath}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }

    private static string BuildProxyServer(AppSettings settings)
    {
        return $"http=127.0.0.1:{settings.HttpPort};https=127.0.0.1:{settings.HttpPort};socks=127.0.0.1:{settings.SocksPort}";
    }

    private static ProxySettingsSnapshot CaptureSnapshot(RegistryKey key, string appliedProxyServer)
    {
        var valueNames = key.GetValueNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new ProxySettingsSnapshot(
            valueNames.Contains("ProxyEnable"),
            ReadDword(key, "ProxyEnable"),
            valueNames.Contains("ProxyServer"),
            key.GetValue("ProxyServer") as string,
            valueNames.Contains("ProxyOverride"),
            key.GetValue("ProxyOverride") as string,
            appliedProxyServer);
    }

    private static int ReadDword(RegistryKey key, string name)
    {
        try
        {
            return Convert.ToInt32(key.GetValue(name, 0));
        }
        catch
        {
            return 0;
        }
    }

    private static void RestoreSnapshot(RegistryKey key, ProxySettingsSnapshot snapshot)
    {
        RestoreValue(key, "ProxyEnable", snapshot.ProxyEnableExists, snapshot.ProxyEnable, RegistryValueKind.DWord);
        RestoreValue(key, "ProxyServer", snapshot.ProxyServerExists, snapshot.ProxyServer ?? string.Empty, RegistryValueKind.String);
        RestoreValue(key, "ProxyOverride", snapshot.ProxyOverrideExists, snapshot.ProxyOverride ?? string.Empty, RegistryValueKind.String);
    }

    private static void RestoreValue(
        RegistryKey key,
        string name,
        bool existed,
        object value,
        RegistryValueKind kind)
    {
        if (existed)
        {
            key.SetValue(name, value, kind);
        }
        else
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
    }

    private ProxySettingsSnapshot? LoadBackup()
    {
        if (!File.Exists(_paths.SystemProxyBackupFile))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProxySettingsSnapshot>(
                File.ReadAllText(_paths.SystemProxyBackupFile),
                JsonOptions);
        }
        catch (Exception ex)
        {
            _log.Warn($"读取系统代理备份失败：{ex.Message}");
            DeleteBackup();
            return null;
        }
    }

    private void SaveBackup(ProxySettingsSnapshot snapshot)
    {
        var temporaryFile = $"{_paths.SystemProxyBackupFile}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryFile, JsonSerializer.Serialize(snapshot, JsonOptions));
            File.Move(temporaryFile, _paths.SystemProxyBackupFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryFile))
            {
                File.Delete(temporaryFile);
            }
        }
    }

    private void DeleteBackup()
    {
        try
        {
            if (File.Exists(_paths.SystemProxyBackupFile))
            {
                File.Delete(_paths.SystemProxyBackupFile);
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"删除系统代理备份失败：{ex.Message}");
        }
    }

    private static void RefreshInternetOptions()
    {
        InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
    }

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    private sealed record ProxySettingsSnapshot(
        bool ProxyEnableExists,
        int ProxyEnable,
        bool ProxyServerExists,
        string? ProxyServer,
        bool ProxyOverrideExists,
        string? ProxyOverride,
        string AppliedProxyServer);
}
