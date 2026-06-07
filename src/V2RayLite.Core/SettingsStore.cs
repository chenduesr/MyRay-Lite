using System.Text.Json;

namespace V2RayLite.Core;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;

    public SettingsStore(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        _paths.Ensure();
        if (!File.Exists(_paths.SettingsFile))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = File.OpenRead(_paths.SettingsFile);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        _paths.Ensure();
        await using var stream = File.Create(_paths.SettingsFile);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
    }

    public async Task<List<ProxyNode>> LoadNodesAsync(CancellationToken cancellationToken = default)
    {
        _paths.Ensure();
        if (!File.Exists(_paths.NodesFile))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_paths.NodesFile);
            return await JsonSerializer.DeserializeAsync<List<ProxyNode>>(stream, JsonOptions, cancellationToken)
                ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task SaveNodesAsync(IEnumerable<ProxyNode> nodes, CancellationToken cancellationToken = default)
    {
        _paths.Ensure();
        await using var stream = File.Create(_paths.NodesFile);
        await JsonSerializer.SerializeAsync(stream, nodes.ToList(), JsonOptions, cancellationToken);
    }
}
