namespace Puppeteer.Core;

public sealed record RootFolder(Guid Id, string Path, DateTimeOffset CreatedAt);

public sealed record Project(
    Guid Id,
    string Name,
    string Path,
    Guid RootId,
    string PrimaryTechnology,
    IReadOnlyList<string> Technologies,
    IReadOnlyList<string> Hierarchy,
    IReadOnlyList<CommandPreset> Presets,
    DateTimeOffset? LastOpenedAt = null,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? UpdatedAt = null,
    string? CustomIconPath = null,
    GitStatus? Git = null,
    IReadOnlyList<ProjectSession>? Sessions = null,
    string? Category = null,
    bool IconFill = false)
{
    public bool IsRunning => Sessions is not null && Sessions.Any(s => s.Running);
    public bool HasGitChanges => Git is not null && Git.ModifiedFileCount > 0;
    public int RunningSessionCount => Sessions?.Count(s => s.Running) ?? 0;
}

public sealed record CommandPreset(string Name, string Command);
public sealed record GitFileChange(string Path, string Status);
public sealed record GitStatus(
    string Branch,
    int ModifiedFileCount,
    bool UpToDate = true,
    IReadOnlyList<GitFileChange>? Files = null,
    int Ahead = 0,
    int Behind = 0,
    string? Head = null,
    DateTimeOffset? LastCommitAt = null,
    string? LastCommitSubject = null,
    string? RemoteUrl = null,
    string? GitHubVisibility = null);

/// <summary>A project's live state at one moment, kept so the history survives the next scan. Cards
/// show the newest one; the doc's factual header fields are written from it.</summary>
public sealed record ProjectStateSnapshot(
    Guid ProjectId,
    DateTimeOffset CapturedAt,
    string? Branch,
    string? Head,
    int ModifiedFileCount,
    int Ahead,
    int Behind,
    DateTimeOffset? LastCommitAt,
    string? LastCommitSubject,
    string? Status);

/// <summary>The stored association between a project and its doc. <paramref name="Manual"/> marks a
/// link the user chose by hand, which automatic matching must never overwrite.</summary>
public sealed record ProjectDocLink(Guid ProjectId, string DocPath, bool Manual, DateTimeOffset LinkedAt);
public sealed record ProjectSession(string Name, string Command, string Duration, bool Running);
public sealed record ProjectDetectionResult(string PrimaryTechnology, IReadOnlyList<string> Technologies, IReadOnlyList<CommandPreset> Presets);
public sealed record IconCandidate(string Path, int Width, int Height, int Score);
public sealed record TerminalOptions(string ProjectPath, string Shell, string? Command = null, string? Name = null);

public enum TerminalSessionState { Created, Running, Exited, Stopped, Failed }
