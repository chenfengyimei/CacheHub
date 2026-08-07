using CacheHub.Storage.Database;
using Microsoft.Data.Sqlite;

namespace CacheHub.Storage.Database.Migrations;

/// <summary>
/// Migration 0009: Persistent cache tables.
/// R7-W001: cache_entries, cache_dependencies, cache_stats.
/// </summary>
public sealed class Migration0009PersistentCache : MigrationBase
{
    public Migration0009PersistentCache() : base(9, "Persistent cache tables (cache_entries, cache_stats)") { }

    public override void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        ExecuteSql(connection, transaction,
            """
            CREATE TABLE IF NOT EXISTS cache_entries (
                key TEXT NOT NULL,
                cache_type TEXT NOT NULL,
                version TEXT NOT NULL,
                created_at TEXT NOT NULL,
                size_bytes INTEGER NOT NULL,
                dependency_hash TEXT,
                ttl_seconds INTEGER,
                producer_version TEXT,
                blob_path TEXT,
                content_hash TEXT,
                PRIMARY KEY (key, cache_type)
            );
            """);

        ExecuteSql(connection, transaction,
            "CREATE INDEX IF NOT EXISTS idx_cache_entries_dependency ON cache_entries(dependency_hash);");

        ExecuteSql(connection, transaction,
            "CREATE INDEX IF NOT EXISTS idx_cache_entries_type ON cache_entries(cache_type);");

        ExecuteSql(connection, transaction,
            """
            CREATE TABLE IF NOT EXISTS cache_stats (
                cache_type TEXT PRIMARY KEY,
                hits INTEGER NOT NULL DEFAULT 0,
                misses INTEGER NOT NULL DEFAULT 0,
                evictions INTEGER NOT NULL DEFAULT 0,
                total_bytes_stored INTEGER NOT NULL DEFAULT 0
            );
            """);
    }
}
