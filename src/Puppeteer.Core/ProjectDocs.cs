using System.Text;

namespace Puppeteer.Core;

/// <summary>One line of a doc's header block. A <c>**Key:** value</c> line has both halves; any
/// other line — a continuation bullet under Stack, a stray note — is carried with an empty
/// <see cref="Key"/> and its text verbatim, so the block's order survives a rewrite.</summary>
public sealed record DocField(string Key, string Value)
{
    public bool IsField => Key.Length > 0;
}

/// <summary>A <c>## Heading</c> and its body, kept verbatim so round-tripping a doc through
/// Puppeteer never reflows prose it did not author.</summary>
public sealed record DocSection(string Heading, string Body);

/// <summary>A parsed per-project state document. <see cref="Raw"/> is what was on disk; every other
/// member is a view over it. Sections and fields keep their original order.</summary>
public sealed record ProjectDoc(
    string FilePath,
    string Title,
    IReadOnlyList<DocField> Header,
    IReadOnlyList<DocSection> Sections,
    string Raw,
    DateTimeOffset ModifiedAt)
{
    /// <summary>False for a markdown file in the vault that has neither a title nor a header field —
    /// a stray README rather than a state doc. Those are listed but never rewritten.</summary>
    public bool IsStateDoc { get; init; } = true;

    /// <summary>The header's <c>**Key:** value</c> lines, in order, without the raw ones.</summary>
    public IReadOnlyList<DocField> Fields => Header.Where(f => f.IsField).ToArray();

    public string? Field(string key) => Header.FirstOrDefault(f => f.IsField && f.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;
    public string? Section(string heading) => Sections.FirstOrDefault(s => s.Heading.Equals(heading, StringComparison.OrdinalIgnoreCase))?.Body;

    public string? Location => Field(ProjectDocFields.Location);
    public string? Stack => Field(ProjectDocFields.Stack);
    public string? Status => Field(ProjectDocFields.Status);
    public string? LastActivity => Field(ProjectDocFields.LastActivity);

    /// <summary>Names this doc answers to besides its title: the file name and anything listed in an
    /// <c>Aliases</c> field. Projects are matched against all of them.</summary>
    public IReadOnlyList<string> Names()
    {
        var names = new List<string> { Title, Path.GetFileNameWithoutExtension(FilePath) };
        if (Field(ProjectDocFields.Aliases) is { Length: > 0 } aliases)
            names.AddRange(aliases.Split([',', '/', '·'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return names.Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

public static class ProjectDocFields
{
    public const string Location = "Location";
    public const string Category = "Category";
    public const string Client = "Client or personal";
    public const string Stack = "Stack";
    public const string Release = "Current release/version";
    public const string LastActivity = "Last activity";
    public const string Status = "Status";
    public const string Aliases = "Aliases";

    /// <summary>The order a header block reads best in. A field Puppeteer adds to a doc that lacks it
    /// slots in here rather than landing at the bottom, so hand-written and generated docs match.</summary>
    public static readonly IReadOnlyList<string> PreferredOrder =
        [Location, Category, Client, Status, Stack, Release, LastActivity, Aliases];
}

public static class ProjectDocSections
{
    public const string Summary = "Summary";
    public const string Works = "What works";
    public const string Broken = "What's broken / incomplete";
    public const string Next = "Next steps";
    public const string Notes = "Notes";

    public static readonly IReadOnlyList<string> Standard = [Summary, Works, Broken, Next, Notes];
}

/// <summary>The lifecycle label a doc carries in its <c>Status</c> field. Puppeteer reads it, filters
/// on it and writes it back, but the doc — not the database — owns it, so editing the markdown by
/// hand or from another machine is just as valid as using the app.</summary>
public static class ProjectDocStatuses
{
    public const string Active = "Active";
    public const string Paused = "Paused";
    public const string Shipped = "Shipped";
    public const string Archived = "Archived";
    public const string Idea = "Idea";
    public const string Abandoned = "Abandoned";

    public static readonly IReadOnlyList<string> Known = [Active, Paused, Shipped, Archived, Idea, Abandoned];

    /// <summary>Reads a status out of freeform text — the index marks archived rows as
    /// <c>**[ARCHIVED]**</c> and docs sometimes say "paused as of …" rather than carrying a field.
    /// The earliest word wins, so "[ARCHIVED] — no longer active" reads as archived, not active.</summary>
    public static string? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string? found = null;
        var earliest = int.MaxValue;
        foreach (var status in Known)
        {
            var at = IndexOfWord(text, status);
            if (at < 0 || at >= earliest) continue;
            earliest = at;
            found = status;
        }
        return found;
    }

    private static int IndexOfWord(string text, string word)
    {
        for (var at = 0; at <= text.Length - word.Length; at++)
        {
            if (string.Compare(text, at, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0) continue;
            if (at > 0 && char.IsLetter(text[at - 1])) continue;
            var after = at + word.Length;
            if (after < text.Length && char.IsLetter(text[after])) continue;
            return at;
        }
        return -1;
    }
}

/// <summary>Reads and writes the per-project markdown state docs.
///
/// The format is the one already in the vault: an <c>#</c> title, a block of <c>**Key:** value</c>
/// lines, then <c>##</c> sections. Everything the parser does not recognise is carried through
/// untouched — section bodies are never reformatted, unknown fields keep their place, and stray
/// header lines survive — because these files are edited by hand far more often than by Puppeteer.</summary>
public static class ProjectDocFormat
{
    public static ProjectDoc Parse(string filePath, string text, DateTimeOffset modifiedAt)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var title = "";
        var header = new List<DocField>();
        var sections = new List<DocSection>();

        // Only a leading "# " line is the title. Hunting further down the file for one would mean
        // quietly dropping everything above it, and a file that opens with prose is not this format.
        var index = 0;
        while (index < lines.Length && lines[index].Trim().Length == 0) index++;
        var titled = index < lines.Length && lines[index].StartsWith("# ");
        if (titled) { title = lines[index][2..].Trim(); index++; }
        else index = 0;

        var sawField = false;
        for (; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.StartsWith("## ")) break;
            if (TryParseField(line, out var field)) { header.Add(field); sawField = true; }
            else if (line.Trim().Length > 0) header.Add(new("", line));
        }
        // A file with no title and no header fields is not a state doc at all — a stray README swept
        // up from the vault, say. Keep it whole rather than reshaping it into one.
        if (!titled && !sawField) return new(filePath, Path.GetFileNameWithoutExtension(filePath), [], [], text, modifiedAt) { IsStateDoc = false };

        string? heading = null;
        var body = new List<string>();
        for (; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.StartsWith("## "))
            {
                if (heading is not null) sections.Add(new(heading, Join(body)));
                heading = line[3..].Trim();
                body.Clear();
                continue;
            }
            body.Add(line);
        }
        if (heading is not null) sections.Add(new(heading, Join(body)));

        if (title.Length == 0) title = Path.GetFileNameWithoutExtension(filePath);
        return new(filePath, title, header, sections, text, modifiedAt);
    }

    public static string Render(ProjectDoc doc)
    {
        if (!doc.IsStateDoc) return doc.Raw;
        var builder = new StringBuilder();
        builder.Append("# ").Append(doc.Title).Append('\n').Append('\n');
        foreach (var entry in doc.Header)
            builder.Append(entry switch
            {
                { IsField: false } => entry.Value,
                { Value.Length: 0 } => $"**{entry.Key}:**",
                _ => $"**{entry.Key}:** {entry.Value}",
            }).Append('\n');
        foreach (var section in doc.Sections)
        {
            builder.Append('\n').Append("## ").Append(section.Heading).Append('\n');
            if (section.Body.Length > 0) builder.Append(section.Body).Append('\n');
        }
        return builder.ToString();
    }

    public static ProjectDoc WithField(ProjectDoc doc, string key, string? value)
    {
        var header = doc.Header.ToList();
        var at = header.FindIndex(f => f.IsField && f.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (value is null)
        {
            if (at < 0) return doc;
            header.RemoveAt(at);
        }
        else if (at >= 0) header[at] = new(header[at].Key, value);
        else header.Insert(HeaderInsertionPoint(header, key), new(key, value));
        return doc with { Header = header };
    }

    public static ProjectDoc WithSection(ProjectDoc doc, string heading, string body)
    {
        var sections = doc.Sections.ToList();
        var at = sections.FindIndex(s => s.Heading.Equals(heading, StringComparison.OrdinalIgnoreCase));
        var trimmed = body.Replace("\r\n", "\n").TrimEnd();
        if (at >= 0) sections[at] = new(sections[at].Heading, trimmed);
        else sections.Insert(InsertionPoint(sections, heading), new(heading, trimmed));
        return doc with { Sections = sections };
    }

    /// <summary>A blank doc in the vault's own shape, ready for the user to fill in. The factual
    /// fields are pre-filled from what Puppeteer already knows; the prose is left empty rather than
    /// stuffed with placeholder text nobody would delete.</summary>
    public static ProjectDoc Create(string filePath, string title, IReadOnlyList<DocField> fields) =>
        new(filePath, title, fields, ProjectDocSections.Standard.Select(s => new DocSection(s, "")).ToArray(), "", DateTimeOffset.UtcNow);

    private static bool TryParseField(string line, out DocField field)
    {
        field = new("", "");
        if (!line.StartsWith("**")) return false;
        var close = line.IndexOf(":**", 2, StringComparison.Ordinal);
        if (close < 0) return false;
        var key = line[2..close].Trim();
        if (key.Length == 0 || key.Contains("**", StringComparison.Ordinal)) return false;
        // Only the separator's own space is dropped. Two trailing spaces are a markdown hard break,
        // and trimming them silently rewraps the header when the doc is saved.
        field = new(key, line[(close + 3)..].TrimStart());
        return true;
    }

    /// <summary>Where a field Puppeteer is adding belongs in the header: just before the first known
    /// field that outranks it, so it never lands under a continuation line belonging to another.</summary>
    private static int HeaderInsertionPoint(List<DocField> header, string key)
    {
        var rank = IndexOf(ProjectDocFields.PreferredOrder, key);
        if (rank < 0) return header.Count;
        var lastKnown = -1;
        for (var i = 0; i < header.Count; i++)
        {
            if (!header[i].IsField) continue;
            var existing = IndexOf(ProjectDocFields.PreferredOrder, header[i].Key);
            if (existing > rank) return i;
            if (existing >= 0) lastKnown = i;
        }
        return lastKnown < 0 ? header.Count : lastKnown + 1;
    }

    private static int InsertionPoint(List<DocSection> sections, string heading) =>
        InsertionPoint(ProjectDocSections.Standard, sections.Select(s => s.Heading), heading, sections.Count);

    private static int InsertionPoint(IReadOnlyList<string> order, IEnumerable<string> present, string value, int fallback)
    {
        var rank = IndexOf(order, value);
        if (rank < 0) return fallback;
        var at = 0;
        foreach (var existing in present)
        {
            var existingRank = IndexOf(order, existing);
            if (existingRank < 0 || existingRank > rank) return at;
            at++;
        }
        return at;
    }

    private static int IndexOf(IReadOnlyList<string> values, string value)
    {
        for (var i = 0; i < values.Count; i++)
            if (values[i].Equals(value, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    // Only trailing blank lines go: the gap between one section and the next is the renderer's to
    // add. A blank line the author put directly under a heading is theirs, and is kept.
    private static string Join(List<string> lines)
    {
        var end = lines.Count;
        while (end > 0 && lines[end - 1].Trim().Length == 0) end--;
        return string.Join('\n', lines.Take(end));
    }
}
