using Microsoft.Data.Sqlite;

namespace CacheHub.Storage.Database.Migrations;

/// <summary>
/// Migration 5: Adds budget detail columns and JSON payload columns to context_packages
/// to prevent data loss on read (budget fields were hardcoded; selected/excluded lists were empty).
/// SQLite ALTER TABLE ADD COLUMN is DDL that does NOT roll back with transactions,
/// so we guard each ADD with a PRAGMA table_info existence check.
/// </summary>
public sealed class Migration0005ContextPackageDetails : MigrationBase
{
    public Migration0005ContextPackageDetails() : base(5, "Add budget details and JSON payload to context_packages") { }

    public override void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        // Budget detail columns (were hardcoded in MapManifest)
        AddColumnIfNotExists(connection, transaction, "context_packages", "model_context_window", "INTEGER NOT NULL DEFAULT 128000");
        AddColumnIfNotExists(connection, transaction, "context_packages", "agent_reserved_tokens", "INTEGER NOT NULL DEFAULT 18000");
        AddColumnIfNotExists(connection, transaction, "context_packages", "response_reserved_tokens", "INTEGER NOT NULL DEFAULT 12000");
        AddColumnIfNotExists(connection, transaction, "context_packages", "safety_margin", "INTEGER NOT NULL DEFAULT 10000");

        // Query parser version (was hardcoded)
        AddColumnIfNotExists(connection, transaction, "context_packages", "query_parser_version", "TEXT NOT NULL DEFAULT 'deterministic-query-v1'");

        // Tokenizer info
        AddColumnIfNotExists(connection, transaction, "context_packages", "tokenizer", "TEXT");
        AddColumnIfNotExists(connection, transaction, "context_packages", "tokenizer_version", "TEXT");

        // JSON columns for selected files and excluded candidates (were always empty on read)
        AddColumnIfNotExists(connection, transaction, "context_packages", "selected_files_json", "TEXT NOT NULL DEFAULT '[]'");
        AddColumnIfNotExists(connection, transaction, "context_packages", "excluded_candidates_json", "TEXT NOT NULL DEFAULT '[]'");

        // Safety detail columns (were partially lost)
        AddColumnIfNotExists(connection, transaction, "context_packages", "ignore_rules_hash", "TEXT");
        AddColumnIfNotExists(connection, transaction, "context_packages", "security_policy_version", "TEXT");
        AddColumnIfNotExists(connection, transaction, "context_packages", "secret_scanner_version", "TEXT");
        AddColumnIfNotExists(connection, transaction, "context_packages", "approval_id", "TEXT");
        AddColumnIfNotExists(connection, transaction, "context_packages", "sensitive_exclusions_json", "TEXT");
    }

    /// <summary>
    /// Adds a column to a table only if it doesn't already exist.
    /// SQLite ALTER TABLE ADD COLUMN is DDL that doesn't roll back, so we must guard.
    /// </summary>
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
            if (string.Equals(reader.GetString(1), column, System.StringComparison.OrdinalIgnoreCase))
                return; // Column already exists
        }
        reader.Close();

        ExecuteSql(connection, transaction, $"ALTER TABLE {table} ADD COLUMN {column} {definition};");
    }
}
