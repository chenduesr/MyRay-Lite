using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace V2RayLite.Core;

public sealed class DelayTestService
{
    private const int TcpProbeConcurrency = 80;
    private static readonly TimeSpan TcpProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly AppPaths _paths;
    private readonly XrayConfigBuilder _configBuilder;
    private readonly AppLogService _log;

    public DelayTestService(AppPaths paths, XrayConfigBuilder configBuilder, AppLogService log)
    {
        _paths = paths;
        _configBuilder = configBuilder;
        _log = log;
    }

    public async Task<int?> TestProxyDelayAsync(ProxyNode node, AppSettings settings, CancellationToken cancellationToken = default)
    {
        ResetNodeForTest(node, settings.DelayTestMode);

        if (!PrepareSupportedNode(node))
        {
            return null;
        }

        var result = settings.DelayTestMode == DelayTestMode.Tcp
            ? await TestTcpConnectDelayAsync(node, TcpProbeTimeout, cancellationToken)
            : await TestWithTemporaryXrayAsync(node, settings, cancellationToken);

        ApplyResult(node, result);
        return node.DelayMs;
    }

    public async Task TestManyAsync(IEnumerable<ProxyNode> nodes, AppSettings settings, Action<ProxyNode>? onNodeUpdated = null, CancellationToken cancellationToken = default)
    {
        var nodeList = nodes.ToList();
        var supportedNodes = new List<ProxyNode>();

        foreach (var node in nodeList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResetNodeForTest(node, settings.DelayTestMode);

            if (!PrepareSupportedNode(node))
            {
                onNodeUpdated?.Invoke(node);
                continue;
            }

            node.Status = NodeStatus.Testing;
            supportedNodes.Add(node);
            onNodeUpdated?.Invoke(node);
        }

        if (supportedNodes.Count == 0)
        {
            return;
        }

        if (settings.DelayTestMode == DelayTestMode.Tcp)
        {
            _log.Info($"开始 TCP 批量测速：{supportedNodes.Count} 个节点，并发 {TcpProbeConcurrency}。");
            await RunTcpBatchDelayTestAsync(supportedNodes, onNodeUpdated, cancellationToken);
            return;
        }

        var batchSize = Math.Clamp(settings.DelayTestBatchSize, 1, 80);
        _log.Info($"开始 {FormatMode(settings.DelayTestMode)} 批量测速：{supportedNodes.Count} 个节点，每批 {batchSize} 个。");

        foreach (var batch in supportedNodes.Chunk(batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RunXrayBatchDelayTestAsync(batch, settings, onNodeUpdated, cancellationToken);
        }

        var retryNodes = supportedNodes
            .Where(node => node.Status == NodeStatus.Unavailable && node.LastFailureKind is DelayFailureKind.HttpTimeout or DelayFailureKind.DownloadTimeout or DelayFailureKind.ConnectionFailed)
            .ToList();

        if (retryNodes.Count == 0)
        {
            return;
        }

        var retryBatchSize = Math.Clamp(settings.DelayTestRetryBatchSize, 1, 20);
        _log.Info($"开始失败重试：{retryNodes.Count} 个节点，每批 {retryBatchSize} 个。");
        foreach (var node in retryNodes)
        {
            ResetNodeForTest(node, settings.DelayTestMode);
            node.Status = NodeStatus.Testing;
            onNodeUpdated?.Invoke(node);
        }

        foreach (var batch in retryNodes.Chunk(retryBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RunXrayBatchDelayTestAsync(batch, settings, onNodeUpdated, cancellationToken);
        }
    }

    private static async Task RunTcpBatchDelayTestAsync(
        IReadOnlyList<ProxyNode> nodes,
        Action<ProxyNode>? onNodeUpdated,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(TcpProbeConcurrency);
        var tasks = nodes.Select(async node =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var result = await TestTcpConnectDelayAsync(node, TcpProbeTimeout, cancellationToken);
                ApplyResult(node, result);
                onNodeUpdated?.Invoke(node);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task<ProbeResult> TestWithTemporaryXrayAsync(
        ProxyNode node,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var exe = Path.Combine(_paths.FindXrayDirectory(), "xray.exe");
        if (!File.Exists(exe))
        {
            return ProbeResult.Fail(DelayFailureKind.MissingCore, "未找到 xray.exe，请先下载核心。");
        }

        var httpPort = GetFreeTcpPort();
        var socksPort = GetFreeTcpPort();
        var tempSettings = new AppSettings
        {
            HttpPort = httpPort,
            SocksPort = socksPort,
            ProxyMode = ProxyMode.Global
        };

        _paths.Ensure();
        var configFile = Path.Combine(_paths.AppDataRoot, $"xray-probe-{Guid.NewGuid():N}.json");
        var logFile = Path.Combine(_paths.LogDirectory, $"xray-probe-{DateTime.Now:yyyyMMdd-HHmmss}-{node.Id}.log");
        Process? process = null;

        try
        {
            await File.WriteAllTextAsync(configFile, _configBuilder.Build(node, tempSettings), cancellationToken);
            process = StartXray(exe, configFile, logFile);
            await Task.Delay(900, cancellationToken);

            if (process.HasExited)
            {
                var issue = XrayErrorAdvisor.Analyze(ReadFileTail(logFile));
                return ProbeResult.Fail(DelayFailureKind.XrayStartupFailed, $"{issue.Title}：{issue.Suggestion}");
            }

            return await ProbeThroughHttpPortAsync(httpPort, settings, cancellationToken);
        }
        catch (NotSupportedException ex)
        {
            return ProbeResult.Fail(DelayFailureKind.UnsupportedProtocol, ex.Message);
        }
        catch (Exception ex)
        {
            var issue = XrayErrorAdvisor.Analyze(ex.Message);
            return ProbeResult.Fail(DelayFailureKind.XrayConfigInvalid, $"{issue.Title}：{issue.Suggestion}");
        }
        finally
        {
            StopProcess(process);
            DeleteFile(configFile);
        }
    }

    private async Task RunXrayBatchDelayTestAsync(
        IReadOnlyList<ProxyNode> nodes,
        AppSettings settings,
        Action<ProxyNode>? onNodeUpdated,
        CancellationToken cancellationToken)
    {
        var exe = Path.Combine(_paths.FindXrayDirectory(), "xray.exe");
        if (!File.Exists(exe))
        {
            MarkBatchUnavailable(nodes, DelayFailureKind.MissingCore, "未找到 xray.exe，请先下载核心。", onNodeUpdated);
            return;
        }

        var ports = nodes.Select(_ => GetFreeTcpPort()).ToList();
        _paths.Ensure();
        var configFile = Path.Combine(_paths.AppDataRoot, $"xray-probe-batch-{Guid.NewGuid():N}.json");
        var logFile = Path.Combine(_paths.LogDirectory, $"xray-probe-batch-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        Process? process = null;

        try
        {
            await File.WriteAllTextAsync(configFile, _configBuilder.BuildBatchDelayTest(nodes, ports), cancellationToken);
            process = StartXray(exe, configFile, logFile);
            await Task.Delay(1200, cancellationToken);

            if (process.HasExited)
            {
                var issue = XrayErrorAdvisor.Analyze(ReadFileTail(logFile));
                MarkBatchUnavailable(nodes, DelayFailureKind.XrayStartupFailed, $"{issue.Title}：{issue.Suggestion}", onNodeUpdated);
                return;
            }

            var timeout = TimeSpan.FromSeconds(Math.Clamp(settings.DelayTestTimeoutSeconds, 3, 60));
            var tasks = nodes.Select((node, index) => TestNodeThroughPortAsync(
                node,
                ports[index],
                settings,
                timeout,
                onNodeUpdated,
                cancellationToken));

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            var issue = XrayErrorAdvisor.Analyze(ex.Message);
            MarkBatchUnavailable(nodes.Where(node => node.Status == NodeStatus.Testing), DelayFailureKind.XrayConfigInvalid, $"{issue.Title}：{issue.Suggestion}", onNodeUpdated);
        }
        finally
        {
            StopProcess(process);
            DeleteFile(configFile);
        }
    }

    private static async Task TestNodeThroughPortAsync(
        ProxyNode node,
        int httpPort,
        AppSettings settings,
        TimeSpan timeout,
        Action<ProxyNode>? onNodeUpdated,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await ProbeThroughHttpPortAsync(httpPort, settings, cancellationToken, timeout);
            ApplyResult(node, result);
        }
        catch (Exception ex)
        {
            ApplyResult(node, ProbeResult.Fail(DelayFailureKind.Unknown, ex.Message));
        }
        finally
        {
            onNodeUpdated?.Invoke(node);
        }
    }

    private static async Task<ProbeResult> ProbeThroughHttpPortAsync(
        int httpPort,
        AppSettings settings,
        CancellationToken cancellationToken,
        TimeSpan? timeoutOverride = null)
    {
        var timeout = timeoutOverride ?? TimeSpan.FromSeconds(Math.Clamp(settings.DelayTestTimeoutSeconds, 3, 60));
        var handler = new HttpClientHandler
        {
            Proxy = new WebProxy($"http://127.0.0.1:{httpPort}"),
            UseProxy = true
        };

        using var client = new HttpClient(handler) { Timeout = timeout };
        using var request = new HttpRequestMessage(HttpMethod.Get, NormalizeTestUrl(settings.DelayTestUrl, settings.DelayTestMode));
        request.Headers.UserAgent.ParseAdd("MyRayLite/1.0");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode && (int)response.StatusCode is < 200 or >= 500)
            {
                return ProbeResult.Fail(DelayFailureKind.InvalidResponse, $"测速地址返回 HTTP {(int)response.StatusCode}。");
            }

            var headerMs = (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue);
            if (settings.DelayTestMode != DelayTestMode.Download)
            {
                return ProbeResult.Success(headerMs, null);
            }

            var targetBytes = Math.Clamp(settings.DelayDownloadBytes, 128 * 1024, 8 * 1024 * 1024);
            var bytes = await ReadResponseBytesAsync(response, targetBytes, cancellationToken);
            stopwatch.Stop();

            if (bytes <= 0)
            {
                return ProbeResult.Fail(DelayFailureKind.InvalidResponse, "下载测速没有读取到有效数据。");
            }

            var seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
            var megabytesPerSecond = bytes / 1024d / 1024d / seconds;
            return ProbeResult.Success(headerMs, megabytesPerSecond);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProbeResult.Fail(settings.DelayTestMode == DelayTestMode.Download ? DelayFailureKind.DownloadTimeout : DelayFailureKind.HttpTimeout, "测速超时。");
        }
        catch (TaskCanceledException)
        {
            return ProbeResult.Fail(settings.DelayTestMode == DelayTestMode.Download ? DelayFailureKind.DownloadTimeout : DelayFailureKind.HttpTimeout, "测速超时。");
        }
        catch (HttpRequestException ex)
        {
            return ProbeResult.Fail(DelayFailureKind.ConnectionFailed, ex.Message);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    private static async Task<int> ReadResponseBytesAsync(HttpResponseMessage response, int targetBytes, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[32 * 1024];
        var total = 0;

        while (total < targetBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, targetBytes - total)), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static async Task<ProbeResult> TestTcpConnectDelayAsync(
        ProxyNode node,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(node.Address) || node.Port <= 0)
        {
            return ProbeResult.Fail(DelayFailureKind.ConnectionFailed, "节点地址或端口为空。");
        }

        try
        {
            using var client = new TcpClient();
            var stopwatch = Stopwatch.StartNew();
            await client.ConnectAsync(node.Address, node.Port, cancellationToken).AsTask().WaitAsync(timeout, cancellationToken);
            stopwatch.Stop();
            return ProbeResult.Success((int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue), null);
        }
        catch (TimeoutException)
        {
            return ProbeResult.Fail(DelayFailureKind.TcpTimeout, "TCP 连接超时。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProbeResult.Fail(DelayFailureKind.TcpTimeout, "TCP 连接超时。");
        }
        catch (SocketException ex)
        {
            return ProbeResult.Fail(DelayFailureKind.ConnectionFailed, ex.Message);
        }
        catch (Exception ex)
        {
            return ProbeResult.Fail(DelayFailureKind.ConnectionFailed, ex.Message);
        }
    }

    private static void ResetNodeForTest(ProxyNode node, DelayTestMode mode)
    {
        node.DelayMs = null;
        node.DownloadMbps = null;
        node.LastDelayTestMode = mode;
        node.LastFailureKind = DelayFailureKind.None;
        node.LastFailureReason = null;
        node.LastTested = DateTimeOffset.Now;
    }

    private bool PrepareSupportedNode(ProxyNode node)
    {
        if (node.IsSupportedByXray)
        {
            return true;
        }

        node.UnsupportedReason ??= $"Xray 暂不支持协议：{node.Protocol}";
        node.LastFailureKind = DelayFailureKind.UnsupportedProtocol;
        node.LastFailureReason = node.UnsupportedReason;
        node.Status = NodeStatus.Unavailable;
        _log.Warn($"跳过不支持节点：{node.Name}，{node.UnsupportedReason}");
        return false;
    }

    private static void ApplyResult(ProxyNode node, ProbeResult result)
    {
        node.DelayMs = result.DelayMs;
        node.DownloadMbps = result.DownloadMbps;
        node.LastFailureKind = result.FailureKind;
        node.LastFailureReason = result.FailureReason;
        node.Status = result.IsSuccess ? NodeStatus.Available : NodeStatus.Unavailable;
        node.LastTested = DateTimeOffset.Now;
    }

    private static string NormalizeTestUrl(string? url, DelayTestMode mode)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            return url.Trim();
        }

        return mode == DelayTestMode.Download
            ? "https://speed.cloudflare.com/__down?bytes=1048576"
            : "http://www.gstatic.com/generate_204";
    }

    private static string FormatMode(DelayTestMode mode) => mode switch
    {
        DelayTestMode.Tcp => "TCP",
        DelayTestMode.Http => "HTTP",
        DelayTestMode.Download => "下载",
        _ => "测速"
    };

    private static Process StartXray(string exe, string configFile, string logFile)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"run -config \"{configFile}\"",
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            },
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, e) => AppendProbeLog(logFile, e.Data);
        process.ErrorDataReceived += (_, e) => AppendProbeLog(logFile, e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static void MarkBatchUnavailable(IEnumerable<ProxyNode> nodes, DelayFailureKind kind, string reason, Action<ProxyNode>? onNodeUpdated)
    {
        foreach (var node in nodes)
        {
            node.DelayMs = null;
            node.DownloadMbps = null;
            node.Status = NodeStatus.Unavailable;
            node.LastFailureKind = kind;
            node.LastFailureReason = reason;
            node.LastTested = DateTimeOffset.Now;
            onNodeUpdated?.Invoke(node);
        }
    }

    private static void StopProcess(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(1500);
            }

            process?.Dispose();
        }
        catch
        {
            // Best effort.
        }
    }

    private static void DeleteFile(string file)
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
            // Best effort.
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void AppendProbeLog(string file, string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        try
        {
            File.AppendAllText(file, line + Environment.NewLine);
        }
        catch
        {
            // Best effort.
        }
    }

    private static string ReadFileTail(string file)
    {
        try
        {
            return File.Exists(file)
                ? string.Join(Environment.NewLine, File.ReadLines(file).TakeLast(30))
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private readonly record struct ProbeResult(bool IsSuccess, int? DelayMs, double? DownloadMbps, DelayFailureKind FailureKind, string? FailureReason)
    {
        public static ProbeResult Success(int delayMs, double? downloadMbps)
        {
            return new ProbeResult(true, delayMs, downloadMbps, DelayFailureKind.None, null);
        }

        public static ProbeResult Fail(DelayFailureKind kind, string reason)
        {
            return new ProbeResult(false, null, null, kind, reason);
        }
    }
}
