using Puppeteer.Core;

namespace Puppeteer.Core.Tests;

/// <summary>Several of Mohaned's "projects" are two or more repos in one documented folder — AUP has
/// a backend, Warraq an App and a Website. The doc describes the folder, so its Location and Stack
/// are meant to differ from any one repo inside it.</summary>
public class AncestorDocTests
{
    private const string ParentPath = @"D:\Programming\Web\Hackathons\AUP";

    private static Project Child() =>
        new(Guid.NewGuid(), "backend", ParentPath + @"\backend", Guid.Empty, "Python", ["Python", "FastAPI"], [], []);

    private static ProjectDoc ParentDoc()
    {
        var text = string.Join('\n',
            "# AUP",
            "",
            "**Location:** " + ParentPath,
            "**Status:** Active",
            "**Stack:** Flutter, FastAPI",
            "",
            "## Summary", "s",
            "## What works", "w",
            "## What's broken / incomplete", "b",
            "## Next steps", "n",
            "## Notes", "x",
            "");
        return ProjectDocFormat.Parse(@"C:\vault\Web\AUP.md", text, DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void DoesNotCallAParentDocsLocationStale()
    {
        var report = ProjectDocDrift.Compare(Child(), ParentDoc(), DocMatchConfidence.Ancestor);
        Assert.False(report.Drift.HasFlag(DocDrift.StaleLocation));
        Assert.False(report.Drift.HasFlag(DocDrift.StaleStack));
        Assert.False(report.NeedsWriteup);
    }

    [Fact]
    public void StillCallsItStaleWhenTheDocIsMeantToBeThisFolder()
    {
        var report = ProjectDocDrift.Compare(Child(), ParentDoc(), DocMatchConfidence.FolderName);
        Assert.True(report.Drift.HasFlag(DocDrift.StaleLocation));
    }

    [Fact]
    public void FindsTheParentDocForARepoInsideIt()
    {
        var match = ProjectDocMatcher.Best(Child(), [ParentDoc()])!;
        Assert.Equal(DocMatchConfidence.Ancestor, match.Confidence);
    }
}
