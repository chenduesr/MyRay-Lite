namespace V2RayLite.Core;

public enum ProxyMode
{
    Rule,
    Global,
    Direct
}

public enum ProtocolType
{
    Vmess,
    Vless,
    Trojan,
    Shadowsocks,
    Socks,
    Http,
    Unknown
}

public enum NodeStatus
{
    Unknown,
    Available,
    Unavailable,
    Testing
}

public sealed class AppSettings
{
    public string SubscriptionUrl { get; set; } = string.Empty;
    public ProxyMode ProxyMode { get; set; } = ProxyMode.Rule;
    public bool StartOnBoot { get; set; } = true;
    public bool AutoConnectOnLaunch { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public int HttpPort { get; set; } = 7890;
    public int SocksPort { get; set; } = 7891;
    public string DelayTestUrl { get; set; } = "http://www.gstatic.com/generate_204";
    public string? ActiveNodeId { get; set; }
    public DateTimeOffset? LastSubscriptionUpdate { get; set; }
}

public sealed class ProxyNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "未命名节点";
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; }
    public ProtocolType Protocol { get; set; } = ProtocolType.Unknown;
    public string Raw { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? Password { get; set; }
    public string? Method { get; set; }
    public string Network { get; set; } = "tcp";
    public string Security { get; set; } = "none";
    public string? Flow { get; set; }
    public string? Host { get; set; }
    public string? Path { get; set; }
    public string? ServiceName { get; set; }
    public string? Sni { get; set; }
    public string? Fingerprint { get; set; }
    public string? PublicKey { get; set; }
    public string? ShortId { get; set; }
    public string? SpiderX { get; set; }
    public int AlterId { get; set; }
    public string? VmessSecurity { get; set; }
    public string? UnsupportedReason { get; set; }
    public int? DelayMs { get; set; }
    public NodeStatus Status { get; set; } = NodeStatus.Unknown;
    public DateTimeOffset? LastTested { get; set; }
    public bool IsActive { get; set; }

    public bool IsSupportedByXray => Protocol is ProtocolType.Vmess
        or ProtocolType.Vless
        or ProtocolType.Trojan
        or ProtocolType.Shadowsocks
        or ProtocolType.Socks
        or ProtocolType.Http;

    public string DisplayDelay => Status switch
    {
        NodeStatus.Available when DelayMs is not null => $"{DelayMs}ms",
        NodeStatus.Unavailable => "不可用",
        NodeStatus.Testing => "测试中",
        _ => "—"
    };
}

public sealed class SubscriptionSnapshot
{
    public List<ProxyNode> Nodes { get; set; } = [];
    public DateTimeOffset? LastUpdated { get; set; }
    public string StatusText { get; set; } = "未更新";
}

public sealed class RuntimeState
{
    public bool IsProxyEnabled { get; set; }
    public string StatusText => IsProxyEnabled ? "已开启" : "未开启";
    public string SidebarStatus => IsProxyEnabled ? "已连接" : "未连接";
}

public sealed class LogEntry
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string Level { get; set; } = "INFO";
    public string Message { get; set; } = string.Empty;

    public override string ToString()
    {
        return $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level}] {Message}";
    }
}
