namespace Puppeteer.Core;

/// <summary>How sure Puppeteer is that a doc describes a project. Anything below
/// <see cref="Suggested"/> is not a match; <see cref="Suggested"/> is offered to the user but never
/// linked on its own, because silently attaching the wrong doc to a project is worse than none.</summary>
public enum DocMatchConfidence { None = 0, Suggested = 1, Ai = 2, Ancestor = 3, Name = 4, FolderName = 5, Index = 6, ExactPath = 7 }

public sealed record DocMatch(ProjectDoc Doc, DocMatchConfidence Confidence, string Reason);

/// <summary>Resolves which state doc belongs to which project.
///
/// The vault's <c>Location</c> lines go stale every time a folder is moved or renamed, so an exact
/// path match is only the first of four attempts: the folder name, then the doc's own names (title,
/// file name, and anything in its <c>Aliases</c> field), then a typo-tolerant pass whose results are
/// offered rather than applied.</summary>
public static class ProjectDocMatcher
{
    public static IReadOnlyDictionary<Guid, DocMatch> Match(IEnumerable<Project> projects, IEnumerable<ProjectDoc> docs)
    {
        var candidates = docs.ToArray();
        var matches = new Dictionary<Guid, DocMatch>();
        var claimed = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (Best(project, candidates) is not { } match) continue;
            // Two unrelated projects must not both claim one doc — the stronger claim keeps it. An
            // ancestor match is the exception: a doc covering a folder that holds several repos (an
            // App and a Website half, say) genuinely describes all of them.
            if (match.Confidence > DocMatchConfidence.Ancestor && claimed.TryGetValue(match.Doc.FilePath, out var owner)
                && matches[owner].Confidence > DocMatchConfidence.Ancestor)
            {
                if (matches[owner].Confidence >= match.Confidence) continue;
                matches.Remove(owner);
            }
            matches[project.Id] = match;
            claimed[match.Doc.FilePath] = project.Id;
        }
        return matches;
    }

    public static DocMatch? Best(Project project, IReadOnlyList<ProjectDoc> docs)
    {
        DocMatch? best = null;
        foreach (var doc in docs)
        {
            var match = Score(project, doc);
            if (match.Confidence == DocMatchConfidence.None) continue;
            if (best is null || match.Confidence > best.Confidence) best = match;
        }
        return best;
    }

    private static DocMatch Score(Project project, ProjectDoc doc)
    {
        var folder = FolderName(project.Path);
        if (doc.Location is { Length: > 0 } location)
        {
            if (SamePath(location, project.Path)) return new(doc, DocMatchConfidence.ExactPath, "the doc's Location is this folder");
            if (Normalize(FolderName(location)) == Normalize(folder)) return new(doc, DocMatchConfidence.FolderName, "the doc's Location ends in the same folder name");
        }
        var names = doc.Names();
        if (names.Any(name => Normalize(name) == Normalize(project.Name))) return new(doc, DocMatchConfidence.Name, "the doc is named after this project");
        if (names.Any(name => Normalize(name) == Normalize(folder))) return new(doc, DocMatchConfidence.Name, "the doc is named after this folder");
        // Scanning finds the repos, not the folder above them: a doc for AUP describes AUP\backend
        // too, and one for Warraq covers both its App and its Website.
        if (doc.Location is { Length: > 0 } parent && IsUnder(project.Path, parent))
            return new(doc, DocMatchConfidence.Ancestor, $"this sits inside the doc's Location, {FolderName(parent)}");
        if (names.Any(name => IsUnder(project.Path, name, byFolderName: true)))
            return new(doc, DocMatchConfidence.Ancestor, $"this sits inside a folder named after the doc");
        foreach (var name in names)
            if (IsNearMiss(Normalize(name), Normalize(project.Name)))
                return new(doc, DocMatchConfidence.Suggested, $"“{name}” is close to “{project.Name}”");
        return new(doc, DocMatchConfidence.None, "");
    }

    public static bool SamePath(string left, string right)
    {
        static string Clean(string path) => path.Replace('/', '\\').TrimEnd('\\').Trim();
        return Clean(left).Equals(Clean(right), StringComparison.OrdinalIgnoreCase);
    }

    /// <param name="byFolderName">Match any segment of the project's path against
    /// <paramref name="ancestor"/> as a name, rather than treating it as a full path.</param>
    private static bool IsUnder(string projectPath, string ancestor, bool byFolderName = false)
    {
        var segments = projectPath.Replace('/', '\\').TrimEnd('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return false;
        if (byFolderName)
        {
            var name = Normalize(ancestor);
            return name.Length >= 3 && segments[..^1].Any(segment => Normalize(segment) == name);
        }
        var cleaned = ancestor.Replace('/', '\\').TrimEnd('\\').Trim();
        return cleaned.Length > 0 && projectPath.StartsWith(cleaned + "\\", StringComparison.OrdinalIgnoreCase);
    }

    public static string FolderName(string path)
    {
        var cleaned = path.Replace('/', '\\').TrimEnd('\\');
        var at = cleaned.LastIndexOf('\\');
        return at < 0 ? cleaned : cleaned[(at + 1)..];
    }

    /// <summary>Lowercases and drops everything that is not a letter or digit, so
    /// <c>Private School</c>, <c>private-school</c> and <c>PrivateSchool</c> all compare equal.</summary>
    public static string Normalize(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        foreach (var character in value)
            if (char.IsLetterOrDigit(character)) buffer[length++] = char.ToLowerInvariant(character);
        return new string(buffer[..length]);
    }

    private static bool IsNearMiss(string left, string right)
    {
        if (left.Length < 4 || right.Length < 4) return false;
        if (left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal))
            return Math.Min(left.Length, right.Length) * 10 >= Math.Max(left.Length, right.Length) * 6;
        return Distance(left, right) <= (Math.Min(left.Length, right.Length) >= 7 ? 2 : 1);
    }

    private static int Distance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++) previous[j] = j;
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }
}
