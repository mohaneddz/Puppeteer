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
    private readonly Dictionary<Guid, DocMatch> _docSuggestions = [];
    private readonly Dictionary<Guid, ProjectDocLink> _docLinks = [];
    // Docs the LLM matched to a project, and the set it has already been asked about (matched or not),
    // so a reload does not re-spend credits on the same leftovers. Both reset when the folders change.
    private readonly Dictionary<Guid, string> _aiMatches = [];
    private readonly HashSet<Guid> _aiTried = [];
    private Dictionary<Guid, ProjectStateSnapshot> _snapshots = [];
    private ProjectIndexLookup? _index;
    private readonly Dictionary<Guid, ProjectIndexEntry> _indexRows = [];
    // Looked up once per load rather than per row: the docs list is rebuilt on every search
    // keystroke, and that is no place for a hundred file-existence checks.
    private Dictionary<Guid, string> _readmes = [];
    // Our own save fires the vault watcher a moment later. Remember what we just wrote so the app
    // does not announce its own edit as an external one.
    private (string Path, DateTime At) _lastWrite;
    private DispatcherTimer? _vaultDebounce;

    public BatchCollection<ProjectDocEntry> DocEntries { get; } = [];
    public IReadOnlyList<string> DocFilters { get; } = ["All", "Undocumented", "Documented", "Active", "Paused", "Shipped", "Archived"];

    /// <summary>Every folder scanned for docs, added like project roots. The first is the primary,
    /// where a newly created doc is written and where a beside-the-vault index is looked for.</summary>
    public ObservableCollection<string> DocsFolders { get; } = [];
    private void SaveDocsFolders() => SavePref("DocsFolders", string.Join("\n", DocsFolders));

    private string _indexFile = "";
    /// <summary>The single markdown file that maps the whole library and links to the per-project
    /// docs. Read only — Puppeteer takes each row's marker and one-line summary from it, and never
    /// writes to it, because it is mostly hand-written prose.</summary>
    public string IndexFile
    {
        get => _indexFile;
        set { if (!Set(ref _indexFile, value)) return; SavePref("DocsIndexFile", value.Trim()); _ = LoadDocsAsync(); }
    }

    public bool HasIndex => _index is not null;
    public string IndexSummary => _index is null
        ? "No index file connected."
        : $"{_index.Entries.Count} rows · {_indexRows.Count} matched to a project";

    private bool _docsAutoSnapshot = true;
    public bool DocsAutoSnapshot
    {
        get => _docsAutoSnapshot;
        set { if (Set(ref _docsAutoSnapshot, value)) SavePref("DocsAutoSnapshot", value ? "1" : "0"); }
    }

    private string _docFilter = "All";
    public string DocFilter { get => _docFilter; set { if (Set(ref _docFilter, value)) RebuildDocEntries(); } }

    private string _docSearch = "";
    public string DocSearch { get => _docSearch; set { if (Set(ref _docSearch, value)) { Raise(nameof(ActiveSearch)); RebuildDocEntries(); } } }

    /// <summary>The index's own section headings — Web, Mobile, Desktop (Tauri), AI — used as a
    /// filter, so the page groups the library the way the index already does.</summary>
    public ObservableCollection<string> DocSections { get; } = ["All"];
    private string _docSection = "All";
    public string DocSection { get => _docSection; set { if (Set(ref _docSection, value)) RebuildDocEntries(); } }

    private string _docView = "List";
    public string DocView { get => _docView; set { if (Set(ref _docView, value)) SavePref("DocsView", value); } }
    public RelayCommand SetDocViewCommand { get; private set; } = null!;

    public bool HasVault => _vault?.VaultPath is not null;
    public string DocsSummary => !HasVault
        ? "No docs folder connected."
        : $"{_docs.Count} doc{(_docs.Count == 1 ? "" : "s")} · {_docMatches.Count} linked · {_readmes.Count} with a README";

    public RelayCommand AddDocsFolderCommand { get; private set; } = null!;
    public RelayCommand RemoveDocsFolderCommand { get; private set; } = null!;
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
    public AsyncRelayCommand LinkAllDocsCommand { get; private set; } = null!;
    public RelayCommand SelectDocEntryCommand { get; private set; } = null!;
    public RelayCommand ChooseIndexFileCommand { get; private set; } = null!;
    public RelayCommand ClearIndexFileCommand { get; private set; } = null!;
    public AsyncRelayCommand AcceptDocSuggestionCommand { get; private set; } = null!;

    private void InitializeDocs()
    {
        AddDocsFolderCommand = new(_ => AddDocsFolder());
        RemoveDocsFolderCommand = new(p => RemoveDocsFolder(p as string), p => p is string);
        OpenDocsFolderCommand = new(p => { if ((p as string ?? _vault?.VaultPath) is { Length: > 0 } path) _launcher.OpenFolder(path); }, _ => HasVault);
        ReloadDocsCommand = new(_ => { _aiMatches.Clear(); _aiTried.Clear(); return LoadDocsAsync(); });
        OpenDocCommand = new(p => OpenDoc((p as ProjectDocEntry)?.Doc ?? SelectedDoc), p => ((p as ProjectDocEntry)?.Doc ?? SelectedDoc) is not null);
        CreateDocCommand = new(p => CreateDocAsync((p as ProjectDocEntry)?.Project ?? SelectedProject), _ => HasVault);
        LinkDocCommand = new(_ => LinkDocAsync(), _ => HasVault && SelectedProject is not null);
        UnlinkDocCommand = new(_ => UnlinkDocAsync(), _ => SelectedProject is not null && SelectedDoc is not null);
        SaveDocCommand = new(_ => SaveDocAsync(), _ => DocDirty);
        RevertDocCommand = new(_ => ReloadSelectedDocAsync(), _ => SelectedDoc is not null);
        SyncDocFactsCommand = new(_ => SyncFactsAsync(SelectedProject), _ => DescribesItsOwnFolder(SelectedProject));
        SyncAllDocFactsCommand = new(_ => SyncAllFactsAsync(), _ => HasVault && _docMatches.Count > 0);
        LinkAllDocsCommand = new(_ => LinkAllAsync(), _ => HasVault && (_docMatches.Count > 0 || _docSuggestions.Count > 0));
        ChooseIndexFileCommand = new(_ => { if (_picker.PickMarkdown() is { Length: > 0 } file) IndexFile = file; });
        ClearIndexFileCommand = new(_ => IndexFile = "");
        ToggleDocEditorCommand = new(_ => DocEditorOpen = !DocEditorOpen);
        SetDocViewCommand = new(p => DocView = p?.ToString() ?? "List");
        AcceptDocSuggestionCommand = new(p => AcceptSuggestionAsync((p as ProjectDocEntry)?.Project ?? SelectedProject),
            p => SuggestionFor((p as ProjectDocEntry)?.Project ?? SelectedProject) is not null);
        SelectDocEntryCommand = new(p =>
        {
            if (p is not ProjectDocEntry entry) return;
            // Use the collection's current entry so the selected visual survives a filtered/rebuilt list.
            SelectedDocEntry = entry;
            SelectedProject = Projects.FirstOrDefault(x => x.Id == entry.Project.Id) ?? entry.Project;
        });

        InitializeReader();
        InitializeDocsTree();
        PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SelectedProject)) { LoadSelectedDoc(); _ = LoadHistoryAsync(); } };
        DocEntries.CollectionChanged += (_, _) => Raise(nameof(DocEntryCount));
    }


    public int DocEntryCount => DocEntries.Count;

    // Keep the Docs page's ListBox selection in step with the global project selection. The
    // inspector remains project-based, but this gives the card the persistent selected state that
    // makes it clear which doc is currently being inspected.
    private ProjectDocEntry? _selectedDocEntry;
    public ProjectDocEntry? SelectedDocEntry
    {
        get => _selectedDocEntry;
        set
        {
            if (!Set(ref _selectedDocEntry, value) || value is null) return;
            if (SelectedProject?.Id != value.Project.Id) SelectedProject = value.Project;
        }
    }

    private void AddDocsFolder()
    {
        if (_picker.PickFolder() is not { Length: > 0 } picked) return;
        var full = Path.GetFullPath(picked);
        if (DocsFolders.Any(f => string.Equals(Path.GetFullPath(f), full, StringComparison.OrdinalIgnoreCase)))
        { Status = "That folder is already a docs folder."; return; }
        DocsFolders.Add(full);
        SaveDocsFolders();
        OpenVault();
        Status = $"Added docs folder {Path.GetFileName(full)}";
    }

    private void RemoveDocsFolder(string? path)
    {
        if (path is null) return;
        var at = DocsFolders.ToList().FindIndex(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        if (at < 0) return;
        DocsFolders.RemoveAt(at);
        SaveDocsFolders();
        OpenVault();
        Status = "Removed docs folder";
    }

    private void OpenVault()
    {
        if (_vault is null) return;
        // A changed folder set is a fresh library — re-evaluate everything the LLM had decided.
        _aiMatches.Clear();
        _aiTried.Clear();
        _vault.Changed -= OnVaultChanged;
        _vault.Open(DocsFolders.ToArray());
        _vault.Changed += OnVaultChanged;
        Raise(nameof(HasVault)); Raise(nameof(HasDocsFolders));
        _ = LoadDocsAsync();
    }

    public bool HasDocsFolders => DocsFolders.Count > 0;

    // A single save can raise several watcher events, and an editor writing the file raises more.
    // Coalesce them, then reload once.
    private void OnVaultChanged(object? sender, string path)
    {
        if (string.Equals(path, _lastWrite.Path, StringComparison.OrdinalIgnoreCase) && DateTime.UtcNow - _lastWrite.At < TimeSpan.FromSeconds(3)) return;
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            _docsReloadPending = true;
            if (!IsUiActive) return;
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

    private bool _loadingDocs;
    public async Task LoadDocsAsync(bool external = false)
    {
        if (!IsUiActive) { _docsReloadPending = true; return; }
        if (_loadingDocs) { _docsReloadPending = true; return; }
        _docsReloadPending = false;
        if (_vault is null) return;
        _loadingDocs = true;
        try
        {
            var docs = await _vault.LoadAsync();
            var links = await _repository.GetDocLinksAsync();
            var projects = _allProjects.ToArray();
            var readmes = await Task.Run(() => projects
                .Select(p => (p.Id, Path: ProjectReadme.Find(p.Path)))
                .Where(x => x.Path is not null)
                .ToDictionary(x => x.Id, x => x.Path!));
            _readmes = readmes;
            _snapshots = new(await _repository.GetLatestSnapshotsAsync());

            _docs.Clear();
            _docs.AddRange(docs.Where(d => d.IsStateDoc));
            _docLinks.Clear();
            foreach (var link in links) _docLinks[link.ProjectId] = link;

            LoadIndex();
            MatchDocs();
            RebuildDocEntries();
            RebuildDocsTree();
            Raise(nameof(DocsSummary)); Raise(nameof(HasIndex)); Raise(nameof(IndexSummary));
            // The inspector is filled in before the vault has finished loading on startup, so the
            // selected project has to be looked at again once the docs are actually here.
            LoadSelectedDoc(fromDisk: true);
            RefreshReader();
            if (external) Status = "Docs folder changed on disk — reloaded.";
            _ = RunAiMatchingAsync();
        }
        catch (Exception e) { Status = $"Couldn't read the docs folder: {e.Message}"; }
        finally
        {
            _loadingDocs = false;
            if (_docsReloadPending && IsUiActive)
            {
                _vaultDebounce ??= CreateVaultDebounce();
                _vaultDebounce.Stop();
                _vaultDebounce.Start();
            }
        }
    }

    /// <summary>Reads the index, defaulting to a projects.md sitting beside the docs when no file has
    /// been chosen — which is where it lives in the layout this was built against.</summary>
    private void LoadIndex()
    {
        _index = null;
        _indexRows.Clear();
        var path = _indexFile;
        if (string.IsNullOrWhiteSpace(path) && _vault?.VaultPath is { } vault)
        {
            var beside = Path.Combine(vault, "projects.md");
            if (File.Exists(beside)) path = beside;
        }
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try { _index = new(ProjectIndex.Parse(File.ReadAllText(path)), _vault?.VaultPath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        foreach (var project in _allProjects)
            if (_index.For(project) is { } entry) _indexRows[project.Id] = entry;

        var sections = new[] { "All" }
            .Concat(_index.Entries.Select(e => e.Section).Where(x => x.Length > 0).Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (DocSections.SequenceEqual(sections)) return;
        DocSections.Clear();
        foreach (var section in sections) DocSections.Add(section);
        if (!sections.Contains(_docSection, StringComparer.OrdinalIgnoreCase)) { _docSection = "All"; Raise(nameof(DocSection)); }
    }

    public string ResolvedIndexFile
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_indexFile)) return _indexFile;
            var beside = _vault?.VaultPath is { } vault ? Path.Combine(vault, "projects.md") : "";
            return File.Exists(beside) ? beside : "";
        }
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
        _docSuggestions.Clear();
        var automatic = ProjectDocMatcher.Match(_allProjects.Where(p => !_docMatches.ContainsKey(p.Id)), remaining);
        foreach (var (projectId, match) in automatic)
        {
            // A near-miss on a name is a guess. Attaching the wrong doc to a project is worse than
            // leaving it undocumented, so it is offered for one click rather than applied.
            if (match.Confidence == DocMatchConfidence.Suggested) _docSuggestions[projectId] = match;
            else _docMatches[projectId] = match;
        }

        // The index states which doc belongs to which project in Mohaned's own words, so it outranks
        // matching on a name or a folder — it is the only thing that resolves a folder renamed away
        // from its doc, Follio to PrivateSchool.md or Pharma-Touch to Ordonance.md. A doc whose own
        // Location is this exact folder still wins: that is the project saying so itself.
        if (_index is null) return;
        foreach (var project in _allProjects)
        {
            if (_docLinks.GetValueOrDefault(project.Id)?.Manual == true) continue;
            if (!_indexRows.TryGetValue(project.Id, out var row) || _index.DocPathOf(row) is not { } docPath) continue;
            if (_docMatches.GetValueOrDefault(project.Id)?.Confidence >= DocMatchConfidence.ExactPath) continue;
            var doc = _docs.FirstOrDefault(d => string.Equals(d.FilePath, docPath, StringComparison.OrdinalIgnoreCase));
            if (doc is null) continue;
            _docMatches[project.Id] = new(doc, DocMatchConfidence.Index, $"the index links “{row.Name}” to this doc");
            _docSuggestions.Remove(project.Id);
        }

        // Fold in what the LLM matched, for projects the heuristics left undocumented. It never
        // overrides a heuristic match, and it cannot claim a doc another project already holds.
        foreach (var (projectId, docPath) in _aiMatches)
        {
            if (_docMatches.ContainsKey(projectId)) continue;
            if (_allProjects.All(p => p.Id != projectId)) continue;
            if (_docs.FirstOrDefault(d => string.Equals(d.FilePath, docPath, StringComparison.OrdinalIgnoreCase)) is not { } doc) continue;
            if (_docMatches.Values.Any(m => string.Equals(m.Doc.FilePath, docPath, StringComparison.OrdinalIgnoreCase))) continue;
            _docMatches[projectId] = new(doc, DocMatchConfidence.Ai, "matched by AI");
            _docSuggestions.Remove(projectId);
        }
    }

    private bool _aiMatching;
    /// <summary>Asks the LLM to place the projects the heuristics left undocumented. Runs off the load
    /// path so it never blocks the page, spends nothing when there is no key or nothing is unmatched,
    /// and never asks about the same project twice in a session.</summary>
    private async Task RunAiMatchingAsync()
    {
        if (_aiMatching || _vault is null || _docs.Count == 0) return;
        var key = string.IsNullOrWhiteSpace(_groqApiKey) ? _config.GroqApiKeyFromEnv : _groqApiKey;
        if (string.IsNullOrWhiteSpace(key)) return;
        var unmatched = _allProjects.Where(p => !_docMatches.ContainsKey(p.Id) && !_aiTried.Contains(p.Id)).ToArray();
        if (unmatched.Length == 0) return;

        _aiMatching = true;
        try
        {
            var docs = _docs.ToArray();
            var hints = BuildIndexHints(unmatched);
            var matches = await _docMatcher.MatchAsync(unmatched, docs, hints, key!);
            foreach (var project in unmatched) _aiTried.Add(project.Id);
            foreach (var (projectId, docPath) in matches) _aiMatches[projectId] = docPath;
            if (matches.Count == 0) return;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MatchDocs();
                RebuildDocEntries();
                LoadSelectedDoc();
                Raise(nameof(DocsSummary));
                Status = $"AI matched {matches.Count} doc{(matches.Count == 1 ? "" : "s")} to a project";
            });
        }
        catch { /* leave the heuristics' result in place */ }
        finally { _aiMatching = false; }
    }

    private Dictionary<Guid, string> BuildIndexHints(IEnumerable<Project> projects)
    {
        var hints = new Dictionary<Guid, string>();
        if (_index is null) return hints;
        foreach (var project in projects)
            if (_indexRows.TryGetValue(project.Id, out var row) && _index.DocPathOf(row) is { } docPath)
                hints[project.Id] = Path.GetFileName(docPath);
        return hints;
    }

    private void RebuildDocEntries()
    {
        if (!IsUiActive || CurrentPage != "Docs") return;
        var terms = _docSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var entries = _allProjects
            .Select(project => new ProjectDocEntry(project,
                _docMatches.GetValueOrDefault(project.Id)?.Doc,
                _docMatches.GetValueOrDefault(project.Id)?.Confidence ?? DocMatchConfidence.None,
                _docLinks.GetValueOrDefault(project.Id)?.Manual == true,
                _docSuggestions.GetValueOrDefault(project.Id)?.Doc,
                _indexRows.GetValueOrDefault(project.Id),
                _readmes.GetValueOrDefault(project.Id),
                _snapshots.GetValueOrDefault(project.Id)))
            .Where(Keep)
            .OrderByDescending(e => e.HasDoc)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        DocEntries.ReplaceAll(entries);
        SelectedDocEntry = entries.FirstOrDefault(entry => entry.Project.Id == SelectedProject?.Id);
        Raise(nameof(DocsSummary));

        bool Keep(ProjectDocEntry entry)
        {
            // A docs-folder filter shows the docs that live under it, so undocumented projects drop out.
            if (_selectedDocsFolder is { } docsFolder && (entry.Doc is null || !IsWithinFolder(entry.Doc.FilePath, docsFolder.Path)))
                return false;
            var passesFilter = _docFilter switch
            {
                "Undocumented" => !entry.HasDoc,
                "Documented" => entry.HasDoc,
                "All" => true,
                _ => entry.Status.Equals(_docFilter, StringComparison.OrdinalIgnoreCase),
            };
            if (!passesFilter) return false;
            if (!_docSection.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                // A project the index does not list has no section, so it cannot be in the one asked for.
                var section = _indexRows.GetValueOrDefault(entry.Project.Id)?.Section;
                if (section is null || !section.Equals(_docSection, StringComparison.OrdinalIgnoreCase)) return false;
            }
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

    /// <summary>Whether the selected project has a linked doc whose facts (Location, Stack, Last
    /// activity) can be rewritten from the repo — the doc must describe this exact folder.</summary>
    public bool CanSyncSelectedDocFacts => DescribesItsOwnFolder(SelectedProject);
    public string SelectedLinkNote => DocEntryFor(SelectedProject)?.LinkNote ?? "";

    /// <summary>The editor is folded away until asked for: the inspector is for looking at a project,
    /// and five boxes of prose is not that.</summary>
    private bool _docEditorOpen;
    public bool DocEditorOpen { get => _docEditorOpen; set => Set(ref _docEditorOpen, value); }
    public RelayCommand ToggleDocEditorCommand { get; private set; } = null!;

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

    /// <summary>Generic access to a prose section by its heading, so the field editor modal and the AI
    /// generator can work with whichever field was invoked instead of five near-identical code paths.</summary>
    private string GetDocField(string section) => section switch
    {
        ProjectDocSections.Summary => DocSummary,
        ProjectDocSections.Works => DocWorks,
        ProjectDocSections.Broken => DocBroken,
        ProjectDocSections.Next => DocNext,
        ProjectDocSections.Notes => DocNotes,
        _ => "",
    };

    private void SetDocField(string section, string value)
    {
        switch (section)
        {
            case ProjectDocSections.Summary: DocSummary = value; break;
            case ProjectDocSections.Works: DocWorks = value; break;
            case ProjectDocSections.Broken: DocBroken = value; break;
            case ProjectDocSections.Next: DocNext = value; break;
            case ProjectDocSections.Notes: DocNotes = value; break;
        }
    }

    /// <summary>The name shown for the selected project. Blanking it goes back to the folder's own
    /// name; anything else is remembered against the path and survives the folder disappearing.</summary>
    public string SelectedProjectName
    {
        get => _selectedProject?.Name ?? "";
        set
        {
            if (_selectedProject is not { } project) return;
            var wanted = value.Trim();
            var custom = wanted.Length == 0 || wanted.Equals(project.FolderName, StringComparison.Ordinal) ? null : wanted;
            if (custom == project.CustomName) return;
            var renamed = project with { Name = custom ?? project.FolderName, CustomName = custom };
            _selectedProject = renamed;
            var at = _allProjects.FindIndex(p => p.Id == renamed.Id);
            if (at >= 0) _allProjects[at] = renamed;
            _ = QueueWriteAsync(() => _repository.SetProjectNameAsync(renamed.Id, custom));
            Raise(nameof(SelectedProjectName));
            Raise(nameof(SelectedProject));
            Refresh();
            Status = custom is null ? $"Name reset to {renamed.FolderName}" : $"Renamed to {custom}";
        }
    }

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
        Raise(nameof(SelectedProjectName));
        foreach (var name in new[] { nameof(DocStatusValue), nameof(DocSummary), nameof(DocWorks), nameof(DocBroken), nameof(DocNext), nameof(DocNotes),
                                     nameof(CanSyncSelectedDocFacts), nameof(SelectedLinkNote),
                                     nameof(SelectedSuggestionName), nameof(HasSelectedSuggestion) })
            Raise(name);
        DocDirty = false;
        DocConflicted = false;
        DocEditorOpen = false;
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
        await WriteDocAsync(edited, $"Saved {Path.GetFileName(doc.FilePath)}");
    }

    private async Task WriteDocAsync(ProjectDoc doc, string successMessage)
    {
        if (_vault is null) return;
        _lastWrite = (doc.FilePath, DateTime.UtcNow);
        var result = await _vault.WriteAsync(doc);
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
            Raise(nameof(CanSyncSelectedDocFacts));
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
        await WriteDocAsync(doc, $"Created {Path.GetFileName(path)}");
        await LinkAsync(project.Id, path, manual: false);
    }

    /// <summary>Uses the vault's own folder names rather than inventing one: a Tauri project belongs
    /// in whatever folder the other Tauri docs already sit in.</summary>
    private string MatchExistingCategoryFolder(string category)
    {
        var folders = _vault?.Categories() ?? [];
        return folders.FirstOrDefault(f => ProjectDocMatcher.Normalize(f) == ProjectDocMatcher.Normalize(category)) ?? category;
    }

    /// <summary>Whether the project's doc is about that folder, rather than one above it. Only then
    /// may Puppeteer rewrite the doc's Location and Stack — a doc covering three repos must not be
    /// pointed at whichever of them happened to be selected.</summary>
    private bool DescribesItsOwnFolder(Project? project) =>
        project is not null && _docMatches.GetValueOrDefault(project.Id) is { Confidence: not DocMatchConfidence.Ancestor };

    private DocMatch? SuggestionFor(Project? project) => project is null ? null : _docSuggestions.GetValueOrDefault(project.Id);

    public string SelectedSuggestionName => SuggestionFor(SelectedProject) is { } match ? Path.GetFileName(match.Doc.FilePath) : "";
    public bool HasSelectedSuggestion => SuggestionFor(SelectedProject) is not null;

    private async Task AcceptSuggestionAsync(Project? project)
    {
        if (project is null || SuggestionFor(project) is not { } match) return;
        await LinkAsync(project.Id, match.Doc.FilePath, manual: true);
        Status = $"Linked {Path.GetFileName(match.Doc.FilePath)} to {project.Name}";
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
        if (!DescribesItsOwnFolder(project) || _docMatches.GetValueOrDefault(project!.Id)?.Doc is not { } doc) return;
        var updated = ApplyFacts(doc, project);
        if (ProjectDocFormat.Render(updated) == ProjectDocFormat.Render(doc)) { Status = $"{project.Name}'s doc already matches."; return; }
        await WriteDocAsync(updated, $"Updated {Path.GetFileName(doc.FilePath)} from the repo");
        LoadSelectedDoc();
    }

    /// <summary>Fixes every doc Puppeteer could attach but has not been told to, in one pass: each
    /// automatic match is written down as a link of its own, and each near-miss it was only willing
    /// to offer is accepted. Nothing is invented — a project with no candidate stays undocumented.</summary>
    private async Task LinkAllAsync()
    {
        var pending = _allProjects
            .Where(p => _docLinks.GetValueOrDefault(p.Id) is null)
            .Select(p => (Project: p, Match: _docMatches.GetValueOrDefault(p.Id) ?? _docSuggestions.GetValueOrDefault(p.Id)))
            .Where(x => x.Match is not null)
            .ToArray();
        if (pending.Length == 0) { Status = "Every project that has a doc is already linked to it."; return; }

        var guesses = pending.Count(x => x.Match!.Confidence == DocMatchConfidence.Suggested);
        var confirmed = await ConfirmAsync(
            "Link every project to its doc?",
            $"Link {pending.Length} project{(pending.Length == 1 ? "" : "s")} to the doc Puppeteer found for {(pending.Length == 1 ? "it" : "them")}?"
            + (guesses > 0 ? $"\n\n{guesses} of those are name near-misses rather than certain matches." : "")
            + "\n\nNo file is changed; you can unlink any of them afterwards.",
            "Link", "Cancel");
        if (!confirmed) return;

        var now = DateTimeOffset.UtcNow;
        foreach (var (project, match) in pending)
            await _repository.SetDocLinkAsync(new(project.Id, match!.Doc.FilePath, true, now));
        await LoadDocsAsync();
        LoadSelectedDoc();
        Status = $"Linked {pending.Length} project{(pending.Length == 1 ? "" : "s")} to a doc";
    }

    private async Task SyncAllFactsAsync()
    {
        var targets = _allProjects.Where(DescribesItsOwnFolder).ToArray();
        if (targets.Length == 0) return;
        if (!await ConfirmAsync(
            "Sync docs with the repos?",
            $"Update the Location, Stack and Last activity lines in {targets.Length} docs from what is on disk?\n\nProse sections are not touched.",
            "Sync facts", "Cancel")) return;

        var written = 0;
        foreach (var project in targets)
        {
            if (_docMatches.GetValueOrDefault(project.Id)?.Doc is not { } doc) continue;
            var updated = ApplyFacts(doc, project);
            if (ProjectDocFormat.Render(updated) == ProjectDocFormat.Render(doc)) continue;
            _lastWrite = (doc.FilePath, DateTime.UtcNow);
            var result = await _vault!.WriteAsync(updated);
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
        DocsFolders.Clear();
        var stored = await _repository.GetSettingAsync("DocsFolders");
        if (string.IsNullOrWhiteSpace(stored))
        {
            // Migrate the old single-folder setting into the list the first time this runs.
            if (await _repository.GetSettingAsync("DocsFolder") is { Length: > 0 } legacy) { DocsFolders.Add(legacy); SaveDocsFolders(); }
        }
        else
        {
            foreach (var folder in stored.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                DocsFolders.Add(folder);
        }
        Raise(nameof(HasDocsFolders));
        _indexFile = await _repository.GetSettingAsync("DocsIndexFile") ?? "";
        Raise(nameof(IndexFile));
        _docView = await _repository.GetSettingAsync("DocsView") ?? _docView;
        Raise(nameof(DocView));
        _docsAutoSnapshot = await _repository.GetSettingAsync("DocsAutoSnapshot") != "0";
        Raise(nameof(DocsAutoSnapshot));
        _folderPane = await _repository.GetSettingAsync("FolderPane") == DocsPane ? DocsPane : ProjectsPane;
        Raise(nameof(FolderPane)); Raise(nameof(IsProjectsPane)); Raise(nameof(IsDocsPane));
        OpenVault();
    }
}
