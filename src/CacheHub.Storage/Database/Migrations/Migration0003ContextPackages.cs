using Microsoft.Data.Sqlite;

namespace CacheHub.Storage.Database.Migrations;

/// <summary>
/// Migration 3: Creates context_packages table for persisting manifests.
/// </summary>
public sealed class Migration0003ContextPackages : MigrationBase
{
    public Migration0003ContextPackages() : base(3, "Create context_packages table") { }

    public override void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        ExecuteSql(connection, transaction,
            """
            CREATE TABLE IF NOT EXISTS context_packages (
                id TEXT PRIMARY KEY,
                schema_version INTEGER NOT NULL,
                workspace_id TEXT NOT NULL,
                index_snapshot_id TEXT NOT NULL,
                task_text TEXT NOT NULL,
                ranking_profile_id TEXT NOT NULL,
                ranking_profile_version INTEGER NOT NULL,
                context_target INTEGER NOT NULL,
                context_hard_limit INTEGER NOT NULL,
                actual_estimate INTEGER NOT NULL,
                selected_file_count INTEGER NOT NULL DEFAULT 0,
                excluded_count INTEGER NOT NULL DEFAULT 0,
                cloud_send_allowed INTEGER NOT NULL DEFAULT 1,
                secrets_scan_passed INTEGER NOT NULL DEFAULT 1,
                context_engine_version TEXT NOT NULL,
                chunking_strategy_version TEXT NOT NULL,
                token_budget_policy_version TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE INDEX IF NOT EXISTS idx_ctx_packages_workspace ON context_packages(workspace_id);
            CREATE INDEX IF NOT EXISTS idx_ctx_packages_created ON context_packages(created_at);
            """);
    }
}
