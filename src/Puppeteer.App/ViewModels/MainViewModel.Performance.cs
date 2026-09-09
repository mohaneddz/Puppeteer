using System.Windows.Threading;

namespace Puppeteer.App.ViewModels;

public sealed partial class MainViewModel
{
    public bool IsUiActive { get; private set; } = true;
    private bool _refreshPending;
    private bool _docsReloadPending;
    private bool _gitRefreshPending;
    private bool _classificationPending;

    // Trimming the working set is only worth it once the window has actually stayed in the tray, so it's
    // deferred: a quick hide/show toggle cancels it and never pays the heap-compaction cost.
    private DispatcherTimer? _idleTrim;

    public void SetUiActive(bool active)
    {
        if (IsUiActive == active) return;
        IsUiActive = active;
        UpdateDisplayTimer();
        if (!active)
        {
            _toast?.Stop();
            _searchDebounce?.Stop();
            _vaultDebounce?.Stop();
            _refreshPending = true;
            ScheduleIdleTrim();
            return;
        }
        _idleTrim?.Stop();
        foreach (var session in Sessions) session.FlushOutput();
        if (_refreshPending) { _refreshPending = false; Refresh(); }
        if (_docsReloadPending) { _docsReloadPending = false; _ = LoadDocsAsync(); }
        if (_gitRefreshPending) { _gitRefreshPending = false; _ = RefreshGitAsync(); }
        if (_classificationPending) { _classificationPending = false; _ = ClassifyUncategorizedAsync(); }
    }

    private void UpdateDisplayTimer()
    {
        if (IsUiActive && Sessions.Any(s => s.Running || s.HasPendingOutput)) _durations?.Start();
        else _durations?.Stop();
    }

    private void ScheduleIdleTrim()
    {
        _idleTrim ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(4) };
        _idleTrim.Tick -= OnIdleTrim;
        _idleTrim.Tick += OnIdleTrim;
        _idleTrim.Stop();
        _idleTrim.Start();
    }

    private void OnIdleTrim(object? sender, EventArgs e)
    {
        _idleTrim?.Stop();
        if (!IsUiActive) Services.MemoryTrimmer.Trim();
    }
}
