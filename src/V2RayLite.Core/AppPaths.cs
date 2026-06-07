namespace V2RayLite.Core;

public sealed class AppPaths
{
    public AppPaths(string? appDataRoot = null)
    {
        AppDataRoot = appDataRoot
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "V2RayLite");
        SettingsFile = Path.Combine(AppDataRoot, "settings.json");
        NodesFile = Path.Combine(AppDataRoot, "nodes.json");
        GeneratedConfigFile = Path.Combine(AppDataRoot, "xray.generated.json");
        LogDirectory = Path.Combine(AppDataRoot, "logs");
        AppLogFile = Path.Combine(LogDirectory, "myray-lite.log");
    }

    public string AppDataRoot { get; }
    public string SettingsFile { get; }
    public string NodesFile { get; }
    public string GeneratedConfigFile { get; }
    public string LogDirectory { get; }
    public string AppLogFile { get; }

    public void Ensure()
    {
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(LogDirectory);
    }

    public string FindXrayDirectory()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new List<string>
        {
            Path.Combine(baseDir, "bin", "xray"),
            Path.Combine(baseDir, "xray")
        };

        var cursor = new DirectoryInfo(baseDir);
        while (cursor is not null)
        {
            candidates.Add(Path.Combine(cursor.FullName, "bin", "xray"));
            cursor = cursor.Parent;
        }

        return candidates.FirstOrDefault(path => File.Exists(Path.Combine(path, "xray.exe")))
            ?? candidates.First(path => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}xray", StringComparison.OrdinalIgnoreCase));
    }
}
