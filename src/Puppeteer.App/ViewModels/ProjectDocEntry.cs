using Puppeteer.Core;

namespace Puppeteer.App.ViewModels;

/// <summary>One row of the Docs page: a project, the doc that describes it (if any), and how far the
/// two have drifted apart.</summary>
public sealed class ProjectDocEntry(Project project, ProjectDoc? doc, DocMatchConfidence confidence, bool manual, DocDriftReport drift, ProjectStateSnapshot? snapshot)
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

    public string Status => Doc is null ? "Undocumented" : ProjectDocStatuses.Parse(Doc.Status) ?? "Unlabelled";

    /// <summary>Why this doc was attached, shown so an automatic guess can be recognised as one.</summary>
    public string LinkNote => Doc is null ? "" : manual ? "linked by hand" : confidence switch
    {
        DocMatchConfidence.ExactPath => "matched on path",
        DocMatchConfidence.FolderName => "matched on folder name",
        DocMatchConfidence.Name => "matched on name",
        _ => "suggested",
    };

    public bool IsSuggestion => !manual && confidence == DocMatchConfidence.Suggested;

    public string Summary => Doc?.Section(ProjectDocSections.Summary) is { Length: > 0 } summary
        ? FirstLine(summary)
        : Doc is null ? "No state doc yet." : "No summary written.";

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
