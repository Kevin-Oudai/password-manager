using PasswordManager.Services;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace PasswordManager;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        LogService.Initialize();
        LogService.Info("App", "Application startup.");

        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;

        LogService.Info("App", $"Application exit with code {e.ApplicationExitCode}.");
        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogService.Error("DispatcherUnhandledException", e.Exception);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            LogService.Error("AppDomainUnhandledException", exception);
        }
        else
        {
            LogService.Warning("AppDomainUnhandledException", "Non-exception unhandled error object received.");
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogService.Error("TaskSchedulerUnobservedTaskException", e.Exception);
        e.SetObserved();
    }
}
