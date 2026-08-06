using Microsoft.Data.Sqlite;

namespace CacheHub.Storage.Database.Migrations;

/// <summary>
/// Migration 7: Adds remaining ContextPackageManifest fields to context_packages
/// for complete round-trip persistence (R1-W010).
/// </summary>
public sealed class Migration0007ContextPackageFields : MigrationBase
{
    public Migration0007ContextPackageFields() : base(7, "Add repository_commit, branch, dirty_state_hash, extracted_symbols_json, extracted_paths_json, parser_versions_json, repo_map_version, parent_package_id to context_packages") { }

    public override void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        AddColumnIfNotExists(connection, transaction, "context_packages", "repository_commit", "TEXT");
        AddColumnIfNotExists(connection, transaction, "context_packages", "branch", "TEXT");
        AddColumnIfNotExists(connection, transaction, "context_packages", "dirty_state_hash", "TEXT");
        AddColumnIfNotExists(connection, transaction, "context_packages", "extracted_symbols_json", "TEXT");
        AddColumnIfNotExists(connection, transaction, "context_packages", "extracted_paths_json", "TEXT");
        AddColumnIfNotExists(connection, transaction, "context_packages", "parser_versions_json", "TEXT");
        AddColumnIfNotExists(connection, transaction, "context_packages", "repo_map_version", "TEXT");
        AddColumnIfNotExists(connection, transaction, "context_packages", "parent_package_id", "TEXT");
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
