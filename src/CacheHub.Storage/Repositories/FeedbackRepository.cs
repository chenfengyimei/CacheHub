using System.Globalization;
using CacheHub.Core.Feedback;
using CacheHub.Storage.Database;
using Microsoft.Data.Sqlite;

namespace CacheHub.Storage.Repositories;

public interface IFeedbackRepository
{
    Task SaveAsync(ContextFeedback feedback, CancellationToken ct = default);
    Task<ContextFeedback?> FindByContextPackageIdAsync(string contextPackageId, CancellationToken ct = default);
    Task<IReadOnlyList<ContextFeedback>> ListByWorkspaceAsync(string workspaceId, int limit = 20, CancellationToken ct = default);
}

public sealed class SqliteFeedbackRepository(SqliteConnectionFactory factory) : IFeedbackRepository
{
    public async Task SaveAsync(ContextFeedback feedback, CancellationToken ct = default)
    {
        await using var conn = factory.CreateOpenConnection();
        var feedbackId = Guid.NewGuid().ToString("N");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO context_feedback
                (id, context_package_id, client_id, client_version, model,
                 task_completed, missing_context_reported, user_intervention_count,
                 total_workflow_input_tokens, total_workflow_output_tokens, tests_passed)
            VALUES ($id, $ctxId, $clientId, $clientVer, $model,
                    $taskDone, $missingCtx, $userInt, $inputTokens, $outputTokens, $testsPassed);
            """;
        cmd.Parameters.AddWithValue("$id", feedbackId);
        cmd.Parameters.AddWithValue("$ctxId", feedback.ContextPackageId);
        cmd.Parameters.AddWithValue("$clientId", (object?)feedback.ClientId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$clientVer", (object?)feedback.ClientVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$model", (object?)feedback.Model ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$taskDone", feedback.TaskCompleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$missingCtx", feedback.MissingContextReported ? 1 : 0);
        cmd.Parameters.AddWithValue("$userInt", feedback.UserInterventionCount);
        cmd.Parameters.AddWithValue("$inputTokens", (object?)feedback.TotalWorkflowInputTokens ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$outputTokens", (object?)feedback.TotalWorkflowOutputTokens ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$testsPassed", (object?)feedback.TestsPassed ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);

        // Save file lists
        await SaveFileListAsync(conn, feedbackId, "actually_read", feedback.FilesActuallyRead, ct);
        await SaveFileListAsync(conn, feedbackId, "additional_requested", feedback.AdditionalFilesRequested, ct);
        await SaveFileListAsync(conn, feedbackId, "selected_used", feedback.SelectedFilesUsed, ct);
        await SaveFileListAsync(conn, feedbackId, "selected_ignored", feedback.SelectedFilesIgnored, ct);
        await SaveFileListAsync(conn, feedbackId, "patch_files", feedback.PatchFiles, ct);
        await SaveFileListAsync(conn, feedbackId, "tests_run", feedback.TestsRun, ct);
    }

    public async Task<ContextFeedback?> FindByContextPackageIdAsync(string contextPackageId, CancellationToken ct = default)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM context_feedback WHERE context_package_id = $ctxId LIMIT 1;";
        cmd.Parameters.AddWithValue("$ctxId", contextPackageId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return MapFeedback(reader, conn);
    }

    public async Task<IReadOnlyList<ContextFeedback>> ListByWorkspaceAsync(string workspaceId, int limit = 20, CancellationToken ct = default)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT cf.* FROM context_feedback cf
            INNER JOIN context_packages cp ON cf.context_package_id = cp.id
            WHERE cp.workspace_id = $ws
            ORDER BY cf.created_at DESC LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        cmd.Parameters.AddWithValue("$limit", limit);
        var results = new List<ContextFeedback>();
        var pending = new List<(string feedbackId, ContextFeedback stub)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var feedbackId = reader.GetString(reader.GetOrdinal("id"));
            pending.Add((feedbackId, ReadScalarFields(reader)));
        }
        // Reader is now closed (end of while / disposed). Load file lists safely.
        foreach (var (fid, stub) in pending)
            results.Add(LoadFileLists(conn, fid, stub));
        return results;
    }

    private static ContextFeedback ReadScalarFields(SqliteDataReader reader) => new()
    {
        ContextPackageId = reader.GetString(reader.GetOrdinal("context_package_id")),
        ClientId = reader.IsDBNull(reader.GetOrdinal("client_id")) ? null : reader.GetString(reader.GetOrdinal("client_id")),
        ClientVersion = reader.IsDBNull(reader.GetOrdinal("client_version")) ? null : reader.GetString(reader.GetOrdinal("client_version")),
        Model = reader.IsDBNull(reader.GetOrdinal("model")) ? null : reader.GetString(reader.GetOrdinal("model")),
        TaskCompleted = reader.GetInt32(reader.GetOrdinal("task_completed")) == 1,
        MissingContextReported = reader.GetInt32(reader.GetOrdinal("missing_context_reported")) == 1,
        UserInterventionCount = reader.GetInt32(reader.GetOrdinal("user_intervention_count")),
        TotalWorkflowInputTokens = reader.IsDBNull(reader.GetOrdinal("total_workflow_input_tokens")) ? null : reader.GetInt32(reader.GetOrdinal("total_workflow_input_tokens")),
        TotalWorkflowOutputTokens = reader.IsDBNull(reader.GetOrdinal("total_workflow_output_tokens")) ? null : reader.GetInt32(reader.GetOrdinal("total_workflow_output_tokens")),
        TestsPassed = reader.IsDBNull(reader.GetOrdinal("tests_passed")) ? null : reader.GetInt32(reader.GetOrdinal("tests_passed")) == 1,
        FilesActuallyRead = [],
        AdditionalFilesRequested = [],
        SelectedFilesUsed = [],
        SelectedFilesIgnored = [],
        PatchFiles = [],
        TestsRun = [],
    };

    private static ContextFeedback LoadFileLists(SqliteConnection conn, string feedbackId, ContextFeedback stub) => stub with
    {
        FilesActuallyRead = LoadFileList(conn, feedbackId, "actually_read"),
        AdditionalFilesRequested = LoadFileList(conn, feedbackId, "additional_requested"),
        SelectedFilesUsed = LoadFileList(conn, feedbackId, "selected_used"),
        SelectedFilesIgnored = LoadFileList(conn, feedbackId, "selected_ignored"),
        PatchFiles = LoadFileList(conn, feedbackId, "patch_files"),
        TestsRun = LoadFileList(conn, feedbackId, "tests_run"),
    };

    private static async Task SaveFileListAsync(SqliteConnection conn, string feedbackId, string fileType, IReadOnlyList<string> files, CancellationToken ct)
    {
        foreach (var file in files)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO feedback_files (id, feedback_id, file_path, file_type) VALUES ($id, $fid, $path, $type);";
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("$fid", feedbackId);
            cmd.Parameters.AddWithValue("$path", file);
            cmd.Parameters.AddWithValue("$type", fileType);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static ContextFeedback MapFeedback(SqliteDataReader reader, SqliteConnection conn)
    {
        var feedbackId = reader.GetString(reader.GetOrdinal("id"));
        var ctxId = reader.GetString(reader.GetOrdinal("context_package_id"));
        return new ContextFeedback
        {
            ContextPackageId = ctxId,
            ClientId = reader.IsDBNull(reader.GetOrdinal("client_id")) ? null : reader.GetString(reader.GetOrdinal("client_id")),
            ClientVersion = reader.IsDBNull(reader.GetOrdinal("client_version")) ? null : reader.GetString(reader.GetOrdinal("client_version")),
            Model = reader.IsDBNull(reader.GetOrdinal("model")) ? null : reader.GetString(reader.GetOrdinal("model")),
            TaskCompleted = reader.GetInt32(reader.GetOrdinal("task_completed")) == 1,
            MissingContextReported = reader.GetInt32(reader.GetOrdinal("missing_context_reported")) == 1,
            UserInterventionCount = reader.GetInt32(reader.GetOrdinal("user_intervention_count")),
            TotalWorkflowInputTokens = reader.IsDBNull(reader.GetOrdinal("total_workflow_input_tokens")) ? null : reader.GetInt32(reader.GetOrdinal("total_workflow_input_tokens")),
            TotalWorkflowOutputTokens = reader.IsDBNull(reader.GetOrdinal("total_workflow_output_tokens")) ? null : reader.GetInt32(reader.GetOrdinal("total_workflow_output_tokens")),
            TestsPassed = reader.IsDBNull(reader.GetOrdinal("tests_passed")) ? null : reader.GetInt32(reader.GetOrdinal("tests_passed")) == 1,
            FilesActuallyRead = LoadFileList(conn, feedbackId, "actually_read"),
            AdditionalFilesRequested = LoadFileList(conn, feedbackId, "additional_requested"),
            SelectedFilesUsed = LoadFileList(conn, feedbackId, "selected_used"),
            SelectedFilesIgnored = LoadFileList(conn, feedbackId, "selected_ignored"),
            PatchFiles = LoadFileList(conn, feedbackId, "patch_files"),
            TestsRun = LoadFileList(conn, feedbackId, "tests_run"),
        };
    }

    private static List<string> LoadFileList(SqliteConnection conn, string feedbackId, string fileType)
    {
        var result = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT file_path FROM feedback_files WHERE feedback_id = $fid AND file_type = $type;";
        cmd.Parameters.AddWithValue("$fid", feedbackId);
        cmd.Parameters.AddWithValue("$type", fileType);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(reader.GetString(0));
        return result;
    }
}
