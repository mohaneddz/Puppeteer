using Puppeteer.Core;

namespace Puppeteer.Core.Tests;

public class ProjectIndexTests
{
    private static readonly string Sample = string.Join('\n',
        "# Projects Index",
        "",
        "Some prose about a folder restructure.",
        "",
        "## Web",
        "| Project | Location | Client | Summary |",
        "|---|---|---|---|",
        "| [Cosmetocare](Web/Cosmetocare.md) | `Web\\Personal\\Cosmetocare` | Personal | Ingredient safety platform. |",
        "| **[ARCHIVED]** [HackatonBackend](Web/HackatonBackend.md) | *(no longer on disk)* | Hackathon | RAG chatbot API. |",
        "| **[PAUSED]** Taiba Tours | `Web\\School\\Taiba Tours` | School | Multilingual travel site. |",
        "| [Portfolio](Web/Portfolio.md) | `Web\\Personal\\My-Portfolio`, `Web\\Personal\\Me.Stats` | Personal | Three sibling folders. |",
        "| ~~[story2](Mobile/story2.md)~~ | — | Personal | Consolidated elsewhere. |",
        "",
        "## QT *(new stack)*",
        "| Project | Location | Client | Summary |",
        "|---|---|---|---|",
        "| [Manimation](QT/Manimation.md) | `Desktop\\QT\\Personal\\Manimation` | Personal | PySide6 studio. |");

    private static IReadOnlyList<ProjectIndexEntry> Parse() => ProjectIndex.Parse(Sample);

    [Fact]
    public void SkipsHeaderRowsSeparatorsAndProse()
    {
        Assert.Equal(6, Parse().Count);
        Assert.DoesNotContain(Parse(), e => e.Name == "Project");
    }

    [Fact]
    public void ReadsANameItsDocAndItsLocation()
    {
        var entry = Parse().First(e => e.Name == "Cosmetocare");
        Assert.Equal("Web/Cosmetocare.md", entry.DocPath);
        Assert.Equal([@"Web\Personal\Cosmetocare"], entry.Locations);
        Assert.Equal("Personal", entry.Client);
        Assert.Equal("Web", entry.Section);
    }

    [Fact]
    public void ReadsTheBracketedMarkerAsAStatus()
    {
        Assert.Equal(ProjectDocStatuses.Archived, Parse().First(e => e.Name == "HackatonBackend").Status);
        Assert.Equal(ProjectDocStatuses.Paused, Parse().First(e => e.Name == "Taiba Tours").Status);
        Assert.Null(Parse().First(e => e.Name == "Cosmetocare").Status);
    }

    [Fact]
    public void KeepsARowWithAMarkerButNoLink()
    {
        var entry = Parse().First(e => e.Name == "Taiba Tours");
        Assert.Null(entry.DocPath);
        Assert.Equal("PAUSED", entry.Marker);
    }

    [Fact]
    public void ReadsEveryLocationOnARowThatListsSeveral()
    {
        Assert.Equal(2, Parse().First(e => e.Name == "Portfolio").Locations.Count);
    }

    [Fact]
    public void StripsEmphasisFromANameAndASummary()
    {
        var entry = Parse().First(e => e.Name == "story2");
        Assert.Equal("Consolidated elsewhere.", entry.Summary);
    }

    [Fact]
    public void TrimsTheAsideOffASectionHeading()
    {
        Assert.Equal("QT", Parse().First(e => e.Name == "Manimation").Section);
    }

    [Fact]
    public void FindsTheRowForAProjectByTheTailOfItsPath()
    {
        var lookup = new ProjectIndexLookup(Parse(), @"C:\vault");
        var project = new Project(Guid.NewGuid(), "Cosmetocare", @"D:\Programming\Web\Personal\Cosmetocare", Guid.Empty, "Next.js", ["Next.js"], [], []);
        var entry = lookup.For(project)!;
        Assert.Equal("Cosmetocare", entry.Name);
        Assert.Equal(@"C:\vault\Web\Cosmetocare.md", lookup.DocPathOf(entry));
    }

    [Fact]
    public void FallsBackToTheNameWhenNoPathMatches()
    {
        var lookup = new ProjectIndexLookup(Parse(), @"C:\vault");
        var moved = new Project(Guid.NewGuid(), "Manimation", @"D:\somewhere\else\Manimation", Guid.Empty, "Qt", ["Qt"], [], []);
        Assert.Equal("Manimation", lookup.For(moved)?.Name);
    }

    [Fact]
    public void FindsNothingForAProjectTheIndexDoesNotList()
    {
        var lookup = new ProjectIndexLookup(Parse(), @"C:\vault");
        var unknown = new Project(Guid.NewGuid(), "Puppeteer", @"D:\Programming\Desktop\.Net\Personal\Puppeteer", Guid.Empty, ".NET", [".NET"], [], []);
        Assert.Null(lookup.For(unknown));
    }

    [Fact]
    public void DoesNotSplitACellOnAPipeInsideACodeSpan()
    {
        var entries = ProjectIndex.Parse(string.Join('\n',
            "## Web",
            "| Project | Location | Client | Summary |",
            "|---|---|---|---|",
            "| [Pipes](Web/Pipes.md) | `Web\\Pipes` | Personal | Runs `a | b` in a shell. |"));
        Assert.Equal("Runs `a | b` in a shell.", entries.Single().Summary);
    }
}
