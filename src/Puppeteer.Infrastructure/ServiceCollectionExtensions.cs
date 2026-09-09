using Microsoft.Extensions.DependencyInjection;
using Puppeteer.Core;

namespace Puppeteer.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPuppeteerInfrastructure(this IServiceCollection services, string databasePath) => services
        .AddSingleton<IProjectDetector, SignatureProjectDetector>()
        .AddSingleton<IGitMetadataService, GitMetadataService>()
        .AddSingleton<IProjectScanner, ProjectScanner>()
        .AddSingleton<IProjectRepository>(_ => new SqliteProjectRepository(databasePath))
        .AddSingleton<IProjectBackupService>(_ => new ProjectBackupService(databasePath))
        .AddSingleton<IIconDiscoveryService, IconDiscoveryService>()
        .AddSingleton<IProjectIconProvider, ProjectIconProvider>()
        .AddSingleton<IProjectLauncher, ProjectLauncher>()
        .AddSingleton<ITerminalService, ProcessTerminalService>()
        .AddSingleton<IFileSystemWatchService, FileSystemWatchService>()
        .AddSingleton<IProjectDocVault, ProjectDocVault>()
        .AddSingleton<IProjectClassifier>(_ => new GroqProjectClassifier(new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) }))
        .AddSingleton<IDocMatcher>(_ => new GroqDocMatcher(new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(40) }))
        .AddSingleton<IDocFieldGenerator>(_ => new GroqDocFieldGenerator(new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) }))
        .AddSingleton<ProjectSearchService>();
}
