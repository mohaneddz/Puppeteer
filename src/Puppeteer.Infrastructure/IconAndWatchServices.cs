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
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".avif", ".ico", ".svg" };
    private static readonly string[] LikelyFolders = ["", "public", "assets", "src/assets", "app", "src-tauri/icons", "Resources", "Assets"];
    public Task<IReadOnlyList<IconCandidate>> FindAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var candidates = new Dictionary<string, IconCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in LikelyFolders)
        {
            var folder = Path.Combine(projectPath, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(folder)) continue;
            try
            {
                foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly).Where(x => Extensions.Contains(Path.GetExtension(x))))
                { cancellationToken.ThrowIfCancellationRequested(); var (width, height) = ReadPngSize(path); candidates[path] = new(path, width, height, IconCandidateRanker.Score(path, width, height)); }
            }
            catch (UnauthorizedAccessException) { }
        }
        return Task.FromResult<IReadOnlyList<IconCandidate>>(candidates.Values.OrderByDescending(x => x.Score).Take(30).ToArray());
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
