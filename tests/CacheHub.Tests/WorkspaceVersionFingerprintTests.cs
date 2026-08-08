using CacheHub.Core.Repository;
using CacheHub.Core.Identifiers;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// V7-W01: WorkspaceVersionFingerprint tests — git state capture, snapshot binding, and manifest provenance.
/// </summary>
public class WorkspaceVersionFingerprintTests
{
    [Fact]
    public void GitState_ComputeFingerprint_IsDeterministic()
    {
        var fp1 = GitState.ComputeFingerprint("abc123", "main", "/tmp", ["file1.cs", "file2.ts"]);
        var fp2 = GitState.ComputeFingerprint("abc123", "main", "/tmp", ["file1.cs", "file2.ts"]);
        Assert.Equal(fp1, fp2);
    }

    [Fact]
    public void GitState_ComputeFingerprint_DifferentCommit_DifferentFingerprint()
    {
        var fp1 = GitState.ComputeFingerprint("abc123", "main", "/tmp", []);
        var fp2 = GitState.ComputeFingerprint("def456", "main", "/tmp", []);
        Assert.NotEqual(fp1, fp2);
    }

    [Fact]
    public void GitState_ComputeFingerprint_DifferentBranch_DifferentFingerprint()
    {
        var fp1 = GitState.ComputeFingerprint("abc123", "main", "/tmp", []);
        var fp2 = GitState.ComputeFingerprint("abc123", "dev", "/tmp", []);
        Assert.NotEqual(fp1, fp2);
    }

    [Fact]
    public void GitState_ComputeFingerprint_DifferentDirtyFiles_DifferentFingerprint()
    {
        var fp1 = GitState.ComputeFingerprint("abc123", "main", "/tmp", ["file1.cs"]);
        var fp2 = GitState.ComputeFingerprint("abc123", "main", "/tmp", ["file2.ts"]);
        Assert.NotEqual(fp1, fp2);
    }

    [Fact]
    public void GitState_CreateNonGit_HasFingerprint()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cachehub_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "test.cs"), "public class Foo {}");
            var state = GitState.CreateNonGit(tempDir, ["test.cs"]);
            Assert.NotNull(state.Fingerprint);
            Assert.Null(state.Commit);
            Assert.Null(state.Branch);
            Assert.False(state.IsDirty);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Migration0011_AddsGitStateColumns()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cachehub_test_{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(dbPath);
            var runner = new MigrationRunner(factory, dbPath,
            [
                new Migration0001Initial(),
                new Migration0002Fts5(),
                new Migration0003ContextPackages(),
                new Migration0004Feedback(),
                new Migration0005ContextPackageDetails(),
                new Migration0006SchemaV2(),
                new Migration0007ContextPackageFields(),
                new Migration0008ContextPackageFk(),
                new Migration0009PersistentCache(),
                new Migration0010RelationSourceColumn(),
                new Migration0011SnapshotGitState(),
            ]);
            runner.Migrate();

            // Insert a workspace first (FK constraint)
            await using var conn = factory.CreateOpenConnection();
            using var wsCmd = conn.CreateCommand();
            wsCmd.CommandText = "INSERT INTO workspaces (id, name, root_path, root_path_hash, status) VALUES ('ws-1', 'test', '/tmp', 'hash1', 'Imported');";
            wsCmd.ExecuteNonQuery();

            // Insert a snapshot with git state
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO index_snapshots (id, workspace_id, status, file_count, repository_commit, branch, is_dirty, workspace_fingerprint)
                VALUES ('snap-1', 'ws-1', 'Active', 10, 'abc123', 'main', 1, 'fp-test-123');
                """;
            cmd.ExecuteNonQuery();

            // Read it back
            using var readCmd = conn.CreateCommand();
            readCmd.CommandText = "SELECT repository_commit, branch, is_dirty, workspace_fingerprint FROM index_snapshots WHERE id = 'snap-1';";
            await using var reader = readCmd.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("abc123", reader.GetString(0));
            Assert.Equal("main", reader.GetString(1));
            Assert.True(reader.GetBoolean(2));
            Assert.Equal("fp-test-123", reader.GetString(3));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void ContextBuildRequest_AcceptsGitStateFields()
    {
        var request = new CacheHub.Context.Engine.ContextBuildRequest
        {
            WorkspaceId = WorkspaceId.New(),
            IndexSnapshotId = IndexSnapshotId.New(),
            Task = "test task",
            RepositoryCommit = "abc123",
            Branch = "main",
            IsDirty = true,
            WorkspaceFingerprint = "fp-test-456",
        };

        Assert.Equal("abc123", request.RepositoryCommit);
        Assert.Equal("main", request.Branch);
        Assert.True(request.IsDirty);
        Assert.Equal("fp-test-456", request.WorkspaceFingerprint);
    }
}
