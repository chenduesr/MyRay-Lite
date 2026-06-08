using System.Net;
using System.Text;
using System.Text.Json;

namespace V2RayLite.Core;

public sealed class SubscriptionParser
{
    private static readonly string[] SupportedSchemes =
    [
        "vmess://",
        "vless://",
        "trojan://",
        "ss://",
        "socks://",
        "http://",
        "https://",
        "hysteria2://",
        "hy2://",
        "tuic://",
        "anytls://"
    ];

    public IReadOnlyList<ProxyNode> Parse(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        var candidates = ExpandPayload(payload.Trim());
        var nodes = new List<ProxyNode>();

        foreach (var line in candidates)
        {
            try
            {
                var node = ParseLine(line);
                if (node is not null)
                {
                    nodes.Add(node);
                }
            }
            catch
            {
                // Skip malformed entries and keep the rest of the subscription usable.
            }
        }

        if (nodes.Count == 0)
        {
            nodes.AddRange(ParseClashYaml(DecodeBase64IfNeeded(payload.Trim())));
        }

        return nodes
            .GroupBy(node => node.Id)
            .Select(group => group.First())
            .ToList();
    }

    private static IEnumerable<string> ExpandPayload(string payload)
    {
        var decoded = DecodeBase64IfNeeded(payload);
        foreach (var text in new[] { payload, decoded }.Distinct())
        {
            foreach (var token in ExtractLinks(text))
            {
                yield return token;
            }
        }
    }

