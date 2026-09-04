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
        session.Exited += (_, _) => { };
        lock (_gate) _sessions.Add(session);
        await session.StartAsync(cancellationToken);
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
        if (!string.IsNullOrWhiteSpace(command))
        {
            // A configured editor is invoked with the project directory as its argument — "code .",
            // "rider", "subl" and friends all take that shape — and without the shell, so the command
            // is resolved against PATH rather than the file-association table.
            var info = new ProcessStartInfo(command) { WorkingDirectory = path, UseShellExecute = false, CreateNoWindow = true };
            info.ArgumentList.Add(path);
            Process.Start(info);
            return;
        }
        var solution = Directory.EnumerateFiles(path, "*.sln*").FirstOrDefault();
        Process.Start(new ProcessStartInfo(solution ?? path) { UseShellExecute = true });
    }
}

public sealed class GitMetadataService : IGitMetadataService
{
    public async Task<GitStatus?> GetStatusAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(Path.Combine(projectPath, ".git"))) return null;
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
            return new(branch, changes.Length, UpToDate: changes.Length == 0, changes.Length == 0 ? null : changes.Take(5).ToArray());
        }
        catch { return null; }
    }
}
