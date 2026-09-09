using System.Diagnostics;
using Puppeteer.Core;

namespace Puppeteer.Infrastructure;

public sealed class ProcessTerminalService : ITerminalService
{
    private readonly List<ITerminalSession> _sessions = [];
    private readonly object _gate = new();
    public IReadOnlyCollection<ITerminalSession> ActiveSessions { get { lock (_gate) return _sessions.Where(s => s.State == TerminalSessionState.Running).ToArray(); } }
    public async Task<ITerminalSession> CreateAsync(TerminalOptions options, CancellationToken cancellationToken = default)
    {
        var session = new ProcessTerminalSession(options);
        session.Exited += (_, _) => { lock (_gate) _sessions.Remove(session); };
        lock (_gate) _sessions.Add(session);
        try { await session.StartAsync(cancellationToken); }
        catch { lock (_gate) _sessions.Remove(session); throw; }
        return session;
    }
}

public sealed class ProcessTerminalSession(TerminalOptions options) : ITerminalSession
{
    private Process? _process;
    public Guid Id { get; } = Guid.NewGuid();
    public string ProjectPath => options.ProjectPath;
    public string Shell => options.Shell;
    public string Name => options.Name ?? "terminal";
    public string? Command => options.Command;
    public DateTimeOffset StartedAt { get; private set; }
    public TerminalSessionState State { get; private set; }
    public event EventHandler<string>? OutputReceived;
    public event EventHandler? Exited;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(ProjectPath)) throw new DirectoryNotFoundException(ProjectPath);
        var info = new ProcessStartInfo { FileName = Shell, WorkingDirectory = ProjectPath, UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (!string.IsNullOrWhiteSpace(options.Command))
        {
            if (Path.GetFileNameWithoutExtension(Shell).Equals("cmd", StringComparison.OrdinalIgnoreCase)) { info.ArgumentList.Add("/K"); info.ArgumentList.Add(options.Command); }
            else { info.ArgumentList.Add("-NoExit"); info.ArgumentList.Add("-Command"); info.ArgumentList.Add(options.Command); }
        }
        _process = new Process { StartInfo = info, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) OutputReceived?.Invoke(this, e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) OutputReceived?.Invoke(this, e.Data); };
        _process.Exited += (_, _) => { State = TerminalSessionState.Exited; Exited?.Invoke(this, EventArgs.Empty); };
        try { _process.Start(); _process.BeginOutputReadLine(); _process.BeginErrorReadLine(); StartedAt = DateTimeOffset.UtcNow; State = TerminalSessionState.Running; }
        catch { State = TerminalSessionState.Failed; _process.Dispose(); throw; }
        return Task.CompletedTask;
    }
    public async Task WriteAsync(string input, CancellationToken cancellationToken = default)
    { if (_process is null || State != TerminalSessionState.Running) return; await _process.StandardInput.WriteLineAsync(input.AsMemory(), cancellationToken); await _process.StandardInput.FlushAsync(cancellationToken); }
    public Task StopAsync(CancellationToken cancellationToken = default)
    { if (_process is { HasExited: false }) _process.Kill(true); State = TerminalSessionState.Stopped; return Task.CompletedTask; }
}

public sealed class ProjectLauncher : IProjectLauncher
{
    public void OpenFolder(string path) => Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    public void OpenInIde(string path, string? command = null)
    {
        using var process = Process.Start(CreateEditorStartInfo(path, command));
    }

    internal static ProcessStartInfo CreateEditorStartInfo(string path, string? command = null)
    {
        // Open the repository folder rather than relying on Windows' file association for a .sln,
        // which commonly launches Visual Studio instead of VS Code. "code ." remains supported
        // for existing settings, but the repository path is supplied explicitly.
        var executable = string.IsNullOrWhiteSpace(command) ? "code" : command.Trim();
        if (executable.EndsWith(" .", StringComparison.Ordinal)) executable = executable[..^2].TrimEnd();
        executable = executable.Trim('"');
        executable = ResolveEditor(executable);

        path = Path.GetFullPath(path);
        if (!Directory.Exists(path) && !File.Exists(path))
            throw new FileNotFoundException("The file or folder to open no longer exists.", path);

        // A reader can open a markdown file as well as a project directory. ProcessStartInfo requires
        // its working directory to be a directory; using the file path here prevents Windows from
        // starting the editor at all.
        var workingDirectory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            UseShellExecute = Path.GetExtension(executable).Equals(".cmd", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(executable).Equals(".bat", StringComparison.OrdinalIgnoreCase),
            CreateNoWindow = true,
        };
        info.ArgumentList.Add(path);
        return info;
    }