    private static IEnumerable<string> ExtractLinks(string text)
    {
        var normalized = text.Replace("\r", "\n");
        foreach (var line in normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("proxies:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (SupportedSchemes.Any(scheme => line.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)))
            {
                yield return line;
                continue;
            }

            foreach (var scheme in SupportedSchemes)
            {
                var index = line.IndexOf(scheme, StringComparison.OrdinalIgnoreCase);
                while (index >= 0)
                {
                    var next = SupportedSchemes
                        .Select(s => line.IndexOf(s, index + scheme.Length, StringComparison.OrdinalIgnoreCase))
                        .Where(i => i > index)
                        .DefaultIfEmpty(line.Length)
                        .Min();
                    yield return line[index..next].Trim().Trim(',', '"', '\'');
                    index = line.IndexOf(scheme, next, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    private static ProxyNode? ParseLine(string line)
    {
        if (line.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
        {
            return ParseVmess(line);
        }

        if (line.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
        {
            return ParseUriNode(line, ProtocolType.Vless);
        }

        if (line.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase))
        {
            return ParseUriNode(line, ProtocolType.Trojan);
        }

        if (line.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
        {
            return ParseShadowsocks(line);
        }

        if (line.StartsWith("socks://", StringComparison.OrdinalIgnoreCase))
        {
            return ParseUriNode(line, ProtocolType.Socks);
        }

        if (line.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || line.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return ParseUriNode(line, ProtocolType.Http);
        }

        if (line.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase) || line.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase))
        {
            return ParseUriNode(line, ProtocolType.Hysteria2);
        }

        if (line.StartsWith("tuic://", StringComparison.OrdinalIgnoreCase))
        {
            return ParseUriNode(line, ProtocolType.Tuic);
        }

        if (line.StartsWith("anytls://", StringComparison.OrdinalIgnoreCase))
        {
            return ParseUriNode(line, ProtocolType.AnyTls);
        }

        return null;
    }

    private static ProxyNode? ParseVmess(string line)
    {
        var body = line["vmess://".Length..];
        var json = DecodeBase64IfNeeded(body);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var address = GetString(root, "add");
        var port = GetInt(root, "port");
        var id = GetString(root, "id");
        if (string.IsNullOrWhiteSpace(address) || port <= 0 || string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var security = GetString(root, "tls");
        if (string.IsNullOrWhiteSpace(security))
        {
            security = GetString(root, "security");
        }

        var vmessSecurity = GetString(root, "scy");
        var network = GetString(root, "net");

        return new ProxyNode
        {
            Id = StableId(line),
            Raw = line,
            Protocol = ProtocolType.Vmess,
            Name = CleanName(GetString(root, "ps"), address),
            Address = address,
            Port = port,
            UserId = id,
            AlterId = GetInt(root, "aid"),
            VmessSecurity = string.IsNullOrWhiteSpace(vmessSecurity) ? "auto" : vmessSecurity,
            Network = string.IsNullOrWhiteSpace(network) ? "tcp" : network,
            Security = string.IsNullOrWhiteSpace(security) ? "none" : security,
            Host = GetString(root, "host"),
            Path = GetString(root, "path"),
            Sni = GetString(root, "sni")
        };
    }

    private static ProxyNode? ParseUriNode(string line, ProtocolType protocol)
    {
        if (!Uri.TryCreate(line, UriKind.Absolute, out var uri) || uri.Port <= 0)
        {
            return null;
        }

        var user = Uri.UnescapeDataString(uri.UserInfo);
        var query = ParseQuery(uri.Query);
        var name = CleanName(string.IsNullOrWhiteSpace(uri.Fragment) ? null : Uri.UnescapeDataString(uri.Fragment), uri.Host);
        var network = GetQuery(query, "type", "tcp") ?? "tcp";
        var defaultSecurity = protocol is ProtocolType.Trojan or ProtocolType.Hysteria2 or ProtocolType.AnyTls ? "tls" : "none";
        var security = GetQuery(query, "security", defaultSecurity) ?? defaultSecurity;

        string? username = null;
        string? password = null;
        if (protocol is ProtocolType.Http or ProtocolType.Socks && !string.IsNullOrWhiteSpace(user))
        {
            var parts = user.Split(':', 2);
            username = parts[0];
            password = parts.Length == 2 ? parts[1] : null;
        }
        else if (protocol == ProtocolType.Tuic && !string.IsNullOrWhiteSpace(user))
        {
            var parts = user.Split(':', 2);
            username = parts[0];
            password = parts.Length == 2 ? parts[1] : GetQuery(query, "password");
        }

        var node = new ProxyNode
        {
            Id = StableId(line),
            Raw = line,
            Protocol = protocol,
            Name = name,
            Address = uri.Host,
            Port = uri.Port,
            UserId = protocol == ProtocolType.Vless ? user : username,
            Password = protocol is ProtocolType.Trojan or ProtocolType.Hysteria2 or ProtocolType.AnyTls ? user : password,
            Network = network,
            Security = security,
            Flow = GetQuery(query, "flow"),
            Host = GetQuery(query, "host"),
            Path = GetQuery(query, "path"),
            ServiceName = GetQuery(query, "serviceName", GetQuery(query, "grpc-service-name")),
            Sni = GetQuery(query, "sni", GetQuery(query, "peer", GetQuery(query, "servername"))),
            Fingerprint = GetQuery(query, "fp", GetQuery(query, "fingerprint", GetQuery(query, "client-fingerprint"))),
            PublicKey = GetQuery(query, "pbk", GetQuery(query, "public-key")),
            ShortId = GetQuery(query, "sid", GetQuery(query, "short-id")),
            SpiderX = GetQuery(query, "spx", GetQuery(query, "spider-x")),
            Alpn = GetQuery(query, "alpn"),
            Obfs = GetQuery(query, "obfs", GetQuery(query, "obfs-type")),
            ObfsPassword = GetQuery(query, "obfs-password", GetQuery(query, "obfs_password")),
            CongestionControl = GetQuery(query, "congestion-control"),
            AllowInsecure = IsTrue(GetQuery(query, "allowInsecure", GetQuery(query, "insecure")))
        };

        if (protocol == ProtocolType.Tuic)
        {
            node.UnsupportedReason = "已解析 TUIC 节点，但当前 Xray-core 不支持 TUIC 出站；可等待后续 sing-box 核心支持。";
        }

        return node;
    }

    private static ProxyNode? ParseShadowsocks(string line)
    {
        var withoutScheme = line["ss://".Length..];
        var fragmentIndex = withoutScheme.IndexOf('#');
        var name = fragmentIndex >= 0 ? Uri.UnescapeDataString(withoutScheme[(fragmentIndex + 1)..]) : null;
        var body = fragmentIndex >= 0 ? withoutScheme[..fragmentIndex] : withoutScheme;
        var queryIndex = body.IndexOf("/?", StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            body = body[..queryIndex];
        }

        var atIndex = body.LastIndexOf('@');
        string method;
        string password;
        string hostPort;
        if (atIndex >= 0)
        {
            var userInfo = DecodeBase64IfNeeded(body[..atIndex]);
            var parts = userInfo.Split(':', 2);
            if (parts.Length != 2)
            {
                return null;
            }

            method = parts[0];
            password = parts[1];
            hostPort = body[(atIndex + 1)..];
        }
        else
        {
            var decoded = DecodeBase64IfNeeded(body);
            var decodedAt = decoded.LastIndexOf('@');
            if (decodedAt < 0)
            {
                return null;
            }

            var parts = decoded[..decodedAt].Split(':', 2);
            if (parts.Length != 2)
            {
                return null;
            }

            method = parts[0];
            password = parts[1];
            hostPort = decoded[(decodedAt + 1)..];
        }

        var hp = SplitHostPort(hostPort);
        if (hp is null)
        {
            return null;
        }

        return new ProxyNode
        {
            Id = StableId(line),
            Raw = line,
            Protocol = ProtocolType.Shadowsocks,
            Name = CleanName(name, hp.Value.Host),
            Address = hp.Value.Host,
            Port = hp.Value.Port,
            Method = method,
            Password = password
        };
    }

    private static IEnumerable<ProxyNode> ParseClashYaml(string text)
    {
        var nodes = new List<ProxyNode>();
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var currentRaw = new StringBuilder();

        foreach (var rawLine in text.Replace("\r", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.Trim();
            if (trimmed.StartsWith("- {", StringComparison.Ordinal))
            {
                FlushInlineClash(trimmed, nodes);
                continue;
            }

            if (trimmed.StartsWith("- name:", StringComparison.OrdinalIgnoreCase))
            {
                FlushBlockClash(current, currentRaw.ToString(), nodes);
                current.Clear();
                currentRaw.Clear();
                AddYamlKeyValue(current, trimmed[2..]);
                currentRaw.AppendLine(rawLine);
                continue;
            }

            if (current.Count > 0)
            {
                currentRaw.AppendLine(rawLine);
                AddYamlKeyValue(current, trimmed);
            }
        }

        FlushBlockClash(current, currentRaw.ToString(), nodes);
        return nodes;
    }

    private static void FlushInlineClash(string line, List<ProxyNode> nodes)
    {
        var content = line.Trim().TrimStart('-').Trim().Trim('{', '}');
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in content.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            AddYamlKeyValue(map, part.Trim());
        }

        var node = BuildClashNode(map, line);
        if (node is not null)
        {
            nodes.Add(node);
        }
    }

    private static void FlushBlockClash(Dictionary<string, string> map, string raw, List<ProxyNode> nodes)
    {
        if (map.Count == 0)
        {
            return;
        }

        var node = BuildClashNode(map, raw);
        if (node is not null)
        {
            nodes.Add(node);
        }
    }

    private static ProxyNode? BuildClashNode(Dictionary<string, string> map, string raw)
    {
        var type = GetMap(map, "type")?.ToLowerInvariant();
        var server = GetMap(map, "server");
        var portText = GetMap(map, "port");
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(server) || !int.TryParse(portText, out var port))
        {
            return null;
        }

        var protocol = type switch
        {
            "vmess" => ProtocolType.Vmess,
            "vless" => ProtocolType.Vless,
            "trojan" => ProtocolType.Trojan,
            "ss" => ProtocolType.Shadowsocks,
            "shadowsocks" => ProtocolType.Shadowsocks,
            "socks5" => ProtocolType.Socks,
            "http" => ProtocolType.Http,
            "hysteria2" => ProtocolType.Hysteria2,
            "hy2" => ProtocolType.Hysteria2,
            "tuic" => ProtocolType.Tuic,
            "anytls" => ProtocolType.AnyTls,
            _ => ProtocolType.Unknown
        };

        var hasReality = !string.IsNullOrWhiteSpace(GetMap(map, "public-key", GetMap(map, "pbk")));
        var tlsEnabled = IsTrue(GetMap(map, "tls"))
            || protocol is ProtocolType.Trojan or ProtocolType.Hysteria2 or ProtocolType.AnyTls;
        var security = hasReality ? "reality" : tlsEnabled ? "tls" : GetMap(map, "security", "none") ?? "none";
        var unsupportedReason = protocol switch
        {
            ProtocolType.Unknown => $"Xray 暂不支持 Clash 节点类型：{type}",
            ProtocolType.Tuic => "已解析 TUIC 节点，但当前 Xray-core 不支持 TUIC 出站；可等待后续 sing-box 核心支持。",
            _ => null
        };

        var node = new ProxyNode
        {
            Id = StableId(raw),
            Raw = raw,
            Name = CleanName(GetMap(map, "name"), server),
            Address = server,
            Port = port,
            Protocol = protocol,
            UserId = GetMap(map, "uuid", GetMap(map, "username")),
            Password = GetMap(map, "password"),
            Method = GetMap(map, "cipher"),
            AlterId = int.TryParse(GetMap(map, "alterId", GetMap(map, "alter-id")), out var alterId) ? alterId : 0,
            VmessSecurity = GetMap(map, "security", "auto"),
            Network = GetMap(map, "network", "tcp") ?? "tcp",
            Security = security,
            Sni = GetMap(map, "servername", GetMap(map, "sni")),
            Host = GetMap(map, "Host", GetMap(map, "host")),
            Path = GetMap(map, "path", GetMap(map, "ws-path")),
            ServiceName = GetMap(map, "grpc-service-name", GetMap(map, "service-name")),
            Fingerprint = GetMap(map, "client-fingerprint", GetMap(map, "fingerprint")),
            PublicKey = GetMap(map, "public-key", GetMap(map, "pbk")),
            ShortId = GetMap(map, "short-id", GetMap(map, "sid")),
            SpiderX = GetMap(map, "spider-x", GetMap(map, "spx")),
            Alpn = GetMap(map, "alpn"),
            Obfs = GetMap(map, "obfs", GetMap(map, "obfs-type")),
            ObfsPassword = GetMap(map, "obfs-password", GetMap(map, "obfs_password")),
            CongestionControl = GetMap(map, "congestion-control"),
            AllowInsecure = IsTrue(GetMap(map, "skip-cert-verify", GetMap(map, "allowInsecure", GetMap(map, "insecure")))),
            UnsupportedReason = protocol == ProtocolType.Unknown ? $"Xray 暂不支持 Clash 节点类型：{type}" : null
        };

        node.UnsupportedReason = unsupportedReason ?? node.UnsupportedReason;
        return node;
    }

    private static void AddYamlKeyValue(Dictionary<string, string> map, string text)
    {
        var index = text.IndexOf(':');
        if (index <= 0)
        {
            return;
        }

        var key = text[..index].Trim().Trim('"', '\'');
        var value = text[(index + 1)..].Trim().Trim('"', '\'');
        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
        {
            map[key] = value;
            AddInlineYamlObject(map, value);
        }
    }

    private static void AddInlineYamlObject(Dictionary<string, string> map, string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
        {
            return;
        }

        foreach (var part in SplitInlineYaml(trimmed[1..^1]))
        {
            AddYamlKeyValue(map, part);
        }
    }

    private static IEnumerable<string> SplitInlineYaml(string value)
    {
        var depth = 0;
        var start = 0;
        for (var index = 0; index < value.Length; index++)
        {
            depth += value[index] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0
            };

            if (value[index] == ',' && depth == 0)
            {
                yield return value[start..index].Trim();
                start = index + 1;
            }
        }

        if (start < value.Length)
        {
            yield return value[start..].Trim();
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return result;
        }

        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = WebUtility.UrlDecode(parts[0]);
            var value = parts.Length == 2 ? WebUtility.UrlDecode(parts[1]) : string.Empty;
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static (string Host, int Port)? SplitHostPort(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            var close = trimmed.IndexOf(']');
            if (close > 0 && close + 2 < trimmed.Length && int.TryParse(trimmed[(close + 2)..], out var ipv6Port))
            {
                return (trimmed[1..close], ipv6Port);
            }
        }

        var hp = trimmed.Split(':', 2);
        return hp.Length == 2 && int.TryParse(hp[1], out var port) ? (hp[0], port) : null;
    }

    private static string? GetQuery(Dictionary<string, string> query, string key, string? fallback = null)
    {
        return query.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
    }

    private static string? GetMap(Dictionary<string, string> map, string key, string? fallback = null)
    {
        return map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
    }

    private static bool IsTrue(string? value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
    }

    private static string DecodeBase64IfNeeded(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Contains("://", StringComparison.Ordinal) || trimmed.StartsWith('{') || trimmed.Contains("proxies:", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        try
        {
            var normalized = trimmed.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
            var bytes = Convert.FromBase64String(normalized);
            var decoded = Encoding.UTF8.GetString(bytes);
            return string.IsNullOrWhiteSpace(decoded) ? value : decoded;
        }
        catch
        {
            return value;
        }
    }

    private static string StableId(string value)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static string CleanName(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static int GetInt(JsonElement element, string name)
    {
        var value = GetString(element, name);
        return int.TryParse(value, out var parsed) ? parsed : 0;
    }
}
