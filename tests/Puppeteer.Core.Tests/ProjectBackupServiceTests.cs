using Puppeteer.Core;
using Puppeteer.Infrastructure;

namespace Puppeteer.Core.Tests;

public sealed class ProjectBackupServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Puppeteer.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RestoreAsync_restores_project_library_state()
    {
        Directory.CreateDirectory(_directory);
        var database = Path.Combine(_directory, "puppeteer.db");
        var backup = Path.Combine(_directory, "library.puppeteer-backup");
        using var repository = new SqliteProjectRepository(database);
        await repository.InitializeAsync();
        var root = new RootFolder(Guid.NewGuid(), Path.Combine(_directory, "projects"), DateTimeOffset.UtcNow);
        await repository.AddRootAsync(root);
        await repository.UpsertProjectsAsync([new Project(Guid.NewGuid(), "Atlas", Path.Combine(root.Path, "Atlas"), root.Id, "C#", ["C#"], [], [])]);
        await repository.SetSettingAsync("Pinned", "state");

        var service = new ProjectBackupService(database);
        var created = await service.CreateAsync(backup);
        await repository.RemoveRootAsync(root.Id);

        var restored = await service.RestoreAsync(backup);

        Assert.Equal(1, created.RootCount);
        Assert.Equal(1, created.ProjectCount);
        Assert.Equal(created, restored);
        Assert.Single(await repository.GetRootsAsync());
        Assert.Single(await repository.GetProjectsAsync());
        Assert.Equal("state", await repository.GetSettingAsync("Pinned"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); } catch { }
    }
}
