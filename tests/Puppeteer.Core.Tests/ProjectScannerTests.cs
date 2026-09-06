using Microsoft.Extensions.Logging.Abstractions;
using Puppeteer.Core;
using Puppeteer.Infrastructure;

namespace Puppeteer.Core.Tests;

public sealed class ProjectScannerTests
{
    [Fact]
    public async Task SlnxRootSuppressesNestedCsprojCards()
    {
        var path = Path.Combine(Path.GetTempPath(), "puppeteer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(path, "src", "App"));

        try
        {
            await File.WriteAllTextAsync(Path.Combine(path, "App.slnx"), "<Solution />");
            await File.WriteAllTextAsync(Path.Combine(path, "src", "App", "App.csproj"), "<Project />");
            var root = new RootFolder(Guid.NewGuid(), path, DateTimeOffset.UtcNow);
            var scanner = new ProjectScanner(new SignatureProjectDetector(), NullLogger<ProjectScanner>.Instance);

            var projects = await scanner.ScanAsync(root);

            var project = Assert.Single(projects);
            Assert.Equal(path, project.Path);
            Assert.Equal(Path.GetFileName(path), project.Name);
        }
        finally
        {
            Directory.Delete(path, true);
        }
    }
}
