using System.Windows;
using System.Windows.Threading;
using System.Threading;
using V2RayLite.Core;

namespace V2RayLite.App;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = "MyRayLite.SingleInstance";
    private readonly AppPaths _paths = new();
    private readonly CrashReportService _crashReportService;
    private Mutex? _singleInstanceMutex;

    public App()
    {
        _crashReportService = new CrashReportService(_paths);
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "MyRay Lite 已经在运行，不能重复打开第二个实例。",
                "MyRay Lite",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch
        {
            // Best effort during shutdown.
        }

        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrash(e.Exception, "DispatcherUnhandledException");
        e.Handled = true;
        System.Windows.MessageBox.Show(
            "MyRay Lite 遇到异常，已保存崩溃日志。你可以在日志诊断页生成诊断包。",
            "MyRay Lite",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteCrash(exception, "UnhandledException");
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrash(e.Exception, "UnobservedTaskException");
        e.SetObserved();
    }

    private void WriteCrash(Exception exception, string source)
    {
        try
        {
            _crashReportService.WriteCrash(exception, source);
        }
        catch
        {
            // Crash logging must never throw from exception handlers.
        }
    }
}
