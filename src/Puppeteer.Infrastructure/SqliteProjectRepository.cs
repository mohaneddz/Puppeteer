using System.Text.Json;
using Microsoft.Data.Sqlite;
using Puppeteer.Core;

namespace Puppeteer.Infrastructure;

public sealed class SqliteProjectRepository : IProjectRepository, IDisposable
{
    private readonly string _connectionString;
    public SqliteProjectRepository(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var lease = await LeaseAsync(cancellationToken);
        var connection = lease.Connection;
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            CREATE TABLE IF NOT EXISTS RootFolder(Id TEXT PRIMARY KEY, Path TEXT NOT NULL UNIQUE, CreatedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Project(Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Path TEXT NOT NULL UNIQUE, RootId TEXT NOT NULL,
                PrimaryTechnology TEXT NOT NULL, TechnologiesJson TEXT NOT NULL, HierarchyJson TEXT NOT NULL, PresetsJson TEXT NOT NULL,
                LastOpenedAt TEXT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL, CustomIconPath TEXT NULL, IconFill INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS DetectedTechnology(ProjectId TEXT NOT NULL, Name TEXT NOT NULL, PRIMARY KEY(ProjectId, Name));
            CREATE TABLE IF NOT EXISTS ProjectTag(ProjectId TEXT NOT NULL, Name TEXT NOT NULL, PRIMARY KEY(ProjectId, Name));
            CREATE TABLE IF NOT EXISTS ProjectIcon(ProjectId TEXT PRIMARY KEY, Path TEXT NULL, UpdatedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS TerminalPreset(ProjectId TEXT NOT NULL, Name TEXT NOT NULL, Command TEXT NOT NULL, PRIMARY KEY(ProjectId, Name));
            CREATE TABLE IF NOT EXISTS AppSetting(Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        // Category was added after the first release; older databases need the column backfilled.
        try
        {
            var migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE Project ADD COLUMN Category TEXT NULL";
            await migrate.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException) { /* column already exists */ }
        try
        {
            var migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE Project ADD COLUMN IconFill INTEGER NOT NULL DEFAULT 0";
            await migrate.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException) { /* column already exists */ }
    }

    public async Task<IReadOnlyList<RootFolder>> GetRootsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<RootFolder>();
        using var lease = await LeaseAsync(cancellationToken);
        var connection = lease.Connection;
        var command = connection.CreateCommand(); command.CommandText = "SELECT Id, Path, CreatedAt FROM RootFolder ORDER BY Path";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2))));
        return result;
    }

    public async Task AddRootAsync(RootFolder root, CancellationToken cancellationToken = default)
    {
        using var lease = await LeaseAsync(cancellationToken);
        var connection = lease.Connection;
        var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO RootFolder(Id, Path, CreatedAt) VALUES($id,$path,$created)";
        command.Parameters.AddWithValue("$id", root.Id.ToString()); command.Parameters.AddWithValue("$path", root.Path); command.Parameters.AddWithValue("$created", root.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveRootAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        using var lease = await LeaseAsync(cancellationToken);
        var connection = lease.Connection;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var sql in new[] { "DELETE FROM Project WHERE RootId=$id", "DELETE FROM RootFolder WHERE Id=$id" })
        { var command = connection.CreateCommand(); command.Transaction = (SqliteTransaction)transaction; command.CommandText = sql; command.Parameters.AddWithValue("$id", rootId.ToString()); await command.ExecuteNonQueryAsync(cancellationToken); }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<Project>();
        using var lease = await LeaseAsync(cancellationToken);
        var connection = lease.Connection;
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,Name,Path,RootId,PrimaryTechnology,TechnologiesJson,HierarchyJson,PresetsJson,LastOpenedAt,CreatedAt,UpdatedAt,CustomIconPath,Category,IconFill FROM Project ORDER BY Name";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), Guid.Parse(reader.GetString(3)), reader.GetString(4),
                JsonSerializer.Deserialize<string[]>(reader.GetString(5)) ?? [], JsonSerializer.Deserialize<string[]>(reader.GetString(6)) ?? [],
                JsonSerializer.Deserialize<CommandPreset[]>(reader.GetString(7)) ?? [], reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)),
                DateTimeOffset.Parse(reader.GetString(9)), DateTimeOffset.Parse(reader.GetString(10)), reader.IsDBNull(11) ? null : reader.GetString(11),
                Category: reader.IsDBNull(12) ? null : reader.GetString(12), IconFill: reader.GetInt64(13) != 0));
        return result;
    }

    public async Task UpsertProjectsAsync(IEnumerable<Project> projects, CancellationToken cancellationToken = default)
    {
        using var lease = await LeaseAsync(cancellationToken);
        var connection = lease.Connection;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var project in projects)
        {
            var command = connection.CreateCommand(); command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO Project(Id,Name,Path,RootId,PrimaryTechnology,TechnologiesJson,HierarchyJson,PresetsJson,LastOpenedAt,CreatedAt,UpdatedAt,CustomIconPath,Category,IconFill)
                VALUES($id,$name,$path,$root,$primary,$tech,$hierarchy,$presets,$opened,$created,$updated,$icon,$category,$iconFill)
                ON CONFLICT(Id) DO UPDATE SET Name=$name,Path=$path,PrimaryTechnology=$primary,TechnologiesJson=$tech,HierarchyJson=$hierarchy,PresetsJson=$presets,UpdatedAt=$updated,Category=COALESCE(Project.Category,excluded.Category),IconFill=Project.IconFill;
                """;
            command.Parameters.AddWithValue("$id", project.Id.ToString()); command.Parameters.AddWithValue("$name", project.Name); command.Parameters.AddWithValue("$path", project.Path);
            command.Parameters.AddWithValue("$root", project.RootId.ToString()); command.Parameters.AddWithValue("$primary", project.PrimaryTechnology);
            command.Parameters.AddWithValue("$tech", JsonSerializer.Serialize(project.Technologies)); command.Parameters.AddWithValue("$hierarchy", JsonSerializer.Serialize(project.Hierarchy));
            command.Parameters.AddWithValue("$presets", JsonSerializer.Serialize(project.Presets)); command.Parameters.AddWithValue("$opened", (object?)project.LastOpenedAt?.ToString("O") ?? DBNull.Value);
            command.Parameters.AddWithValue("$created", (project.CreatedAt ?? DateTimeOffset.UtcNow).ToString("O")); command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$icon", (object?)project.CustomIconPath ?? DBNull.Value); command.Parameters.AddWithValue("$category", (object?)project.Category ?? DBNull.Value); command.Parameters.AddWithValue("$iconFill", project.IconFill ? 1 : 0); await command.ExecuteNonQueryAsync(cancellationToken);

            await ExecuteAsync(connection, (SqliteTransaction)transaction, "DELETE FROM DetectedTechnology WHERE ProjectId=$id; DELETE FROM ProjectTag WHERE ProjectId=$id; DELETE FROM TerminalPreset WHERE ProjectId=$id", project.Id, cancellationToken);
            foreach (var technology in project.Technologies)
                await InsertPairAsync(connection, (SqliteTransaction)transaction, "DetectedTechnology", project.Id, technology, cancellationToken);
            foreach (var tag in project.Hierarchy)
                await InsertPairAsync(connection, (SqliteTransaction)transaction, "ProjectTag", project.Id, tag, cancellationToken);
            foreach (var preset in project.Presets)
            {
                var presetCommand = connection.CreateCommand(); presetCommand.Transaction = (SqliteTransaction)transaction;
                presetCommand.CommandText = "INSERT OR REPLACE INTO TerminalPreset(ProjectId,Name,Command) VALUES($id,$name,$command)";
                presetCommand.Parameters.AddWithValue("$id", project.Id.ToString()); presetCommand.Parameters.AddWithValue("$name", preset.Name); presetCommand.Parameters.AddWithValue("$command", preset.Command);
                await presetCommand.ExecuteNonQueryAsync(cancellationToken);
            }
            if (project.CustomIconPath is not null)
            {
                var iconCommand = connection.CreateCommand(); iconCommand.Transaction = (SqliteTransaction)transaction;
                iconCommand.CommandText = "INSERT OR REPLACE INTO ProjectIcon(ProjectId,Path,UpdatedAt) VALUES($id,$path,$updated)";
                iconCommand.Parameters.AddWithValue("$id", project.Id.ToString()); iconCommand.Parameters.AddWithValue("$path", project.CustomIconPath); iconCommand.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                await iconCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteProjectsAsync(IEnumerable<Guid> projectIds, CancellationToken cancellationToken = default)
    {
        var ids = projectIds.Distinct().ToArray();
        if (ids.Length == 0) return;
        using var lease = await LeaseAsync(cancellationToken);
        var connection = lease.Connection;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var id in ids)
            await ExecuteAsync(connection, (SqliteTransaction)transaction, "DELETE FROM DetectedTechnology WHERE ProjectId=$id; DELETE FROM ProjectTag WHERE ProjectId=$id; DELETE FROM TerminalPreset WHERE ProjectId=$id; DELETE FROM ProjectIcon WHERE ProjectId=$id; DELETE FROM Project WHERE Id=$id", id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetProjectIconAsync(Guid projectId, string? iconPath, CancellationToken cancellationToken = default)
    {
        using var lease = await LeaseAsync(cancellationToken);
        var connection = lease.Connection;
        var command = connection.CreateCommand(); command.CommandText = "UPDATE Project SET CustomIconPath=$path,UpdatedAt=$updated WHERE Id=$id";
        command.Parameters.AddWithValue("$path", (object?)iconPath ?? DBNull.Value); command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O")); command.Parameters.AddWithValue("$id", projectId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
        var metadata = connection.CreateCommand();
        metadata.CommandText = iconPath is null ? "DELETE FROM ProjectIcon WHERE ProjectId=$id" : "INSERT OR REPLACE INTO ProjectIcon(ProjectId,Path,UpdatedAt) VALUES($id,$path,$updated)";
        metadata.Parameters.AddWithValue("$id", projectId.ToString()); metadata.Parameters.AddWithValue("$path", (object?)iconPath ?? DBNull.Value); metadata.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await metadata.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetProjectIconFillAsync(Guid projectId, bool fill, CancellationToken cancellationToken = default)
    {
        using var lease = await LeaseAsync(cancellationToken);
        var connection = lease.Connection;
        var command = connection.CreateCommand(); command.CommandText = "UPDATE Project SET IconFill=$fill,UpdatedAt=$updated WHERE Id=$id";
        command.Parameters.AddWithValue("$fill", fill ? 1 : 0); command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O")); command.Parameters.AddWithValue("$id", projectId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetProjectCategoryAsync(Guid projectId, string? category, CancellationToken cancellationToken = default)
    {
        using var lease = await LeaseAsync(cancellationToken);
        var connection = lease.Connection;
        var command = connection.CreateCommand(); command.CommandText = "UPDATE Project SET Category=$category,UpdatedAt=$updated WHERE Id=$id";
        command.Parameters.AddWithValue("$category", (object?)category ?? DBNull.Value); command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O")); command.Parameters.AddWithValue("$id", projectId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetProjectOpenedAsync(Guid projectId, DateTimeOffset openedAt, CancellationToken cancellationToken = default)
    {
        using var lease = await LeaseAsync(cancellationToken);
        var connection = lease.Connection;
        var command = connection.CreateCommand(); command.CommandText = "UPDATE Project SET LastOpenedAt=$opened WHERE Id=$id";
        command.Parameters.AddWithValue("$opened", openedAt.ToString("O")); command.Parameters.AddWithValue("$id", projectId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        using var lease = await LeaseAsync(cancellationToken);
        var connection = lease.Connection;
        var command = connection.CreateCommand(); command.CommandText = "SELECT Value FROM AppSetting WHERE Key=$key";
        command.Parameters.AddWithValue("$key", key);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value as string;
    }

    public async Task SetSettingAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        using var lease = await LeaseAsync(cancellationToken);
        var connection = lease.Connection;
        var command = connection.CreateCommand();
        command.CommandText = string.IsNullOrEmpty(value) ? "DELETE FROM AppSetting WHERE Key=$key" : "INSERT OR REPLACE INTO AppSetting(Key,Value) VALUES($key,$value)";
        command.Parameters.AddWithValue("$key", key); command.Parameters.AddWithValue("$value", (object?)value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetSettingsAsync(IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken = default)
    {
        if (values.Count == 0) return;
        using var lease = await LeaseAsync(cancellationToken);
        var connection = lease.Connection;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var (key, value) in values)
        {
            var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = string.IsNullOrEmpty(value) ? "DELETE FROM AppSetting WHERE Key=$key" : "INSERT OR REPLACE INTO AppSetting(Key,Value) VALUES($key,$value)";
            command.Parameters.AddWithValue("$key", key); command.Parameters.AddWithValue("$value", (object?)value ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, Guid projectId, CancellationToken cancellationToken)
    { var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; command.Parameters.AddWithValue("$id", projectId.ToString()); await command.ExecuteNonQueryAsync(cancellationToken); }

    private static async Task InsertPairAsync(SqliteConnection connection, SqliteTransaction transaction, string table, Guid projectId, string value, CancellationToken cancellationToken)
    { var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = $"INSERT OR REPLACE INTO {table}(ProjectId,Name) VALUES($id,$name)"; command.Parameters.AddWithValue("$id", projectId.ToString()); command.Parameters.AddWithValue("$name", value); await command.ExecuteNonQueryAsync(cancellationToken); }

    // One connection, opened once and held for the life of the app, serialized by a gate. Opening a
    // SqliteConnection per call meant every preference write — and there is one per panel resize,
    // sort change and window close — paid to reopen the database file.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SqliteConnection? _connection;

    private async Task<Lease> LeaseAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        if (_connection is null)
        {
            _connection = new SqliteConnection(_connectionString);
            await _connection.OpenAsync(cancellationToken);
        }
        return new Lease(this);
    }

    private readonly struct Lease(SqliteProjectRepository owner) : IDisposable
    {
        public SqliteConnection Connection => owner._connection!;
        public void Dispose() => owner._gate.Release();
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
        _gate.Dispose();
    }
}
