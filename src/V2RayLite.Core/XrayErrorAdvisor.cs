namespace V2RayLite.Core;

public static class XrayErrorAdvisor
{
    public static DiagnosticIssue Analyze(string message)
    {
        var lower = message.ToLowerInvariant();

        if (ContainsAny(lower, "address already in use", "failed to listen", "bind", "only one usage"))
        {
            return new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.Error,
                Title = "端口被占用",
                Detail = "Xray 无法监听本地代理端口。",
                Suggestion = "在设置页更换 HTTP/SOCKS 端口，或关闭正在占用端口的程序。"
            };
        }

        if (ContainsAny(lower, "failed to parse", "invalid config", "invalid json", "unknown field"))
        {
            return new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.Error,
                Title = "配置格式错误",
                Detail = "Xray 无法解析生成的配置文件。",
                Suggestion = "重新选择节点或更新订阅；如果仍失败，请把日志中的节点协议参数发给开发者检查。"
            };
        }

        if (ContainsAny(lower, "unknown outbound protocol", "unsupported protocol", "unknown protocol"))
        {
            return new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.Error,
                Title = "协议不支持",
                Detail = "当前 Xray-core 不支持该节点协议或参数。",
                Suggestion = "更新 Xray-core，或换用 VMess、VLESS、Trojan、Shadowsocks、Hysteria2、AnyTLS 等已支持节点。"
            };
        }

        if (ContainsAny(lower, "geosite.dat", "geoip.dat", "failed to load geo"))
        {
            return new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.Error,
                Title = "缺少 geo 数据文件",
                Detail = "规则路由需要 geoip.dat / geosite.dat。",
                Suggestion = "点击设置页“下载核心”，或重新下载完整 Release 包。"
            };
        }

        if (ContainsAny(lower, "reality", "publickey", "shortid", "fingerprint"))
        {
            return new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.Warning,
                Title = "Reality 参数可能不完整",
                Detail = "节点 Reality 参数无法被 Xray 正确使用。",
                Suggestion = "更新订阅解析后重试，或检查 publicKey、shortId、serverName、fingerprint 参数。"
            };
        }

        return new DiagnosticIssue
        {
            Severity = DiagnosticSeverity.Warning,
            Title = "Xray 运行异常",
            Detail = string.IsNullOrWhiteSpace(message) ? "未捕获到详细错误。" : message,
            Suggestion = "查看日志诊断页的 Xray 输出，确认节点参数、端口和核心文件是否正常。"
        };
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(value.Contains);
    }
}
