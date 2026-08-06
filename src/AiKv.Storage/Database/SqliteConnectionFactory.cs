using Microsoft.Data.Sqlite;

namespace AiKv.Storage.Database;

/// <summary>
/// Creates SQLite connections with WAL mode, busy timeout, and consistent settings.
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = 30, // seconds
        }.ToString();
    }

    public SqliteConnection CreateOpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText =
            """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA busy_timeout = 30000;
            PRAGMA foreign_keys = ON;
            """;
        pragma.ExecuteNonQuery();
        return connection;
    }

    public string ConnectionString => _connectionString;
}
