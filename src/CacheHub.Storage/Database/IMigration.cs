using Microsoft.Data.Sqlite;

namespace CacheHub.Storage.Database;

/// <summary>
/// Represents a single database migration with a version number.
/// </summary>
public interface IMigration
{
    int Version { get; }
    string Description { get; }
    void Up(SqliteConnection connection, SqliteTransaction transaction);
}

/// <summary>
/// Base class for migrations providing common SQL execution.
/// </summary>
public abstract class MigrationBase(int version, string description) : IMigration
{
    public int Version { get; } = version;
    public string Description { get; } = description;

    public abstract void Up(SqliteConnection connection, SqliteTransaction transaction);

    protected static void ExecuteSql(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
