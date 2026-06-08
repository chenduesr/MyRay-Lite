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
            return new UpdateCheckResult(false, null, null, "没有读取到最新版本信息。");
        }

        var latestVersion = ParseVersion(latest.TagName);
        if (latestVersion is null)
        {
            _log.Warn($"无法解析最新版本号：{latest.TagName}");
            return new UpdateCheckResult(false, latest.TagName, latest.HtmlUrl, "最新版本号格式无法识别。");
        }

        var hasUpdate = latestVersion > currentVersion;
        return new UpdateCheckResult(
            hasUpdate,
            latest.TagName,
            latest.HtmlUrl,
            hasUpdate ? $"发现新版本 {latest.TagName}。" : $"当前已是最新版本 {latest.TagName}。");
    }

    private static Version? ParseVersion(string value)
    {
        var normalized = value.Trim().TrimStart('v', 'V');
        return Version.TryParse(normalized, out var version) ? version : null;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }
}

public sealed record UpdateCheckResult(
    bool HasUpdate,
    string? LatestVersion,
    string? ReleaseUrl,
    string Message);
