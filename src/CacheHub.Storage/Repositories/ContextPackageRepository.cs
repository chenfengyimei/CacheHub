using System.Globalization;
using System.Text.Json;
using CacheHub.Core.Context;
using CacheHub.Core.Identifiers;
using CacheHub.Storage.Database;
using Microsoft.Data.Sqlite;

namespace CacheHub.Storage.Repositories;

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
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = false };

    public async Task SaveAsync(ContextPackageManifest manifest, CancellationToken ct = default)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO context_packages
                (id, schema_version, workspace_id, index_snapshot_id, task_text, ranking_profile_id,
                 ranking_profile_version, context_target, context_hard_limit, actual_estimate,
                 model_context_window, agent_reserved_tokens, response_reserved_tokens, safety_margin,
                 query_parser_version, tokenizer, tokenizer_version,
                 selected_file_count, excluded_count, selected_files_json, excluded_candidates_json,
                 cloud_send_allowed, secrets_scan_passed,
                 ignore_rules_hash, security_policy_version, secret_scanner_version, approval_id,
                 sensitive_exclusions_json,
                 context_engine_version, chunking_strategy_version, token_budget_policy_version,
                 created_at,
                 repository_commit, branch, dirty_state_hash,
                 extracted_symbols_json, extracted_paths_json, parser_versions_json,
                 repo_map_version, parent_package_id)
            VALUES ($id, $sv, $ws, $snap, $task, $rpId, $rpVer, $ct, $chl, $ae,
                    $mcw, $art, $rrt, $sm, $qpv, $tok, $tokVer,
                    $sfc, $ec, $sfJson, $ecJson,
                    $csa, $ssp,
                    $irh, $spv, $ssv, $aid,
                    $seJson,
                    $cev, $csv, $tbpv, $ca,
                    $rc, $br, $dsh,
                    $esJson, $epJson, $pvJson,
                    $rmv, $ppid);
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
        AddParam(cmd, "$mcw", manifest.Budget.ModelContextWindow);
        AddParam(cmd, "$art", manifest.Budget.AgentReservedTokens);
        AddParam(cmd, "$rrt", manifest.Budget.ResponseReservedTokens);
        AddParam(cmd, "$sm", manifest.Budget.SafetyMargin);
        AddParam(cmd, "$qpv", manifest.Task.QueryParserVersion);
        AddParam(cmd, "$tok", (object?)manifest.Budget.Tokenizer ?? DBNull.Value);
        AddParam(cmd, "$tokVer", (object?)manifest.Budget.TokenizerVersion ?? DBNull.Value);
        AddParam(cmd, "$sfc", manifest.SelectedFiles.Count);
        AddParam(cmd, "$ec", manifest.ExcludedCandidates.Count);
        AddParam(cmd, "$sfJson", JsonSerializer.Serialize(manifest.SelectedFiles, _jsonOpts));
        AddParam(cmd, "$ecJson", JsonSerializer.Serialize(manifest.ExcludedCandidates, _jsonOpts));
        AddParam(cmd, "$csa", manifest.Safety.CloudSendAllowed ? 1 : 0);
        AddParam(cmd, "$ssp", manifest.Safety.SecretsScanPassed ? 1 : 0);
        AddParam(cmd, "$irh", (object?)manifest.Safety.IgnoreRulesHash ?? DBNull.Value);
        AddParam(cmd, "$spv", (object?)manifest.Safety.SecurityPolicyVersion ?? DBNull.Value);
        AddParam(cmd, "$ssv", (object?)manifest.Safety.SecretScannerVersion ?? DBNull.Value);
        AddParam(cmd, "$aid", (object?)manifest.Safety.ApprovalId ?? DBNull.Value);
        AddParam(cmd, "$seJson", manifest.Safety.SensitiveExclusions is not null
            ? JsonSerializer.Serialize(manifest.Safety.SensitiveExclusions, _jsonOpts)
            : DBNull.Value);
        AddParam(cmd, "$cev", manifest.ContextEngineVersion);
        AddParam(cmd, "$csv", manifest.ChunkingStrategyVersion);
        AddParam(cmd, "$tbpv", manifest.TokenBudgetPolicyVersion);
        AddParam(cmd, "$ca", manifest.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        AddParam(cmd, "$rc", (object?)manifest.RepositoryCommit ?? DBNull.Value);
        AddParam(cmd, "$br", (object?)manifest.Branch ?? DBNull.Value);
        AddParam(cmd, "$dsh", (object?)manifest.DirtyStateHash ?? DBNull.Value);
        AddParam(cmd, "$esJson", manifest.Task.ExtractedSymbols is not null
            ? JsonSerializer.Serialize(manifest.Task.ExtractedSymbols, _jsonOpts)
            : DBNull.Value);
        AddParam(cmd, "$epJson", manifest.Task.ExtractedPaths is not null
            ? JsonSerializer.Serialize(manifest.Task.ExtractedPaths, _jsonOpts)
            : DBNull.Value);
        AddParam(cmd, "$pvJson", manifest.ParserVersions is not null
            ? JsonSerializer.Serialize(manifest.ParserVersions, _jsonOpts)
            : DBNull.Value);
        AddParam(cmd, "$rmv", (object?)manifest.RepoMapVersion ?? DBNull.Value);
        AddParam(cmd, "$ppid", (object?)manifest.ParentPackageId?.Value ?? DBNull.Value);
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
        var selectedFiles = DeserializeList<SelectedFile>(reader, "selected_files_json");
        var excludedCandidates = DeserializeList<ExcludedCandidate>(reader, "excluded_candidates_json");
        var sensitiveExclusions = DeserializeList<string>(reader, "sensitive_exclusions_json");
        var extractedSymbols = DeserializeList<string>(reader, "extracted_symbols_json");
        var extractedPaths = DeserializeList<string>(reader, "extracted_paths_json");
        var parserVersionsJson = GetNullableString(reader, "parser_versions_json");
        var parserVersions = string.IsNullOrEmpty(parserVersionsJson)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(parserVersionsJson, _jsonOpts);
        var parentPackageId = GetNullableString(reader, "parent_package_id");

        return new ContextPackageManifest
        {
            Id = ContextPackageId.Parse(reader.GetString(reader.GetOrdinal("id"))),
            SchemaVersion = reader.GetInt32(reader.GetOrdinal("schema_version")),
            WorkspaceId = WorkspaceId.Parse(reader.GetString(reader.GetOrdinal("workspace_id"))),
            IndexSnapshotId = IndexSnapshotId.Parse(reader.GetString(reader.GetOrdinal("index_snapshot_id"))),
            RepositoryCommit = GetNullableString(reader, "repository_commit"),
            Branch = GetNullableString(reader, "branch"),
            DirtyStateHash = GetNullableString(reader, "dirty_state_hash"),
            Task = new TaskInfo
            {
                OriginalText = reader.GetString(reader.GetOrdinal("task_text")),
                QueryParserVersion = reader.GetString(reader.GetOrdinal("query_parser_version")),
                ExtractedSymbols = extractedSymbols.Count > 0 ? extractedSymbols : null,
                ExtractedPaths = extractedPaths.Count > 0 ? extractedPaths : null,
            },
            Ranking = new RankingInfo
            {
                ProfileId = reader.GetString(reader.GetOrdinal("ranking_profile_id")),
                ProfileVersion = reader.GetInt32(reader.GetOrdinal("ranking_profile_version")),
            },
            Budget = new BudgetInfo
            {
                ModelContextWindow = reader.GetInt32(reader.GetOrdinal("model_context_window")),
                AgentReservedTokens = reader.GetInt32(reader.GetOrdinal("agent_reserved_tokens")),
                ResponseReservedTokens = reader.GetInt32(reader.GetOrdinal("response_reserved_tokens")),
                ContextTarget = reader.GetInt32(reader.GetOrdinal("context_target")),
                ContextHardLimit = reader.GetInt32(reader.GetOrdinal("context_hard_limit")),
                SafetyMargin = reader.GetInt32(reader.GetOrdinal("safety_margin")),
                ActualEstimate = reader.GetInt32(reader.GetOrdinal("actual_estimate")),
                Tokenizer = reader.IsDBNull(reader.GetOrdinal("tokenizer")) ? null : reader.GetString(reader.GetOrdinal("tokenizer")),
                TokenizerVersion = reader.IsDBNull(reader.GetOrdinal("tokenizer_version")) ? null : reader.GetString(reader.GetOrdinal("tokenizer_version")),
            },
            SelectedFiles = selectedFiles,
            ExcludedCandidates = excludedCandidates,
            Safety = new SafetyInfo
            {
                CloudSendAllowed = reader.GetInt32(reader.GetOrdinal("cloud_send_allowed")) == 1,
                SecretsScanPassed = reader.GetInt32(reader.GetOrdinal("secrets_scan_passed")) == 1,
                IgnoreRulesHash = reader.IsDBNull(reader.GetOrdinal("ignore_rules_hash")) ? null : reader.GetString(reader.GetOrdinal("ignore_rules_hash")),
                SecurityPolicyVersion = reader.IsDBNull(reader.GetOrdinal("security_policy_version")) ? null : reader.GetString(reader.GetOrdinal("security_policy_version")),
                SecretScannerVersion = reader.IsDBNull(reader.GetOrdinal("secret_scanner_version")) ? null : reader.GetString(reader.GetOrdinal("secret_scanner_version")),
                ApprovalId = reader.IsDBNull(reader.GetOrdinal("approval_id")) ? null : reader.GetString(reader.GetOrdinal("approval_id")),
                SensitiveExclusions = sensitiveExclusions,
            },
            ContextEngineVersion = reader.GetString(reader.GetOrdinal("context_engine_version")),
            ChunkingStrategyVersion = reader.GetString(reader.GetOrdinal("chunking_strategy_version")),
            TokenBudgetPolicyVersion = reader.GetString(reader.GetOrdinal("token_budget_policy_version")),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at")), CultureInfo.InvariantCulture),
            ParserVersions = parserVersions,
            RepoMapVersion = GetNullableString(reader, "repo_map_version"),
            ParentPackageId = parentPackageId is not null ? ContextPackageId.Parse(parentPackageId) : null,
        };
    }

    private static List<T> DeserializeList<T>(SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal)) return [];
        var json = reader.GetString(ordinal);
        return string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<T>>(json, _jsonOpts) ?? [];
    }

    private static void AddParam(SqliteCommand cmd, string name, object value)
    {
        cmd.Parameters.AddWithValue(name, value is null ? DBNull.Value : value);
    }

    private static string? GetNullableString(SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
