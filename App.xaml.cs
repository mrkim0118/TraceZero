using System;
using System.Windows;
using System.Windows.Threading;
using SecureFileEraser.Services;
using Velopack;

namespace SecureFileEraser;

public partial class App : Application
{
    [STAThread]
    public static void Main()
    {
        StartupDiagnostics.Log("App.Main entered.");
        VelopackApp.Build().Run();
        StartupDiagnostics.Log("Velopack initialization complete.");

        var app = new App();
        app.InitializeComponent();
        StartupDiagnostics.Log("App.InitializeComponent complete.");
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        StartupDiagnostics.Log("App.OnStartup entered.");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        base.OnStartup(e);
        StartupDiagnostics.Log($"App.OnStartup completed. StartupUri={StartupUri}");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StartupDiagnostics.Log($"App.OnExit code={e.ApplicationExitCode}");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        StartupDiagnostics.LogException("DispatcherUnhandledException", e.Exception);
        MessageBox.Show(
            $"프로그램 시작 중 오류가 발생했습니다.\n\n{e.Exception}",
            "Secure File Eraser 오류",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(-1);
    }

    private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            StartupDiagnostics.LogException("CurrentDomainUnhandledException", ex);
        }
        else
        {
            StartupDiagnostics.Log($"CurrentDomainUnhandledException: {e.ExceptionObject}");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        StartupDiagnostics.LogException("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }
}
