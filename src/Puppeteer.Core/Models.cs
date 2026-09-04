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
public sealed record GitStatus(string Branch, int ModifiedFileCount, bool UpToDate = true, IReadOnlyList<GitFileChange>? Files = null);
public sealed record ProjectSession(string Name, string Command, string Duration, bool Running);
public sealed record ProjectDetectionResult(string PrimaryTechnology, IReadOnlyList<string> Technologies, IReadOnlyList<CommandPreset> Presets);
public sealed record IconCandidate(string Path, int Width, int Height, int Score);
public sealed record TerminalOptions(string ProjectPath, string Shell, string? Command = null, string? Name = null);

public enum TerminalSessionState { Created, Running, Exited, Stopped, Failed }
