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

    public async Task<string> DownloadLatestWindowsX64Async(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/XTLS/Xray-core/releases/latest");
        request.Headers.UserAgent.ParseAdd("V2RayLite/1.0");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var apiStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(apiStream, cancellationToken: cancellationToken);
        var asset = document.RootElement.GetProperty("assets")
            .EnumerateArray()
            .FirstOrDefault(item =>
            {
                var name = item.GetProperty("name").GetString() ?? string.Empty;
                return name.Contains("windows-64", StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            });

        if (asset.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException("未在最新 Xray-core release 中找到 Windows x64 zip。");
        }

        var downloadUrl = asset.GetProperty("browser_download_url").GetString()
            ?? throw new InvalidOperationException("Xray-core 下载地址为空。");
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
        foreach (var entry in archive.Entries.Where(e =>
                     string.Equals(Path.GetFileName(e.FullName), "xray.exe", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(Path.GetFileName(e.FullName), "geoip.dat", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(Path.GetFileName(e.FullName), "geosite.dat", StringComparison.OrdinalIgnoreCase)))
        {
            entry.ExtractToFile(Path.Combine(xrayDir, Path.GetFileName(entry.FullName)), overwrite: true);
        }

        File.Delete(zipPath);
        return xrayDir;
    }
}
