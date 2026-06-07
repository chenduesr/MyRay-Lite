using System.Net;
using System.Text;
using System.Text.Json;

namespace V2RayLite.Core;

public sealed class SubscriptionParser
{
    public IReadOnlyList<ProxyNode> Parse(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        var text = DecodeBase64IfNeeded(payload.Trim());
        var lines = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("#", StringComparison.Ordinal))
            .ToArray();

        var nodes = new List<ProxyNode>();
        foreach (var line in lines)
        {
            var node = ParseLine(line);
            if (node is not null)
            {
                nodes.Add(node);
            }
        }

        return nodes;
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
        var security = GetQuery(query, "security", protocol == ProtocolType.Trojan ? "tls" : "none") ?? "none";

        return new ProxyNode
        {
            Id = StableId(line),
            Raw = line,
            Protocol = protocol,
            Name = name,
            Address = uri.Host,
            Port = uri.Port,
            UserId = protocol == ProtocolType.Vless ? user : null,
            Password = protocol is ProtocolType.Trojan or ProtocolType.Socks ? user : null,
            Network = network,
            Security = security,
            Flow = GetQuery(query, "flow"),
            Host = GetQuery(query, "host"),
            Path = GetQuery(query, "path"),
            ServiceName = GetQuery(query, "serviceName"),
            Sni = GetQuery(query, "sni", GetQuery(query, "peer")),
            Fingerprint = GetQuery(query, "fp"),
            PublicKey = GetQuery(query, "pbk"),
            ShortId = GetQuery(query, "sid"),
            SpiderX = GetQuery(query, "spx")
        };
    }

    private static ProxyNode? ParseShadowsocks(string line)
    {
        var withoutScheme = line["ss://".Length..];
        var fragmentIndex = withoutScheme.IndexOf('#');
        var name = fragmentIndex >= 0 ? Uri.UnescapeDataString(withoutScheme[(fragmentIndex + 1)..]) : null;
        var body = fragmentIndex >= 0 ? withoutScheme[..fragmentIndex] : withoutScheme;
        var pluginIndex = body.IndexOf("/?", StringComparison.Ordinal);
        if (pluginIndex >= 0)
        {
            body = body[..pluginIndex];
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
            var atDecodedIndex = decoded.LastIndexOf('@');
            if (atDecodedIndex < 0)
            {
                return null;
            }

            var parts = decoded[..atDecodedIndex].Split(':', 2);
            if (parts.Length != 2)
            {
                return null;
            }

            method = parts[0];
            password = parts[1];
            hostPort = decoded[(atDecodedIndex + 1)..];
        }

        var hp = hostPort.Split(':', 2);
        if (hp.Length != 2 || !int.TryParse(hp[1], out var port))
        {
            return null;
        }

        return new ProxyNode
        {
            Id = StableId(line),
            Raw = line,
            Protocol = ProtocolType.Shadowsocks,
            Name = CleanName(name, hp[0]),
            Address = hp[0],
            Port = port,
            Method = method,
            Password = password
        };
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

    private static string? GetQuery(Dictionary<string, string> query, string key, string? fallback = null)
    {
        return query.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
    }

    private static string DecodeBase64IfNeeded(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Contains("://", StringComparison.Ordinal) || trimmed.StartsWith('{'))
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
            _ => null
        };
    }

    private static int GetInt(JsonElement element, string name)
    {
        var value = GetString(element, name);
        return int.TryParse(value, out var parsed) ? parsed : 0;
    }
}
