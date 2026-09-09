using System.Text;
using Puppeteer.Core;

namespace Puppeteer.Infrastructure;

/// <summary>The folder of markdown state docs, treated as shared and externally edited.
///
/// Nothing is cached across a read: the vault is a folder in a synced/edited tree, and the app is
/// only one of the things writing to it. Writes go to a sibling temporary file and then replace the
/// original, so a crash mid-save cannot leave a half-written doc, and a write is refused outright
/// when the file changed since the caller last read it.</summary>
public sealed class ProjectDocVault : IProjectDocVault
{
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase) { ".git", ".obsidian", ".trash", "node_modules" };
    /// <summary>Index and readme files map a vault, they do not describe one project.</summary>
    private static readonly HashSet<string> SkippedFiles = new(StringComparer.OrdinalIgnoreCase) { "projects", "index", "readme" };

    private readonly List<FileSystemWatcher> _watchers = [];
    public IReadOnlyList<string> VaultPaths { get; private set; } = [];
    public string? VaultPath => VaultPaths.Count > 0 ? VaultPaths[0] : null;
    public event EventHandler<string>? Changed;

    public void Open(IReadOnlyList<string> vaultPaths)
    {
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
        // Keep only real, distinct folders, and drop any that sits inside another so its docs are not
        // loaded and watched twice.
        var folders = (vaultPaths ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        VaultPaths = folders
            .Where(path => !folders.Any(other => !ReferenceEquals(other, path) && IsUnder(path, other)))
            .ToArray();
        foreach (var path in VaultPaths)
        {
            var watcher = new FileSystemWatcher(path, "*.md") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
            FileSystemEventHandler changed = (_, e) => { if (IsDoc(e.FullPath)) Changed?.Invoke(this, e.FullPath); };
            watcher.Created += changed; watcher.Deleted += changed; watcher.Changed += changed;
            watcher.Renamed += (_, e) => { if (IsDoc(e.FullPath)) Changed?.Invoke(this, e.FullPath); };
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    private static bool IsUnder(string path, string ancestor)
    {
        var relative = Path.GetRelativePath(ancestor, path);
        return relative != "." && !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    public Task<IReadOnlyList<ProjectDoc>> LoadAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var docs = new List<ProjectDoc>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var vault in VaultPaths)
            foreach (var file in EnumerateDocs(vault, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (seen.Add(file) && ReadDoc(file) is { } doc) docs.Add(doc);
            }
        return (IReadOnlyList<ProjectDoc>)docs;
    }, cancellationToken);

    public Task<ProjectDoc?> ReadAsync(string docPath, CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadDoc(docPath), cancellationToken);

    public Task<DocWriteResult> WriteAsync(ProjectDoc doc, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        if (VaultPath is null) return new DocWriteResult(DocWriteOutcome.NoVault, null, "No docs folder is set.");
        try
        {
            var existed = File.Exists(doc.FilePath);
            // Compare the text, not the timestamp. A modified time has to be given slack for
            // filesystem rounding, and any slack is a window in which an outside edit is lost.
            if (existed && Current(doc.FilePath) != doc.Raw)
                return new DocWriteResult(DocWriteOutcome.Conflict, ReadDoc(doc.FilePath),
                    doc.Raw.Length == 0 ? "A doc already exists at that path." : "The doc changed on disk since it was opened.");

            Directory.CreateDirectory(Path.GetDirectoryName(doc.FilePath)!);
            var text = ProjectDocFormat.Render(doc);
            var temporary = doc.FilePath + TemporarySuffix;
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            if (existed) File.Replace(temporary, doc.FilePath, null);
            else File.Move(temporary, doc.FilePath);
            return new DocWriteResult(DocWriteOutcome.Written, ReadDoc(doc.FilePath), null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new DocWriteResult(DocWriteOutcome.Failed, null, e.Message);
        }
    }, cancellationToken);

    public string PathFor(string category, string projectName)
    {
        if (VaultPath is null) return "";
        var folder = string.IsNullOrWhiteSpace(category) ? VaultPath : Path.Combine(VaultPath, Sanitize(category));
        return Path.Combine(folder, Sanitize(projectName) + ".md");
    }

    public IReadOnlyList<string> Categories()
    {
        if (VaultPath is null) return [];
        try
        {
            return Directory.EnumerateDirectories(VaultPath)
                .Select(Path.GetFileName)
                .Where(name => name is { Length: > 0 } && !name.StartsWith('.') && !SkippedFolders.Contains(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray()!;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    public void Dispose() { foreach (var watcher in _watchers) watcher.Dispose(); _watchers.Clear(); }

    /// <summary>A watcher filtered to "*.md" still reports Windows' replace temporaries, which are
    /// named after the file they are replacing — Hive.md~RF3a1.TMP — and are not docs.</summary>
    private static bool IsDoc(string path) =>
        Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(TemporarySuffix, StringComparison.OrdinalIgnoreCase);

    private const string TemporarySuffix = ".puppeteer.tmp";

    private static ProjectDoc? ReadDoc(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return ProjectDocFormat.Parse(path, File.ReadAllText(path), Modified(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private static DateTimeOffset Modified(string path) => new FileInfo(path).LastWriteTimeUtc;

    private static string Current(string path)
    {
        try { return File.ReadAllText(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    private static IEnumerable<string> EnumerateDocs(string root, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(current, "*.md"); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var file in files)
                if (!SkippedFiles.Contains(Path.GetFileNameWithoutExtension(file))) yield return file;
            IEnumerable<string> folders;
            try { folders = Directory.EnumerateDirectories(current); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var folder in folders)
            {
                var name = Path.GetFileName(folder);
                if (!name.StartsWith('.') && !SkippedFolders.Contains(name)) pending.Push(folder);
            }
        }
    }

    private static string Sanitize(string name)
    {
        var cleaned = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? ' ' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "Untitled" : cleaned;
    }
}
