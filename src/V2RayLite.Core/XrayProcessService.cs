using System.Diagnostics;

namespace V2RayLite.Core;

public sealed class XrayProcessService
{
    private readonly AppPaths _paths;
    private readonly XrayConfigBuilder _configBuilder;
    private Process? _process;

    public XrayProcessService(AppPaths paths, XrayConfigBuilder configBuilder)
    {
        _paths = paths;
        _configBuilder = configBuilder;
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

        _process.Start();
        _ = Task.Run(() => DrainLogsAsync(_process), CancellationToken.None);
        await Task.Delay(700, cancellationToken);

        if (_process.HasExited)
        {
            throw new InvalidOperationException("Xray 启动失败，请检查节点配置和端口是否被占用。");
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
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(2000);
            }
        }
        catch
        {
            // Best effort on app shutdown.
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    private async Task DrainLogsAsync(Process process)
    {
        try
        {
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(stdout) && string.IsNullOrWhiteSpace(stderr))
            {
                return;
            }

            _paths.Ensure();
            var logFile = Path.Combine(_paths.LogDirectory, $"xray-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            await File.WriteAllTextAsync(logFile, stdout + Environment.NewLine + stderr);
        }
        catch
        {
            // Logging cannot block runtime control.
        }
    }
}
