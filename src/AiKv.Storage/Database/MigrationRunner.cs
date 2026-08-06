using Microsoft.Data.Sqlite;

namespace AiKv.Storage.Database;

/// <summary>
/// Runs database migrations with schema version tracking.
/// On failure, the old database is preserved; migration is rolled back.
/// </summary>
public sealed class MigrationRunner
{
    private readonly SqliteConnectionFactory _factory;
    private readonly string _databasePath;
    private readonly IReadOnlyList<IMigration> _migrations;

    public MigrationRunner(SqliteConnectionFactory factory, string databasePath, IReadOnlyList<IMigration> migrations)
    {
        _factory = factory;
        _databasePath = databasePath;
        _migrations = migrations.OrderBy(m => m.Version).ToList();
    }

    /// <summary>
    /// Runs all pending migrations. Returns the number of migrations applied.
    /// </summary>
    public int Migrate()
    {
        EnsureSchemaVersionTable();

        var currentVersion = GetCurrentVersion();
        var pending = _migrations.Where(m => m.Version > currentVersion).ToList();

        if (pending.Count == 0) return 0;

        using var connection = _factory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var migration in pending)
            {
                migration.Up(connection, transaction);
                UpdateSchemaVersion(connection, transaction, migration.Version, migration.Description);
            }
            transaction.Commit();
            return pending.Count;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public int GetCurrentVersion()
    {
        using var connection = _factory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT MAX(version) FROM __schema_version WHERE applied = 1";
        var result = cmd.ExecuteScalar();
        return result is long version ? (int)version : 0;
    }

    private void EnsureSchemaVersionTable()
    {
        using var connection = _factory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS __schema_version (
                version INTEGER PRIMARY KEY,
                description TEXT NOT NULL,
                applied_at TEXT NOT NULL DEFAULT (datetime('now')),
                applied INTEGER NOT NULL DEFAULT 1
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static void UpdateSchemaVersion(SqliteConnection connection, SqliteTransaction transaction, int version, string description)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText =
            """
            INSERT INTO __schema_version (version, description) VALUES ($version, $description);
            """;
        cmd.Parameters.AddWithValue("$version", version);
        cmd.Parameters.AddWithValue("$description", description);
        cmd.ExecuteNonQuery();
    }
}
