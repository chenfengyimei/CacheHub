using Microsoft.Data.Sqlite;

namespace CacheHub.Storage.Database.Migrations;

/// <summary>
/// Migration 6: Schema v2 — adds mtime/hash_kind/parser_version to files,
/// creates symbols/imports/relations tables for parser results.
/// SQLite ALTER TABLE ADD COLUMN is DDL that does NOT roll back, so we guard each ADD.
/// </summary>
public sealed class Migration0006SchemaV2 : MigrationBase
{
    public Migration0006SchemaV2() : base(6, "Schema v2: file mtime/hash_kind/parser_version + symbols/imports/relations tables") { }

    public override void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        // files table additions: mtime, hash_kind, parser_version
        AddColumnIfNotExists(connection, transaction, "files", "mtime", "TEXT");
        AddColumnIfNotExists(connection, transaction, "files", "hash_kind", "TEXT NOT NULL DEFAULT 'full'");
        AddColumnIfNotExists(connection, transaction, "files", "parser_version", "TEXT");
        AddColumnIfNotExists(connection, transaction, "files", "parser_id", "TEXT");

        // symbols table — stores parsed code symbols per file
        ExecuteSql(connection, transaction,
            """
            CREATE TABLE IF NOT EXISTS file_symbols (
                id TEXT PRIMARY KEY,
                file_id TEXT NOT NULL REFERENCES files(id) ON DELETE CASCADE,
                snapshot_id TEXT NOT NULL REFERENCES index_snapshots(id) ON DELETE CASCADE,
                name TEXT NOT NULL,
                kind TEXT NOT NULL,
                start_line INTEGER NOT NULL,
                end_line INTEGER NOT NULL,
                modifier TEXT,
                confidence TEXT NOT NULL DEFAULT 'syntactic'
            );
            CREATE INDEX IF NOT EXISTS idx_symbols_file ON file_symbols(file_id);
            CREATE INDEX IF NOT EXISTS idx_symbols_snapshot ON file_symbols(snapshot_id);
            CREATE INDEX IF NOT EXISTS idx_symbols_name ON file_symbols(name);
            """);

        // imports table — stores parsed import statements per file
        ExecuteSql(connection, transaction,
            """
            CREATE TABLE IF NOT EXISTS file_imports (
                id TEXT PRIMARY KEY,
                file_id TEXT NOT NULL REFERENCES files(id) ON DELETE CASCADE,
                snapshot_id TEXT NOT NULL REFERENCES index_snapshots(id) ON DELETE CASCADE,
                module TEXT NOT NULL,
                imported_name TEXT,
                line INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_imports_file ON file_imports(file_id);
            CREATE INDEX IF NOT EXISTS idx_imports_snapshot ON file_imports(snapshot_id);
            CREATE INDEX IF NOT EXISTS idx_imports_module ON file_imports(module);
            """);

        // relations table — stores heuristic call/reference relations between symbols
        ExecuteSql(connection, transaction,
            """
            CREATE TABLE IF NOT EXISTS file_relations (
                id TEXT PRIMARY KEY,
                file_id TEXT NOT NULL REFERENCES files(id) ON DELETE CASCADE,
                snapshot_id TEXT NOT NULL REFERENCES index_snapshots(id) ON DELETE CASCADE,
                source_symbol TEXT NOT NULL,
                target_symbol TEXT NOT NULL,
                relation_type TEXT NOT NULL,
                confidence TEXT NOT NULL DEFAULT 'heuristic',
                line INTEGER
            );
            CREATE INDEX IF NOT EXISTS idx_relations_file ON file_relations(file_id);
            CREATE INDEX IF NOT EXISTS idx_relations_snapshot ON file_relations(snapshot_id);
            CREATE INDEX IF NOT EXISTS idx_relations_source ON file_relations(source_symbol);
            CREATE INDEX IF NOT EXISTS idx_relations_target ON file_relations(target_symbol);
            """);
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
