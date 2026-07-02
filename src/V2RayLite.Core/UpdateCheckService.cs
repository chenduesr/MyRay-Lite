using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace V2RayLite.Core;

public sealed class UpdateCheckService
{
    private readonly HttpClient _httpClient;
    private readonly AppLogService _log;

    public UpdateCheckService(HttpClient httpClient, AppLogService log)
    {
        _httpClient = httpClient;
        _log = log;
    }

    public async Task<UpdateCheckResult> CheckLatestAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.github.com/repos/chenduesr/MyRay-Lite/releases/latest");
        request.Headers.UserAgent.ParseAdd("MyRayLite/1.0");

        var release = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        release.EnsureSuccessStatusCode();

        var latest = await release.Content
            .ReadFromJsonAsync<GitHubRelease>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (latest is null || string.IsNullOrWhiteSpace(latest.TagName))
        {
            return new UpdateCheckResult(false, null, null, null, null, "没有读取到最新版本信息。");
        }

        var latestVersion = ParseVersion(latest.TagName);
        if (latestVersion is null)
        {
            _log.Warn($"无法解析最新版本号：{latest.TagName}");
            return new UpdateCheckResult(false, latest.TagName, latest.HtmlUrl, null, null, "最新版本号格式无法识别。");
        }

        var hasUpdate = latestVersion > currentVersion;
        var message = hasUpdate
            ? $"发现新版本 {latest.TagName}。"
            : latestVersion < currentVersion
                ? $"当前版本 {currentVersion} 高于最新 Release {latest.TagName}。"
                : $"当前已经是最新版本 {latest.TagName}。";
        var installer = latest.Assets
            .Where(asset => asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                            asset.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
            .Select(ToReleaseAsset)
            .FirstOrDefault();
        var portable = latest.Assets
            .Where(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                            asset.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase))
            .Select(ToReleaseAsset)
            .FirstOrDefault();

        return new UpdateCheckResult(
            hasUpdate,
            latest.TagName,
            latest.HtmlUrl,
            installer,
            portable,
            message);
    }

    public async Task<string> DownloadInstallerAsync(
        ReleaseAsset asset,
        AppPaths paths,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(asset.DownloadUrl))
        {
            throw new InvalidOperationException("更新安装包下载地址为空。");
        }

        paths.Ensure();
        var outputFile = Path.Combine(paths.UpdateDirectory, asset.Name);
        var tempFile = outputFile + ".download";
        DeleteFileIfExists(tempFile);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
            request.Headers.UserAgent.ParseAdd("MyRayLite/1.0");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? asset.Size;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var output = File.Create(tempFile))
            {
                var buffer = new byte[64 * 1024];
                long downloaded = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    downloaded += read;
                    if (total > 0)
                    {
                        progress?.Report(Math.Clamp(downloaded / (double)total, 0, 1));
                    }
                }
            }

            DeleteFileIfExists(outputFile);
            File.Move(tempFile, outputFile);

            progress?.Report(1);
            _log.Info($"更新安装包已下载：{outputFile}");
            return outputFile;
        }
        catch
        {
            DeleteFileIfExists(tempFile);
            throw;
        }
    }

    private static Version? ParseVersion(string value)
    {
        var normalized = value.Trim().TrimStart('v', 'V');
        return Version.TryParse(normalized, out var version) ? version : null;
    }

    private static void DeleteFileIfExists(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAsset> Assets { get; set; } = [];
    }

    private sealed class GitHubReleaseAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string DownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    private static ReleaseAsset ToReleaseAsset(GitHubReleaseAsset asset)
    {
        return new ReleaseAsset
        {
            Name = asset.Name,
            DownloadUrl = asset.DownloadUrl,
            Size = asset.Size
        };
    }
}

public sealed record UpdateCheckResult(
    bool HasUpdate,
    string? LatestVersion,
    string? ReleaseUrl,
    ReleaseAsset? InstallerAsset,
    ReleaseAsset? PortableAsset,
    string Message);