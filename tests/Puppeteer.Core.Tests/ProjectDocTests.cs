using Puppeteer.Core;

namespace Puppeteer.Core.Tests;

public class ProjectDocParsingTests
{
    private const string Sample = """
        # Hive

        **Location:** D:\Programming\Desktop\Tauri\Personal\Hive
        **Category:** Desktop (Tauri)
        **Client or personal:** Personal
        **Stack:** Tauri 2 + Rust, React 19, SQLite
        **Last activity:** 2026-07-29 (last commit `a03985c`)

        ## Summary
        A local-first photo library manager.

        ## What works
        - Gallery timeline
        - Face detection

        ## Next steps
        - Rewrite the README

        ## Notes
        Heavier Rust dependency set than a typical Tauri app.
        """;

    private static ProjectDoc Parse(string text = Sample) =>
        ProjectDocFormat.Parse(@"C:\vault\Desktop\Hive.md", text.Replace("\r\n", "\n"), DateTimeOffset.UnixEpoch);

    [Fact]
    public void ReadsTitleFieldsAndSections()
    {
        var doc = Parse();
        Assert.Equal("Hive", doc.Title);
        Assert.Equal(@"D:\Programming\Desktop\Tauri\Personal\Hive", doc.Location);
        Assert.Equal("Personal", doc.Field(ProjectDocFields.Client));
        Assert.Equal(["Summary", "What works", "Next steps", "Notes"], doc.Sections.Select(s => s.Heading));
        Assert.Equal("- Gallery timeline\n- Face detection", doc.Section("What works"));
    }

    [Fact]
    public void RoundTripsAWellFormedDocByteForByte()
    {
        var text = Sample.Replace("\r\n", "\n") + "\n";
        Assert.Equal(text, ProjectDocFormat.Render(Parse(text)));
    }

    [Fact]
    public void RenderingIsStableAcrossRepeatedParses()
    {
        var once = ProjectDocFormat.Render(Parse());
        var twice = ProjectDocFormat.Render(ProjectDocFormat.Parse(@"C:\vault\Desktop\Hive.md", once, DateTimeOffset.UnixEpoch));
        Assert.Equal(once, twice);
    }

    [Fact]
    public void KeepsSectionsItDoesNotKnowAbout()
    {
        var doc = Parse(Sample + "\n\n## Deployment notes\nRuns on the office NAS.\n");
        var edited = ProjectDocFormat.WithSection(doc, ProjectDocSections.Summary, "Rewritten.");
        var rendered = ProjectDocFormat.Render(edited);
        Assert.Contains("## Deployment notes\nRuns on the office NAS.", rendered);
        Assert.Contains("## Summary\nRewritten.", rendered);
    }

    [Fact]
    public void KeepsUnknownHeaderFieldsAndTheirOrder()
    {
        var doc = Parse("# X\n\n**Location:** C:\\x\n**Bus factor:** 1\n\n## Summary\nY\n");
        var rendered = ProjectDocFormat.Render(ProjectDocFormat.WithField(doc, ProjectDocFields.Status, "Active"));
        Assert.Contains("**Bus factor:** 1", rendered);
        Assert.True(rendered.IndexOf("**Location:**", StringComparison.Ordinal) < rendered.IndexOf("**Status:**", StringComparison.Ordinal));
    }

    [Fact]
    public void PutsANewFieldInTheHeaderBlocksUsualOrder()
    {
        var doc = ProjectDocFormat.WithField(Parse(), ProjectDocFields.Status, "Paused");
        var keys = doc.Fields.Select(f => f.Key).ToArray();
        Assert.Equal(3, Array.IndexOf(keys, ProjectDocFields.Status));
    }

    [Fact]
    public void ReplacesAFieldWithoutMovingIt()
    {
        var doc = ProjectDocFormat.WithField(Parse(), ProjectDocFields.Location, @"D:\new\path");
        Assert.Equal(@"D:\new\path", doc.Location);
        Assert.Equal(0, doc.Fields.ToList().FindIndex(f => f.Key == ProjectDocFields.Location));
    }

