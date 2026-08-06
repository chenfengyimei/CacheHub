using System.Globalization;
using AiKv.Core.Context;
using AiKv.Core.Identifiers;
using AiKv.Storage.Database;
using Microsoft.Data.Sqlite;

namespace AiKv.Storage.Repositories;

/// <summary>
/// Persists Context Package manifests to SQLite.
/// </summary>
public interface IContextPackageRepository
{
    Task SaveAsync(ContextPackageManifest manifest, CancellationToken ct = default);
    Task<ContextPackageManifest?> FindByIdAsync(ContextPackageId id, CancellationToken ct = default);
    Task<IReadOnlyList<ContextPackageManifest>> ListByWorkspaceAsync(WorkspaceId workspaceId, int limit = 20, CancellationToken ct = default);
    Task RemoveAsync(ContextPackageId id, CancellationToken ct = default);
}

public sealed class SqliteContextPackageRepository(SqliteConnectionFactory factory) : IContextPackageRepository
{
    public async Task SaveAsync(ContextPackageManifest manifest, CancellationToken ct = default)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO context_packages
                (id, schema_version, workspace_id, index_snapshot_id, task_text, ranking_profile_id,
                 ranking_profile_version, context_target, context_hard_limit, actual_estimate,
                 selected_file_count, excluded_count, cloud_send_allowed, secrets_scan_passed,
                 context_engine_version, chunking_strategy_version, token_budget_policy_version,
                 created_at)
            VALUES ($id, $sv, $ws, $snap, $task, $rpId, $rpVer, $ct, $chl, $ae,
                    $sfc, $ec, $csa, $ssp, $cev, $csv, $tbpv, $ca);
            """;
        AddParam(cmd, "$id", manifest.Id.Value);
        AddParam(cmd, "$sv", manifest.SchemaVersion);
        AddParam(cmd, "$ws", manifest.WorkspaceId.Value);
        AddParam(cmd, "$snap", manifest.IndexSnapshotId.Value);
        AddParam(cmd, "$task", manifest.Task.OriginalText);
        AddParam(cmd, "$rpId", manifest.Ranking.ProfileId);
        AddParam(cmd, "$rpVer", manifest.Ranking.ProfileVersion);
        AddParam(cmd, "$ct", manifest.Budget.ContextTarget);
        AddParam(cmd, "$chl", manifest.Budget.ContextHardLimit);
        AddParam(cmd, "$ae", manifest.Budget.ActualEstimate);
        AddParam(cmd, "$sfc", manifest.SelectedFiles.Count);
        AddParam(cmd, "$ec", manifest.ExcludedCandidates.Count);
        AddParam(cmd, "$csa", manifest.Safety.CloudSendAllowed ? 1 : 0);
        AddParam(cmd, "$ssp", manifest.Safety.SecretsScanPassed ? 1 : 0);
        AddParam(cmd, "$cev", manifest.ContextEngineVersion);
        AddParam(cmd, "$csv", manifest.ChunkingStrategyVersion);
        AddParam(cmd, "$tbpv", manifest.TokenBudgetPolicyVersion);
        AddParam(cmd, "$ca", manifest.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<ContextPackageManifest?> FindByIdAsync(ContextPackageId id, CancellationToken ct = default)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM context_packages WHERE id = $id LIMIT 1;";
        AddParam(cmd, "$id", id.Value);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return MapManifest(reader);
    }

    public async Task<IReadOnlyList<ContextPackageManifest>> ListByWorkspaceAsync(WorkspaceId workspaceId, int limit = 20, CancellationToken ct = default)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM context_packages WHERE workspace_id = $ws ORDER BY created_at DESC LIMIT $limit;";
        AddParam(cmd, "$ws", workspaceId.Value);
        AddParam(cmd, "$limit", limit);
        var results = new List<ContextPackageManifest>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(MapManifest(reader));
        return results;
    }

    public async Task RemoveAsync(ContextPackageId id, CancellationToken ct = default)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM context_packages WHERE id = $id;";
        AddParam(cmd, "$id", id.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static ContextPackageManifest MapManifest(SqliteDataReader reader)
    {
        return new ContextPackageManifest
        {
            Id = ContextPackageId.Parse(reader.GetString(reader.GetOrdinal("id"))),
            SchemaVersion = reader.GetInt32(reader.GetOrdinal("schema_version")),
            WorkspaceId = WorkspaceId.Parse(reader.GetString(reader.GetOrdinal("workspace_id"))),
            IndexSnapshotId = IndexSnapshotId.Parse(reader.GetString(reader.GetOrdinal("index_snapshot_id"))),
            Task = new Core.Context.TaskInfo
            {
                OriginalText = reader.GetString(reader.GetOrdinal("task_text")),
                QueryParserVersion = "deterministic-query-v1",
            },
            Ranking = new Core.Context.RankingInfo
            {
                ProfileId = reader.GetString(reader.GetOrdinal("ranking_profile_id")),
                ProfileVersion = reader.GetInt32(reader.GetOrdinal("ranking_profile_version")),
            },
            Budget = new Core.Context.BudgetInfo
            {
                ModelContextWindow = 128000,
                AgentReservedTokens = 18000,
                ResponseReservedTokens = 12000,
                ContextTarget = reader.GetInt32(reader.GetOrdinal("context_target")),
                ContextHardLimit = reader.GetInt32(reader.GetOrdinal("context_hard_limit")),
                SafetyMargin = 10000,
                ActualEstimate = reader.GetInt32(reader.GetOrdinal("actual_estimate")),
            },
            SelectedFiles = [],
            ExcludedCandidates = [],
            Safety = new Core.Context.SafetyInfo
            {
                CloudSendAllowed = reader.GetInt32(reader.GetOrdinal("cloud_send_allowed")) == 1,
                SecretsScanPassed = reader.GetInt32(reader.GetOrdinal("secrets_scan_passed")) == 1,
            },
            ContextEngineVersion = reader.GetString(reader.GetOrdinal("context_engine_version")),
            ChunkingStrategyVersion = reader.GetString(reader.GetOrdinal("chunking_strategy_version")),
            TokenBudgetPolicyVersion = reader.GetString(reader.GetOrdinal("token_budget_policy_version")),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at")), CultureInfo.InvariantCulture),
        };
    }

    private static void AddParam(SqliteCommand cmd, string name, object value)
    {
        cmd.Parameters.AddWithValue(name, value is null ? DBNull.Value : value);
    }
}
