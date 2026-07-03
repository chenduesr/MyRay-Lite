using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace V2RayLite.Core;

public sealed class XrayDownloadService
{
    private readonly HttpClient _httpClient;
    private readonly AppPaths _paths;

    public XrayDownloadService(HttpClient httpClient, AppPaths paths)
    {
        _httpClient = httpClient;
        _paths = paths;
    }

    public string GetInstalledVersionText()
    {
        var exe = Path.Combine(_paths.FindXrayDirectory(), "xray.exe");
        if (!File.Exists(exe))
        {
            return "未安装";
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "version",
                    WorkingDirectory = Path.GetDirectoryName(exe)!,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadLine();
            if (!process.WaitForExit(2000))
            {
                process.Kill(entireProcessTree: true);
            }

            return string.IsNullOrWhiteSpace(output) ? "已安装" : output.Trim();
        }
        catch
        {
            return "已安装，版本读取失败";
        }
    }

    public string GetGeoDataStatusText()
    {
        var xrayDir = _paths.FindXrayDirectory();
        var geoip = Path.Combine(xrayDir, "geoip.dat");
        var geosite = Path.Combine(xrayDir, "geosite.dat");
        if (!File.Exists(geoip) && !File.Exists(geosite))
        {
            return "未安装";
        }

        var times = new[] { geoip, geosite }
            .Where(File.Exists)
            .Select(File.GetLastWriteTime)
            .ToList();
        return times.Count == 0 ? "未安装" : $"已安装 · {times.Min():yyyy-MM-dd}";
    }

    public async Task<string> CheckLatestVersionAsync(CancellationToken cancellationToken = default)
    {
        var release = await GetLatestReleaseAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(release.TagName) ? "未知" : release.TagName;
    }

    public async Task<string> DownloadLatestWindowsX64Async(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var release = await GetLatestReleaseAsync(cancellationToken);
        var asset = FindWindowsX64Asset(release);
        await DownloadAndExtractAsync(asset.DownloadUrl, includeExecutable: true, includeGeoData: true, progress, cancellationToken);
        return _paths.FindXrayDirectory();
    }

    public async Task<string> DownloadLatestGeoDataAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var release = await GetLatestReleaseAsync(cancellationToken);
        var asset = FindWindowsX64Asset(release);
        await DownloadAndExtractAsync(asset.DownloadUrl, includeExecutable: false, includeGeoData: true, progress, cancellationToken);
        return _paths.FindXrayDirectory();
    }

    private async Task DownloadAndExtractAsync(string downloadUrl, bool includeExecutable, bool includeGeoData, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var xrayDir = _paths.FindXrayDirectory();
        Directory.CreateDirectory(xrayDir);
        var zipPath = Path.Combine(Path.GetTempPath(), $"xray-{Guid.NewGuid():N}.zip");

        using (var download = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            download.EnsureSuccessStatusCode();
            var total = download.Content.Headers.ContentLength;
            await using var source = await download.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = File.Create(zipPath);
            var buffer = new byte[81920];
            long readTotal = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                readTotal += read;
                if (total is > 0)
                {
                    progress?.Report((double)readTotal / total.Value);
                }
            }
        }

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries.Where(entry => ShouldExtract(entry, includeExecutable, includeGeoData)))
        {
            entry.ExtractToFile(Path.Combine(xrayDir, Path.GetFileName(entry.FullName)), overwrite: true);
        }

        File.Delete(zipPath);
    }

    private static bool ShouldExtract(ZipArchiveEntry entry, bool includeExecutable, bool includeGeoData)
    {
        var name = Path.GetFileName(entry.FullName);
        if (includeExecutable && string.Equals(name, "xray.exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return includeGeoData &&
            (string.Equals(name, "geoip.dat", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(name, "geosite.dat", StringComparison.OrdinalIgnoreCase));
    }

    private static XrayReleaseAsset FindWindowsX64Asset(XrayReleaseInfo release)
    {
        return release.Assets.FirstOrDefault(item =>
            item.Name.Contains("windows-64", StringComparison.OrdinalIgnoreCase) &&
            item.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("未在最新 Xray-core release 中找到 Windows x64 zip。");
    }

    private async Task<XrayReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/XTLS/Xray-core/releases/latest");
        request.Headers.UserAgent.ParseAdd("MyRayLite/1.8");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var apiStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(apiStream, cancellationToken: cancellationToken);
        var tagName = document.RootElement.TryGetProperty("tag_name", out var tagProperty)
            ? tagProperty.GetString() ?? string.Empty
            : string.Empty;
        var assets = document.RootElement.GetProperty("assets")
            .EnumerateArray()
            .Select(item => new XrayReleaseAsset(
                item.GetProperty("name").GetString() ?? string.Empty,
                item.GetProperty("browser_download_url").GetString() ?? string.Empty))
            .Where(item => !string.IsNullOrWhiteSpace(item.DownloadUrl))
            .ToList();

        return new XrayReleaseInfo(tagName, assets);
    }
}

public sealed record XrayReleaseInfo(string TagName, IReadOnlyList<XrayReleaseAsset> Assets);

public sealed record XrayReleaseAsset(string Name, string DownloadUrl);