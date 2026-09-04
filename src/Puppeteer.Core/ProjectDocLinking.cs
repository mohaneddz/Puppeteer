namespace Puppeteer.Core;

/// <summary>How sure Puppeteer is that a doc describes a project. Anything below
/// <see cref="Suggested"/> is not a match; <see cref="Suggested"/> is offered to the user but never
/// linked on its own, because silently attaching the wrong doc to a project is worse than none.</summary>
public enum DocMatchConfidence { None = 0, Suggested = 1, Name = 2, FolderName = 3, ExactPath = 4 }

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
            // Two projects can share a folder name (an App and a Website half, say). The stronger
            // claim keeps the doc; the weaker one is left undocumented rather than double-linked.
            if (claimed.TryGetValue(match.Doc.FilePath, out var owner))
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

/// <summary>Every way a project's doc can disagree with the project itself. These are reported, never
/// fixed silently — the doc holds Mohaned's own judgement and the repo holds the facts, and only some
/// of the differences are the doc being wrong.</summary>
[Flags]
public enum DocDrift
{
    None = 0,
    NoDoc = 1 << 0,
    StaleLocation = 1 << 1,
    StaleStack = 1 << 2,
    StaleActivity = 1 << 3,
    NoStatus = 1 << 4,
    EmptySections = 1 << 5,
    Uncommitted = 1 << 6,
    Unpushed = 1 << 7,
    OffMainBranch = 1 << 8,
}

public sealed record DocDriftReport(Guid ProjectId, DocDrift Drift, IReadOnlyList<string> Reasons)
{
    public bool Any => Drift != DocDrift.None;
    /// <summary>Drift the doc itself can fix, as opposed to facts about the repo's current state.</summary>
    public bool NeedsWriteup => (Drift & (DocDrift.NoDoc | DocDrift.StaleLocation | DocDrift.StaleStack | DocDrift.StaleActivity | DocDrift.NoStatus | DocDrift.EmptySections)) != 0;
}

public static class ProjectDocDrift
{
    public static DocDriftReport Compare(Project project, ProjectDoc? doc)
    {
        var drift = DocDrift.None;
        var reasons = new List<string>();
        void Flag(DocDrift kind, string reason) { drift |= kind; reasons.Add(reason); }

        if (doc is null) Flag(DocDrift.NoDoc, "No state doc in the vault");
        else
        {
            if (doc.Location is { Length: > 0 } location && !ProjectDocMatcher.SamePath(location, project.Path))
                Flag(DocDrift.StaleLocation, $"Doc still points at {location}");
            if (doc.Stack is { Length: > 0 } stack && !project.Technologies.Any(t => stack.Contains(t, StringComparison.OrdinalIgnoreCase)))
                Flag(DocDrift.StaleStack, $"Doc's stack mentions none of {string.Join(", ", project.Technologies)}");
            if (ProjectDocStatuses.Parse(doc.Status) is null)
                Flag(DocDrift.NoStatus, "Doc has no Status field");
            var empty = ProjectDocSections.Standard.Where(s => string.IsNullOrWhiteSpace(doc.Section(s))).ToArray();
            if (empty.Length > 0)
                Flag(DocDrift.EmptySections, $"Empty: {string.Join(", ", empty)}");
            if (project.Git?.LastCommitAt is { } committed && committed > doc.ModifiedAt)
                Flag(DocDrift.StaleActivity, $"Newer commits than the doc ({committed:yyyy-MM-dd})");
        }

        if (project.Git is { } git)
        {
            if (git.ModifiedFileCount > 0)
                Flag(DocDrift.Uncommitted, $"{git.ModifiedFileCount} uncommitted file{(git.ModifiedFileCount == 1 ? "" : "s")}");
            if (git.Ahead > 0)
                Flag(DocDrift.Unpushed, $"{git.Ahead} unpushed commit{(git.Ahead == 1 ? "" : "s")}");
            if (git.Branch is { Length: > 0 } branch && branch is not ("main" or "master" or "unknown"))
                Flag(DocDrift.OffMainBranch, $"On branch {branch}");
        }

        return new(project.Id, drift, reasons);
    }
}
