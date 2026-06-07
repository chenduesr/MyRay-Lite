using System.Text.Json;
using System.Text.Json.Nodes;

namespace V2RayLite.Core;

public sealed class XrayConfigBuilder
{
    public string Build(ProxyNode node, AppSettings settings)
    {
        var proxyOutbound = BuildProxyOutbound(node);
        var routing = BuildRouting(settings.ProxyMode);
        var config = new JsonObject
        {
            ["log"] = new JsonObject
            {
                ["loglevel"] = "warning"
            },
            ["inbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["tag"] = "http",
                    ["listen"] = "127.0.0.1",
                    ["port"] = settings.HttpPort,
                    ["protocol"] = "http",
                    ["settings"] = new JsonObject { ["timeout"] = 0 }
                },
                new JsonObject
                {
                    ["tag"] = "socks",
                    ["listen"] = "127.0.0.1",
                    ["port"] = settings.SocksPort,
                    ["protocol"] = "socks",
                    ["settings"] = new JsonObject
                    {
                        ["udp"] = true,
                        ["auth"] = "noauth"
                    }
                }
            },
            ["outbounds"] = new JsonArray
            {
                settings.ProxyMode == ProxyMode.Direct ? BuildDirectOutbound("proxy") : proxyOutbound,
                BuildDirectOutbound("direct"),
                new JsonObject
                {
                    ["tag"] = "block",
                    ["protocol"] = "blackhole",
                    ["settings"] = new JsonObject()
                }
            },
            ["routing"] = routing
        };

        return config.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject BuildProxyOutbound(ProxyNode node)
    {
        return node.Protocol switch
        {
            ProtocolType.Vmess => BuildVmess(node),
            ProtocolType.Vless => BuildVless(node),
            ProtocolType.Trojan => BuildTrojan(node),
            ProtocolType.Shadowsocks => BuildShadowsocks(node),
            ProtocolType.Socks => BuildSocks(node),
            _ => throw new InvalidOperationException($"Unsupported protocol: {node.Protocol}")
        };
    }

    private static JsonObject BuildVmess(ProxyNode node)
    {
        return AddStreamSettings(node, new JsonObject
        {
            ["tag"] = "proxy",
            ["protocol"] = "vmess",
            ["settings"] = new JsonObject
            {
                ["vnext"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["address"] = node.Address,
                        ["port"] = node.Port,
                        ["users"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["id"] = node.UserId,
                                ["alterId"] = node.AlterId,
                                ["security"] = string.IsNullOrWhiteSpace(node.VmessSecurity) ? "auto" : node.VmessSecurity
                            }
                        }
                    }
                }
            }
        });
    }

    private static JsonObject BuildVless(ProxyNode node)
    {
        var user = new JsonObject
        {
            ["id"] = node.UserId,
            ["encryption"] = "none"
        };
        if (!string.IsNullOrWhiteSpace(node.Flow))
        {
            user["flow"] = node.Flow;
        }

        return AddStreamSettings(node, new JsonObject
        {
            ["tag"] = "proxy",
            ["protocol"] = "vless",
            ["settings"] = new JsonObject
            {
                ["vnext"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["address"] = node.Address,
                        ["port"] = node.Port,
                        ["users"] = new JsonArray { user }
                    }
                }
            }
        });
    }

    private static JsonObject BuildTrojan(ProxyNode node)
    {
        return AddStreamSettings(node, new JsonObject
        {
            ["tag"] = "proxy",
            ["protocol"] = "trojan",
            ["settings"] = new JsonObject
            {
                ["servers"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["address"] = node.Address,
                        ["port"] = node.Port,
                        ["password"] = node.Password
                    }
                }
            }
        });
    }

    private static JsonObject BuildShadowsocks(ProxyNode node)
    {
        return new JsonObject
        {
            ["tag"] = "proxy",
            ["protocol"] = "shadowsocks",
            ["settings"] = new JsonObject
            {
                ["servers"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["address"] = node.Address,
                        ["port"] = node.Port,
                        ["method"] = node.Method,
                        ["password"] = node.Password
                    }
                }
            }
        };
    }

    private static JsonObject BuildSocks(ProxyNode node)
    {
        return new JsonObject
        {
            ["tag"] = "proxy",
            ["protocol"] = "socks",
            ["settings"] = new JsonObject
            {
                ["servers"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["address"] = node.Address,
                        ["port"] = node.Port
                    }
                }
            }
        };
    }

    private static JsonObject AddStreamSettings(ProxyNode node, JsonObject outbound)
    {
        var stream = new JsonObject
        {
            ["network"] = string.IsNullOrWhiteSpace(node.Network) ? "tcp" : node.Network,
            ["security"] = string.IsNullOrWhiteSpace(node.Security) ? "none" : node.Security
        };

        if (!string.Equals(node.Security, "none", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(node.Security, "reality", StringComparison.OrdinalIgnoreCase))
            {
                var reality = new JsonObject();
                AddIfNotEmpty(reality, "serverName", node.Sni);
                AddIfNotEmpty(reality, "fingerprint", node.Fingerprint);
                AddIfNotEmpty(reality, "publicKey", node.PublicKey);
                AddIfNotEmpty(reality, "shortId", node.ShortId);
                AddIfNotEmpty(reality, "spiderX", node.SpiderX);
                stream["realitySettings"] = reality;
            }
            else
            {
                var tls = new JsonObject { ["allowInsecure"] = false };
                AddIfNotEmpty(tls, "serverName", node.Sni);
                AddIfNotEmpty(tls, "fingerprint", node.Fingerprint);
                stream["tlsSettings"] = tls;
            }
        }

        switch (node.Network.ToLowerInvariant())
        {
            case "ws":
                var ws = new JsonObject();
                AddIfNotEmpty(ws, "path", node.Path);
                if (!string.IsNullOrWhiteSpace(node.Host))
                {
                    ws["headers"] = new JsonObject { ["Host"] = node.Host };
                }
                stream["wsSettings"] = ws;
                break;
            case "grpc":
                var grpc = new JsonObject();
                AddIfNotEmpty(grpc, "serviceName", node.ServiceName);
                stream["grpcSettings"] = grpc;
                break;
            case "http":
            case "h2":
                var http = new JsonObject();
                if (!string.IsNullOrWhiteSpace(node.Host))
                {
                    http["host"] = new JsonArray(node.Host);
                }
                AddIfNotEmpty(http, "path", node.Path);
                stream["httpSettings"] = http;
                break;
        }

        outbound["streamSettings"] = stream;
        return outbound;
    }

    private static JsonObject BuildDirectOutbound(string tag)
    {
        return new JsonObject
        {
            ["tag"] = tag,
            ["protocol"] = "freedom",
            ["settings"] = new JsonObject()
        };
    }

    private static JsonObject BuildRouting(ProxyMode mode)
    {
        var rules = new JsonArray();
        if (mode == ProxyMode.Rule)
        {
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["ip"] = new JsonArray("geoip:private", "geoip:cn"),
                ["outboundTag"] = "direct"
            });
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["domain"] = new JsonArray("geosite:cn"),
                ["outboundTag"] = "direct"
            });
        }

        return new JsonObject
        {
            ["domainStrategy"] = "AsIs",
            ["rules"] = rules
        };
    }

    private static void AddIfNotEmpty(JsonObject target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value;
        }
    }
}
