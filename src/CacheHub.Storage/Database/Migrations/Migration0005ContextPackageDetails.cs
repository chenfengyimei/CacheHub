using Microsoft.Data.Sqlite;

namespace CacheHub.Storage.Database.Migrations;

/// <summary>
/// Migration 5: Adds budget detail columns and JSON payload columns to context_packages
/// to prevent data loss on read (budget fields were hardcoded; selected/excluded lists were empty).
/// </summary>
public sealed class Migration0005ContextPackageDetails : MigrationBase
{
    public Migration0005ContextPackageDetails() : base(5, "Add budget details and JSON payload to context_packages") { }

    public override void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        // Budget detail columns (were hardcoded in MapManifest)
        ExecuteSql(connection, transaction, "ALTER TABLE context_packages ADD COLUMN model_context_window INTEGER NOT NULL DEFAULT 128000;");
        ExecuteSql(connection, transaction, "ALTER TABLE context_packages ADD COLUMN agent_reserved_tokens INTEGER NOT NULL DEFAULT 18000;");
        ExecuteSql(connection, transaction, "ALTER TABLE context_packages ADD COLUMN response_reserved_tokens INTEGER NOT NULL DEFAULT 12000;");
        ExecuteSql(connection, transaction, "ALTER TABLE context_packages ADD COLUMN safety_margin INTEGER NOT NULL DEFAULT 10000;");

        // Query parser version (was hardcoded)
        ExecuteSql(connection, transaction, "ALTER TABLE context_packages ADD COLUMN query_parser_version TEXT NOT NULL DEFAULT 'deterministic-query-v1';");

        // Tokenizer info
        ExecuteSql(connection, transaction, "ALTER TABLE context_packages ADD COLUMN tokenizer TEXT;");
        ExecuteSql(connection, transaction, "ALTER TABLE context_packages ADD COLUMN tokenizer_version TEXT;");

        // JSON columns for selected files and excluded candidates (were always empty on read)
        ExecuteSql(connection, transaction, "ALTER TABLE context_packages ADD COLUMN selected_files_json TEXT NOT NULL DEFAULT '[]';");
        ExecuteSql(connection, transaction, "ALTER TABLE context_packages ADD COLUMN excluded_candidates_json TEXT NOT NULL DEFAULT '[]';");

        // Safety detail columns (were partially lost)
        ExecuteSql(connection, transaction, "ALTER TABLE context_packages ADD COLUMN ignore_rules_hash TEXT;");
        ExecuteSql(connection, transaction, "ALTER TABLE context_packages ADD COLUMN security_policy_version TEXT;");
        ExecuteSql(connection, transaction, "ALTER TABLE context_packages ADD COLUMN secret_scanner_version TEXT;");
        ExecuteSql(connection, transaction, "ALTER TABLE context_packages ADD COLUMN approval_id TEXT;");
        ExecuteSql(connection, transaction, "ALTER TABLE context_packages ADD COLUMN sensitive_exclusions_json TEXT;");
    }
}
