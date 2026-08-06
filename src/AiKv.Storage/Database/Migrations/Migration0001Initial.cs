using Microsoft.Data.Sqlite;

namespace AiKv.Storage.Database.Migrations;

/// <summary>
/// Migration 1: Creates base tables for workspaces, files, index snapshots, and jobs.
/// </summary>
public sealed class Migration0001Initial : MigrationBase
{
    public Migration0001Initial() : base(1, "Create base tables") { }

    public override void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        ExecuteSql(connection, transaction,
            """
            CREATE TABLE IF NOT EXISTS workspaces (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                root_path TEXT NOT NULL,
                root_path_hash TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'Imported',
                security_policy_version TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                updated_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS repositories (
                id TEXT PRIMARY KEY,
                workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
                url TEXT,
                branch TEXT,
                commit_hash TEXT,
                remote_type TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS components (
                id TEXT PRIMARY KEY,
                workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
                path TEXT NOT NULL,
                language TEXT,
                framework TEXT,
                build_system TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS index_snapshots (
                id TEXT PRIMARY KEY,
                workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
                status TEXT NOT NULL DEFAULT 'Building',
                file_count INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                completed_at TEXT
            );

            CREATE TABLE IF NOT EXISTS files (
                id TEXT PRIMARY KEY,
                snapshot_id TEXT NOT NULL REFERENCES index_snapshots(id) ON DELETE CASCADE,
                path TEXT NOT NULL,
                normalized_path TEXT NOT NULL,
                size INTEGER NOT NULL,
                content_hash TEXT,
                language TEXT,
                is_binary INTEGER NOT NULL DEFAULT 0,
                status TEXT NOT NULL DEFAULT 'Discovered',
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS background_jobs (
                id TEXT PRIMARY KEY,
                workspace_id TEXT REFERENCES workspaces(id) ON DELETE CASCADE,
                type TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'Queued',
                progress INTEGER NOT NULL DEFAULT 0,
                total INTEGER NOT NULL DEFAULT 0,
                error_message TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                started_at TEXT,
                completed_at TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_files_snapshot ON files(snapshot_id);
            CREATE INDEX IF NOT EXISTS idx_files_path ON files(normalized_path);
            CREATE INDEX IF NOT EXISTS idx_snapshots_workspace ON index_snapshots(workspace_id);
            CREATE INDEX IF NOT EXISTS idx_jobs_workspace ON background_jobs(workspace_id);
            """);
    }
}
