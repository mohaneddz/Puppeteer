namespace Puppeteer.Core;

public static class ProjectPathRules
{
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", ".next", "dist", "build", "target", "bin", "obj",
        ".venv", "venv", ".idea", ".vs", ".dart_tool", "coverage"
    };

    public static bool IsIgnoredDirectory(string path) => Ignored.Contains(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)));
}

public static class HierarchyInferer
{
    public static IReadOnlyList<string> Infer(string rootPath, string projectPath)
    {
        var relative = Path.GetRelativePath(rootPath, projectPath);
        var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        return segments.Length <= 1 ? [] : segments[..^1];
    }
}

/// <summary>Maps a project's detected stack to a coarse product <b>type</b> (what it builds).</summary>
public static class ProjectTypeRules
{
    private static readonly (string Type, string[] Technologies)[] Map =
    [
        ("Desktop", ["Tauri", ".NET", "WPF", "Qt", "Electron", "C++"]),
        ("Mobile", ["Flutter", "Android", "iOS", "React Native", "Kotlin", "Swift"]),
        ("Website", ["Next.js", "Vite", "Vue", "React", "Node.js", "Svelte", "Astro", "TypeScript"]),
        ("Game", ["Godot", "Unity"]),
    ];

    public static readonly IReadOnlyList<string> Known = ["Desktop", "Mobile", "Website", "Game", "Other"];

    public static string Of(Project project)
    {
        foreach (var (type, technologies) in Map)
            if (technologies.Contains(project.PrimaryTechnology, StringComparer.OrdinalIgnoreCase)
                || project.Technologies.Any(t => technologies.Contains(t, StringComparer.OrdinalIgnoreCase)))
                return type;
        return "Other";
    }
}

/// <summary>Infers a project's <b>category</b> (its purpose — personal, client work, a hackathon,
/// coursework…) from its folder path. This is the fast, offline classifier; an optional LLM pass can
/// refine the ones this can't place.</summary>
public static class ProjectCategoryRules
{
    public static readonly IReadOnlyList<string> Known = ["Personal", "Client", "Hackathon", "Course", "Work", "Other"];

    private static readonly (string Category, string[] Keywords)[] Map =
    [
        ("Client", ["client", "clients", "freelance", "contract"]),
        ("Hackathon", ["hackathon", "hackathons", "jam", "gamejam"]),
        ("Course", ["school", "course", "courses", "university", "college", "uni", "tutorial", "learning", "study"]),
        ("Work", ["work", "job", "company", "office"]),
        ("Personal", ["personal", "hobby", "side", "projects"]),
    ];

    /// <summary>Returns a category if a path segment clearly names one, otherwise null (leave it to the LLM / Other).</summary>
    public static string? FromPath(string path)
    {
        var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var (category, keywords) in Map)
            if (segments.Any(s => keywords.Contains(s, StringComparer.OrdinalIgnoreCase)))
                return category;
        return null;
    }
}

public sealed class ProjectSearchService
{
    public IReadOnlyList<Project> Filter(IEnumerable<Project> projects, string? query,
        string? type = null, string? technology = null, string? category = null, IReadOnlySet<Guid>? runningProjectIds = null)
    {
        var terms = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return projects.Where(project =>
        {
            if (IsSet(type) && !ProjectTypeRules.Of(project).Equals(type, StringComparison.OrdinalIgnoreCase)) return false;
            if (IsSet(technology) && !project.Technologies.Any(t => t.Equals(technology, StringComparison.OrdinalIgnoreCase))) return false;
            if (IsSet(category) && !string.Equals(project.Category ?? "Other", category, StringComparison.OrdinalIgnoreCase)) return false;

            var searchable = string.Join(' ', project.Name, project.Path, project.PrimaryTechnology, project.Category ?? "",
                string.Join(' ', project.Technologies), string.Join(' ', project.Hierarchy));
            return terms.All(term => term.Equals("running", StringComparison.OrdinalIgnoreCase)
                ? runningProjectIds?.Contains(project.Id) == true
                : searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
        }).ToArray();
    }

    private static bool IsSet(string? value) => !string.IsNullOrWhiteSpace(value) && !value.Equals("All", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> WithAll(IEnumerable<string> values) => new[] { "All" }.Concat(values).ToArray();

    public IReadOnlyList<string> BuildTypes(IEnumerable<Project> projects)
    {
        var present = projects.Select(ProjectTypeRules.Of).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return WithAll(ProjectTypeRules.Known.Where(present.Contains));
    }

    public IReadOnlyList<string> BuildTechnologies(IEnumerable<Project> projects) =>
        WithAll(projects.SelectMany(p => p.Technologies).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase));

    public IReadOnlyList<string> BuildCategories(IEnumerable<Project> projects)
    {
        var present = projects.Select(p => p.Category ?? "Other").ToHashSet(StringComparer.OrdinalIgnoreCase);
        return WithAll(ProjectCategoryRules.Known.Where(present.Contains));
    }
}

public static class IconCandidateRanker
{
    private static readonly string[] PreferredNames = ["icon", "logo", "app-icon", "app_icon", "appicon", "favicon", "apple-touch-icon", "brand", "mark"];

    public static int Score(string path, int width, int height)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var preferredIndex = Array.FindIndex(PreferredNames, x => name.Equals(x, StringComparison.OrdinalIgnoreCase));
        var score = preferredIndex >= 0 ? 200 - preferredIndex * 10 : 0;
        if (name.StartsWith("favicon", StringComparison.OrdinalIgnoreCase) || name.StartsWith("apple-touch-icon", StringComparison.OrdinalIgnoreCase)) score = Math.Max(score, 180);
        if (name.Contains("icon", StringComparison.OrdinalIgnoreCase) || name.Contains("logo", StringComparison.OrdinalIgnoreCase)) score = Math.Max(score, 140);
        if (path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part.Equals("icons", StringComparison.OrdinalIgnoreCase) || part.Equals("assets", StringComparison.OrdinalIgnoreCase))) score += 25;
        if (name.Contains("screenshot", StringComparison.OrdinalIgnoreCase) || name.Contains("banner", StringComparison.OrdinalIgnoreCase) || name.Contains("hero", StringComparison.OrdinalIgnoreCase)) score -= 80;
        if (width > 0 && height > 0)
        {
            var ratio = (double)Math.Min(width, height) / Math.Max(width, height);
            score += (int)(ratio * 100);
            if (width is >= 64 and <= 1024 && height is >= 64 and <= 1024) score += 30;
        }
        var normalized = path.Replace('\\', '/');
        if (normalized.Contains("/src-tauri/icons/", StringComparison.OrdinalIgnoreCase)) score += 40;
        if (normalized.Contains("/assets/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/public/", StringComparison.OrdinalIgnoreCase)) score += 20;
        return score;
    }
}
