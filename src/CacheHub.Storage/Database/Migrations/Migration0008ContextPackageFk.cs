using Microsoft.Data.Sqlite;

namespace CacheHub.Storage.Database.Migrations;

/// <summary>
/// Migration 8: Clean up orphaned context_packages and create indexes for FK integrity.
/// SQLite doesn't support adding FK constraints to existing columns, so we add indexes
/// and a cleanup step to prevent orphan accumulation.
/// DATA-P2-001 fix.
/// </summary>
public sealed class Migration0008ContextPackageFk : MigrationBase
{
    public Migration0008ContextPackageFk() : base(8, "Context package FK indexes + orphan cleanup") { }

    public override void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        // 1. Clean up orphaned context_packages (workspace_id not in workspaces)
        ExecuteSql(connection, transaction,
            "DELETE FROM context_packages WHERE workspace_id NOT IN (SELECT id FROM workspaces);");

        // 2. Create indexes on context_packages FK columns for faster joins and integrity checks
        ExecuteSql(connection, transaction,
            "CREATE INDEX IF NOT EXISTS idx_context_packages_workspace ON context_packages(workspace_id);");

        ExecuteSql(connection, transaction,
            "CREATE INDEX IF NOT EXISTS idx_context_packages_snapshot ON context_packages(index_snapshot_id);");
    }
}
