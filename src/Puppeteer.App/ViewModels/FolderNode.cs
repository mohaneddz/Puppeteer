using System.Collections.ObjectModel;
using Puppeteer.Core;

namespace Puppeteer.App.ViewModels;

/// <summary>A node in the sidebar folder tree. Selecting one filters the project grid to everything
/// beneath its path.</summary>
public sealed class FolderNode(string name, string path, int depth)
{
    public string Name { get; } = name;
    public string Path { get; } = path;
    public int Depth { get; } = depth;
    public int ProjectCount { get; set; }
    /// <summary>Bound two-way to the TreeViewItem so expanding a folder sticks. Roots start open —
    /// a tree that shows one collapsed row per root tells the user nothing about their projects.</summary>
    public bool IsExpanded { get; set; }
    public ObservableCollection<FolderNode> Children { get; } = [];

    public static IReadOnlyList<FolderNode> Build(IEnumerable<RootFolder> roots, IReadOnlyList<Project> projects)
    {
        var nodes = new List<FolderNode>();
        foreach (var root in roots.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase))
        {
            var owned = projects.Where(p => p.RootId == root.Id).ToArray();
            if (owned.Length == 0) continue;
            var rootNode = new FolderNode(System.IO.Path.GetFileName(root.Path.TrimEnd(System.IO.Path.DirectorySeparatorChar)) is { Length: > 0 } n ? n : root.Path, root.Path, 0)
            {
                ProjectCount = owned.Length,
                IsExpanded = true
            };
            var index = new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase) { [""] = rootNode };
            foreach (var project in owned)
            {
                var parent = rootNode;
                var accumulated = "";
                foreach (var segment in project.Hierarchy)
                {
                    accumulated = accumulated.Length == 0 ? segment : $"{accumulated}/{segment}";
                    if (!index.TryGetValue(accumulated, out var child))
                    {
                        child = new FolderNode(segment, System.IO.Path.Combine(parent.Path, segment), parent.Depth + 1);
                        parent.Children.Add(child);
                        index[accumulated] = child;
                    }
                    child.ProjectCount++;
                    parent = child;
                }
            }
            nodes.Add(rootNode);
        }
        return nodes;
    }
}
