using Microsoft.Data.Sqlite;

namespace CacheHub.Storage.Database.Migrations;

/// <summary>
/// Migration 2: Creates FTS5 full-text search index for file contents.
/// </summary>
public sealed class Migration0002Fts5 : MigrationBase
{
    public Migration0002Fts5() : base(2, "Create FTS5 index") { }

    public override void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        ExecuteSql(connection, transaction,
            """
            CREATE VIRTUAL TABLE IF NOT EXISTS file_contents_fts USING fts5(
                path,
                normalized_path,
                content,
                language,
                content_hash,
                snapshot_id UNINDEXED,
                tokenize = 'unicode61'
            );

            CREATE TABLE IF NOT EXISTS file_chunks (
                id TEXT PRIMARY KEY,
                snapshot_id TEXT NOT NULL REFERENCES index_snapshots(id) ON DELETE CASCADE,
                file_path TEXT NOT NULL,
                start_line INTEGER NOT NULL,
                end_line INTEGER NOT NULL,
                content TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE INDEX IF NOT EXISTS idx_file_chunks_snapshot ON file_chunks(snapshot_id);
            CREATE INDEX IF NOT EXISTS idx_file_chunks_path ON file_chunks(file_path);
            """);
    }
}
