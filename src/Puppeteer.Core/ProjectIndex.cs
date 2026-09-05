using System.Text;

namespace Puppeteer.Core;

/// <summary>One row of the index: the project, the doc it links to, where it lives, and whatever the
/// row was marked with. <see cref="Marker"/> is the raw bracketed label as written — <c>ARCHIVED</c>,
/// <c>Container</c>, <c>NEW, undocumented</c> — because not all of them are lifecycle statuses.</summary>
public sealed record ProjectIndexEntry(
    string Name,
    string? DocPath,
    IReadOnlyList<string> Locations,
    string Client,
    string Summary,
    string? Marker,
    string? Status,
    string Section);

/// <summary>Reads the single markdown file that maps the whole library — the one that sits beside the
/// per-project docs and links to them.
///
/// It is prose with tables in it, not data, so the parser only takes what is unambiguous: the
/// <c>## Section</c> it is under, the bracketed marker at the head of a row, the link to the doc, the
/// backticked paths, and the last cell of prose. Anything it cannot read is skipped, never guessed at,
/// and the file is only ever read.</summary>
public static class ProjectIndex
{
    private static readonly (string Marker, string Status)[] Statuses =
    [
        ("archived", ProjectDocStatuses.Archived),
        ("paused", ProjectDocStatuses.Paused),
        ("abandoned", ProjectDocStatuses.Abandoned),
        ("deprioritized", ProjectDocStatuses.Paused),
        ("shipped", ProjectDocStatuses.Shipped),
        ("released", ProjectDocStatuses.Shipped),
    ];

    public static IReadOnlyList<ProjectIndexEntry> Parse(string text)
    {
        var entries = new List<ProjectIndexEntry>();
        var section = "";
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("## "))
            {
                section = CleanSection(line[3..]);
                continue;
            }
            if (!line.StartsWith('|')) continue;

            var cells = SplitRow(line);
            if (cells.Length < 4) continue;
            // The header and its dashed separator are rows too.
            if (cells[0].Equals("Project", StringComparison.OrdinalIgnoreCase)) continue;
            if (cells.All(c => c.Length > 0 && c.All(ch => ch is '-' or ':' or ' '))) continue;

            var (marker, remainder) = TakeMarker(cells[0]);
            var (name, docPath) = TakeLink(remainder);
            if (name.Length == 0) continue;
            entries.Add(new(name, docPath, Backticked(cells[1]), Plain(cells[2]), Plain(cells[3]), marker, StatusFor(marker), section));
        }
        return entries;
    }

    /// <summary>The section heading without the parenthetical asides the index annotates them with —
    /// <c>QT *(new stack)*</c> is the QT section.</summary>
    private static string CleanSection(string heading)
    {
        var at = heading.IndexOf("*(", StringComparison.Ordinal);
        return (at < 0 ? heading : heading[..at]).Trim();
    }

    private static string[] SplitRow(string line)
    {
        var trimmed = line.Trim('|');
        var cells = new List<string>();
        var current = new StringBuilder();
        var code = false;
        foreach (var character in trimmed)
        {
            if (character == '`') code = !code;
            // A pipe inside a code span is part of the text, not a cell boundary.
            if (character == '|' && !code) { cells.Add(current.ToString().Trim()); current.Clear(); continue; }
            current.Append(character);
        }
        cells.Add(current.ToString().Trim());
        return [.. cells];
    }

    private static (string? Marker, string Remainder) TakeMarker(string cell)
    {
        var text = cell.Trim();
        if (!text.StartsWith("**[", StringComparison.Ordinal)) return (null, text);
        var close = text.IndexOf("]**", 3, StringComparison.Ordinal);
        if (close < 0) return (null, text);
        return (text[3..close].Trim(), text[(close + 3)..].Trim());
    }

    private static (string Name, string? DocPath) TakeLink(string cell)
    {
        var text = cell.Trim().Trim('~').Trim();
        var open = text.IndexOf('[');
        var close = text.IndexOf("](", StringComparison.Ordinal);
        var end = close < 0 ? -1 : text.IndexOf(')', close);
        if (open < 0 || close < open || end < 0) return (Plain(text), null);
        var target = text[(close + 2)..end].Trim();
        return (Plain(text[(open + 1)..close]), target.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? target : null);
    }

    private static IReadOnlyList<string> Backticked(string cell)
    {
        var found = new List<string>();
        var at = 0;
        while (true)
        {
            var open = cell.IndexOf('`', at);
            if (open < 0) break;
            var close = cell.IndexOf('`', open + 1);
            if (close < 0) break;
            var value = cell[(open + 1)..close].Trim();
            if (value.Length > 0) found.Add(value);
            at = close + 1;
        }
        return found;
    }

    /// <summary>Strips the emphasis marks so a name or a summary reads as text.</summary>
    private static string Plain(string cell) =>
        cell.Replace("**", "").Replace("~~", "").Replace("*(", "(").Replace(")*", ")").Trim();

    private static string? StatusFor(string? marker)
    {
        if (marker is null) return null;
        foreach (var (needle, status) in Statuses)
            if (marker.Contains(needle, StringComparison.OrdinalIgnoreCase)) return status;
        return null;
    }
}

/// <summary>The index resolved against a vault and a set of projects: which row describes which
/// project, so a row's marker and one-line summary can stand in where a doc says nothing.</summary>
public sealed class ProjectIndexLookup(IReadOnlyList<ProjectIndexEntry> entries, string? vaultPath)
{
    public IReadOnlyList<ProjectIndexEntry> Entries { get; } = entries;

    /// <summary>The absolute path of the doc a row links to, or null when the row links nowhere.</summary>
    public string? DocPathOf(ProjectIndexEntry entry) =>
        vaultPath is null || entry.DocPath is null
            ? null
            // Links are written as markdown targets, so a space arrives as %20.
            : Path.GetFullPath(Path.Combine(vaultPath, Uri.UnescapeDataString(entry.DocPath).Replace('/', Path.DirectorySeparatorChar)));

    public ProjectIndexEntry? For(Project project)
    {
        ProjectIndexEntry? byName = null;
        foreach (var entry in Entries)
        {
            // A row's locations are written relative to D:\Programming and go stale like everything
            // else, so the tail of the path is what is compared.
            foreach (var location in entry.Locations)
                if (ProjectDocMatcher.SamePath(location, project.Path)
                    || project.Path.EndsWith("\\" + location.Replace('/', '\\').Trim('\\'), StringComparison.OrdinalIgnoreCase))
                    return entry;
            byName ??= ProjectDocMatcher.Normalize(entry.Name) == ProjectDocMatcher.Normalize(project.Name) ? entry : null;
        }
        return byName;
    }
}
