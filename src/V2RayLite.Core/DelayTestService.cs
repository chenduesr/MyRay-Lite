using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace V2RayLite.Core;

public sealed class DelayTestService
{
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
            process = new Process
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
            await Task.Delay(900, cancellationToken);

            if (process.HasExited)
            {
                _log.Warn($"测速 Xray 启动失败：{node.Name}");
                return null;
            }

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

            return response.IsSuccessStatusCode || (int)response.StatusCode is >= 200 and < 500
                ? (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue)
                : null;
        }
        catch (Exception ex)
        {
            _log.Warn($"节点测速失败：{node.Name}，{ex.Message}");
            return null;
        }
        finally
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

            try
            {
                if (File.Exists(configFile))
                {
                    File.Delete(configFile);
                }
            }
            catch
            {
                // Best effort.
            }
        }
    }

    public async Task TestManyAsync(IEnumerable<ProxyNode> nodes, string testUrl, Action<ProxyNode>? onNodeUpdated = null, CancellationToken cancellationToken = default)
    {
        using var gate = new SemaphoreSlim(3);
        var tasks = nodes.Select(async node =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                node.Status = NodeStatus.Testing;
                onNodeUpdated?.Invoke(node);

                var delay = await TestProxyDelayAsync(node, testUrl, TimeSpan.FromSeconds(10), cancellationToken);
                node.DelayMs = delay;
                node.Status = delay is null ? NodeStatus.Unavailable : NodeStatus.Available;
                node.LastTested = DateTimeOffset.Now;
                onNodeUpdated?.Invoke(node);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
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
