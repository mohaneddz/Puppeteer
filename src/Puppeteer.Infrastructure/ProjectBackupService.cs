using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Puppeteer.Core;

namespace Puppeteer.Infrastructure;

/// <summary>Uses SQLite's online backup API rather than copying the live database file, so a WAL
/// journal can never leave a backup missing the user's most recent changes.</summary>
public sealed class ProjectBackupService(string databasePath) : IProjectBackupService
{
    private const string DatabaseEntry = "puppeteer.db";
    private const string ManifestEntry = "manifest.json";
    private readonly string _databasePath = databasePath;

    public async Task<BackupResult> CreateAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        var fullPath = Path.GetFullPath(backupPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var scratch = CreateScratchDirectory();
        try
        {
            var snapshot = Path.Combine(scratch, DatabaseEntry);
            var result = await CopyDatabaseAsync(_databasePath, snapshot, cancellationToken);
            var temporaryArchive = Path.Combine(scratch, "backup.tmp");
            using (var archive = ZipFile.Open(temporaryArchive, ZipArchiveMode.Create))
            {
                var manifest = archive.CreateEntry(ManifestEntry, CompressionLevel.Fastest);
                await using (var stream = manifest.Open())
                    await JsonSerializer.SerializeAsync(stream, new BackupManifest(1, DateTimeOffset.UtcNow), cancellationToken: cancellationToken);
                archive.CreateEntryFromFile(snapshot, DatabaseEntry, CompressionLevel.Optimal);
            }
            File.Move(temporaryArchive, fullPath, true);
            return result;
        }
        finally { TryDelete(scratch); }
    }

    public async Task<BackupResult> RestoreAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        var fullPath = Path.GetFullPath(backupPath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The selected backup file no longer exists.", fullPath);
        var scratch = CreateScratchDirectory();
        try
        {
            var snapshot = Path.Combine(scratch, DatabaseEntry);
            using (var archive = ZipFile.OpenRead(fullPath))
            {
                var manifest = archive.GetEntry(ManifestEntry) ?? throw new InvalidDataException("This is not a Puppeteer backup file.");
                await using (var manifestStream = manifest.Open())
                {
                    var info = await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream, cancellationToken: cancellationToken);
                    if (info is null || info.FormatVersion != 1) throw new InvalidDataException("This backup was created by an unsupported version of Puppeteer.");
                }
                var database = archive.GetEntry(DatabaseEntry) ?? throw new InvalidDataException("The backup does not contain project data.");
                await using var source = database.Open();
                await using var target = File.Create(snapshot);
                await source.CopyToAsync(target, cancellationToken);
            }

            var result = await ValidateAndCountAsync(snapshot, cancellationToken);
            await CopyDatabaseAsync(snapshot, _databasePath, cancellationToken);
            return result;
        }
        finally { TryDelete(scratch); }
    }

    private static async Task<BackupResult> CopyDatabaseAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        // The copied snapshot is immediately put into an archive. Pooling would keep its file handle
        // alive after disposal and prevent the archiver from opening it on Windows.
        var sourceConnectionString = new SqliteConnectionStringBuilder { DataSource = sourcePath, Pooling = false }.ToString();
        var destinationConnectionString = new SqliteConnectionStringBuilder { DataSource = destinationPath, Pooling = false }.ToString();
        await using var source = new SqliteConnection(sourceConnectionString);
        await using var destination = new SqliteConnection(destinationConnectionString);
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
        return await ValidateAndCountAsync(destination, cancellationToken);
    }

    private static async Task<BackupResult> ValidateAndCountAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await connection.OpenAsync(cancellationToken);
        return await ValidateAndCountAsync(connection, cancellationToken);
    }

    private static async Task<BackupResult> ValidateAndCountAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var schema = connection.CreateCommand();
        schema.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('RootFolder', 'Project', 'AppSetting', 'ProjectDocLink')";
        var tableCount = Convert.ToInt32(await schema.ExecuteScalarAsync(cancellationToken));
        if (tableCount < 4) throw new InvalidDataException("The backup does not contain a valid Puppeteer data store.");
        var counts = connection.CreateCommand();
        counts.CommandText = "SELECT (SELECT COUNT(*) FROM RootFolder), (SELECT COUNT(*) FROM Project)";
        await using var reader = await counts.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new BackupResult(reader.GetInt32(0), reader.GetInt32(1));
    }

    private static string CreateScratchDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Puppeteer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void TryDelete(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch { /* temporary files are harmless */ }
    }

    private sealed record BackupManifest(int FormatVersion, DateTimeOffset CreatedAt);
}
