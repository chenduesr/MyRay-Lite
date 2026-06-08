using System.Text.Json;
using System.Text.Json.Nodes;

namespace V2RayLite.Core;

public sealed class XrayConfigBuilder
{
    public string Build(ProxyNode node, AppSettings settings)
    {
        if (!node.IsSupportedByXray)
        {
            throw new NotSupportedException(node.UnsupportedReason ?? $"Xray 暂不支持协议：{node.Protocol}");
        }

        var proxyOutbound = BuildProxyOutbound(node);
        var routing = BuildRouting(settings.ProxyMode, settings);
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

        var dns = BuildDns(settings);
        if (dns is not null)
        {
            config["dns"] = dns;
        }

        if (settings.EnableCustomDns && settings.EnableFakeDns)
        {
            config["fakedns"] = new JsonArray
            {
                new JsonObject
                {
                    ["ipPool"] = "198.18.0.0/16",
                    ["poolSize"] = 65535
                }
            };
        }

        return config.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public string BuildBatchDelayTest(IReadOnlyList<ProxyNode> nodes, IReadOnlyList<int> httpPorts)
    {
        if (nodes.Count != httpPorts.Count)
        {
            throw new ArgumentException("Node and port counts must match.");
        }

        var inbounds = new JsonArray();
        var outbounds = new JsonArray();
        var rules = new JsonArray();

        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            if (!node.IsSupportedByXray)
            {
                throw new NotSupportedException(node.UnsupportedReason ?? $"Xray 暂不支持协议：{node.Protocol}");
            }

            var inboundTag = $"probe-in-{index}";
            var outboundTag = $"probe-out-{index}";

            inbounds.Add(new JsonObject
            {
                ["tag"] = inboundTag,
                ["listen"] = "127.0.0.1",
                ["port"] = httpPorts[index],
                ["protocol"] = "http",
                ["settings"] = new JsonObject { ["timeout"] = 0 }
            });

            var outbound = BuildProxyOutbound(node);
            outbound["tag"] = outboundTag;
            outbounds.Add(outbound);

            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["inboundTag"] = new JsonArray(inboundTag),
                ["outboundTag"] = outboundTag
            });
        }

        outbounds.Add(BuildDirectOutbound("direct"));
        outbounds.Add(new JsonObject
        {
            ["tag"] = "block",
            ["protocol"] = "blackhole",
            ["settings"] = new JsonObject()
        });

        var config = new JsonObject
        {
            ["log"] = new JsonObject
            {
                ["loglevel"] = "warning"
            },
            ["inbounds"] = inbounds,
            ["outbounds"] = outbounds,
            ["routing"] = new JsonObject
            {
                ["domainStrategy"] = "AsIs",
                ["rules"] = rules
            }
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
            ProtocolType.Http => BuildHttp(node),
            ProtocolType.Hysteria2 => BuildHysteria2(node),
            ProtocolType.AnyTls => BuildAnyTls(node),
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
        var server = new JsonObject
        {
            ["address"] = node.Address,
            ["port"] = node.Port
        };
        if (!string.IsNullOrWhiteSpace(node.UserId))
        {
            server["users"] = new JsonArray
            {
                new JsonObject
                {
                    ["user"] = node.UserId,
                    ["pass"] = node.Password
                }
            };
        }

        return new JsonObject
        {
            ["tag"] = "proxy",
            ["protocol"] = "socks",
            ["settings"] = new JsonObject
            {
                ["servers"] = new JsonArray { server }
            }
        };
    }

    private static JsonObject BuildHttp(ProxyNode node)
    {
        var server = new JsonObject
        {
            ["address"] = node.Address,
            ["port"] = node.Port
        };
        if (!string.IsNullOrWhiteSpace(node.UserId))
        {
            server["users"] = new JsonArray
            {
                new JsonObject
                {
                    ["user"] = node.UserId,
                    ["pass"] = node.Password
                }
            };
        }

        return new JsonObject
        {
            ["tag"] = "proxy",
            ["protocol"] = "http",
            ["settings"] = new JsonObject
            {
                ["servers"] = new JsonArray { server }
            }
        };
    }

    private static JsonObject BuildHysteria2(ProxyNode node)
    {
        var server = new JsonObject
        {
            ["address"] = node.Address,
            ["port"] = node.Port,
            ["password"] = node.Password
        };
        AddIfNotEmpty(server, "email", node.UserId);
        AddIfNotEmpty(server, "obfs", node.Obfs);
        AddIfNotEmpty(server, "obfsPassword", node.ObfsPassword);

        var outbound = new JsonObject
        {
            ["tag"] = "proxy",
            ["protocol"] = "hysteria2",
            ["settings"] = new JsonObject
            {
                ["servers"] = new JsonArray { server }
            }
        };

        return AddTlsSettings(node, outbound);
    }

    private static JsonObject BuildAnyTls(ProxyNode node)
    {
        var server = new JsonObject
        {
            ["address"] = node.Address,
            ["port"] = node.Port,
            ["password"] = node.Password
        };

        var outbound = new JsonObject
        {
            ["tag"] = "proxy",
            ["protocol"] = "anytls",
            ["settings"] = new JsonObject
            {
                ["servers"] = new JsonArray { server }
            }
        };

        return AddTlsSettings(node, outbound);
    }

    private static JsonObject AddTlsSettings(ProxyNode node, JsonObject outbound)
    {
        var tls = new JsonObject { ["allowInsecure"] = node.AllowInsecure };
        AddIfNotEmpty(tls, "serverName", node.Sni);
        AddIfNotEmpty(tls, "fingerprint", node.Fingerprint);
        AddAlpn(tls, node.Alpn);

        outbound["streamSettings"] = new JsonObject
        {
            ["security"] = "tls",
            ["tlsSettings"] = tls
        };

        return outbound;
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
                var tls = new JsonObject { ["allowInsecure"] = node.AllowInsecure };
                AddIfNotEmpty(tls, "serverName", node.Sni);
                AddIfNotEmpty(tls, "fingerprint", node.Fingerprint);
                AddAlpn(tls, node.Alpn);
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

    private static JsonObject BuildRouting(ProxyMode mode, AppSettings settings)
    {
        var rules = new JsonArray();
        if (mode == ProxyMode.Rule)
        {
            AddCustomRoutingRule(rules, "block", ParseList(settings.BlockDomains), ParseList(settings.BlockIps));
            AddCustomRoutingRule(rules, "proxy", ParseList(settings.ProxyDomains), ParseList(settings.ProxyIps));
            AddCustomRoutingRule(rules, "direct", ParseList(settings.DirectDomains), ParseList(settings.DirectIps));

            if (settings.BypassMainland)
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

            if (settings.RoutingRuleMode == RoutingRuleMode.Whitelist)
            {
                rules.Add(new JsonObject
                {
                    ["type"] = "field",
                    ["network"] = "tcp,udp",
                    ["outboundTag"] = "direct"
                });
            }
        }

        return new JsonObject
        {
            ["domainStrategy"] = "AsIs",
            ["rules"] = rules
        };
    }

    private static JsonObject? BuildDns(AppSettings settings)
    {
        if (!settings.EnableCustomDns)
        {
            return null;
        }

        var servers = new JsonArray();
        if (settings.EnableFakeDns)
        {
            servers.Add("fakedns");
        }

        var domesticServers = ParseList(settings.DomesticDns);
        var foreignServers = ParseList(settings.ForeignDns);
        var dohServers = ParseList(settings.DohDns);
        var dotServers = ParseList(settings.DotDns);

        if (settings.EnableSplitDns)
        {
            foreach (var server in domesticServers.DefaultIfEmpty("223.5.5.5"))
            {
                servers.Add(new JsonObject
                {
                    ["address"] = server,
                    ["domains"] = new JsonArray("geosite:cn"),
                    ["expectIPs"] = new JsonArray("geoip:cn")
                });
            }

            foreach (var server in foreignServers.Concat(dohServers).Concat(dotServers).DefaultIfEmpty("1.1.1.1"))
            {
                servers.Add(new JsonObject
                {
                    ["address"] = server,
                    ["domains"] = new JsonArray("geosite:geolocation-!cn")
                });
            }
        }
        else
        {
            foreach (var server in domesticServers.Concat(foreignServers).Concat(dohServers).Concat(dotServers))
            {
                servers.Add(server);
            }
        }

        if (servers.Count == 0)
        {
            servers.Add("1.1.1.1");
        }

        return new JsonObject
        {
            ["queryStrategy"] = settings.EnableFakeDns ? "UseIP" : "UseIPv4",
            ["servers"] = servers
        };
    }

    private static void AddCustomRoutingRule(JsonArray rules, string outboundTag, IReadOnlyList<string> domains, IReadOnlyList<string> ips)
    {
        if (domains.Count == 0 && ips.Count == 0)
        {
            return;
        }

        var rule = new JsonObject
        {
            ["type"] = "field",
            ["outboundTag"] = outboundTag
        };

        if (domains.Count > 0)
        {
            rule["domain"] = new JsonArray(domains.Select(item => JsonValue.Create(item)).ToArray());
        }

        if (ips.Count > 0)
        {
            rule["ip"] = new JsonArray(ips.Select(item => JsonValue.Create(item)).ToArray());
        }

        rules.Add(rule);
    }

    private static List<string> ParseList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(['\r', '\n', ',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddIfNotEmpty(JsonObject target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value;
        }
    }

    private static void AddAlpn(JsonObject target, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        target["alpn"] = new JsonArray(value
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => JsonValue.Create(item))
            .ToArray());
    }
}
