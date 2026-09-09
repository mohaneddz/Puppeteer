using System.Collections.ObjectModel;
using Puppeteer.Core;

namespace Puppeteer.App.ViewModels;

/// <summary>The sidebar's Documentation half. The FOLDERS panel toggles between the project roots and
/// the docs folders; the docs tree mirrors the folder layout of the loaded docs, and picking a node
/// filters the Docs page to that folder.</summary>
public sealed partial class MainViewModel
{
    public const string ProjectsPane = "Projects";
    public const string DocsPane = "Documentation";

    private string _folderPane = ProjectsPane;
    /// <summary>Which tree the FOLDERS panel shows. Saved, so the sidebar comes back the way it was.</summary>
    public string FolderPane
    {
        get => _folderPane;
        set
        {
            if (!Set(ref _folderPane, value)) return;
            SavePref("FolderPane", value);
            Raise(nameof(IsProjectsPane));
            Raise(nameof(IsDocsPane));
        }
    }
    public bool IsProjectsPane => _folderPane == ProjectsPane;
    public bool IsDocsPane => _folderPane == DocsPane;
    public RelayCommand SetFolderPaneCommand { get; private set; } = null!;

    public ObservableCollection<FolderNode> DocsFolderTree { get; } = [];

    private FolderNode? _selectedDocsFolder;
    /// <summary>The docs folder node the page is filtered to, if any.</summary>
    public FolderNode? SelectedDocsFolder
    {
        get => _selectedDocsFolder;
        set { if (Set(ref _selectedDocsFolder, value)) { Raise(nameof(DocsFolderFilterActive)); RebuildDocEntries(); } }
    }
    public bool DocsFolderFilterActive => _selectedDocsFolder is not null;

    public RelayCommand SelectDocsFolderCommand { get; private set; } = null!;
    public RelayCommand ClearDocsFolderFilterCommand { get; private set; } = null!;

    private void InitializeDocsTree()
    {
        SetFolderPaneCommand = new(p => FolderPane = p?.ToString() ?? ProjectsPane);
        SelectDocsFolderCommand = new(p => { SelectedDocsFolder = p as FolderNode; if (p is FolderNode) CurrentPage = "Docs"; });
        ClearDocsFolderFilterCommand = new(_ => SelectedDocsFolder = null);
    }

    /// <summary>Rebuilds the docs tree from the folders on disk and the docs loaded from them — one
    /// node per docs folder, then a node per category subfolder that holds a doc.</summary>
    private void RebuildDocsTree()
    {
        DocsFolderTree.Clear();
        foreach (var folder in DocsFolders)
        {
            if (!Directory.Exists(folder)) continue;
            var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } n ? n : folder;
            var root = new FolderNode(name, folder, 0) { IsExpanded = true };
            var index = new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase) { [""] = root };
            foreach (var doc in _docs.Where(d => IsWithinFolder(d.FilePath, folder)))
            {
                root.ProjectCount++;
                var relative = Path.GetDirectoryName(Path.GetRelativePath(folder, doc.FilePath)) ?? "";
                var parent = root;
                var accumulated = "";
                foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
                {
                    accumulated = accumulated.Length == 0 ? segment : $"{accumulated}/{segment}";
                    if (!index.TryGetValue(accumulated, out var child))
                    {
                        child = new FolderNode(segment, Path.Combine(parent.Path, segment), parent.Depth + 1);
                        parent.Children.Add(child);
                        index[accumulated] = child;
                    }
                    child.ProjectCount++;
                    parent = child;
                }
            }
            DocsFolderTree.Add(root);
        }
        // A folder the filter pointed at can vanish when folders are removed; drop the stale filter.
        if (_selectedDocsFolder is { } selected && DocsFolders.All(f => !IsWithinFolder(selected.Path, f) && !string.Equals(f, selected.Path, StringComparison.OrdinalIgnoreCase)))
            SelectedDocsFolder = null;
    }
}
