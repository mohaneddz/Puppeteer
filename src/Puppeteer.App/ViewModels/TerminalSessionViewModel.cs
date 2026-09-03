using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using Puppeteer.Core;

namespace Puppeteer.App.ViewModels;

public sealed class TerminalSessionViewModel : ObservableObject
{
    // Keep the backlog bounded so a chatty process (a dev server, a watch task) can't grow the
    // output buffer without limit and exhaust memory.
    private const int MaxOutputLines = 2000;

    public ITerminalSession? Session { get; }
    public ObservableCollection<string> Output { get; } = [];
    public string Name { get; }
    public string ProjectName { get; }
    public string ProjectPath { get; }
    public string Command { get; }
    public Brush Accent { get; }

    private bool _running;
    public bool Running { get => _running; private set => Set(ref _running, value); }

    private string _input = "";
    public string Input { get => _input; set => Set(ref _input, value); }

    public RelayCommand SendCommand { get; }

    public TerminalSessionViewModel(ITerminalSession session, Brush? accent = null)
    {
        Session = session;
        Name = session.Name;
        ProjectName = Path.GetFileName(session.ProjectPath.TrimEnd(Path.DirectorySeparatorChar));
        ProjectPath = session.ProjectPath;
        Command = session.Shell;
        Accent = accent ?? (Brush)Application.Current.Resources["SuccessBrush"];
        _running = session.State == TerminalSessionState.Running;
        SendCommand = new(_ => _ = SendAsync(), _ => Running);
        session.OutputReceived += (_, line) => Application.Current.Dispatcher.BeginInvoke(() => Append(line));
        session.Exited += (_, _) => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            Running = false;
            Append("[process exited]");
        });
    }

    private void Append(string line)
    {
        Output.Add(line);
        while (Output.Count > MaxOutputLines) Output.RemoveAt(0);
    }

    public async Task SendAsync()
    {
        var text = Input;
        if (string.IsNullOrEmpty(text) || Session is null || !Running) return;
        Input = "";
        Append($"$ {text}");
        await Session.WriteAsync(text);
    }
}
