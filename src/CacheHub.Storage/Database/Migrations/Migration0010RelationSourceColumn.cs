using Microsoft.Data.Sqlite;

namespace CacheHub.Storage.Database.Migrations;

/// <summary>
/// Migration 10: Fix file_relations schema — add `source TEXT` column for parser name,
/// and clean up mangled data from the confidence/line/source mapping bug (P0-1).
/// </summary>
public sealed class Migration0010RelationSourceColumn : MigrationBase
{
    public Migration0010RelationSourceColumn() : base(10, "file_relations: add source column + fix mangled data") { }

    public override void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        // 1. Add source column for parser name (was previously mis-stored in confidence column)
        AddColumnIfNotExists(connection, transaction, "file_relations", "source", "TEXT");

        // 2. Migrate any existing data: the confidence column currently holds parser names (strings),
        //    and the line column holds confidence values (doubles stored as int).
        //    Move parser name from confidence → source, move confidence from line → confidence, reset line.
        ExecuteSql(connection, transaction,
            """
            UPDATE file_relations
            SET source = CASE
                WHEN confidence IS NOT NULL AND confidence != 'heuristic' AND confidence NOT LIKE '%[0-9]%' THEN confidence
                ELSE NULL
            END,
            confidence = CASE
                WHEN line IS NOT NULL AND line > 0 AND line <= 1 THEN CAST(line AS TEXT)
                ELSE '0'
            END,
            line = NULL
            WHERE confidence IS NOT NULL AND confidence != 'heuristic';
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
