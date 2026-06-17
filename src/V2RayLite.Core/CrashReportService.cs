using System.Diagnostics;

namespace V2RayLite.Core;

public sealed class CrashReportService
{
    private readonly AppPaths _paths;

    public CrashReportService(AppPaths paths)
    {
        _paths = paths;
    }

    public string WriteCrash(Exception exception, string source)
    {
        _paths.Ensure();
        var file = Path.Combine(_paths.CrashDirectory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var text = string.Join(Environment.NewLine,
            $"Time: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}",
            $"Source: {source}",
            $"App: MyRay Lite",
            $"Process: {Process.GetCurrentProcess().ProcessName}",
            $"OS: {Environment.OSVersion}",
            $"Runtime: {Environment.Version}",
            string.Empty,
            exception.ToString());

        File.WriteAllText(file, text);
        return file;
    }
}
