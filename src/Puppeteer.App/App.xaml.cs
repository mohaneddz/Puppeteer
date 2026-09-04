using System.Windows;
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
 protected override async void OnStartup(StartupEventArgs e) { base.OnStartup(e); var dataFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Puppeteer"); _host=Host.CreateDefaultBuilder().ConfigureServices(services=>{ services.AddPuppeteerInfrastructure(Path.Combine(dataFolder,"puppeteer.db")); services.AddSingleton(DotEnv.Load()); services.AddSingleton<IFilePicker,FilePicker>(); services.AddSingleton<IStartupService,WindowsStartupService>(); services.AddSingleton<TrayIcon>(); services.AddSingleton<MainViewModel>(); services.AddSingleton<MainWindow>(); }).Build(); await _host.StartAsync(); await _host.Services.GetRequiredService<IProjectRepository>().InitializeAsync(); var vm=_host.Services.GetRequiredService<MainViewModel>(); await vm.LoadAsync();
  // ShutdownMode is explicit because the window can be hidden to the tray while the app keeps
  // running; the default (last window closed) would exit the moment it is hidden.
  ShutdownMode=ShutdownMode.OnExplicitShutdown;
  var window=_host.Services.GetRequiredService<MainWindow>();
  window.Show();
  if(e.Args.Contains(WindowsStartupService.StartMinimizedArgument)) window.StartHidden(); }
 protected override async void OnExit(ExitEventArgs e) { if(_host is not null){_host.Services.GetService<TrayIcon>()?.Dispose();await _host.StopAsync();_host.Dispose();} base.OnExit(e); }
}
