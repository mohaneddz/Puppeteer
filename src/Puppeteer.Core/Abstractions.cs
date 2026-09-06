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
    Task DeleteProjectsAsync(IEnumerable<Guid> projectIds, CancellationToken cancellationToken = default);
    Task SetProjectIconAsync(Guid projectId, string? iconPath, CancellationToken cancellationToken = default);
    Task SetProjectIconFillAsync(Guid projectId, bool fill, CancellationToken cancellationToken = default);
    Task SetProjectIconShapeAsync(Guid projectId, string shape, CancellationToken cancellationToken = default);
    Task SetProjectCategoryAsync(Guid projectId, string? category, CancellationToken cancellationToken = default);
    Task SetProjectOpenedAsync(Guid projectId, DateTimeOffset openedAt, CancellationToken cancellationToken = default);
    Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default);
    Task SetSettingAsync(string key, string? value, CancellationToken cancellationToken = default);
    /// <summary>Writes several settings in one transaction. Used for snapshots that must land
    /// together and promptly — the window layout saved during shutdown, above all.</summary>
    Task SetSettingsAsync(IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken = default);
    Task SetProjectNameAsync(Guid projectId, string? customName, CancellationToken cancellationToken = default);
    /// <summary>Removes a project and everything remembered about it. Unlike
    /// <see cref="DeleteProjectsAsync"/>, which only drops the scanned row when a folder is no longer
    /// on disk, this is for a project deliberately thrown away and must not be undone by a rescan.</summary>
    Task ForgetProjectsAsync(IEnumerable<Guid> projectIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectDocLink>> GetDocLinksAsync(CancellationToken cancellationToken = default);
    Task SetDocLinkAsync(ProjectDocLink link, CancellationToken cancellationToken = default);
    Task RemoveDocLinkAsync(Guid projectId, CancellationToken cancellationToken = default);
    /// <summary>Stores a snapshot, skipping it when nothing has moved since the last one — an idle
    /// project rescanned every launch should not grow a row a day.</summary>
    Task AddSnapshotAsync(ProjectStateSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectStateSnapshot>> GetSnapshotsAsync(Guid projectId, int limit = 40, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, ProjectStateSnapshot>> GetLatestSnapshotsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Creates and restores a portable copy of Puppeteer's complete local library state.
/// Backups contain the database only: they remember projects, roots, customizations, links and
/// preferences, but never copy or overwrite the project folders themselves.</summary>
public interface IProjectBackupService
{
    Task<BackupResult> CreateAsync(string backupPath, CancellationToken cancellationToken = default);
    Task<BackupResult> RestoreAsync(string backupPath, CancellationToken cancellationToken = default);
}

public sealed record BackupResult(int RootCount, int ProjectCount);

/// <summary>Reads and writes the folder of markdown state docs.
///
/// The vault is shared with whatever else edits it — an editor, another machine over sync, a Claude
/// session — so nothing here caches a file it has not re-read, every write goes through a temporary
/// file and a replace, and <see cref="Changed"/> reports edits that arrived from outside.</summary>
public interface IProjectDocVault : IDisposable
{
    string? VaultPath { get; }
    /// <summary>Raised on the thread pool when a doc changes on disk. The argument is the doc's path.</summary>
    event EventHandler<string>? Changed;
    void Open(string? vaultPath);
    Task<IReadOnlyList<ProjectDoc>> LoadAsync(CancellationToken cancellationToken = default);
    Task<ProjectDoc?> ReadAsync(string docPath, CancellationToken cancellationToken = default);
    /// <summary>Writes the doc back. The text it was parsed from is the guard: if the file on disk no
    /// longer matches, someone else has edited it and the write is refused rather than applied. A doc
    /// that was never read from disk is a new one, and an existing file at its path is also refused.</summary>
    Task<DocWriteResult> WriteAsync(ProjectDoc doc, CancellationToken cancellationToken = default);
    /// <summary>Where a doc for this project would live: <c>&lt;vault&gt;/&lt;category&gt;/&lt;name&gt;.md</c>.</summary>
    string PathFor(string category, string projectName);
    IReadOnlyList<string> Categories();
}

public enum DocWriteOutcome { Written, Conflict, NoVault, Failed }
public sealed record DocWriteResult(DocWriteOutcome Outcome, ProjectDoc? Doc, string? Message);

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

/// <summary>Writes one section of a project's state doc from the project's own README and file layout.
/// Implementations may call an LLM; a null result means "could not generate".</summary>
public interface IDocFieldGenerator
{
    Task<string?> GenerateAsync(string section, string projectName, string projectPath, string apiKey, CancellationToken cancellationToken = default);
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