    [Fact]
    public void FallsBackToTheFileNameWhenThereIsNoTitle()
    {
        var doc = ProjectDocFormat.Parse(@"C:\vault\Web\Forsa.md", "**Stack:** FastAPI\n\n## Summary\nNo heading here.\n", DateTimeOffset.UnixEpoch);
        Assert.Equal("Forsa", doc.Title);
        Assert.Equal("No heading here.", doc.Section("Summary"));
    }

    [Fact]
    public void LeavesAStrayMarkdownFileAloneInsteadOfReshapingIt()
    {
        var readme = "<h1>\n  <img src=\"logo.svg\"/>\n  BMO\n</h1>\n\n## Getting started\nRun it.\n";
        var doc = ProjectDocFormat.Parse(@"C:\vault\stray\README.md", readme, DateTimeOffset.UnixEpoch);
        Assert.False(doc.IsStateDoc);
        Assert.Equal(readme, ProjectDocFormat.Render(doc));
    }

    [Fact]
    public void KeepsContinuationLinesWhereTheyWereInTheHeader()
    {
        var text = "# Warraq\n\n**Stack:**\n  - **App**: Tauri 2\n  - **Website**: Next.js\n**Current release/version:** 0.1.0\n\n## Summary\nx\n";
        var doc = ProjectDocFormat.Parse(@"C:\vault\Desktop\Warraq.md", text, DateTimeOffset.UnixEpoch);
        Assert.Equal(text, ProjectDocFormat.Render(doc));
        Assert.Equal(["Stack", "Current release/version"], doc.Fields.Select(f => f.Key));
    }

    [Fact]
    public void KeepsTheTrailingSpacesThatMakeAMarkdownHardBreak()
    {
        var text = "# Twins\n\n**Location:** C:\\x  \n**Category:** Mobile  \n\n## Summary\nx\n";
        Assert.Equal(text, ProjectDocFormat.Render(Parse(text)));
    }

    [Fact]
    public void AddsAFieldAboveAnotherFieldsContinuationLines()
    {
        var doc = Parse("# Warraq\n\n**Location:** C:\\x\n**Stack:**\n  - App: Tauri\n\n## Summary\nx\n");
        var rendered = ProjectDocFormat.Render(ProjectDocFormat.WithField(doc, ProjectDocFields.Status, "Active"));
        Assert.Contains("**Location:** C:\\x\n**Status:** Active\n**Stack:**\n  - App: Tauri", rendered);
    }

    [Fact]
    public void DoesNotMistakeBoldProseForAField()
    {
        var doc = Parse("# X\n\n**This is emphasis, not a field**\n**Stack:** Rust\n\n## Summary\nY\n");
        Assert.Equal(["Stack"], doc.Fields.Select(f => f.Key));
        Assert.Contains("**This is emphasis, not a field**", ProjectDocFormat.Render(doc));
    }

    [Fact]
    public void ReadsStatusOutOfFreeformText()
    {
        Assert.Equal("Archived", ProjectDocStatuses.Parse("**[ARCHIVED]** no longer active"));
        Assert.Equal("Paused", ProjectDocStatuses.Parse("Paused as of 2026-08-24"));
        Assert.Null(ProjectDocStatuses.Parse("Client — Think Tech Solutions"));
    }
}

public class ProjectDocMatchingTests
{
    private static Project ProjectAt(string path, string? name = null) =>
        new(Guid.NewGuid(), name ?? ProjectDocMatcher.FolderName(path), path, Guid.Empty, "Tauri", ["Tauri", "Rust"], [], []);

    private static ProjectDoc Doc(string fileName, params string[] fields) =>
        ProjectDocFormat.Parse($@"C:\vault\Desktop\{fileName}.md",
            $"# {fileName}\n\n{string.Join('\n', fields)}\n\n## Summary\nx\n", DateTimeOffset.UnixEpoch);

