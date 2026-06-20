using System.Windows;
using System.Windows.Threading;
using System.Threading;
using V2RayLite.Core;

namespace V2RayLite.App;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = "MyRayLite.SingleInstance";
    private const string ShowMainWindowEventName = "MyRayLite.ShowMainWindow";
    private readonly AppPaths _paths = new();
    private readonly CrashReportService _crashReportService;
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showMainWindowEvent;
    private bool _isExiting;

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
            SignalExistingInstance();
            Shutdown();
            return;
        }

        _showMainWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowMainWindowEventName);
        StartShowMainWindowListener();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _isExiting = true;
            _showMainWindowEvent?.Set();
            _showMainWindowEvent?.Dispose();
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch
        {
            // Best effort during shutdown.
        }

        base.OnExit(e);
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var showEvent = EventWaitHandle.OpenExisting(ShowMainWindowEventName);
            showEvent.Set();
        }
        catch
        {
            System.Windows.MessageBox.Show(
                "MyRay Lite 已经在运行。请从托盘图标打开主窗口。",
                "MyRay Lite",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void StartShowMainWindowListener()
    {
        var showEvent = _showMainWindowEvent;
        if (showEvent is null)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            while (!_isExiting)
            {
                try
                {
                    showEvent.WaitOne();
                    if (_isExiting)
                    {
                        break;
                    }

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (MainWindow is MainWindow mainWindow)
                        {
                            mainWindow.ShowFromTray();
                        }
                    }));
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch
                {
                    // Best effort single-instance activation.
                }
            }
        });
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
