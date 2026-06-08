using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace V2RayLite.Core;

public sealed class DelayTestService
{
    private const int BatchSize = 40;
    private const int RetryBatchSize = 8;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly AppPaths _paths;
    private readonly XrayConfigBuilder _configBuilder;
    private readonly AppLogService _log;

    public DelayTestService(AppPaths paths, XrayConfigBuilder configBuilder, AppLogService log)
    {
        _paths = paths;
        _configBuilder = configBuilder;
        _log = log;
    }

    public async Task<int?> TestProxyDelayAsync(ProxyNode node, string testUrl, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!node.IsSupportedByXray)
        {
            node.UnsupportedReason ??= $"Xray 暂不支持协议：{node.Protocol}";
            _log.Warn($"跳过不支持节点：{node.Name}，{node.UnsupportedReason}");
            return null;
        }

        var exe = Path.Combine(_paths.FindXrayDirectory(), "xray.exe");
        if (!File.Exists(exe))
        {
            _log.Error("测速失败：未找到 xray.exe。");
            return null;
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
                _log.Warn($"测速 Xray 启动失败：{node.Name}");
                return null;
            }

            return await ProbeThroughHttpPortAsync(node, httpPort, testUrl, timeout, cancellationToken);
        }
        catch (Exception ex)
        {
            _log.Warn($"节点测速失败：{node.Name}，{ex.Message}");
            return null;
        }
        finally
        {
            StopProcess(process);
            DeleteFile(configFile);
        }
    }

    public async Task TestManyAsync(IEnumerable<ProxyNode> nodes, string testUrl, Action<ProxyNode>? onNodeUpdated = null, CancellationToken cancellationToken = default)
    {
        var nodeList = nodes.ToList();
        var supportedNodes = new List<ProxyNode>();

        foreach (var node in nodeList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            node.DelayMs = null;
            node.LastTested = DateTimeOffset.Now;

            if (!node.IsSupportedByXray)
            {
                node.UnsupportedReason ??= $"Xray 暂不支持协议：{node.Protocol}";
                node.Status = NodeStatus.Unavailable;
                onNodeUpdated?.Invoke(node);
                _log.Warn($"跳过不支持节点：{node.Name}，{node.UnsupportedReason}");
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

        _log.Info($"开始批量测速：{supportedNodes.Count} 个节点，每批 {BatchSize} 个。");
        foreach (var batch in supportedNodes.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RunBatchDelayTestAsync(batch, testUrl, DefaultTimeout, onNodeUpdated, cancellationToken);
        }

        var retryNodes = supportedNodes
            .Where(node => node.Status == NodeStatus.Unavailable)
            .ToList();

        if (retryNodes.Count == 0)
        {
            _log.Info("批量测速完成，没有需要重试的节点。");
            return;
        }

        _log.Info($"开始失败重试：{retryNodes.Count} 个节点，每批 {RetryBatchSize} 个。");
        foreach (var node in retryNodes)
        {
            node.DelayMs = null;
            node.Status = NodeStatus.Testing;
            onNodeUpdated?.Invoke(node);
        }

        foreach (var batch in retryNodes.Chunk(RetryBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RunBatchDelayTestAsync(batch, testUrl, DefaultTimeout, onNodeUpdated, cancellationToken);
        }
    }

    private async Task RunBatchDelayTestAsync(
        IReadOnlyList<ProxyNode> nodes,
        string testUrl,
        TimeSpan timeout,
        Action<ProxyNode>? onNodeUpdated,
        CancellationToken cancellationToken)
    {
        var exe = Path.Combine(_paths.FindXrayDirectory(), "xray.exe");
        if (!File.Exists(exe))
        {
            _log.Error("批量测速失败：未找到 xray.exe。");
            MarkBatchUnavailable(nodes, onNodeUpdated);
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
                _log.Warn("批量测速 Xray 启动失败，请查看日志诊断。");
                MarkBatchUnavailable(nodes, onNodeUpdated);
                return;
            }

            var tasks = nodes.Select((node, index) => TestNodeThroughPortAsync(
                node,
                ports[index],
                testUrl,
                timeout,
                onNodeUpdated,
                cancellationToken));

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _log.Warn($"批量测速失败：{ex.Message}");
            MarkBatchUnavailable(nodes.Where(node => node.Status == NodeStatus.Testing), onNodeUpdated);
        }
        finally
        {
            StopProcess(process);
            DeleteFile(configFile);
        }
    }

    private async Task TestNodeThroughPortAsync(
        ProxyNode node,
        int httpPort,
        string testUrl,
        TimeSpan timeout,
        Action<ProxyNode>? onNodeUpdated,
        CancellationToken cancellationToken)
    {
        try
        {
            var delay = await ProbeThroughHttpPortAsync(node, httpPort, testUrl, timeout, cancellationToken);
            node.DelayMs = delay;
            node.Status = delay is null ? NodeStatus.Unavailable : NodeStatus.Available;
        }
        catch (Exception ex)
        {
            _log.Warn($"节点测速失败：{node.Name}，{ex.Message}");
            node.DelayMs = null;
            node.Status = NodeStatus.Unavailable;
        }
        finally
        {
            node.LastTested = DateTimeOffset.Now;
            onNodeUpdated?.Invoke(node);
        }
    }

    private static async Task<int?> ProbeThroughHttpPortAsync(
        ProxyNode node,
        int httpPort,
        string testUrl,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var handler = new HttpClientHandler
        {
            Proxy = new WebProxy($"http://127.0.0.1:{httpPort}"),
            UseProxy = true
        };

        using var client = new HttpClient(handler) { Timeout = timeout };
        using var request = new HttpRequestMessage(HttpMethod.Get, string.IsNullOrWhiteSpace(testUrl) ? "http://www.gstatic.com/generate_204" : testUrl);
        request.Headers.UserAgent.ParseAdd("MyRayLite/1.0");

        var stopwatch = Stopwatch.StartNew();
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        stopwatch.Stop();

        var ok = response.IsSuccessStatusCode || (int)response.StatusCode is >= 200 and < 500;
        return ok ? (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue) : null;
    }

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

    private static void MarkBatchUnavailable(IEnumerable<ProxyNode> nodes, Action<ProxyNode>? onNodeUpdated)
    {
        foreach (var node in nodes)
        {
            node.DelayMs = null;
            node.Status = NodeStatus.Unavailable;
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
}
