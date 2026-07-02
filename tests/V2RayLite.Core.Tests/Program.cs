using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using V2RayLite.Core;

var tests = new (string Name, Func<Task> Body)[]
{
    ("subscription share links", () => { TestSubscriptionShareLinks(); return Task.CompletedTask; }),
    ("clash yaml", () => { TestClashYaml(); return Task.CompletedTask; }),
    ("xray config generation", () => { TestXrayConfigGeneration(); return Task.CompletedTask; }),
    ("routing order", () => { TestRoutingOrder(); return Task.CompletedTask; }),
    ("diagnostic redaction and recent logs", () => { TestDiagnosticPackageRedactionAndRecentLogs(); return Task.CompletedTask; }),
    ("log rotation", () => { TestLogRotation(); return Task.CompletedTask; }),
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        await test.Body();
        Console.WriteLine($"[PASS] {test.Name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"[FAIL] {test.Name}: {ex.Message}");
    }
}

if (failures > 0)
{
    Environment.Exit(1);
}

Console.WriteLine($"{tests.Length} core tests passed.");

static void TestSubscriptionShareLinks()
{
    var parser = new SubscriptionParser();
    var uuid = "11111111-1111-1111-1111-111111111111";
    var vmessJson = JsonSerializer.Serialize(new
    {
        v = "2",
        ps = "US VMess",
        add = "vmess.example.com",
        port = "443",
        id = uuid,
        aid = "0",
        net = "ws",
        type = "none",
        host = "cdn.example.com",
        path = "/ray",
        tls = "tls",
        sni = "vmess.example.com"
    });
    var vmess = "vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(vmessJson));
    var vless = $"vless://{uuid}@vless.example.com:443?encryption=none&security=reality&type=ws&host=cdn.example.com&path=%2Fws&sni=www.microsoft.com&fp=chrome&pbk=public-key&sid=abcd#US%20VLESS";
    var trojan = "trojan://trojan-secret@trojan.example.com:443?security=tls&sni=trojan.example.com#Trojan";
    var ssUserInfo = Convert.ToBase64String(Encoding.UTF8.GetBytes("aes-128-gcm:ss-secret"));
    var shadowsocks = $"ss://{ssUserInfo}@ss.example.com:8388#SS";

    var nodes = parser.Parse(string.Join('\n', [vmess, vless, trojan, shadowsocks]));

    Equal(4, nodes.Count);
    True(nodes.Any(node => node.Protocol == ProtocolType.Vmess && node.Network == "ws" && node.Security == "tls"), "VMess link was not parsed correctly.");
    True(nodes.Any(node => node.Protocol == ProtocolType.Vless && node.Security == "reality" && node.PublicKey == "public-key"), "VLESS Reality link was not parsed correctly.");
    True(nodes.Any(node => node.Protocol == ProtocolType.Trojan && node.Password == "trojan-secret"), "Trojan link was not parsed correctly.");
    True(nodes.Any(node => node.Protocol == ProtocolType.Shadowsocks && node.Method == "aes-128-gcm" && node.Password == "ss-secret"), "Shadowsocks link was not parsed correctly.");
}

static void TestClashYaml()
{
    const string yaml = """
proxies:
  - { name: HK VMess, type: vmess, server: hk.example.com, port: 443, uuid: 22222222-2222-2222-2222-222222222222, alterId: 0, cipher: auto, tls: true, network: ws, ws-path: /ray, Host: hk.cdn.example.com }
  - name: JP Trojan
    type: trojan
    server: jp.example.com
    port: 443
    password: jp-secret
    sni: jp.example.com
  - name: SG SS
    type: ss
    server: sg.example.com
    port: 8388
    cipher: aes-256-gcm
    password: sg-secret
""";

    var nodes = new SubscriptionParser().Parse(yaml);

    Equal(3, nodes.Count);
    True(nodes.Any(node => node.Protocol == ProtocolType.Vmess && node.Host == "hk.cdn.example.com" && node.Path == "/ray"), "Clash VMess node was not parsed correctly.");
    True(nodes.Any(node => node.Protocol == ProtocolType.Trojan && node.Password == "jp-secret"), "Clash Trojan node was not parsed correctly.");
    True(nodes.Any(node => node.Protocol == ProtocolType.Shadowsocks && node.Method == "aes-256-gcm"), "Clash Shadowsocks node was not parsed correctly.");
}

static void TestXrayConfigGeneration()
{
    var node = SampleVlessNode();
    var settings = new AppSettings { HttpPort = 18080, SocksPort = 18081, ProxyMode = ProxyMode.Rule };
    var json = new XrayConfigBuilder().Build(node, settings);
    var root = JsonNode.Parse(json)!.AsObject();

    Equal("http", root["inbounds"]![0]!["protocol"]!.GetValue<string>());
    Equal(18080, root["inbounds"]![0]!["port"]!.GetValue<int>());
    Equal("vless", root["outbounds"]![0]!["protocol"]!.GetValue<string>());
    Equal("proxy", root["outbounds"]![0]!["tag"]!.GetValue<string>());
    Equal("routing", root.ContainsKey("routing") ? "routing" : "missing");
}

