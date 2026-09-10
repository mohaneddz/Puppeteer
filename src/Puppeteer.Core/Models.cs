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
    bool IconFill = false,
    string? CustomName = null,
    string IconShape = "Rounded",
    string? Status = null)
{
    /// <summary>What the folder is called on disk, regardless of what the user renamed it to here.</summary>
    public string FolderName => System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path));
    public bool IsRunning => Sessions is not null && Sessions.Any(s => s.Running);
    public bool HasGitChanges => Git is not null && Git.ModifiedFileCount > 0;
    public int RunningSessionCount => Sessions?.Count(s => s.Running) ?? 0;
}

public sealed record CommandPreset(string Name, string Command);
public sealed record GitFileChange(string Path, string Status)
{
    public string Description => Status switch
    {
        "??" => "Untracked (new file)",
        "!!" => "Ignored",
        "UU" or "AA" or "DD" or "AU" or "UA" or "DU" or "UD" => "Merge conflict",
        _ when Status.Contains('R') => "Renamed",
        _ when Status.Contains('D') => "Deleted",
        _ when Status.Contains('A') => "Added",
        _ when Status.Contains('M') => "Modified",
        _ when Status.Contains('C') => "Copied",
        _ => "Changed",
    };
}
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

/// <summary>What the user chose for a project, kept apart from the scanned row so it outlives it.
///
/// A project's id is derived from its path, so a folder that disappears and comes back is the same
/// project — but the scanned row is deleted in between, and with it the icon, the name and the
/// category. This survives that, and is reapplied the moment the folder turns up again.</summary>
public sealed record ProjectPreference(
    Guid ProjectId,
    string Path,
    string? CustomName = null,
    string? IconPath = null,
    bool? IconFill = null,
    string? Category = null,
    string? IconShape = null,
    string? Status = null);
public sealed record ProjectSession(string Name, string Command, string Duration, bool Running);
public sealed record ProjectDetectionResult(string PrimaryTechnology, IReadOnlyList<string> Technologies, IReadOnlyList<CommandPreset> Presets);
public sealed record IconCandidate(string Path, int Width, int Height, int Score);
public sealed record TerminalOptions(string ProjectPath, string Shell, string? Command = null, string? Name = null);

public enum TerminalSessionState { Created, Running, Exited, Stopped, Failed }
