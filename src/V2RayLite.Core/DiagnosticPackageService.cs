using System.IO.Compression;
using System.Text.Json;

namespace V2RayLite.Core;

public sealed class DiagnosticPackageService
{
    private readonly AppPaths _paths;

    public DiagnosticPackageService(AppPaths paths)
    {
        _paths = paths;
    }

    public string CreatePackage(AppSettings settings, IEnumerable<ProxyNode> nodes, string appVersion)
    {
        _paths.Ensure();
        var packageFile = Path.Combine(_paths.DiagnosticDirectory, $"myray-lite-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        if (File.Exists(packageFile))
        {
            File.Delete(packageFile);
        }

        using var archive = ZipFile.Open(packageFile, ZipArchiveMode.Create);
        AddText(archive, "system.txt", BuildSystemText(appVersion));
        AddText(archive, "settings.redacted.json", JsonSerializer.Serialize(RedactSettings(settings), new JsonSerializerOptions { WriteIndented = true }));
        AddText(archive, "nodes.redacted.json", JsonSerializer.Serialize(nodes.Select(RedactNode).ToList(), new JsonSerializerOptions { WriteIndented = true }));

        AddFiles(archive, _paths.LogDirectory, "logs", "*.log", 8);
        AddFiles(archive, _paths.CrashDirectory, "crashes", "*.log", 8);

        if (File.Exists(_paths.GeneratedConfigFile))
        {
            AddText(archive, "xray.generated.redacted.json", RedactText(File.ReadAllText(_paths.GeneratedConfigFile)));
        }

        return packageFile;
    }

    private static string BuildSystemText(string appVersion)
    {
        return string.Join(Environment.NewLine,
            $"AppVersion: {appVersion}",
            $"Time: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}",
            $"OS: {Environment.OSVersion}",
            $"Runtime: {Environment.Version}",
            $"Machine64Bit: {Environment.Is64BitOperatingSystem}",
            $"Process64Bit: {Environment.Is64BitProcess}");
    }

    private static object RedactSettings(AppSettings settings)
    {
        return new
        {
            settings.SettingsVersion,
            SubscriptionUrl = RedactUrl(settings.SubscriptionUrl),
            settings.ProxyMode,
            settings.RoutingRuleMode,
            settings.StartOnBoot,
            settings.AutoConnectOnLaunch,
            settings.AutoCheckUpdates,
            settings.MinimizeToTray,
            settings.DarkMode,
            settings.HttpPort,
            settings.SocksPort,
            settings.DelayTestUrl,
            settings.DelayTestMode,
            settings.DelayTestBatchSize,
            settings.DelayTestRetryBatchSize,
            settings.DelayTestTimeoutSeconds,
            settings.BypassMainland,
            settings.EnableCustomDns,
            settings.EnableFakeDns,
            settings.EnableSplitDns,
            settings.LastSubscriptionUpdate
        };
    }

    private static object RedactNode(ProxyNode node)
    {
        return new
        {
            node.Name,
            Address = RedactHost(node.Address),
            node.Port,
            node.Protocol,
            node.Network,
            node.Security,
            node.Flow,
            Host = RedactHost(node.Host),
            Sni = RedactHost(node.Sni),
            node.DelayMs,
            node.DownloadMbps,
            node.LastDelayTestMode,
            node.LastFailureKind,
            node.LastFailureReason,
            node.Status,
            node.LastTested,
            node.IsActive,
            HasRaw = !string.IsNullOrWhiteSpace(node.Raw),
            HasPassword = !string.IsNullOrWhiteSpace(node.Password),
            HasUserId = !string.IsNullOrWhiteSpace(node.UserId)
        };
    }

    private static void AddFiles(ZipArchive archive, string directory, string folder, string pattern, int take)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, pattern)
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Take(take)
                     .Reverse())
        {
            archive.CreateEntryFromFile(file, $"{folder}/{Path.GetFileName(file)}", CompressionLevel.Optimal);
        }
    }

    private static void AddText(ZipArchive archive, string name, string text)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(text);
    }

    private static string RedactText(string value)
    {
        return value
            .Replace("password", "password_redacted", StringComparison.OrdinalIgnoreCase)
            .Replace("uuid", "uuid_redacted", StringComparison.OrdinalIgnoreCase);
    }

    private static string RedactUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : "[redacted]";
        }

        return $"{uri.Scheme}://{uri.Host}/[redacted]";
    }

    private static string? RedactHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 2)
        {
            return value.Length <= 3 ? "***" : $"{value[..Math.Min(3, value.Length)]}***";
        }

        return $"***.{parts[^2]}.{parts[^1]}";
    }
}
