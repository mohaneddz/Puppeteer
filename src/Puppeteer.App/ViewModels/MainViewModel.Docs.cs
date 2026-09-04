using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;
using Puppeteer.Core;

namespace Puppeteer.App.ViewModels;

/// <summary>The docs vault half of the main view model.
///
/// The vault holds one markdown doc per project — what it is, what works, what is broken, what comes
/// next — and it is shared with whatever else edits those files. So the app treats disk as the truth:
/// it reloads when a doc changes underneath it, refuses to save over an edit it has not seen, and
/// writes back only the fields the user asked it to.</summary>
public sealed partial class MainViewModel
{
    private IProjectDocVault? _vault;
    private readonly List<ProjectDoc> _docs = [];
    private readonly Dictionary<Guid, DocMatch> _docMatches = [];
    private readonly Dictionary<Guid, ProjectDocLink> _docLinks = [];
    private readonly Dictionary<Guid, DocDriftReport> _docDrift = [];
    private Dictionary<Guid, ProjectStateSnapshot> _snapshots = [];
    // Our own save fires the vault watcher a moment later. Remember what we just wrote so the app
    // does not announce its own edit as an external one.
    private (string Path, DateTime At) _lastWrite;
    private DispatcherTimer? _vaultDebounce;

    public ObservableCollection<ProjectDocEntry> DocEntries { get; } = [];
    public IReadOnlyList<string> DocFilters { get; } = ["All", "Needs writeup", "Undocumented", "Documented", "Active", "Paused", "Shipped", "Archived"];

    private string _docsFolder = "";
    public string DocsFolder
    {
        get => _docsFolder;
        set { if (!Set(ref _docsFolder, value)) return; SavePref("DocsFolder", value.Trim()); OpenVault(); }
    }

    private bool _docsAutoSnapshot = true;
    public bool DocsAutoSnapshot
    {
        get => _docsAutoSnapshot;
        set { if (Set(ref _docsAutoSnapshot, value)) SavePref("DocsAutoSnapshot", value ? "1" : "0"); }
    }

    private string _docFilter = "All";
    public string DocFilter { get => _docFilter; set { if (Set(ref _docFilter, value)) RebuildDocEntries(); } }

    private string _docSearch = "";
    public string DocSearch { get => _docSearch; set { if (Set(ref _docSearch, value)) RebuildDocEntries(); } }

    public bool HasVault => _vault?.VaultPath is not null;
    public string DocsSummary => !HasVault
        ? "No docs folder connected."
        : $"{_docs.Count} doc{(_docs.Count == 1 ? "" : "s")} · {_docMatches.Count} linked · {_docDrift.Values.Count(d => d.NeedsWriteup)} need a writeup";

    public RelayCommand ChooseDocsFolderCommand { get; private set; } = null!;
    public RelayCommand ClearDocsFolderCommand { get; private set; } = null!;
    public RelayCommand OpenDocsFolderCommand { get; private set; } = null!;
    public AsyncRelayCommand ReloadDocsCommand { get; private set; } = null!;
    public RelayCommand OpenDocCommand { get; private set; } = null!;
    public AsyncRelayCommand CreateDocCommand { get; private set; } = null!;
    public AsyncRelayCommand LinkDocCommand { get; private set; } = null!;
    public AsyncRelayCommand UnlinkDocCommand { get; private set; } = null!;
    public AsyncRelayCommand SaveDocCommand { get; private set; } = null!;
    public AsyncRelayCommand RevertDocCommand { get; private set; } = null!;
    public AsyncRelayCommand SyncDocFactsCommand { get; private set; } = null!;
    public AsyncRelayCommand SyncAllDocFactsCommand { get; private set; } = null!;
    public RelayCommand SelectDocEntryCommand { get; private set; } = null!;

