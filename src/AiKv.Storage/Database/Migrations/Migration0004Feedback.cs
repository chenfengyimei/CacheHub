using Microsoft.Data.Sqlite;

namespace AiKv.Storage.Database.Migrations;

/// <summary>
/// Migration 4: Creates context_feedback table for persisting agent feedback.
/// </summary>
public sealed class Migration0004Feedback : MigrationBase
{
    public Migration0004Feedback() : base(4, "Create context_feedback table") { }

    public override void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        ExecuteSql(connection, transaction,
            """
            CREATE TABLE IF NOT EXISTS context_feedback (
                id TEXT PRIMARY KEY,
                context_package_id TEXT NOT NULL,
                client_id TEXT,
                client_version TEXT,
                model TEXT,
                task_completed INTEGER NOT NULL DEFAULT 0,
                missing_context_reported INTEGER NOT NULL DEFAULT 0,
                user_intervention_count INTEGER NOT NULL DEFAULT 0,
                total_workflow_input_tokens INTEGER,
                total_workflow_output_tokens INTEGER,
                tests_passed INTEGER,
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (context_package_id) REFERENCES context_packages(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS feedback_files (
                id TEXT PRIMARY KEY,
                feedback_id TEXT NOT NULL REFERENCES context_feedback(id) ON DELETE CASCADE,
                file_path TEXT NOT NULL,
                file_type TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE INDEX IF NOT EXISTS idx_feedback_context ON context_feedback(context_package_id);
            CREATE INDEX IF NOT EXISTS idx_feedback_files ON feedback_files(feedback_id);
            """);
    }
}