    private static string ResolveEditor(string command)
    {
        // VS Code exposes code.cmd on Windows, not code.exe. Prefer the actual GUI executable
        // so opening a file or folder does not depend on a shell or a freshly inherited PATH.
        var name = Path.GetFileName(command);
        if (name.Equals("code", StringComparison.OrdinalIgnoreCase)
            || name.Equals("code.cmd", StringComparison.OrdinalIgnoreCase))
        {
            var candidates = new List<string>();
            if (Path.IsPathRooted(command))
                candidates.Add(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(command)!, "..", "Code.exe")));
            foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                var bin = folder.Trim().Trim('"');
                if (bin.Length > 0 && File.Exists(Path.Combine(bin, "code.cmd")))
                    candidates.Add(Path.GetFullPath(Path.Combine(bin, "..", "Code.exe")));
            }
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "Code.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft VS Code", "Code.exe"));
            if (candidates.FirstOrDefault(File.Exists) is { } installed) return installed;
        }

        if (File.Exists(command)) return Path.GetFullPath(command);
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var bin = folder.Trim().Trim('"');
            if (bin.Length == 0) continue;
            foreach (var extension in new[] { "", ".exe", ".cmd", ".bat" })
            {
                var candidate = Path.Combine(bin, command + extension);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
        }
        throw new FileNotFoundException($"Couldn't find editor '{command}'. Set the editor executable path in Settings.");
    }
}

public sealed class GitMetadataService : IGitMetadataService
{
    public async Task<GitStatus?> GetStatusAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        // A linked worktree stores a .git *file* pointing back to its common Git directory.
        // Treat it as a repository too, rather than silently classifying it as non-Git.
        var gitMarker = Path.Combine(projectPath, ".git");
        if (!Directory.Exists(gitMarker) && !File.Exists(gitMarker)) return null;
        try
        {
            var info = new ProcessStartInfo("git") { WorkingDirectory = projectPath, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            info.ArgumentList.Add("status"); info.ArgumentList.Add("--porcelain=v1"); info.ArgumentList.Add("--branch");
            using var process = Process.Start(info)!; var output = await process.StandardOutput.ReadToEndAsync(cancellationToken); await process.WaitForExitAsync(cancellationToken);
            var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var branch = lines.FirstOrDefault()?.TrimStart('#', ' ').Split("...", 2)[0] ?? "unknown";
            var changes = lines.Skip(1)
                .Select(line => new GitFileChange(line.Length > 3 ? line[3..].Trim() : line.Trim(), line.Length >= 2 ? line[..2].Trim() : "?"))
                .ToArray();
            var remote = await RunGitAsync(projectPath, cancellationToken, "remote", "get-url", "origin");
            var visibility = remote?.Contains("github.com", StringComparison.OrdinalIgnoreCase) == true
                ? await RunGitHubCliAsync(projectPath, cancellationToken) : null;
            // The branch line carries [ahead N, behind M] only when the branch tracks a remote; a repo
            // with no upstream simply reports neither, which is not the same as being level with one.
            var (ahead, behind) = ParseDivergence(lines.FirstOrDefault() ?? "");
            var head = await RunGitAsync(projectPath, cancellationToken, "log", "-1", "--format=%h%n%cI%n%s");
            var headLines = head?.Split('\n', StringSplitOptions.TrimEntries) ?? [];
            return new(branch, changes.Length, UpToDate: changes.Length == 0, changes.Length == 0 ? null : changes.Take(5).ToArray(),
                Ahead: ahead, Behind: behind,
                Head: headLines.Length > 0 ? headLines[0] : null,
                LastCommitAt: headLines.Length > 1 && DateTimeOffset.TryParse(headLines[1], out var committed) ? committed : null,
                LastCommitSubject: headLines.Length > 2 ? headLines[2] : null,
                RemoteUrl: remote, GitHubVisibility: visibility);
        }
        catch { return null; }
    }

    private static (int Ahead, int Behind) ParseDivergence(string branchLine)
    {
        var open = branchLine.IndexOf('[');
        var close = branchLine.IndexOf(']');
        if (open < 0 || close < open) return (0, 0);
        var ahead = 0; var behind = 0;
        foreach (var part in branchLine[(open + 1)..close].Split(',', StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length != 2 || !int.TryParse(pieces[1], out var count)) continue;
            if (pieces[0] == "ahead") ahead = count;
            else if (pieces[0] == "behind") behind = count;
        }
        return (ahead, behind);
    }

    private static async Task<string?> RunGitAsync(string path, CancellationToken token, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = path, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = await process.StandardOutput.ReadToEndAsync(token); await process.WaitForExitAsync(token);
        return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output.Trim() : null;
    }

    private static async Task<string?> RunGitHubCliAsync(string path, CancellationToken token)
    {
        try
        {
            var info = new ProcessStartInfo("gh") { WorkingDirectory = path, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            info.ArgumentList.Add("repo"); info.ArgumentList.Add("view"); info.ArgumentList.Add("--json"); info.ArgumentList.Add("visibility"); info.ArgumentList.Add("--jq"); info.ArgumentList.Add(".visibility");
            using var process = Process.Start(info)!; var output = await process.StandardOutput.ReadToEndAsync(token); await process.WaitForExitAsync(token);
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output.Trim().ToLowerInvariant() : null;
        }
        catch { return null; }
    }
}
