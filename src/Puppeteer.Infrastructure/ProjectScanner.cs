using Microsoft.Extensions.Logging;
using Puppeteer.Core;

namespace Puppeteer.Infrastructure;

public sealed class ProjectScanner(IProjectDetector detector, IGitMetadataService git, ILogger<ProjectScanner> logger) : IProjectScanner
{
    public async Task<IReadOnlyList<Project>> ScanAsync(RootFolder root, CancellationToken cancellationToken = default)
    {
        var projects = new List<Project>();
        if (!Directory.Exists(root.Path)) return projects;
        var pending = new Stack<string>();
        pending.Push(root.Path);

        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProjectDetectionResult? detection = null;
            try { detection = await detector.DetectAsync(directory, cancellationToken); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { logger.LogDebug(exception, "Could not inspect {Directory}", directory); }

            if (detection is not null)
            {
                var normalized = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
                var id = DeterministicGuid(normalized);
                projects.Add(new(id, Path.GetFileName(normalized), normalized, root.Id, detection.PrimaryTechnology,
                    detection.Technologies, HierarchyInferer.Infer(root.Path, normalized), detection.Presets,
                    CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow,
                    Git: await git.GetStatusAsync(normalized, cancellationToken),
                    Category: ProjectCategoryRules.FromPath(normalized)));
                continue;
            }

            try
            {
                foreach (var child in Directory.EnumerateDirectories(directory))
                    if (!ProjectPathRules.IsIgnoredDirectory(child)) pending.Push(child);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { logger.LogDebug(exception, "Could not enumerate {Directory}", directory); }
        }
        return projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static Guid DeterministicGuid(string value)
    {
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(value.ToUpperInvariant()));
        return new Guid(hash);
    }
}
