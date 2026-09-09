using Puppeteer.Core;

namespace Puppeteer.App.ViewModels;

public sealed partial class MainViewModel
{
    private bool _terminalScrollable = true;
    private bool _terminalHorizontalScrollable;
    public bool TerminalHorizontalScrollable
    {
        get => _terminalHorizontalScrollable;
        set { if (Set(ref _terminalHorizontalScrollable, value)) SavePref("TerminalHorizontalScrollable", value ? "1" : "0"); }
    }
    private RelayCommand? _toggleTerminalHorizontalScrollableCommand;
    public RelayCommand ToggleTerminalHorizontalScrollableCommand => _toggleTerminalHorizontalScrollableCommand ??= new(_ => TerminalHorizontalScrollable = !TerminalHorizontalScrollable);
    public bool TerminalScrollable
    {
        get => _terminalScrollable;
        set { if (Set(ref _terminalScrollable, value)) SavePref("TerminalScrollable", value ? "1" : "0"); }
    }
    private RelayCommand? _toggleTerminalScrollableCommand;
    public RelayCommand ToggleTerminalScrollableCommand => _toggleTerminalScrollableCommand ??= new(_ => TerminalScrollable = !TerminalScrollable);

    private readonly List<TerminalSessionViewModel> _closedTerminals = [];

    public async Task ReopenTerminalAsync()
    {
        if (_closedTerminals.Count == 0) { Status = "No closed terminals to reopen"; return; }
        var previous = _closedTerminals[^1];
        _closedTerminals.RemoveAt(_closedTerminals.Count - 1);
        await OpenTerminalAsync(previous.Project, name: previous.Name, restore: previous);
    }

    public void SelectTerminal(int index)
    {
        if (Sessions.Count == 0) return;
        SelectedSession = Sessions[Math.Clamp(index, 0, Sessions.Count - 1)];
        TerminalOpen = true;
    }

    public void CycleTerminal(int direction)
    {
        if (Sessions.Count > 0) SelectTerminal((Sessions.IndexOf(SelectedSession!) + direction + Sessions.Count) % Sessions.Count);
    }
}