    [Fact]
    public void PrefersTheDocWhoseLocationIsTheProjectFolder()
    {
        var project = ProjectAt(@"D:\Programming\Desktop\Tauri\Personal\Hive");
        var docs = new[] { Doc("Hive", @"**Location:** D:\Programming\Desktop\Tauri\Personal\Hive"), Doc("Hive2") };
        Assert.Equal(DocMatchConfidence.ExactPath, ProjectDocMatcher.Best(project, docs)!.Confidence);
    }

    [Fact]
    public void MatchesOnFolderNameWhenTheLocationWentStale()
    {
        var project = ProjectAt(@"D:\Programming\Desktop\Tauri\Personal\Hive");
        var docs = new[] { Doc("Photos", @"**Location:** D:\Programming\Tauri\Projects\Hive") };
        var match = ProjectDocMatcher.Best(project, docs)!;
        Assert.Equal(DocMatchConfidence.FolderName, match.Confidence);
    }

    [Fact]
    public void MatchesOnAnAliasRecordedInTheDoc()
    {
        var project = ProjectAt(@"D:\Programming\Desktop\Tauri\Clients\Follio");
        var docs = new[] { Doc("PrivateSchool", "**Aliases:** FOLIO, Follio") };
        Assert.Equal(DocMatchConfidence.Name, ProjectDocMatcher.Best(project, docs)!.Confidence);
    }

    [Fact]
    public void IgnoresSpacingAndCaseInNames()
    {
        var project = ProjectAt(@"D:\Programming\Desktop\Tauri\Personal\Story 2");
        Assert.Equal(DocMatchConfidence.Name, ProjectDocMatcher.Best(project, [Doc("story2")])!.Confidence);
    }

    [Fact]
    public void OffersATypoAsASuggestionRatherThanAMatch()
    {
        var project = ProjectAt(@"D:\Programming\Web\Personal\Odyssia");
        Assert.Equal(DocMatchConfidence.Suggested, ProjectDocMatcher.Best(project, [Doc("Odysia")])!.Confidence);
    }

    [Fact]
    public void FindsNothingForAnUnrelatedDoc()
    {
        var project = ProjectAt(@"D:\Programming\Web\Personal\Puppeteer");
        Assert.Null(ProjectDocMatcher.Best(project, [Doc("Sloth")]));
    }

    [Fact]
    public void CoversEveryRepoInsideADocumentedFolder()
    {
        var app = ProjectAt(@"D:\Programming\Desktop\Tauri\Personal\Warraq\App", "App");
        var website = ProjectAt(@"D:\Programming\Desktop\Tauri\Personal\Warraq\Website", "Website");
        var doc = Doc("Warraq", @"**Location:** D:\Programming\Desktop\Tauri\Personal\Warraq");
        var matches = ProjectDocMatcher.Match([app, website], [doc]);
        Assert.Equal(DocMatchConfidence.Ancestor, matches[app.Id].Confidence);
        Assert.Equal(DocMatchConfidence.Ancestor, matches[website.Id].Confidence);
    }

    [Fact]
    public void LetsTheNamedProjectOutrankItsOwnSubfolders()
    {
        var app = ProjectAt(@"D:\Programming\Desktop\Tauri\Personal\Warraq\App", "App");
        var root = ProjectAt(@"D:\Programming\Desktop\Tauri\Personal\Warraq");
        var doc = Doc("Warraq", @"**Location:** D:\Programming\Desktop\Tauri\Personal\Warraq");
        var matches = ProjectDocMatcher.Match([app, root], [doc]);
        Assert.Equal(DocMatchConfidence.ExactPath, matches[root.Id].Confidence);
        Assert.Equal(DocMatchConfidence.Ancestor, matches[app.Id].Confidence);
    }

    [Fact]
    public void DoesNotClaimAnUnrelatedProjectDeeperInTheTree()
    {
        var other = ProjectAt(@"D:\Programming\Web\Personal\Cosmetocare");
        var doc = Doc("Warraq", @"**Location:** D:\Programming\Desktop\Tauri\Personal\Warraq");
        Assert.Null(ProjectDocMatcher.Best(other, [doc]));
    }
}
