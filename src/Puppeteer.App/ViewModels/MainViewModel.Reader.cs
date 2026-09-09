using System.Collections.ObjectModel;
using Puppeteer.Core;

namespace Puppeteer.App.ViewModels;

/// <summary>Reading, as opposed to editing: the project's state doc, its own README, and the index
/// that maps the library. All three are markdown and all three are opened read-only — the reader
/// never writes, so opening the wrong one costs nothing.</summary>
public sealed partial class MainViewModel
{
    public const string ReaderNotes = ProjectDocSections.Notes;
    public const string ReaderReadme = "README";
    public const string ReaderIndex = "Index";

    public ObservableCollection<string> ReaderTabs { get; } = [];
    public IReadOnlyList<string> ReaderNoteSections => ProjectDocSections.Standard;

    private bool _readerOpen;
    public bool ReaderOpen { get => _readerOpen; set => Set(ref _readerOpen, value); }

    private Project? _readerProject;
    private string _readerTab = ReaderNotes;
    public string ReaderTab
    {
        get => _readerTab;
        set
        {
            if (!Set(ref _readerTab, value)) return;
            Raise(nameof(IsReaderNotes));
            LoadReader();
        }
    }
    public bool IsReaderNotes => ReaderTab == ReaderNotes;

    private string _readerNoteSection = ProjectDocSections.Summary;
    public string ReaderNoteSection
    {
        get => _readerNoteSection;
        set { if (Set(ref _readerNoteSection, value)) LoadReader(); }
    }

    private string _readerText = "";
    public string ReaderText { get => _readerText; private set => Set(ref _readerText, value); }

    private string _readerPath = "";
    public string ReaderPath
    {
        get => _readerPath;
        private set { if (Set(ref _readerPath, value)) { Raise(nameof(HasReaderFile)); Raise(nameof(ReaderBasePath)); } }
    }
    public bool HasReaderFile => _readerPath.Length > 0;
    public string ReaderBasePath => _readerPath.Length > 0 ? Path.GetDirectoryName(_readerPath) ?? "" : "";

    public string ReaderTitle => _readerProject?.Name ?? "Projects index";
    public string ReaderSubtitle => _readerPath.Length > 0 ? _readerPath : "Nothing to read here yet.";

    public RelayCommand OpenReaderCommand { get; private set; } = null!;
    public RelayCommand OpenIndexReaderCommand { get; private set; } = null!;
    public RelayCommand CloseReaderCommand { get; private set; } = null!;
    public RelayCommand SetReaderTabCommand { get; private set; } = null!;
    public RelayCommand SetReaderNoteSectionCommand { get; private set; } = null!;
    public RelayCommand OpenReaderFileCommand { get; private set; } = null!;

    private void InitializeReader()
    {
        OpenReaderCommand = new(p => OpenReader((p as ProjectDocEntry)?.Project ?? p as Project ?? SelectedProject));
        OpenIndexReaderCommand = new(_ => OpenReader(null, ReaderIndex), _ => ResolvedIndexFile.Length > 0);
        CloseReaderCommand = new(_ => ReaderOpen = false);
        SetReaderTabCommand = new(p => { if (p?.ToString() is { Length: > 0 } tab) ReaderTab = tab; });
        SetReaderNoteSectionCommand = new(p => { if (p?.ToString() is { Length: > 0 } section) ReaderNoteSection = section; });
        OpenReaderFileCommand = new(_ => OpenReaderFile(), _ => HasReaderFile);
    }

    private void OpenReader(Project? project, string? tab = null)
    {
        _readerProject = project;
        RebuildReaderTabs();
        // Land on whatever this project actually has, rather than an empty tab.
        _readerTab = tab is { Length: > 0 } wanted && ReaderTabs.Contains(wanted) ? wanted
            : ReaderTabs.FirstOrDefault() ?? ReaderNotes;
        Raise(nameof(ReaderTab));
        Raise(nameof(IsReaderNotes));
        Raise(nameof(ReaderTitle));
        LoadReader();
        ReaderOpen = true;
        if (project is not null) SelectedProject = Projects.FirstOrDefault(p => p.Id == project.Id) ?? project;
    }

    private void RebuildReaderTabs()
    {
        var tabs = new List<string>();
        if (_readerProject is { } project)
        {
            tabs.Add(ReaderNotes);
            if (ReadmeOf(project) is not null) tabs.Add(ReaderReadme);
        }
        if (ResolvedIndexFile.Length > 0) tabs.Add(ReaderIndex);
        if (tabs.Count == 0) tabs.Add(ReaderNotes);

        if (ReaderTabs.SequenceEqual(tabs)) return;
        ReaderTabs.Clear();
        foreach (var tab in tabs) ReaderTabs.Add(tab);
    }

    private void LoadReader()
    {
        var path = _readerTab switch
        {
            ReaderReadme => _readerProject is { } project ? ReadmeOf(project) : null,
            ReaderIndex => ResolvedIndexFile is { Length: > 0 } index ? index : null,
            _ => _readerProject is { } project && _docMatches.TryGetValue(project.Id, out var match) ? match.Doc.FilePath : null,
        };
        ReaderPath = path ?? "";
        ReaderText = path is null
            ? Missing()
            : IsReaderNotes && _readerProject is { } selected && _docMatches.TryGetValue(selected.Id, out var doc)
                ? ReadSection(doc.Doc, ReaderNoteSection)
                : Read(path);
        Raise(nameof(ReaderSubtitle));
    }

    private string Missing() => _readerTab switch
    {
        ReaderReadme => $"*{_readerProject?.Name ?? "This project"} has no README in its root folder.*",
        ReaderIndex => "*No index file is connected. Set one in Settings → Project docs.*",
        _ => "*No state doc describes this project yet.*",
    };

    private static string ReadSection(ProjectDoc doc, string section) =>
        doc.Section(section) is { Length: > 0 } text
            ? text
            : $"*No {section.ToLowerInvariant()} has been written yet.*";

    private static string Read(string path)
    {
        try { return File.ReadAllText(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return $"*Couldn't read the file: {e.Message}*"; }
    }

    private string? ReadmeOf(Project project) => _readmes.GetValueOrDefault(project.Id) ?? ProjectReadme.Find(project.Path);

    private void OpenReaderFile()
    {
        if (!HasReaderFile) return;
        try { _launcher.OpenInIde(_readerPath, _ideCommand); Status = $"Opened {Path.GetFileName(_readerPath)}"; }
        catch (Exception e) { Status = $"Couldn't open the file: {e.Message}"; }
    }

    /// <summary>Keeps an open reader in step when the docs reload underneath it.</summary>
    private void RefreshReader()
    {
        if (!ReaderOpen) return;
        RebuildReaderTabs();
        if (!ReaderTabs.Contains(_readerTab))
        {
            _readerTab = ReaderTabs.FirstOrDefault() ?? ReaderNotes;
            Raise(nameof(ReaderTab));
            Raise(nameof(IsReaderNotes));
        }
        LoadReader();
    }
}
