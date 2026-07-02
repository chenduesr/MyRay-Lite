namespace V2RayLite.Core;

public sealed class AppLogService
{
    public const long MaxLogFileBytes = 5 * 1024 * 1024;
    public const int MaxRetainedLogFiles = 5;

    private readonly AppPaths _paths;
    private readonly object _lock = new();

    public AppLogService(AppPaths paths)
    {
        _paths = paths;
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    public void Write(string level, string message)
    {
        try
        {
            _paths.Ensure();
            var entry = new LogEntry { Level = level, Message = message };
            lock (_lock)
            {
                RotateIfNeeded(_paths.AppLogFile, MaxLogFileBytes, MaxRetainedLogFiles);
                File.AppendAllText(_paths.AppLogFile, entry + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must not interrupt proxy control.
        }
    }

    public IReadOnlyList<string> ReadRecentLines(int maxLines = 400)
    {
        try
        {
            _paths.Ensure();
            var lines = new List<string>();
            if (File.Exists(_paths.AppLogFile))
            {
                lines.AddRange(File.ReadLines(_paths.AppLogFile));
            }

            foreach (var file in GetRecentFiles(_paths.LogDirectory, "xray-*.log", MaxRetainedLogFiles).Reverse())
            {
                lines.Add($"===== {Path.GetFileName(file)} =====");
                lines.AddRange(File.ReadLines(file));
            }

            return lines.Count <= maxLines ? lines : lines.Skip(lines.Count - maxLines).ToList();
        }
        catch (Exception ex)
        {
            return [$"读取日志失败：{ex.Message}"];
        }
    }

    public void Clear()
    {
        try
        {
            _paths.Ensure();
            if (File.Exists(_paths.AppLogFile))
            {
                File.Delete(_paths.AppLogFile);
            }

            foreach (var file in Directory.EnumerateFiles(_paths.LogDirectory, "*.log"))
            {
                File.Delete(file);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    public static IReadOnlyList<string> GetRecentFiles(string directory, string pattern, int take = MaxRetainedLogFiles)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(directory, pattern)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(Math.Max(0, take))
            .ToList();
    }

    public static void AppendLineWithRotation(string file, string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var directory = Path.GetDirectoryName(file);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        RotateIfNeeded(file, MaxLogFileBytes, MaxRetainedLogFiles);
        File.AppendAllText(file, line + Environment.NewLine);
    }

    public static void RotateIfNeeded(string file, long maxBytes = MaxLogFileBytes, int retainedFiles = MaxRetainedLogFiles)
    {
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length < maxBytes)
            {
                return;
            }

            var directory = Path.GetDirectoryName(file) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(file);
            var extension = Path.GetExtension(file);
            var oldest = Path.Combine(directory, $"{name}.{retainedFiles}{extension}");
            if (File.Exists(oldest))
            {
                File.Delete(oldest);
            }

            for (var index = retainedFiles - 1; index >= 1; index--)
            {
                var source = Path.Combine(directory, $"{name}.{index}{extension}");
                if (!File.Exists(source))
                {
                    continue;
                }

                var target = Path.Combine(directory, $"{name}.{index + 1}{extension}");
                File.Move(source, target);
            }

            File.Move(file, Path.Combine(directory, $"{name}.1{extension}"));
        }
        catch
        {
            // Logging must remain best effort.
        }
    }
}