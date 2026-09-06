using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using Puppeteer.Core;

namespace Puppeteer.App.ViewModels;

public sealed class TerminalSessionViewModel : ObservableObject
{
    // Keep the backlog bounded so a chatty process (a dev server, a watch task) can't grow the
    // output buffer without limit and exhaust memory. Settable, because how much scrollback is worth
    // holding depends on what the user runs; shared by every session so one setting governs all.
    public static int MaxOutputLines { get; set; } = 2000;

    public ITerminalSession? Session { get; }
    public BatchCollection<string> Output { get; } = [];
    private readonly object _outputGate = new();
    private readonly Queue<string> _pendingOutput = new();
    public bool HasPendingOutput { get { lock (_outputGate) return _pendingOutput.Count > 0; } }
    public string Name { get; }
    public string ProjectName { get; }
    public string ProjectTechnology { get; }
    public string ProjectPath { get; }
    public string Command { get; }
    public Brush Accent { get; }

    private bool _running;
    public bool Running { get => _running; private set => Set(ref _running, value); }

    private DateTimeOffset? _stoppedAt;

    /// <summary>How long the session has been alive, as a compact "3m" / "2h 14m" label. A stopped
    /// session freezes at the age it reached, rather than counting on forever.</summary>
    public string Duration
    {
        get
        {
            if (Session is null) return "";
            var elapsed = (_stoppedAt ?? DateTimeOffset.UtcNow) - Session.StartedAt;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            if (elapsed.TotalMinutes < 1) return $"{(int)elapsed.TotalSeconds}s";
            if (elapsed.TotalHours < 1) return $"{(int)elapsed.TotalMinutes}m";
            return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
        }
    }

    /// <summary>Called by the shared one-second tick so every visible duration advances together
    /// instead of each session owning a timer.</summary>
    private string? _lastDuration;
    public void TickDuration()
    {
        var duration = Duration;
        if (duration == _lastDuration) return;
        _lastDuration = duration;
        Raise(nameof(Duration));
    }

    private string _input = "";
    public string Input { get => _input; set => Set(ref _input, value); }

    public RelayCommand SendCommand { get; }
    public RelayCommand StopCommand { get; }

    public TerminalSessionViewModel(ITerminalSession session, Project project, Brush? accent = null)
    {
        Session = session;
        Name = session.Name;
        ProjectName = project.Name;
        ProjectTechnology = project.PrimaryTechnology;
        ProjectPath = session.ProjectPath;
        // A preset session is identified by what it runs; a bare shell only has its shell to show.
        Command = string.IsNullOrWhiteSpace(session.Command) ? session.Shell : session.Command!;
        Accent = accent ?? (Brush)Application.Current.Resources["SuccessBrush"];
        _running = session.State == TerminalSessionState.Running;
        SendCommand = new(_ => _ = SendAsync(), _ => Running);
        StopCommand = new(_ => { try { Session?.StopAsync(); } catch { } }, _ => Running);
        session.OutputReceived += (_, line) => Append(line);
        session.Exited += (_, _) => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _stoppedAt = DateTimeOffset.UtcNow;
            Running = false;
            Raise(nameof(Duration));
            Append("[process exited]");
        });
    }

    private void Append(string line)
    {
        lock (_outputGate)
        {
            // Bound both pending lines and their size even while the window is hidden for days.
            _pendingOutput.Enqueue(line.Length > 16384 ? line[..16384] : line);
            while (_pendingOutput.Count > MaxOutputLines) _pendingOutput.Dequeue();
        }
    }

    public void FlushOutput()
    {
        string[] lines;
        lock (_outputGate)
        {
            if (_pendingOutput.Count == 0) return;
            lines = _pendingOutput.ToArray();
            _pendingOutput.Clear();
        }
        if (lines.Length <= 16)
        {
            foreach (var line in lines) Output.Add(line);
            while (Output.Count > MaxOutputLines) Output.RemoveAt(0);
        }
        else Output.ReplaceAll(Output.Concat(lines).TakeLast(MaxOutputLines));
    }

    public void ClearOutput()
    {
        lock (_outputGate) _pendingOutput.Clear();
        Output.Clear();
    }

    /// <summary>Drops the oldest lines beyond the current cap. Also called when the cap is lowered,
    /// so an existing session shrinks to the new limit instead of waiting for more output.</summary>
    public void TrimOutput()
    {
        lock (_outputGate)
            while (_pendingOutput.Count > MaxOutputLines) _pendingOutput.Dequeue();
        Output.ReplaceAll(Output.TakeLast(MaxOutputLines));
    }

    private readonly List<string> _history = [];
    private int _historyIndex;

    public async Task SendAsync()
    {
        var text = Input;
        if (string.IsNullOrEmpty(text) || Session is null || !Running) return;
        Input = "";
        if (_history.Count == 0 || _history[^1] != text) _history.Add(text);
        _historyIndex = _history.Count;
        Append($"$ {text}");
        await Session.WriteAsync(text);
    }

    public void HistoryPrevious()
    {
        if (_history.Count == 0) return;
        _historyIndex = Math.Max(0, _historyIndex - 1);
        Input = _history[_historyIndex];
    }

    public void HistoryNext()
    {
        if (_history.Count == 0) return;
        _historyIndex++;
        if (_historyIndex >= _history.Count) { _historyIndex = _history.Count; Input = ""; }
        else Input = _history[_historyIndex];
    }
}