static void TestRoutingOrder()
{
    var settings = new AppSettings
    {
        ProxyMode = ProxyMode.Rule,
        BypassMainland = true,
        BlockDomains = "geosite:category-ads-all",
        DirectDomains = "domain:example.cn",
        ProxyDomains = "domain:example.com"
    };
    var json = new XrayConfigBuilder().Build(SampleVlessNode(), settings);
    var rules = JsonNode.Parse(json)!["routing"]!["rules"]!.AsArray();

    Equal("block", rules[0]!["outboundTag"]!.GetValue<string>());
    Equal("direct", rules[1]!["outboundTag"]!.GetValue<string>());
    Equal("proxy", rules[2]!["outboundTag"]!.GetValue<string>());
    Equal("direct", rules[3]!["outboundTag"]!.GetValue<string>());
    True(rules[3]!["domain"]!.AsArray().Any(item => item!.GetValue<string>() == "geosite:cn"), "Built-in mainland rule should follow custom rules.");
}

static void TestDiagnosticPackageRedactionAndRecentLogs()
{
    var root = CreateTempRoot();
    try
    {
        var paths = new AppPaths(root);
        paths.Ensure();
        File.WriteAllText(paths.GeneratedConfigFile, """
{
  "outbounds": [
    { "protocol": "vless", "settings": { "vnext": [ { "users": [ { "id": "secret-uuid" } ] } ] } },
    { "protocol": "trojan", "settings": { "servers": [ { "password": "secret-password" } ] } }
  ],
  "token": "secret-token"
}
""");

        for (var index = 0; index < 7; index++)
        {
            var file = Path.Combine(paths.LogDirectory, $"xray-{index}.log");
            File.WriteAllText(file, $"log-{index}");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(index));
        }

        var package = new DiagnosticPackageService(paths).CreatePackage(
            new AppSettings { SubscriptionUrl = "https://example.com/sub?token=secret-sub" },
            [new ProxyNode { Name = "Secret", Address = "node.example.com", Port = 443, Protocol = ProtocolType.Trojan, Password = "node-secret" }],
            "v-test");

        using var archive = ZipFile.OpenRead(package);
        var redactedConfig = ReadEntry(archive, "xray.generated.redacted.json");
        False(redactedConfig.Contains("secret-uuid", StringComparison.Ordinal), "Generated config leaked UUID.");
        False(redactedConfig.Contains("secret-password", StringComparison.Ordinal), "Generated config leaked password.");
        False(redactedConfig.Contains("secret-token", StringComparison.Ordinal), "Generated config leaked token.");
        True(redactedConfig.Contains("[redacted]", StringComparison.Ordinal), "Generated config did not include redaction markers.");

        var logEntries = archive.Entries.Count(entry => entry.FullName.StartsWith("logs/", StringComparison.OrdinalIgnoreCase));
        Equal(AppLogService.MaxRetainedLogFiles, logEntries);
    }
    finally
    {
        DeleteDirectory(root);
    }
}

static void TestLogRotation()
{
    var root = CreateTempRoot();
    try
    {
        var paths = new AppPaths(root);
        paths.Ensure();
        File.WriteAllBytes(paths.AppLogFile, new byte[AppLogService.MaxLogFileBytes + 1]);

        new AppLogService(paths).Info("after rotation");

        True(File.Exists(Path.Combine(paths.LogDirectory, "myray-lite.1.log")), "Rotated app log was not created.");
        True(new FileInfo(paths.AppLogFile).Length < AppLogService.MaxLogFileBytes, "Current app log should be smaller after rotation.");
    }
    finally
    {
        DeleteDirectory(root);
    }
}

static ProxyNode SampleVlessNode()
{
    return new ProxyNode
    {
        Id = "node-1",
        Name = "Sample VLESS",
        Address = "vless.example.com",
        Port = 443,
        Protocol = ProtocolType.Vless,
        UserId = "33333333-3333-3333-3333-333333333333",
        Network = "ws",
        Security = "tls",
        Host = "cdn.example.com",
        Path = "/ws",
        Sni = "vless.example.com"
    };
}

static string CreateTempRoot()
{
    var path = Path.Combine(Directory.GetCurrentDirectory(), "test-artifacts", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static string ReadEntry(ZipArchive archive, string name)
{
    var entry = archive.GetEntry(name) ?? throw new InvalidOperationException($"Missing archive entry: {name}");
    using var reader = new StreamReader(entry.Open());
    return reader.ReadToEnd();
}

static void DeleteDirectory(string path)
{
    try
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
    catch
    {
        // Best effort cleanup for tests.
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void False(bool condition, string message)
{
    True(!condition, message);
}
