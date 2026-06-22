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
    private readonly SemaphoreSlim _writeGate = new(1, 1);

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
            var json = await File.ReadAllTextAsync(_paths.SettingsFile, cancellationToken);
            var fileVersion = ReadSettingsVersion(json);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? new AppSettings();
            if (fileVersion is null)
            {
                settings.SettingsVersion = 0;
            }

            return await MigrateSettingsAsync(settings, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new AppSettings();
        }
    }

    private static int? ReadSettingsVersion(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty(nameof(AppSettings.SettingsVersion), out var version) &&
                version.TryGetInt32(out var value))
            {
                return value;
            }
        }
        catch
        {
            // Invalid settings will be handled by normal fallback.
        }

        return null;
    }

    private async Task<AppSettings> MigrateSettingsAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (settings.SettingsVersion >= AppSettings.CurrentSettingsVersion)
        {
            return settings;
        }

        _paths.Ensure();
        var backupFile = Path.Combine(_paths.BackupDirectory, $"settings-v{settings.SettingsVersion}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        try
        {
            if (File.Exists(_paths.SettingsFile))
            {
                File.Copy(_paths.SettingsFile, backupFile, overwrite: false);
            }
        }
        catch
        {
            // Migration backup is best effort.
        }

        settings.SettingsVersion = AppSettings.CurrentSettingsVersion;

        if (settings.DelayTestBatchSize <= 0)
        {
            settings.DelayTestBatchSize = 40;
        }

        if (settings.DelayTestRetryBatchSize <= 0)
        {
            settings.DelayTestRetryBatchSize = 8;
        }

        if (settings.DelayTestTimeoutSeconds <= 0)
        {
            settings.DelayTestTimeoutSeconds = 10;
        }

        if (settings.DelayDownloadBytes <= 0)
        {
            settings.DelayDownloadBytes = 1048576;
        }

        await SaveSettingsAsync(settings, cancellationToken);
        return settings;
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        _paths.Ensure();
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await WriteJsonAtomicallyAsync(_paths.SettingsFile, settings, cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    public async Task SaveNodesAsync(IEnumerable<ProxyNode> nodes, CancellationToken cancellationToken = default)
    {
        _paths.Ensure();
        var snapshot = nodes.ToList();
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await WriteJsonAtomicallyAsync(_paths.NodesFile, snapshot, cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static async Task WriteJsonAtomicallyAsync<T>(
        string destinationFile,
        T value,
        CancellationToken cancellationToken)
    {
        var temporaryFile = $"{destinationFile}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryFile,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(destinationFile))
            {
                File.Replace(temporaryFile, destinationFile, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryFile, destinationFile);
            }
        }
        finally
        {
            if (File.Exists(temporaryFile))
            {
                File.Delete(temporaryFile);
            }
        }
    }
}
