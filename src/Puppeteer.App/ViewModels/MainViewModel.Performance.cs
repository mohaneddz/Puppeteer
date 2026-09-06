namespace Puppeteer.App.ViewModels;

public sealed partial class MainViewModel
{
    public bool IsUiActive { get; private set; } = true;
    private bool _refreshPending;
    private bool _docsReloadPending;
    private bool _gitRefreshPending;
    private bool _classificationPending;

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
            return;
        }
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
}
