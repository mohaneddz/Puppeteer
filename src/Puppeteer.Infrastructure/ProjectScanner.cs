using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Puppeteer.Core;

namespace Puppeteer.Infrastructure;

public sealed class ProjectScanner(IProjectDetector detector, ILogger<ProjectScanner> logger) : IProjectScanner
{
    /// <summary>
    /// Walks a root breadth-first, detecting one level at a time in parallel. Detection has to gate
    /// the descent — a directory that <i>is</i> a project is not searched further — so the levels are
    /// inherently sequential, but everything within a level is independent filesystem work.
    /// </summary>
    public async Task<IReadOnlyList<Project>> ScanAsync(RootFolder root, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(root.Path)) return [];
        var projects = new ConcurrentBag<Project>();
        var frontier = new List<string> { root.Path };
        var options = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount };

        while (frontier.Count > 0)
        {
            var next = new ConcurrentBag<string>();
            await Parallel.ForEachAsync(frontier, options, async (directory, token) =>
            {
                ProjectDetectionResult? detection = null;
                try { detection = await detector.DetectAsync(directory, token); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { logger.LogDebug(exception, "Could not inspect {Directory}", directory); }

                if (detection is not null) { projects.Add(Describe(root, directory, detection)); return; }

                try
                {
                    foreach (var child in Directory.EnumerateDirectories(directory))
                        if (!ProjectPathRules.IsIgnoredDirectory(child)) next.Add(child);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { logger.LogDebug(exception, "Could not enumerate {Directory}", directory); }
            });
            frontier = [.. next];
        }
        return projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // Git status is deliberately not read here: it costs a child process per project and would be
    // stale by the time anyone looked at it. The app refreshes it in the background after loading.
    private static Project Describe(RootFolder root, string directory, ProjectDetectionResult detection)
    {
        var normalized = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        return new(DeterministicGuid(normalized), Path.GetFileName(normalized), normalized, root.Id,
            detection.PrimaryTechnology, detection.Technologies, HierarchyInferer.Infer(root.Path, normalized), detection.Presets,
            CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow,
            Category: ProjectCategoryRules.FromPath(normalized));
    }

    private static Guid DeterministicGuid(string value)
    {
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(value.ToUpperInvariant()));
        return new Guid(hash);
    }
}