    private void InitializeDocs()
    {
        ChooseDocsFolderCommand = new(_ => { if (_picker.PickFolder() is { Length: > 0 } folder) DocsFolder = folder; });
        ClearDocsFolderCommand = new(_ => DocsFolder = "");
        OpenDocsFolderCommand = new(_ => { if (_vault?.VaultPath is { } path) _launcher.OpenFolder(path); }, _ => HasVault);
        ReloadDocsCommand = new(_ => LoadDocsAsync());
        OpenDocCommand = new(p => OpenDoc((p as ProjectDocEntry)?.Doc ?? SelectedDoc), p => ((p as ProjectDocEntry)?.Doc ?? SelectedDoc) is not null);
        CreateDocCommand = new(p => CreateDocAsync((p as ProjectDocEntry)?.Project ?? SelectedProject), _ => HasVault);
        LinkDocCommand = new(_ => LinkDocAsync(), _ => HasVault && SelectedProject is not null);
        UnlinkDocCommand = new(_ => UnlinkDocAsync(), _ => SelectedProject is not null && SelectedDoc is not null);
        SaveDocCommand = new(_ => SaveDocAsync(), _ => DocDirty);
        RevertDocCommand = new(_ => ReloadSelectedDocAsync(), _ => SelectedDoc is not null);
        SyncDocFactsCommand = new(_ => SyncFactsAsync(SelectedProject), _ => SelectedProject is not null && SelectedDoc is not null);
        SyncAllDocFactsCommand = new(_ => SyncAllFactsAsync(), _ => HasVault && _docMatches.Count > 0);
        SelectDocEntryCommand = new(p => { if (p is ProjectDocEntry entry) SelectedProject = Projects.FirstOrDefault(x => x.Id == entry.Project.Id) ?? entry.Project; });

        PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SelectedProject)) { LoadSelectedDoc(); _ = LoadHistoryAsync(); } };
        DocEntries.CollectionChanged += (_, _) => Raise(nameof(DocEntryCount));
        Projects.CollectionChanged += OnProjectsChanged;
    }

    private void OnProjectsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildDocEntries();

    public int DocEntryCount => DocEntries.Count;

    private void OpenVault()
    {
        if (_vault is null) return;
        _vault.Changed -= OnVaultChanged;
        _vault.Open(string.IsNullOrWhiteSpace(_docsFolder) ? null : _docsFolder);
        _vault.Changed += OnVaultChanged;
        Raise(nameof(HasVault));
        _ = LoadDocsAsync();
    }

    // A single save can raise several watcher events, and an editor writing the file raises more.
    // Coalesce them, then reload once.
    private void OnVaultChanged(object? sender, string path)
    {
        if (string.Equals(path, _lastWrite.Path, StringComparison.OrdinalIgnoreCase) && DateTime.UtcNow - _lastWrite.At < TimeSpan.FromSeconds(3)) return;
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            _vaultDebounce ??= CreateVaultDebounce();
            _vaultDebounce.Stop();
            _vaultDebounce.Start();
        });
    }

    private DispatcherTimer CreateVaultDebounce()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        timer.Tick += (_, _) => { timer.Stop(); _ = LoadDocsAsync(external: true); };
        return timer;
    }

    public async Task LoadDocsAsync(bool external = false)
    {
        if (_vault is null) return;
        try
        {
            var docs = await _vault.LoadAsync();
            var links = await _repository.GetDocLinksAsync();
            _snapshots = new(await _repository.GetLatestSnapshotsAsync());

            _docs.Clear();
            _docs.AddRange(docs.Where(d => d.IsStateDoc));
            _docLinks.Clear();
            foreach (var link in links) _docLinks[link.ProjectId] = link;

            MatchDocs();
            RebuildDocEntries();
            Raise(nameof(DocsSummary));
            if (external)
            {
                LoadSelectedDoc(fromDisk: true);
                Status = "Docs folder changed on disk — reloaded.";
            }
        }
        catch (Exception e) { Status = $"Couldn't read the docs folder: {e.Message}"; }
    }

    private void MatchDocs()
    {
        _docMatches.Clear();
        // A link the user made by hand outranks anything matching would decide, and the projects it
        // covers are held back from automatic matching so their docs cannot be claimed twice.
        var spokenFor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (projectId, link) in _docLinks)
        {
            var doc = _docs.FirstOrDefault(d => string.Equals(d.FilePath, link.DocPath, StringComparison.OrdinalIgnoreCase));
            if (doc is null || !link.Manual) continue;
            _docMatches[projectId] = new(doc, DocMatchConfidence.ExactPath, "linked by hand");
            spokenFor.Add(doc.FilePath);
        }
        var remaining = _docs.Where(d => !spokenFor.Contains(d.FilePath)).ToArray();
        foreach (var (projectId, match) in ProjectDocMatcher.Match(_allProjects.Where(p => !_docMatches.ContainsKey(p.Id)), remaining))
            _docMatches[projectId] = match;

        _docDrift.Clear();
        foreach (var project in _allProjects)
            _docDrift[project.Id] = ProjectDocDrift.Compare(project, _docMatches.GetValueOrDefault(project.Id)?.Doc);
    }

    private void RebuildDocEntries()
    {
        var terms = _docSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var entries = _allProjects
            .Select(project => new ProjectDocEntry(project,
                _docMatches.GetValueOrDefault(project.Id)?.Doc,
                _docMatches.GetValueOrDefault(project.Id)?.Confidence ?? DocMatchConfidence.None,
                _docLinks.GetValueOrDefault(project.Id)?.Manual == true,
                _docDrift.GetValueOrDefault(project.Id) ?? new(project.Id, DocDrift.NoDoc, ["No state doc in the vault"]),
                _snapshots.GetValueOrDefault(project.Id)))
            .Where(Keep)
            .OrderByDescending(e => e.NeedsWriteup)
            .ThenByDescending(e => e.DriftCount)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        DocEntries.Clear();
        foreach (var entry in entries) DocEntries.Add(entry);
        Raise(nameof(DocsSummary));

        bool Keep(ProjectDocEntry entry)
        {
            var passesFilter = _docFilter switch
            {
                "Needs writeup" => entry.NeedsWriteup,
                "Undocumented" => !entry.HasDoc,
                "Documented" => entry.HasDoc,
                "All" => true,
                _ => entry.Status.Equals(_docFilter, StringComparison.OrdinalIgnoreCase),
            };
            if (!passesFilter) return false;
            if (terms.Length == 0) return true;
            var haystack = string.Join(' ', entry.Name, entry.Path, entry.Status, entry.Summary, entry.NextStep, entry.DocName);
            return terms.All(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
    }

    // ---- The doc of the currently selected project ----

    private ProjectDoc? _selectedDoc;
    public ProjectDoc? SelectedDoc { get => _selectedDoc; private set { if (Set(ref _selectedDoc, value)) { Raise(nameof(HasSelectedDoc)); Raise(nameof(SelectedDocName)); } } }
    public bool HasSelectedDoc => _selectedDoc is not null;
    public string SelectedDocName => _selectedDoc is null ? "" : Path.GetFileName(_selectedDoc.FilePath);

    public DocDriftReport? SelectedDrift => SelectedProject is { } project ? _docDrift.GetValueOrDefault(project.Id) : null;
    public string SelectedDriftSummary => SelectedDrift is { Reasons.Count: > 0 } report ? string.Join("\n", report.Reasons.Select(r => "· " + r)) : "";
    public bool SelectedHasDrift => SelectedDrift is { Any: true };
    public string SelectedLinkNote => DocEntryFor(SelectedProject)?.LinkNote ?? "";

    private bool _docDirty;
    public bool DocDirty { get => _docDirty; private set { if (Set(ref _docDirty, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }

    private bool _docConflicted;
    public bool DocConflicted { get => _docConflicted; private set => Set(ref _docConflicted, value); }

    private string _docStatus = "", _docSummary = "", _docWorks = "", _docBroken = "", _docNext = "", _docNotes = "";
    public string DocStatusValue { get => _docStatus; set { if (Set(ref _docStatus, value)) DocDirty = true; } }
    public string DocSummary { get => _docSummary; set { if (Set(ref _docSummary, value)) DocDirty = true; } }
    public string DocWorks { get => _docWorks; set { if (Set(ref _docWorks, value)) DocDirty = true; } }
    public string DocBroken { get => _docBroken; set { if (Set(ref _docBroken, value)) DocDirty = true; } }
    public string DocNext { get => _docNext; set { if (Set(ref _docNext, value)) DocDirty = true; } }
    public string DocNotes { get => _docNotes; set { if (Set(ref _docNotes, value)) DocDirty = true; } }
    public IReadOnlyList<string> DocStatusOptions { get; } = ProjectDocStatuses.Known;

    private ProjectDocEntry? DocEntryFor(Project? project) =>
        project is null ? null : DocEntries.FirstOrDefault(e => e.Project.Id == project.Id);

    private void LoadSelectedDoc(bool fromDisk = false)
    {
        // An unsaved edit is the user's work; a reload triggered by something else must not silently
        // discard it. Say so and leave the editor alone until they save or revert.
        if (DocDirty && fromDisk) { DocConflicted = true; return; }
        var doc = SelectedProject is { } project ? _docMatches.GetValueOrDefault(project.Id)?.Doc : null;
        SelectedDoc = doc;
        _docStatus = ProjectDocStatuses.Parse(doc?.Status) ?? "";
        _docSummary = doc?.Section(ProjectDocSections.Summary) ?? "";
        _docWorks = doc?.Section(ProjectDocSections.Works) ?? "";
        _docBroken = doc?.Section(ProjectDocSections.Broken) ?? "";
        _docNext = doc?.Section(ProjectDocSections.Next) ?? "";
        _docNotes = doc?.Section(ProjectDocSections.Notes) ?? "";
        foreach (var name in new[] { nameof(DocStatusValue), nameof(DocSummary), nameof(DocWorks), nameof(DocBroken), nameof(DocNext), nameof(DocNotes),
                                     nameof(SelectedDrift), nameof(SelectedDriftSummary), nameof(SelectedHasDrift), nameof(SelectedLinkNote) })
            Raise(name);
        DocDirty = false;
        DocConflicted = false;
    }

    private void OpenDoc(ProjectDoc? doc)
    {
        if (doc is null) return;
        try { _launcher.OpenInIde(doc.FilePath, _ideCommand); Status = $"Opened {Path.GetFileName(doc.FilePath)}"; }
        catch (Exception e) { Status = $"Couldn't open the doc: {e.Message}"; }
    }

    private async Task SaveDocAsync()
    {
        if (_vault is null || SelectedDoc is not { } doc) return;
        var edited = ProjectDocFormat.WithSection(
            ProjectDocFormat.WithSection(
                ProjectDocFormat.WithSection(
                    ProjectDocFormat.WithSection(
                        ProjectDocFormat.WithSection(doc, ProjectDocSections.Summary, _docSummary),
                        ProjectDocSections.Works, _docWorks),
                    ProjectDocSections.Broken, _docBroken),
                ProjectDocSections.Next, _docNext),
            ProjectDocSections.Notes, _docNotes);
        if (_docStatus.Length > 0) edited = ProjectDocFormat.WithField(edited, ProjectDocFields.Status, _docStatus);
        await WriteDocAsync(edited, doc.ModifiedAt, $"Saved {Path.GetFileName(doc.FilePath)}");
    }

    private async Task WriteDocAsync(ProjectDoc doc, DateTimeOffset? expected, string successMessage)
    {
        if (_vault is null) return;
        _lastWrite = (doc.FilePath, DateTime.UtcNow);
        var result = await _vault.WriteAsync(doc, expected);
        switch (result.Outcome)
        {
            case DocWriteOutcome.Written:
                ReplaceDoc(result.Doc!);
                DocDirty = false;
                DocConflicted = false;
                Status = successMessage;
                break;
            case DocWriteOutcome.Conflict:
                DocConflicted = true;
                Status = "That doc changed on disk — review before saving over it.";
                break;
            default:
                Status = result.Message ?? "Couldn't write the doc.";
                break;
        }
    }

    private void ReplaceDoc(ProjectDoc saved)
    {
        var at = _docs.FindIndex(d => string.Equals(d.FilePath, saved.FilePath, StringComparison.OrdinalIgnoreCase));
        if (at >= 0) _docs[at] = saved; else _docs.Add(saved);
        MatchDocs();
        RebuildDocEntries();
        if (SelectedDoc?.FilePath.Equals(saved.FilePath, StringComparison.OrdinalIgnoreCase) == true)
        {
            SelectedDoc = saved;
            Raise(nameof(SelectedDrift)); Raise(nameof(SelectedDriftSummary)); Raise(nameof(SelectedHasDrift));
        }
    }

    private Task ReloadSelectedDocAsync()
    {
        DocDirty = false;
        LoadSelectedDoc();
        return Task.CompletedTask;
    }

    private async Task CreateDocAsync(Project? project)
    {
        if (_vault is null || project is null) return;
        if (_docMatches.ContainsKey(project.Id)) { Status = $"{project.Name} already has a doc."; return; }
        var category = project.Category is { Length: > 0 } stated ? stated : ProjectTypeRules.Of(project);
        var path = _vault.PathFor(MatchExistingCategoryFolder(category), project.Name);
        if (File.Exists(path)) { Status = $"A doc already exists at {path}."; return; }
        var doc = ProjectDocFormat.Create(path, project.Name, FactualFields(project, null));
        await WriteDocAsync(doc, null, $"Created {Path.GetFileName(path)}");
        await LinkAsync(project.Id, path, manual: false);
    }

    /// <summary>Uses the vault's own folder names rather than inventing one: a Tauri project belongs
    /// in whatever folder the other Tauri docs already sit in.</summary>
    private string MatchExistingCategoryFolder(string category)
    {
        var folders = _vault?.Categories() ?? [];
        return folders.FirstOrDefault(f => ProjectDocMatcher.Normalize(f) == ProjectDocMatcher.Normalize(category)) ?? category;
    }

    private async Task LinkDocAsync()
    {
        if (SelectedProject is not { } project) return;
        var picked = _picker.PickMarkdown();
        if (string.IsNullOrWhiteSpace(picked)) return;
        if (_vault?.VaultPath is { } vault && !picked.StartsWith(vault, StringComparison.OrdinalIgnoreCase))
        { Status = "That file is outside the docs folder."; return; }
        await LinkAsync(project.Id, picked, manual: true);
        Status = $"Linked {Path.GetFileName(picked)} to {project.Name}";
    }

    private async Task LinkAsync(Guid projectId, string docPath, bool manual)
    {
        await _repository.SetDocLinkAsync(new(projectId, docPath, manual, DateTimeOffset.UtcNow));
        await LoadDocsAsync();
        LoadSelectedDoc();
    }

    private async Task UnlinkDocAsync()
    {
        if (SelectedProject is not { } project) return;
        await _repository.RemoveDocLinkAsync(project.Id);
        await LoadDocsAsync();
        LoadSelectedDoc();
        Status = $"Unlinked the doc from {project.Name}";
    }

    /// <summary>Rewrites only the header fields that are statements of fact — where the project is,
    /// what it is built with, when it last moved. Every section of prose is left alone.</summary>
    private async Task SyncFactsAsync(Project? project)
    {
        if (project is null || _docMatches.GetValueOrDefault(project.Id)?.Doc is not { } doc) return;
        var updated = ApplyFacts(doc, project);
        if (ProjectDocFormat.Render(updated) == ProjectDocFormat.Render(doc)) { Status = $"{project.Name}'s doc already matches."; return; }
        await WriteDocAsync(updated, doc.ModifiedAt, $"Updated {Path.GetFileName(doc.FilePath)} from the repo");
        LoadSelectedDoc();
    }

    private async Task SyncAllFactsAsync()
    {
        var targets = _allProjects.Where(p => _docMatches.ContainsKey(p.Id)).ToArray();
        if (targets.Length == 0) return;
        var answer = MessageBox.Show(
            $"Update the Location, Stack and Last activity lines in {targets.Length} docs from what is on disk?\n\nProse sections are not touched.",
            "Sync docs with the repos?", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        var written = 0;
        foreach (var project in targets)
        {
            if (_docMatches.GetValueOrDefault(project.Id)?.Doc is not { } doc) continue;
            var updated = ApplyFacts(doc, project);
            if (ProjectDocFormat.Render(updated) == ProjectDocFormat.Render(doc)) continue;
            _lastWrite = (doc.FilePath, DateTime.UtcNow);
            var result = await _vault!.WriteAsync(updated, doc.ModifiedAt);
            if (result.Outcome == DocWriteOutcome.Written) { ReplaceDoc(result.Doc!); written++; }
        }
        await LoadDocsAsync();
        LoadSelectedDoc();
        Status = written == 0 ? "Every doc already matched its repo." : $"Updated {written} doc{(written == 1 ? "" : "s")}";
    }

    private ProjectDoc ApplyFacts(ProjectDoc doc, Project project)
    {
        var updated = ProjectDocFormat.WithField(doc, ProjectDocFields.Location, project.Path);
        if (doc.Stack is not { Length: > 0 } || (doc.Field(ProjectDocFields.Stack) is { } stack && !project.Technologies.Any(t => stack.Contains(t, StringComparison.OrdinalIgnoreCase))))
            updated = ProjectDocFormat.WithField(updated, ProjectDocFields.Stack, string.Join(", ", project.Technologies));
        if (project.Git is { LastCommitAt: { } committed })
            updated = ProjectDocFormat.WithField(updated, ProjectDocFields.LastActivity, Activity(project, committed));
        if (ProjectDocStatuses.Parse(doc.Status) is null)
            updated = ProjectDocFormat.WithField(updated, ProjectDocFields.Status, ProjectDocStatuses.Active);
        return updated;
    }

    private static string Activity(Project project, DateTimeOffset committed)
    {
        var git = project.Git!;
        var parts = new List<string> { $"{committed:yyyy-MM-dd}" };
        if (git.LastCommitSubject is { Length: > 0 } subject) parts.Add($"last commit `{git.Head}` \"{subject}\"");
        if (git.ModifiedFileCount > 0) parts.Add($"{git.ModifiedFileCount} uncommitted file{(git.ModifiedFileCount == 1 ? "" : "s")}");
        if (git.Ahead > 0) parts.Add($"{git.Ahead} unpushed");
        if (git.Branch is { Length: > 0 } branch) parts.Add($"on `{branch}`");
        return string.Join("; ", parts);
    }

    private IReadOnlyList<DocField> FactualFields(Project project, ProjectDoc? existing) =>
    [
        new(ProjectDocFields.Location, project.Path),
        new(ProjectDocFields.Category, project.Category is { Length: > 0 } category ? category : ProjectTypeRules.Of(project)),
        new(ProjectDocFields.Client, project.Category ?? "Personal"),
        new(ProjectDocFields.Status, ProjectDocStatuses.Parse(existing?.Status) ?? ProjectDocStatuses.Active),
        new(ProjectDocFields.Stack, string.Join(", ", project.Technologies)),
        new(ProjectDocFields.LastActivity, project.Git?.LastCommitAt is { } committed ? Activity(project, committed) : "not scanned yet"),
    ];

    /// <summary>Records where every project stands right now. Called after a git refresh, so the
    /// history builds up on its own without the user having to remember to capture anything.</summary>
    public async Task CaptureSnapshotsAsync()
    {
        if (!_docsAutoSnapshot) return;
        var now = DateTimeOffset.UtcNow;
        foreach (var project in _allProjects.Where(p => p.Git is not null).ToArray())
        {
            var git = project.Git!;
            await _repository.AddSnapshotAsync(new(project.Id, now, git.Branch, git.Head, git.ModifiedFileCount, git.Ahead, git.Behind,
                git.LastCommitAt, git.LastCommitSubject, ProjectDocStatuses.Parse(_docMatches.GetValueOrDefault(project.Id)?.Doc?.Status)));
        }
        _snapshots = new(await _repository.GetLatestSnapshotsAsync());
        RebuildDocEntries();
    }

    public ObservableCollection<ProjectStateSnapshot> SelectedHistory { get; } = [];

    public async Task LoadHistoryAsync()
    {
        SelectedHistory.Clear();
        if (SelectedProject is not { } project) return;
        foreach (var snapshot in await _repository.GetSnapshotsAsync(project.Id)) SelectedHistory.Add(snapshot);
    }

    private async Task LoadDocsPreferencesAsync()
    {
        _docsFolder = await _repository.GetSettingAsync("DocsFolder") ?? "";
        Raise(nameof(DocsFolder));
        _docsAutoSnapshot = await _repository.GetSettingAsync("DocsAutoSnapshot") != "0";
        Raise(nameof(DocsAutoSnapshot));
        OpenVault();
    }
}
