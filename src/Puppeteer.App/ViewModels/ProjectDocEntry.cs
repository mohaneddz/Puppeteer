using Puppeteer.Core;

namespace Puppeteer.App.ViewModels;

/// <summary>One row of the Docs page: a project, the doc that describes it (if any), and how far the
/// two have drifted apart.</summary>
public sealed class ProjectDocEntry(Project project, ProjectDoc? doc, DocMatchConfidence confidence, bool manual, ProjectDoc? suggestion, ProjectIndexEntry? indexRow, string? readmePath, DocDriftReport drift, ProjectStateSnapshot? snapshot)
{
    public Project Project { get; } = project;
    public ProjectDoc? Doc { get; } = doc;
    public DocDriftReport Drift { get; } = drift;
    public ProjectStateSnapshot? Snapshot { get; } = snapshot;

    public string Name => Project.Name;
    public string Path => Project.Path;
    public bool HasDoc => Doc is not null;
    public string DocPath => Doc?.FilePath ?? "";
    public string DocName => Doc is null ? "" : System.IO.Path.GetFileName(Doc.FilePath);

    /// <summary>A doc whose name is close enough to be worth offering, but not to attach on its own.</summary>
    public bool HasSuggestion => Doc is null && suggestion is not null;
    public string SuggestionName => suggestion is null ? "" : System.IO.Path.GetFileName(suggestion.FilePath);

    /// <summary>The project's own README, which sits alongside its state doc in the reader.</summary>
    public bool HasReadme => readmePath is not null;
    public string ReadmePath => readmePath ?? "";

    /// <summary>The doc's own Status field first, then whatever the index row was marked with — the
    /// archived and paused labels live there today, not in the docs.</summary>
    public string Status =>
        ProjectDocStatuses.Parse(Doc?.Status) ?? indexRow?.Status
        ?? (Doc is null ? "Undocumented" : "Unlabelled");

    /// <summary>The index's raw label when it says something the status list does not — "Container",
    /// "NEW, undocumented", "VENDORED".</summary>
    public string IndexNote => indexRow?.Status is null && indexRow?.Marker is { Length: > 0 } marker ? marker : "";
    public bool HasIndexNote => IndexNote.Length > 0;
    public string Client => indexRow?.Client ?? "";

    /// <summary>Why this doc was attached, shown so an automatic guess can be recognised as one.</summary>
    public string LinkNote => Doc is null ? "" : manual ? "linked by hand" : confidence switch
    {
        DocMatchConfidence.ExactPath => "matched on path",
        DocMatchConfidence.Index => "linked from the index",
        DocMatchConfidence.FolderName => "matched on folder name",
        DocMatchConfidence.Name => "matched on name",
        DocMatchConfidence.Ancestor => "part of a documented folder",
        _ => "",
    };

    public string Summary => Doc?.Section(ProjectDocSections.Summary) is { Length: > 0 } summary
        ? FirstLine(summary)
        : indexRow?.Summary is { Length: > 0 } fromIndex ? FirstLine(fromIndex)
        : Doc is null ? "No state doc yet." : "No summary written.";

    /// <summary>A card is three lines of summary wide, and WPF trims by line, not by height — so the
    /// text is cut to fit rather than left to clip through the middle of a line.</summary>
    public string CardSummary => Summary.Length <= 116 ? Summary : Summary[..116].TrimEnd() + "…";
    public string CardNextStep => NextStep.Length <= 88 ? NextStep : NextStep[..88].TrimEnd() + "…";

    public string NextStep => Doc?.Section(ProjectDocSections.Next) is { Length: > 0 } next ? FirstLine(next) : "";
    public bool HasNextStep => NextStep.Length > 0;

    public string DriftSummary => Drift.Reasons.Count == 0 ? "In step with the repo" : string.Join(" · ", Drift.Reasons);
    public bool NeedsWriteup => Drift.NeedsWriteup;
    public int DriftCount => Drift.Reasons.Count;

    public string Activity => Snapshot?.LastCommitAt is { } committed
        ? $"last commit {Ago(committed)}"
        : Doc?.LastActivity is { Length: > 0 } stated ? FirstLine(stated) : "";

    private static string FirstLine(string text)
    {
        var line = text.Replace("\r\n", "\n").Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
        if (line.StartsWith("- ") || line.StartsWith("* ")) line = line[2..].Trim();
        return line.Length > 190 ? line[..190].TrimEnd() + "…" : line;
    }

    private static string Ago(DateTimeOffset when)
    {
        var days = (int)(DateTimeOffset.UtcNow - when).TotalDays;
        return days switch
        {
            <= 0 => "today",
            1 => "yesterday",
            < 30 => $"{days} days ago",
            < 365 => $"{days / 30} month{(days / 30 == 1 ? "" : "s")} ago",
            _ => $"{days / 365} year{(days / 365 == 1 ? "" : "s")} ago",
        };
    }
}
