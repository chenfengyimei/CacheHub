using Microsoft.Data.Sqlite;

namespace CacheHub.Storage.Database.Migrations;

/// <summary>
/// Migration 11: Add Git state columns to index_snapshots for version-aware Context Packages.
/// V7-W01: Enables WorkspaceVersionFingerprint binding and stale detection.
/// </summary>
public sealed class Migration0011SnapshotGitState : MigrationBase
{
    public Migration0011SnapshotGitState() : base(11, "index_snapshots: add repository_commit, branch, is_dirty, workspace_fingerprint") { }

    public override void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        AddColumnIfNotExists(connection, transaction, "index_snapshots", "repository_commit", "TEXT");
        AddColumnIfNotExists(connection, transaction, "index_snapshots", "branch", "TEXT");
        AddColumnIfNotExists(connection, transaction, "index_snapshots", "is_dirty", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfNotExists(connection, transaction, "index_snapshots", "workspace_fingerprint", "TEXT");
    }

    private static void AddColumnIfNotExists(
        SqliteConnection connection, SqliteTransaction transaction,
        string table, string column, string definition)
    {
        using var checkCmd = connection.CreateCommand();
        checkCmd.Transaction = transaction;
        checkCmd.CommandText = $"PRAGMA table_info({table});";
        using var reader = checkCmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return;
        }
        reader.Close();

        ExecuteSql(connection, transaction, $"ALTER TABLE {table} ADD COLUMN {column} {definition};");
    }
}
