namespace V2RayLite.Core;

public sealed class AppLogService
{
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

            foreach (var file in Directory.EnumerateFiles(_paths.LogDirectory, "xray-*.log").OrderByDescending(File.GetLastWriteTimeUtc).Take(5).Reverse())
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

            foreach (var file in Directory.EnumerateFiles(_paths.LogDirectory, "xray-*.log"))
            {
                File.Delete(file);
            }
        }
        catch
        {
            // Best effort.
        }
    }
}
