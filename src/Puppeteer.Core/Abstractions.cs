namespace Puppeteer.Core;

public interface IProjectDetector
{
    Task<ProjectDetectionResult?> DetectAsync(string directory, CancellationToken cancellationToken = default);
}

public interface IProjectScanner
{
    Task<IReadOnlyList<Project>> ScanAsync(RootFolder root, CancellationToken cancellationToken = default);
}

public interface IProjectRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RootFolder>> GetRootsAsync(CancellationToken cancellationToken = default);
    Task AddRootAsync(RootFolder root, CancellationToken cancellationToken = default);
    Task RemoveRootAsync(Guid rootId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken cancellationToken = default);
    Task UpsertProjectsAsync(IEnumerable<Project> projects, CancellationToken cancellationToken = default);
    Task SetProjectIconAsync(Guid projectId, string? iconPath, CancellationToken cancellationToken = default);
    Task SetProjectCategoryAsync(Guid projectId, string? category, CancellationToken cancellationToken = default);
    Task SetProjectOpenedAsync(Guid projectId, DateTimeOffset openedAt, CancellationToken cancellationToken = default);
    Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default);
    Task SetSettingAsync(string key, string? value, CancellationToken cancellationToken = default);
    /// <summary>Writes several settings in one transaction. Used for snapshots that must land
    /// together and promptly — the window layout saved during shutdown, above all.</summary>
    Task SetSettingsAsync(IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken = default);
}

/// <summary>Classifies a project's purpose (personal / client / hackathon / course / work) from its
/// README and path. Implementations may call an LLM; a null result means "could not decide".</summary>
public interface IProjectClassifier
{
    Task<string?> ClassifyAsync(string projectPath, string apiKey, CancellationToken cancellationToken = default);
}

public interface IIconDiscoveryService
{
    Task<IReadOnlyList<IconCandidate>> FindAsync(string projectPath, CancellationToken cancellationToken = default);
}

public interface IProjectIconProvider
{
    string GetDefaultIconKey(string primaryTechnology);
}

public interface IGitMetadataService
{
    Task<GitStatus?> GetStatusAsync(string projectPath, CancellationToken cancellationToken = default);
}

/// <summary>Registers (or unregisters) the app to start with the user's session.</summary>
public interface IStartupService
{
    bool IsEnabled { get; }
    /// <param name="minimized">Start hidden in the notification area rather than showing the window.</param>
    void SetEnabled(bool enabled, bool minimized);
}

public interface IProjectLauncher
{
    void OpenFolder(string path);
    /// <param name="command">Executable or command to launch the project with. When blank, the shell
    /// picks: the project's solution file if there is one, otherwise the folder itself.</param>
    void OpenInIde(string path, string? command = null);
}

public interface ITerminalSession
{
    Guid Id { get; }
    string ProjectPath { get; }
    string Shell { get; }
    string Name { get; }
    string? Command { get; }
    DateTimeOffset StartedAt { get; }
    TerminalSessionState State { get; }
    event EventHandler<string>? OutputReceived;
    event EventHandler? Exited;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task WriteAsync(string input, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface ITerminalService
{
    IReadOnlyCollection<ITerminalSession> ActiveSessions { get; }
    Task<ITerminalSession> CreateAsync(TerminalOptions options, CancellationToken cancellationToken = default);
}

public interface IFileSystemWatchService : IDisposable
{
    event EventHandler<string>? ProjectFilesChanged;
    void Watch(RootFolder root);
    void Stop(Guid rootId);
}
