using System.Globalization;
using CacheHub.Storage.Database;
using Microsoft.Data.Sqlite;

namespace CacheHub.Storage.Repositories;

/// <summary>
/// Repository boundary for workspace persistence.
/// Business layer uses this interface; SQL stays in implementations.
/// </summary>
public interface IWorkspaceRepository
{
    Task<Core.Workspaces.Workspace> InsertAsync(Core.Workspaces.Workspace workspace, CancellationToken ct = default);
    Task<Core.Workspaces.Workspace?> FindByIdAsync(Core.Identifiers.WorkspaceId id, CancellationToken ct = default);
    Task<Core.Workspaces.Workspace?> FindByRootPathHashAsync(string rootPathHash, CancellationToken ct = default);
    Task<IReadOnlyList<Core.Workspaces.Workspace>> ListAllAsync(CancellationToken ct = default);
    Task UpdateStatusAsync(Core.Identifiers.WorkspaceId id, Core.Workspaces.WorkspaceStatus status, CancellationToken ct = default);
    Task RemoveAsync(Core.Identifiers.WorkspaceId id, CancellationToken ct = default);
}

/// <summary>
/// SQLite implementation of workspace repository.
/// </summary>
public sealed class SqliteWorkspaceRepository(SqliteConnectionFactory factory) : IWorkspaceRepository
{
    public async Task<Core.Workspaces.Workspace> InsertAsync(Core.Workspaces.Workspace workspace, CancellationToken ct = default)
    {
        await using var connection = factory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO workspaces (id, name, root_path, root_path_hash, status, security_policy_version, created_at, updated_at)
            VALUES ($id, $name, $rootPath, $rootPathHash, $status, $secPolicy, $createdAt, $updatedAt);
            """;
        cmd.Parameters.AddWithValue("$id", workspace.Id.Value);
        cmd.Parameters.AddWithValue("$name", workspace.Name);
        cmd.Parameters.AddWithValue("$rootPath", workspace.RootPath);
        cmd.Parameters.AddWithValue("$rootPathHash", workspace.RootPathHash);
        cmd.Parameters.AddWithValue("$status", workspace.Status.ToString());
        cmd.Parameters.AddWithValue("$secPolicy", (object?)workspace.SecurityPolicyVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$createdAt", workspace.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$updatedAt", workspace.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return workspace;
    }

    public async Task<Core.Workspaces.Workspace?> FindByIdAsync(Core.Identifiers.WorkspaceId id, CancellationToken ct = default)
    {
        await using var connection = factory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM workspaces WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id.Value);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapWorkspace(reader) : null;
    }

    public async Task<Core.Workspaces.Workspace?> FindByRootPathHashAsync(string rootPathHash, CancellationToken ct = default)
    {
        await using var connection = factory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM workspaces WHERE root_path_hash = $hash LIMIT 1;";
        cmd.Parameters.AddWithValue("$hash", rootPathHash);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapWorkspace(reader) : null;
    }

    public async Task<IReadOnlyList<Core.Workspaces.Workspace>> ListAllAsync(CancellationToken ct = default)
    {
        await using var connection = factory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM workspaces ORDER BY created_at DESC;";
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var results = new List<Core.Workspaces.Workspace>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(MapWorkspace(reader));
        }
        return results;
    }

    public async Task UpdateStatusAsync(Core.Identifiers.WorkspaceId id, Core.Workspaces.WorkspaceStatus status, CancellationToken ct = default)
    {
        await using var connection = factory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE workspaces SET status = $status, updated_at = $updatedAt WHERE id = $id;";
        cmd.Parameters.AddWithValue("$status", status.ToString());
        cmd.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$id", id.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Core.Identifiers.WorkspaceId id, CancellationToken ct = default)
    {
        await using var connection = factory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM workspaces WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static Core.Workspaces.Workspace MapWorkspace(SqliteDataReader reader)
    {
        return new Core.Workspaces.Workspace
        {
            Id = Core.Identifiers.WorkspaceId.Parse(reader.GetString(reader.GetOrdinal("id"))),
            Name = reader.GetString(reader.GetOrdinal("name")),
            RootPath = reader.GetString(reader.GetOrdinal("root_path")),
            RootPathHash = reader.GetString(reader.GetOrdinal("root_path_hash")),
            Status = Enum.Parse<Core.Workspaces.WorkspaceStatus>(reader.GetString(reader.GetOrdinal("status"))),
            SecurityPolicyVersion = reader.IsDBNull(reader.GetOrdinal("security_policy_version")) ? null : reader.GetString(reader.GetOrdinal("security_policy_version")),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at")), CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_at")), CultureInfo.InvariantCulture),
        };
    }
}
