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
 protected override async void OnStartup(StartupEventArgs e) { base.OnStartup(e); var dataFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Puppeteer"); _host=Host.CreateDefaultBuilder().ConfigureServices(services=>{ services.AddPuppeteerInfrastructure(Path.Combine(dataFolder,"puppeteer.db")); services.AddSingleton(DotEnv.Load()); services.AddSingleton<IFilePicker,FilePicker>(); services.AddSingleton<MainViewModel>(); services.AddSingleton<MainWindow>(); }).Build(); await _host.StartAsync(); await _host.Services.GetRequiredService<IProjectRepository>().InitializeAsync(); var vm=_host.Services.GetRequiredService<MainViewModel>(); await vm.LoadAsync(); _host.Services.GetRequiredService<MainWindow>().Show(); }
 protected override async void OnExit(ExitEventArgs e) { if(_host is not null){await _host.StopAsync();_host.Dispose();} base.OnExit(e); }
}
