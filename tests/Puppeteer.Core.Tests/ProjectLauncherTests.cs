using Puppeteer.Infrastructure;

namespace Puppeteer.Core.Tests;

public sealed class ProjectLauncherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Puppeteer-launcher-" + Guid.NewGuid());

    public ProjectLauncherTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void CodeScriptResolvesToGuiExecutableAndPreservesFileArgument()
    {
        var install = Path.Combine(_root, "Editor with spaces");
        var bin = Directory.CreateDirectory(Path.Combine(install, "bin")).FullName;
        var script = Path.Combine(bin, "code.cmd");
        var exe = Path.Combine(install, "Code.exe");
        File.WriteAllText(script, "");
        File.WriteAllText(exe, "");
        var file = Path.Combine(_root, "note with spaces.md");
        File.WriteAllText(file, "# Notes");

        var start = ProjectLauncher.CreateEditorStartInfo(file, $"\"{script}\" .");

        Assert.Equal(exe, start.FileName);
        Assert.Equal(_root, start.WorkingDirectory);
        Assert.Equal(file, Assert.Single(start.ArgumentList));
        Assert.False(start.UseShellExecute);
    }

    [Fact]
    public void CustomEditorKeepsProjectFolderAsWorkingDirectory()
    {
        var exe = Path.Combine(_root, "custom editor.exe");
        File.WriteAllText(exe, "");
        var start = ProjectLauncher.CreateEditorStartInfo(_root, $"\"{exe}\"");
        Assert.Equal(exe, start.FileName);
        Assert.Equal(_root, start.WorkingDirectory);
        Assert.Equal(_root, Assert.Single(start.ArgumentList));
    }

    [Fact]
    public void MissingEditorExplainsHowToConfigureIt()
    {
        var error = Assert.Throws<FileNotFoundException>(() =>
            ProjectLauncher.CreateEditorStartInfo(_root, Path.Combine(_root, "missing.exe")));
        Assert.Contains("Settings", error.Message);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
