using Puppeteer.Core;

namespace Puppeteer.Infrastructure;

public sealed class ProjectIconProvider : IProjectIconProvider
{
    public string GetDefaultIconKey(string primaryTechnology) => primaryTechnology.ToLowerInvariant() switch
    {
        ".net" => "dotnet", "next.js" => "nextjs", "node.js" => "nodejs", _ => primaryTechnology.ToLowerInvariant().Replace(" ", "-")
    };
}

public sealed class IconDiscoveryService : IIconDiscoveryService
{
    private const int MaxCandidates = 24;
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".avif", ".ico", ".svg" };
    private static readonly HashSet<string> IgnoredFolders = new(StringComparer.OrdinalIgnoreCase) { ".git", "node_modules", "bin", "obj", ".next", ".nuxt", "coverage", "vendor" };
    public Task<IReadOnlyList<IconCandidate>> FindAsync(string projectPath, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var candidates = new Dictionary<string, IconCandidate>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in EnumerateImageFiles(projectPath, cancellationToken))
            {
                try
                {
                    var info = new FileInfo(path);
                    if (info.Length > 8 * 1024 * 1024) continue;
                    var (width, height) = ReadPngSize(path);
                    candidates[path] = new(path, width, height, IconCandidateRanker.Score(path, width, height));
                }
                catch (IOException) { }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
        // Candidates are rendered as WPF images. Keeping this list focused avoids synchronously
        // decoding dozens of large screenshots and mockups when a project contains design assets.
        return (IReadOnlyList<IconCandidate>)candidates.Values.OrderByDescending(x => x.Score).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).Take(MaxCandidates).ToArray();
    }, cancellationToken);

    private static IEnumerable<string> EnumerateImageFiles(string folder, CancellationToken cancellationToken)
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((folder, 0));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (current, depth) = pending.Pop();
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(current); }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }
            foreach (var file in files)
                if (Extensions.Contains(Path.GetExtension(file))) yield return file;
            if (depth >= 3) continue;
            IEnumerable<string> directories;
            try { directories = Directory.EnumerateDirectories(current); }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }
            foreach (var directory in directories)
                if (!IgnoredFolders.Contains(Path.GetFileName(directory))) pending.Push((directory, depth + 1));
        }
    }
    private static (int Width, int Height) ReadPngSize(string path)
    {
        if (!Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)) return (0, 0);
        try { using var stream = File.OpenRead(path); Span<byte> bytes = stackalloc byte[24]; if (stream.Read(bytes) == 24) return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes[16..20]), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes[20..24])); }
        catch (IOException) { }
        return (0, 0);
    }
}

public sealed class FileSystemWatchService : IFileSystemWatchService
{
    private readonly Dictionary<Guid, FileSystemWatcher> _watchers = [];
    public event EventHandler<string>? ProjectFilesChanged;
    public void Watch(RootFolder root)
    {
        Stop(root.Id); if (!Directory.Exists(root.Path)) return;
        var watcher = new FileSystemWatcher(root.Path) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite };
        FileSystemEventHandler changed = (_, e) => { if (!e.FullPath.Split(Path.DirectorySeparatorChar).Any(ProjectPathRules.IsIgnoredDirectory)) ProjectFilesChanged?.Invoke(this, e.FullPath); };
        watcher.Created += changed; watcher.Deleted += changed; watcher.Changed += changed; watcher.Renamed += (_, e) => changed(_, e); watcher.EnableRaisingEvents = true; _watchers[root.Id] = watcher;
    }
    public void Stop(Guid rootId) { if (_watchers.Remove(rootId, out var watcher)) watcher.Dispose(); }
    public void Dispose() { foreach (var watcher in _watchers.Values) watcher.Dispose(); _watchers.Clear(); }
}
