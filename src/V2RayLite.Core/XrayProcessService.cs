using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace V2RayLite.Core;

public sealed class XrayProcessService
{
    private readonly AppPaths _paths;
    private readonly XrayConfigBuilder _configBuilder;
    private readonly AppLogService _log;
    private Process? _process;
    private string? _runtimeLogFile;

    public XrayProcessService(AppPaths paths, XrayConfigBuilder configBuilder, AppLogService log)
    {
        _paths = paths;
        _configBuilder = configBuilder;
        _log = log;
    }

    public bool IsRunning => _process is { HasExited: false };

    public string XrayExePath => Path.Combine(_paths.FindXrayDirectory(), "xray.exe");

    public async Task<string> StartAsync(ProxyNode node, AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return "Xray 已在运行";
        }

        var exe = XrayExePath;
        if (!File.Exists(exe))
        {
            throw new FileNotFoundException("未找到 xray.exe，请下载核心或放到 bin/xray。", exe);
        }

        _paths.Ensure();
        var config = _configBuilder.Build(node, settings);
        await File.WriteAllTextAsync(_paths.GeneratedConfigFile, config, cancellationToken);
        _runtimeLogFile = Path.Combine(_paths.LogDirectory, $"xray-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        EnsurePortAvailable(settings.HttpPort, "HTTP");
        EnsurePortAvailable(settings.SocksPort, "SOCKS");

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"run -config \"{_paths.GeneratedConfigFile}\"",
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            },
            EnableRaisingEvents = true
        };

        _process.OutputDataReceived += (_, e) => AppendXrayLog(e.Data);
        _process.ErrorDataReceived += (_, e) => AppendXrayLog(e.Data);
        _process.Exited += (_, _) => _log.Warn("Xray 进程已退出。");

        _log.Info($"启动 Xray：{node.Name} ({node.Protocol} {node.Address}:{node.Port})");
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await Task.Delay(800, cancellationToken);
        if (_process.HasExited)
        {
            var issue = XrayErrorAdvisor.Analyze(ReadRuntimeLogTail());
            _log.Error($"Xray 启动失败：{issue.Title}。{issue.Suggestion}");
            throw new InvalidOperationException($"Xray 启动失败：{issue.Title}。{issue.Suggestion}");
        }

        return "Xray 已启动";
    }

    public void Stop()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _log.Info("正在停止 Xray。");
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(2000);
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"停止 Xray 时发生异常：{ex.Message}");
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    private void AppendXrayLog(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(_runtimeLogFile))
        {
            return;
        }

        try
        {
            _paths.Ensure();
            File.AppendAllText(_runtimeLogFile, line + Environment.NewLine);
        }
        catch
        {
            // Runtime logging is best effort.
        }
    }

    private static void EnsurePortAvailable(int port, string label)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
        }
        catch
        {
            throw new InvalidOperationException($"{label} 端口 {port} 已被占用，请在设置页更换端口或关闭其他代理软件。");
        }
        finally
        {
            listener?.Stop();
        }
    }

    private string ReadRuntimeLogTail()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_runtimeLogFile) || !File.Exists(_runtimeLogFile))
            {
                return string.Empty;
            }

            return string.Join(Environment.NewLine, File.ReadLines(_runtimeLogFile).TakeLast(30));
        }
        catch
        {
            return string.Empty;
        }
    }
}
