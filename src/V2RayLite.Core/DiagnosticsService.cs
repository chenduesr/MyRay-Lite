using System.Net;
using System.Net.Sockets;

namespace V2RayLite.Core;

public sealed class DiagnosticsService
{
    private readonly AppPaths _paths;
    private readonly XrayConfigBuilder _configBuilder;

    public DiagnosticsService(AppPaths paths, XrayConfigBuilder configBuilder)
    {
        _paths = paths;
        _configBuilder = configBuilder;
    }

    public IReadOnlyList<DiagnosticIssue> Run(AppSettings settings, ProxyNode? activeNode)
    {
        var issues = new List<DiagnosticIssue>();
        var xrayDirectory = _paths.FindXrayDirectory();
        var xrayExe = Path.Combine(xrayDirectory, "xray.exe");

        if (!File.Exists(xrayExe))
        {
            issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.Error,
                Title = "未找到 Xray-core",
                Detail = $"缺少 {xrayExe}",
                Suggestion = "点击设置页“下载核心”，或下载包含 Xray-core 的 Release 包。"
            });
        }

        CheckPort(settings.HttpPort, "HTTP", issues);
        CheckPort(settings.SocksPort, "SOCKS", issues);

        if (settings.ProxyMode == ProxyMode.Rule)
        {
            CheckDataFile(Path.Combine(xrayDirectory, "geoip.dat"), "geoip.dat", issues);
            CheckDataFile(Path.Combine(xrayDirectory, "geosite.dat"), "geosite.dat", issues);
        }

        if (activeNode is null)
        {
            issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.Warning,
                Title = "未选择节点",
                Detail = "当前没有可用于连接或测速的节点。",
                Suggestion = "先在订阅页更新订阅，再在节点页选择一个节点。"
            });
        }
        else
        {
            ValidateActiveNode(settings, activeNode, issues);
        }

        if (issues.Count == 0)
        {
            issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.Info,
                Title = "诊断通过",
                Detail = "核心文件、端口和当前节点基础配置没有发现明显问题。",
                Suggestion = "如果仍无法连接，建议切到 HTTP 测速或查看 Xray 运行日志。"
            });
        }

        return issues;
    }

    private void ValidateActiveNode(AppSettings settings, ProxyNode node, List<DiagnosticIssue> issues)
    {
        if (!node.IsSupportedByXray)
        {
            issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.Error,
                Title = "节点协议不支持",
                Detail = node.UnsupportedReason ?? $"当前节点协议为 {node.Protocol}。",
                Suggestion = "换用 Xray 支持的节点，或等待后续版本补齐该协议。"
            });
            return;
        }

        try
        {
            _ = _configBuilder.Build(node, settings);
        }
        catch (Exception ex)
        {
            var issue = XrayErrorAdvisor.Analyze(ex.Message);
            issue.Title = "当前节点配置生成失败";
            issues.Add(issue);
        }
    }

    private static void CheckPort(int port, string label, List<DiagnosticIssue> issues)
    {
        if (port is <= 0 or > 65535)
        {
            issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.Error,
                Title = $"{label} 端口无效",
                Detail = $"当前端口为 {port}。",
                Suggestion = "端口应设置为 1 到 65535 之间，建议使用 7890 / 7891。"
            });
            return;
        }

        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
        }
        catch
        {
            issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.Error,
                Title = $"{label} 端口被占用",
                Detail = $"127.0.0.1:{port} 当前不可监听。",
                Suggestion = "在设置页更换端口，或关闭占用该端口的其他代理软件。"
            });
        }
        finally
        {
            listener?.Stop();
        }
    }

    private static void CheckDataFile(string file, string name, List<DiagnosticIssue> issues)
    {
        if (File.Exists(file))
        {
            return;
        }

        issues.Add(new DiagnosticIssue
        {
            Severity = DiagnosticSeverity.Warning,
            Title = $"缺少 {name}",
            Detail = "规则模式可能无法加载大陆绕过规则。",
            Suggestion = "点击设置页“下载核心”，或把完整 Xray-core 数据文件放到 bin/xray。"
        });
    }
}
