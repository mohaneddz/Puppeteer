using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Puppeteer.App.Services;
using Puppeteer.App.ViewModels;
using Puppeteer.Core;
using Puppeteer.Infrastructure;

namespace Puppeteer.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // OnStartup is async void, so an exception here would otherwise take the process down with no
        // window and nothing on screen to say why — which is exactly the first-run failure mode.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        try
        {
            _host = Host.CreateDefaultBuilder().ConfigureServices(services =>
            {
                services.AddPuppeteerInfrastructure(Path.Combine(MainViewModel.DataFolder, "puppeteer.db"));
                services.AddSingleton(DotEnv.Load());
                services.AddSingleton<IFilePicker, FilePicker>();
                services.AddSingleton<IStartupService, WindowsStartupService>();
                services.AddSingleton<TrayIcon>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            }).Build();

            await _host.StartAsync();
            await _host.Services.GetRequiredService<IProjectRepository>().InitializeAsync();
            await _host.Services.GetRequiredService<MainViewModel>().LoadAsync();

            // Explicit, because the window can be hidden to the tray while the app keeps running; the
            // default (exit when the last window closes) would quit the moment it is hidden.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var window = _host.Services.GetRequiredService<MainWindow>();
            window.Show();
            if (e.Args.Contains(WindowsStartupService.StartMinimizedArgument)) window.StartHidden();
        }
        catch (Exception exception)
        {
            Fail("Puppeteer couldn't start", exception);
            Shutdown();
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Fail("Something went wrong", e.Exception);
        e.Handled = true;
    }

    private static void Fail(string title, Exception exception) =>
        MessageBox.Show(exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.Services.GetService<TrayIcon>()?.Dispose();
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
