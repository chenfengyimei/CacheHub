using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CacheHub.Desktop;
using CacheHub.Context.Cache;
using CacheHub.Context.Engine;
using CacheHub.Context.Explain;
using CacheHub.Context.Parsing;
using CacheHub.Context.Recall;
using CacheHub.Context.Expand;
using CacheHub.Context.Payload;
using CacheHub.Core.Configuration;
using CacheHub.Core.Workflow;
using CacheHub.Core.Capabilities;
using CacheHub.Core.Context;
using CacheHub.Core.Errors;
using CacheHub.Core.Feedback;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Paths;
using Microsoft.Data.Sqlite;
using CacheHub.Core.Parsing;
using CacheHub.Core.Semantic;
using CacheHub.Core.Workspaces;
using CacheHub.Indexing.Parsing;
using CacheHub.Storage;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Search;
using CacheHub.Storage.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Security: generate a random access token for API authentication
// Tests can override via configuration "ApiToken" or env var CACHEHUB_API_TOKEN
var accessToken = builder.Configuration["ApiToken"]
    ?? Environment.GetEnvironmentVariable("CACHEHUB_API_TOKEN")
    ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

// V7-W04: Launch nonce — one-time token for /api/v1/auth/init, prevents arbitrary process from claiming session
// In Testing environment, nonce is bypassed for test convenience
var isTestingEnv = builder.Environment.EnvironmentName == "Testing";
var launchNonce = isTestingEnv ? "test-nonce" : Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
var nonceUsed = false;

builder.Services.AddSingleton<AppDataDirectory>();
builder.Services.AddSingleton<SqliteConnectionFactory>(sp =>
{
    var appData = sp.GetRequiredService<AppDataDirectory>();
    appData.EnsureCreated();
    var dbPath = appData.GetWorkspaceDatabasePath("main");
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
    return factory;
});
builder.Services.AddSingleton<IWorkspaceRepository, SqliteWorkspaceRepository>();
builder.Services.AddSingleton<IContextPackageRepository, SqliteContextPackageRepository>();
builder.Services.AddSingleton<IFeedbackRepository, SqliteFeedbackRepository>();
builder.Services.AddSingleton<ContextPackageCache>(_ =>
{
    try
    {
        var appData = new AppDataDirectory();
        var cacheDbPath = Path.Combine(appData.Root, "context-cache", "cache.db");
        Directory.CreateDirectory(Path.GetDirectoryName(cacheDbPath)!);
        var cacheFactory = new SqliteConnectionFactory(cacheDbPath);
        var runner = new MigrationRunner(cacheFactory, cacheDbPath,
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
        var store = new CacheHub.Storage.Caching.SqliteCacheStore(cacheFactory,
            Path.Combine(appData.Root, "context-cache", "blobs"));
        return new ContextPackageCache(store);
    }
    catch
    {
        return new ContextPackageCache();
    }
});
builder.Services.AddSingleton<ContextEngine>();
builder.Services.AddSingleton<UsageStatsService>();

// Security: force loopback binding only
builder.WebHost.UseUrls("http://127.0.0.1:5099");

var app = builder.Build();

// Security: API authentication middleware — all /api/ routes require a valid bearer token
// V6: Also accept HttpOnly session cookie for same-origin GUI auto-authentication
// V7-W04: Host validation moved BEFORE auth/init; exact match (not StartsWith) to prevent DNS rebinding
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    if (path is not null && path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
    {
        // V7-W04: Host validation FIRST — before any auth logic, including /auth/init
        // Use exact match to prevent DNS rebinding (localhost.evil.com must NOT pass)
        var hostHeader = context.Request.Headers.Host.ToString();
        var hostPart = hostHeader.Split(':')[0]; // strip port
        if (!string.Equals(hostPart, "127.0.0.1", StringComparison.Ordinal) &&
            !string.Equals(hostPart, "localhost", StringComparison.Ordinal) &&
            !string.Equals(hostPart, "::1", StringComparison.Ordinal))
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"Forbidden\",\"code\":\"INVALID_HOST\",\"hint\":\"Only localhost/127.0.0.1 access is allowed.\"}");
            return;
        }

        // V7-W04: /api/v1/auth/init requires launch nonce (one-time use)
        if (path.Equals("/api/v1/auth/init", StringComparison.OrdinalIgnoreCase))
        {
            var nonce = context.Request.Query["nonce"].ToString();
            if (!isTestingEnv && (string.IsNullOrEmpty(nonce) || !nonce.Equals(launchNonce, StringComparison.Ordinal) || nonceUsed))
            {
                context.Response.StatusCode = 403;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"error\":\"Forbidden\",\"code\":\"INVALID_NONCE\",\"hint\":\"Launch nonce is required, invalid, or already used.\"}");
                return;
            }
            nonceUsed = true; // one-time use
            await next();
            return;
        }

#if DEBUG
        // In debug mode, allow requests without token if no Authorization header is present
        // This is for development convenience — production builds always require the token
#endif
        var authHeader = context.Request.Headers.Authorization.ToString();
        var cookieToken = context.Request.Cookies["cachehub_session"];

        var isAuthenticated = false;
        if (!string.IsNullOrEmpty(authHeader) && authHeader.Equals($"Bearer {accessToken}", StringComparison.OrdinalIgnoreCase))
            isAuthenticated = true;
        else if (!string.IsNullOrEmpty(cookieToken) && cookieToken.Equals(accessToken, StringComparison.Ordinal))
            isAuthenticated = true;

        if (!isAuthenticated)
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync($"{{\"error\":\"Unauthorized\",\"code\":\"AUTH_REQUIRED\",\"hint\":\"Call POST /api/v1/auth/init to get a session cookie, or use Authorization: Bearer <token> header.\"}}");
            return;
        }
    }

    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

// V8-P0-03: Replaced weak SafeResolvePath with shared SafePathResolver.
// This helper creates a resolver and resolves the path in one call.
// SafePathResolver handles traversal, prefix-boundary, symlink (including parent dirs), and absolute path rejection.
static string? ResolvePathSafe(string rootPath, string relativePath)
{
    return new SafePathResolver(rootPath).Resolve(relativePath);
}

// V8-P1-01: Creates a file filter for fingerprint scope.
// Only indexed files (not ignored, not binary) participate in the workspace fingerprint.
static Func<string, bool> CreateDesktopFingerprintFilter(
    string workspaceRoot,
    IEnumerable<string>? previouslyIndexedPaths = null)
{
    var ignoreEngine = new CacheHub.Indexing.IgnoreRules.IgnoreRuleEngine()
        .WithDefaults()
        .WithGitIgnore(System.IO.Path.Combine(workspaceRoot, ".gitignore"))
        .WithCacheHubIgnore(System.IO.Path.Combine(workspaceRoot, ".cachehubignore"));

    var knownIndexedPaths = previouslyIndexedPaths is null
        ? null
        : new HashSet<string>(previouslyIndexedPaths.Select(NormalizePath), StringComparer.OrdinalIgnoreCase);

    return relativePath =>
    {
        if (ignoreEngine.IsIgnored(relativePath)) return false;
        var fullPath = System.IO.Path.Combine(workspaceRoot, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(fullPath))
            return knownIndexedPaths?.Contains(NormalizePath(relativePath)) == true;
        var typeInfo = CacheHub.Indexing.Detection.FileTypeDetector.Detect(fullPath, new FileInfo(fullPath).Length);
        return typeInfo.ShouldIndex;
    };

    static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');
}

static async Task<List<IndexedFileInfo>> GetIndexedFilesAsync(SqliteConnectionFactory factory, string workspaceId)
{
    var result = new List<IndexedFileInfo>();
    await using var conn = factory.CreateOpenConnection();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        SELECT f.normalized_path, f.size, f.language, f.content_hash
        FROM files f
        INNER JOIN index_snapshots s ON f.snapshot_id = s.id
        WHERE s.workspace_id = $ws AND s.status IN ('Active', 'ActiveDegraded');
        """;
    cmd.Parameters.AddWithValue("$ws", workspaceId);
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        result.Add(new IndexedFileInfo
        {
            Path = reader.GetString(0),
            NormalizedPath = reader.GetString(0),
            Size = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
            Language = reader.IsDBNull(2) ? "unknown" : reader.GetString(2),
            ContentHash = reader.IsDBNull(3) ? null : reader.GetString(3),
            Symbols = [],
        });
    }
    return result;
}

static async Task<(IndexSnapshotId SnapshotId, string? RepositoryCommit, string? Branch, bool IsDirty, string? WorkspaceFingerprint)?> GetActiveSnapshotIdAsync(SqliteConnectionFactory factory, string workspaceId)
{
    await using var conn = factory.CreateOpenConnection();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT id, repository_commit, branch, is_dirty, workspace_fingerprint FROM index_snapshots WHERE workspace_id = $ws AND status IN ('Active', 'ActiveDegraded') LIMIT 1;";
    cmd.Parameters.AddWithValue("$ws", workspaceId);
    await using var reader = await cmd.ExecuteReaderAsync();
    if (await reader.ReadAsync())
    {
        var commit = reader.IsDBNull(1) ? null : reader.GetString(1);
        var branch = reader.IsDBNull(2) ? null : reader.GetString(2);
        var isDirty = !reader.IsDBNull(3) && reader.GetBoolean(3);
        var fingerprint = reader.IsDBNull(4) ? null : reader.GetString(4);
        return (IndexSnapshotId.Parse(reader.GetString(0)), commit, branch, isDirty, fingerprint);
    }
    return null;
}

static string ResolveFileHashFromDb(SqliteConnectionFactory factory, IndexSnapshotId snapshotId, string path, string? rootPath = null)
{
    // V7-W22: Always compute hash from disk first when file exists.
    // This ensures ContentHash matches the actual content being read from disk,
    // preventing "old Hash + new Content" inconsistency in Context Package.
    if (rootPath is not null)
    {
        var fullPath = ResolvePathSafe(rootPath, path);
        if (fullPath is not null && File.Exists(fullPath))
        {
            try
            {
                return CacheHub.Indexing.Hashing.FileHasher.ComputeFullHashAsync(fullPath).GetAwaiter().GetResult();
            }
            catch { /* fall through to DB hash */ }
        }
    }

    // Fallback: read hash from files table (file may have been deleted from disk)
    using var conn = factory.CreateOpenConnection();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT content_hash FROM files WHERE snapshot_id = $snap AND normalized_path = $path LIMIT 1;";
    cmd.Parameters.AddWithValue("$snap", snapshotId.Value);
    cmd.Parameters.AddWithValue("$path", path);
    var result = cmd.ExecuteScalar();
    if (result is string hash && !string.IsNullOrEmpty(hash) && hash != "pending" && !hash.StartsWith("fp:", StringComparison.Ordinal))
        return hash;
    return "sha256:pending";
}

// V6: Extracted indexing logic — shared by /index and /bootstrap endpoints
static async Task IndexWorkspaceAsync(SqliteConnectionFactory factory, Workspace ws, IndexSnapshotId snapshotId)
{
    var ignoreEngine = new CacheHub.Indexing.IgnoreRules.IgnoreRuleEngine()
        .WithDefaults()
        .WithGitIgnore(System.IO.Path.Combine(ws.RootPath, ".gitignore"))
        .WithCacheHubIgnore(System.IO.Path.Combine(ws.RootPath, ".cachehubignore"));

    var enumerator = new CacheHub.Indexing.Scanning.DirectoryEnumerator();

    var filesToIndex = new List<(string relativePath, string fullPath, long size, string language, bool isBinary, string hash, string content)>();

    await foreach (var file in enumerator.EnumerateAsync(ws.RootPath))
    {
        if (file.IsDirectory) continue;
        var relativePath = CacheHub.Core.Paths.PathNormalizer.GetRelativePath(ws.RootPath, file.Path);
        if (ignoreEngine.IsIgnored(relativePath)) continue;

        var typeInfo = CacheHub.Indexing.Detection.FileTypeDetector.Detect(file.Path, file.Size);
        if (!typeInfo.ShouldIndex) continue;

        var hash = await CacheHub.Indexing.Hashing.FileHasher.HashAsync(file.Path, file.Size);
        var content = await System.IO.File.ReadAllTextAsync(file.Path);

        filesToIndex.Add((relativePath, file.Path, file.Size, typeInfo.Language, typeInfo.IsBinary, hash.Hash, content));
    }

    // Batch write: single connection, single transaction for atomicity
    await using var batchConn = factory.CreateOpenConnection();
    await using var batchTx = await batchConn.BeginTransactionAsync();

    try
    {
        foreach (var (relativePath, fullPath, size, language, isBinary, hash, content) in filesToIndex)
        {
            var parser = SelectParser(relativePath);
            var parseResult = parser.Parse(content, relativePath);

            using var fileCmd = batchConn.CreateCommand();
            fileCmd.Transaction = (SqliteTransaction)batchTx;
            fileCmd.CommandText = """
                INSERT INTO files (id, snapshot_id, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind, parser_id, parser_version)
                VALUES ($id, $snap, $path, $norm, $size, $hash, $lang, $bin, 'Indexed', $hashKind, $parserId, $parserVer);
                """;
            var fileId = Guid.NewGuid().ToString("N");
            fileCmd.Parameters.AddWithValue("$id", fileId);
            fileCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
            fileCmd.Parameters.AddWithValue("$path", relativePath);
            fileCmd.Parameters.AddWithValue("$norm", relativePath);
            fileCmd.Parameters.AddWithValue("$size", size);
            fileCmd.Parameters.AddWithValue("$hash", hash);
            fileCmd.Parameters.AddWithValue("$lang", language);
            fileCmd.Parameters.AddWithValue("$bin", isBinary ? 1 : 0);
            fileCmd.Parameters.AddWithValue("$hashKind", hash.StartsWith("fp:", StringComparison.Ordinal) ? "fingerprint" : "full");
            fileCmd.Parameters.AddWithValue("$parserId", parser.Id);
            fileCmd.Parameters.AddWithValue("$parserVer", parser.Version);
            await fileCmd.ExecuteNonQueryAsync();

            foreach (var symbol in parseResult.Symbols)
            {
                using var symCmd = batchConn.CreateCommand();
                symCmd.Transaction = (SqliteTransaction)batchTx;
                symCmd.CommandText = """
                    INSERT INTO file_symbols (id, file_id, snapshot_id, name, kind, start_line, end_line, modifier, confidence)
                    VALUES ($id, $fid, $snap, $name, $kind, $sl, $el, $mod, $conf);
                    """;
                symCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                symCmd.Parameters.AddWithValue("$fid", fileId);
                symCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
                symCmd.Parameters.AddWithValue("$name", symbol.Name);
                symCmd.Parameters.AddWithValue("$kind", symbol.Kind.ToString());
                symCmd.Parameters.AddWithValue("$sl", symbol.StartLine);
                symCmd.Parameters.AddWithValue("$el", symbol.EndLine);
                symCmd.Parameters.AddWithValue("$mod", (object?)symbol.Modifier ?? DBNull.Value);
                symCmd.Parameters.AddWithValue("$conf", "syntactic");
                await symCmd.ExecuteNonQueryAsync();
            }

            foreach (var import in parseResult.Imports)
            {
                using var impCmd = batchConn.CreateCommand();
                impCmd.Transaction = (SqliteTransaction)batchTx;
                impCmd.CommandText = """
                    INSERT INTO file_imports (id, file_id, snapshot_id, module, imported_name, line)
                    VALUES ($id, $fid, $snap, $mod, $name, $line);
                    """;
                impCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                impCmd.Parameters.AddWithValue("$fid", fileId);
                impCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
                impCmd.Parameters.AddWithValue("$mod", import.Module);
                impCmd.Parameters.AddWithValue("$name", (object?)import.ImportedName ?? DBNull.Value);
                impCmd.Parameters.AddWithValue("$line", import.Line);
                await impCmd.ExecuteNonQueryAsync();
            }

            foreach (var relation in parseResult.Relations)
            {
                using var relCmd = batchConn.CreateCommand();
                relCmd.Transaction = (SqliteTransaction)batchTx;
                relCmd.CommandText = """
                    INSERT INTO file_relations (id, file_id, snapshot_id, source_symbol, target_symbol, relation_type, confidence, line, source)
                    VALUES ($id, $fid, $snap, $src, $tgt, $rt, $conf, $line, $source);
                    """;
                relCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                relCmd.Parameters.AddWithValue("$fid", fileId);
                relCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
                relCmd.Parameters.AddWithValue("$src", string.IsNullOrEmpty(relation.SourceSymbol) ? relation.Relation : relation.SourceSymbol);
                relCmd.Parameters.AddWithValue("$tgt", relation.TargetName);
                relCmd.Parameters.AddWithValue("$rt", relation.RelationType.ToString());
                relCmd.Parameters.AddWithValue("$conf", relation.Confidence.ToString(System.Globalization.CultureInfo.InvariantCulture));
                relCmd.Parameters.AddWithValue("$line", relation.Line > 0 ? relation.Line : DBNull.Value);
                relCmd.Parameters.AddWithValue("$source", relation.Source);
                await relCmd.ExecuteNonQueryAsync();
            }
        }

        await batchTx.CommitAsync();
    }
    catch (Exception)
    {
        await batchTx.RollbackAsync();
        throw;
    }

    // FTS indexing (separate — FTS5 virtual tables don't support DDL in DML transactions)
    var fts = new CacheHub.Storage.Search.Fts5Index(factory);
    foreach (var (relativePath, _, _, language, _, hash, content) in filesToIndex)
    {
        await fts.IndexFileAsync(snapshotId, relativePath, relativePath, content, language, hash);
    }

    // Activate snapshot (workspace-scoped)
    await using var activateConn = factory.CreateOpenConnection();
    await using var activateTx = await activateConn.BeginTransactionAsync();
    using var supCmd = activateConn.CreateCommand();
    supCmd.Transaction = (SqliteTransaction)activateTx;
    supCmd.CommandText = "UPDATE index_snapshots SET status = 'Superseded' WHERE status IN ('Active', 'ActiveDegraded') AND workspace_id = $ws;";
    supCmd.Parameters.AddWithValue("$ws", ws.Id.Value);
    await supCmd.ExecuteNonQueryAsync();

    using var setActiveCmd = activateConn.CreateCommand();
    setActiveCmd.Transaction = (SqliteTransaction)activateTx;
    setActiveCmd.CommandText = "UPDATE index_snapshots SET status = 'Active', file_count = $count, completed_at = datetime('now') WHERE id = $id;";
    setActiveCmd.Parameters.AddWithValue("$count", filesToIndex.Count);
    setActiveCmd.Parameters.AddWithValue("$id", snapshotId.Value);
    await setActiveCmd.ExecuteNonQueryAsync();
    await activateTx.CommitAsync();
}

// V6: Auto-authentication endpoint — sets HttpOnly session cookie for same-origin GUI
// Eliminates the need for users to manually copy/paste the access token.
app.MapPost("/api/v1/auth/init", (HttpContext ctx) =>
{
    ctx.Response.Cookies.Append("cachehub_session", accessToken, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = false, // loopback HTTP is fine — we only bind 127.0.0.1
        Expires = DateTimeOffset.UtcNow.AddHours(24),
    });
    return Results.Ok(new { authenticated = true, message = "Session cookie set." });
});

// === V6 Provider Config (review #33: GUI Provider 配置不足) ===
app.MapGet("/api/v1/config/provider", () =>
{
    var cm = new ConfigManager();
    var config = cm.Load();
    var gw = config.Gateway;
    return Results.Ok(new
    {
        enabled = gw?.Enabled ?? false,
        port = gw?.Port ?? 5218,
        providerUrl = gw?.ProviderUrl ?? "",
        enableCache = gw?.EnableCache ?? true,
        enableSingleFlight = gw?.EnableSingleFlight ?? true,
    });
});

app.MapPost("/api/v1/config/provider", (ProviderConfigApiRequest req) =>
{
    var cm = new ConfigManager();
    var config = cm.Load();
    var gw = config.Gateway ?? new GatewayConfigFile();
    var updated = gw with
    {
        Enabled = req.Enabled ?? gw.Enabled,
        Port = req.Port ?? gw.Port,
        ProviderUrl = req.ProviderUrl ?? gw.ProviderUrl,
        EnableCache = req.EnableCache ?? gw.EnableCache,
        EnableSingleFlight = req.EnableSingleFlight ?? gw.EnableSingleFlight,
    };
    var newConfig = config with { Gateway = updated };
    cm.Save(newConfig);
    return Results.Ok(new { saved = true, providerUrl = updated.ProviderUrl, port = updated.Port });
});

// === Capabilities ===
app.MapGet("/api/v1/capabilities", () => Results.Ok(new CapabilityDiscovery
{
    Version = "0.2.0-prealpha",
    ProtocolVersion = "1.0",
    Capabilities = CapabilityFlags.With(
        Capability.WorkspaceImport, Capability.ContextBuild,
        Capability.ContextExpand, Capability.ContextExplain,
        Capability.ContextFeedback, Capability.FileExport,
        Capability.Cache, Capability.Gateway, Capability.Semantic),
    SchemaVersions = new Dictionary<string, int>
    {
        ["contextPackage"] = 1,
        ["capabilityDiscovery"] = 1,
        ["error"] = 1,
    },
    Limitations = ["Semantic is reference-only (FNV-1a lexical, not full semantic)", "No LSP"],
}));

// === Workspaces ===
app.MapGet("/api/v1/workspaces", async (IWorkspaceRepository repo) =>
{
    var workspaces = await repo.ListAllAsync();
    return Results.Ok(workspaces.Select(w => new
    {
        id = w.Id.Value,
        name = w.Name,
        rootPath = w.RootPath,
        status = w.Status.ToString(),
        createdAt = w.CreatedAt,
    }));
});

app.MapPost("/api/v1/workspaces/import", async (ImportRequest req, IWorkspaceRepository repo) =>
{
    if (string.IsNullOrEmpty(req.Path))
        return Results.BadRequest(ErrorEnvelope.From(ErrorCode.InvalidArgument, "Path is required"));

    var workspace = Workspace.CreateValidated(req.Name ?? new DirectoryInfo(req.Path).Name, req.Path);
    await repo.InsertAsync(workspace);
    return Results.Ok(new { id = workspace.Id.Value, name = workspace.Name, status = workspace.Status.ToString() });
});

// === V5: Bootstrap from URL — clone→detect→import→index in one API call ===
app.MapPost("/api/v1/workspaces/bootstrap", async (BootstrapApiRequest req, IWorkspaceRepository repo, SqliteConnectionFactory factory) =>
{
    if (string.IsNullOrEmpty(req.Url))
        return Results.BadRequest(ErrorEnvelope.From(ErrorCode.InvalidArgument, "Url is required"));

    // Parse URL
    var parsed = CacheHub.Core.Repository.RepositoryUrlParser.Parse(req.Url);
    var repoName = parsed.RepoName ?? "repo";
    var dest = req.Dest ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "CacheHub", "repos", repoName);

    // Step 1: Clone
    var git = new CacheHub.Core.Repository.GitProcessWrapper();
    var clonePlan = new CacheHub.Core.Repository.ClonePlan
    {
        Url = req.Url,
        Destination = dest,
        Depth = 1,
        IncludeSubmodules = false,
        IncludeLfs = false,
        Risks = ["Clone writes to disk", "Network access required"],
    };
    var cloneResult = await git.CloneAsync(clonePlan);
    if (!cloneResult.Success)
        return Results.BadRequest(ErrorEnvelope.From(ErrorCode.ProviderError, $"Clone failed: {cloneResult.ErrorMessage}"));

    // Step 2: Detect
    var detectEngine = new CacheHub.Indexing.Detection.ProjectDetectionEngine();
    var detection = detectEngine.Detect(dest);
    var initPlan = detectEngine.GeneratePlan(detection);

    // Step 3: Import
    var workspace = Workspace.CreateValidated(req.Name ?? repoName, dest);
    await repo.InsertAsync(workspace);

    // V7-W01: Capture Git state for version-aware snapshots
    var gitStateProvider = new CacheHub.Core.Repository.GitStateProvider();
    var gitState = await gitStateProvider.CaptureAsync(workspace.RootPath);

    // Step 4: Build index (reuse the same indexing logic as /index endpoint)
    var snapshotId = IndexSnapshotId.New();
    await using var initConn = factory.CreateOpenConnection();
    using var snapCmd = initConn.CreateCommand();
    snapCmd.CommandText = "INSERT INTO index_snapshots (id, workspace_id, status, file_count, repository_commit, branch, is_dirty, workspace_fingerprint) VALUES ($id, $ws, 'Building', 0, $commit, $branch, $dirty, $fp);";
    snapCmd.Parameters.AddWithValue("$id", snapshotId.Value);
    snapCmd.Parameters.AddWithValue("$ws", workspace.Id.Value);
    snapCmd.Parameters.AddWithValue("$commit", (object?)gitState.Commit ?? DBNull.Value);
    snapCmd.Parameters.AddWithValue("$branch", (object?)gitState.Branch ?? DBNull.Value);
    snapCmd.Parameters.AddWithValue("$dirty", gitState.IsDirty);
    snapCmd.Parameters.AddWithValue("$fp", (object?)gitState.Fingerprint ?? DBNull.Value);
    await snapCmd.ExecuteNonQueryAsync();
    await initConn.DisposeAsync();

    // Run indexing in background
    _ = Task.Run(async () =>
    {
        try
        {
            await IndexWorkspaceAsync(factory, workspace, snapshotId);
            await repo.UpdateStatusAsync(workspace.Id, WorkspaceStatus.Ready);
        }
        catch (Exception)
        {
            await using var failConn = factory.CreateOpenConnection();
            using var failCmd = failConn.CreateCommand();
            failCmd.CommandText = "UPDATE index_snapshots SET status = 'Failed' WHERE id = $id;";
            failCmd.Parameters.AddWithValue("$id", snapshotId.Value);
            await failCmd.ExecuteNonQueryAsync();
        }
    });

    return Results.Ok(new
    {
        bootstrapped = true,
        workspaceId = workspace.Id.Value,
        snapshotId = snapshotId.Value,
        path = dest,
        components = detection.Components.Select(c => new { language = c.Language, framework = c.Framework, path = c.Path }).ToList(),
        isMonorepo = detection.IsMonorepo,
        missingTools = initPlan.MissingTools,
        recommendedActions = initPlan.Actions.Select(a => new { command = a.Command, purpose = a.Purpose, risks = a.Risks }).ToList(),
        requiresApproval = initPlan.Actions.Where(a => a.MayRunScripts || a.WritesToDisk).Select(a => a.Command).ToList(),
        status = "Building",
        message = "Index build started. Poll /api/v1/workspaces/{id}/status for progress.",
    });
});

app.MapGet("/api/v1/workspaces/{id}/status", async (string id, IWorkspaceRepository repo) =>
{
    var ws = await repo.FindByIdAsync(WorkspaceId.Parse(id));
    if (ws is null) return Results.NotFound(ErrorEnvelope.From(ErrorCode.WorkspaceNotFound, "Workspace not found"));
    return Results.Ok(new { id = ws.Id.Value, name = ws.Name, rootPath = ws.RootPath, status = ws.Status.ToString() });
});

app.MapDelete("/api/v1/workspaces/{id}", async (string id, IWorkspaceRepository repo) =>
{
    await repo.RemoveAsync(WorkspaceId.Parse(id));
    return Results.Ok(new { removed = true });
});

// === Index Build (API-P1-003: GUI 索引任务闭环) ===
app.MapPost("/api/v1/workspaces/{id}/index", async (string id, IWorkspaceRepository repo, SqliteConnectionFactory factory) =>
{
    var ws = await repo.FindByIdAsync(WorkspaceId.Parse(id));
    if (ws is null) return Results.NotFound(ErrorEnvelope.From(ErrorCode.WorkspaceNotFound, "Workspace not found"));

    var snapshotId = IndexSnapshotId.New();

    // V7-W01: Capture Git state for version-aware snapshots
    var gitStateProvider = new CacheHub.Core.Repository.GitStateProvider();
    var gitState = await gitStateProvider.CaptureAsync(ws.RootPath);

    // Insert snapshot as Building
    await using var initConn = factory.CreateOpenConnection();
    using var snapCmd = initConn.CreateCommand();
    snapCmd.CommandText = "INSERT INTO index_snapshots (id, workspace_id, status, file_count, repository_commit, branch, is_dirty, workspace_fingerprint) VALUES ($id, $ws, 'Building', 0, $commit, $branch, $dirty, $fp);";
    snapCmd.Parameters.AddWithValue("$id", snapshotId.Value);
    snapCmd.Parameters.AddWithValue("$ws", ws.Id.Value);
    snapCmd.Parameters.AddWithValue("$commit", (object?)gitState.Commit ?? DBNull.Value);
    snapCmd.Parameters.AddWithValue("$branch", (object?)gitState.Branch ?? DBNull.Value);
    snapCmd.Parameters.AddWithValue("$dirty", gitState.IsDirty);
    snapCmd.Parameters.AddWithValue("$fp", (object?)gitState.Fingerprint ?? DBNull.Value);
    await snapCmd.ExecuteNonQueryAsync();
    await initConn.DisposeAsync();

    // Run indexing in background
    _ = Task.Run(async () =>
    {
        try
        {
            await IndexWorkspaceAsync(factory, ws, snapshotId);
            await repo.UpdateStatusAsync(ws.Id, WorkspaceStatus.Ready);
        }
        catch (Exception)
        {
            // Mark snapshot as failed
            await using var failConn = factory.CreateOpenConnection();
            using var failCmd = failConn.CreateCommand();
            failCmd.CommandText = "UPDATE index_snapshots SET status = 'Failed' WHERE id = $id;";
            failCmd.Parameters.AddWithValue("$id", snapshotId.Value);
            await failCmd.ExecuteNonQueryAsync();
        }
    });

    return Results.Ok(new
    {
        workspaceId = ws.Id.Value,
        snapshotId = snapshotId.Value,
        status = "Building",
        message = "Index build started. Poll /api/v1/workspaces/{id}/status for progress.",
    });
});

// === Context ===
app.MapPost("/api/v1/context/build", async (ContextBuildApiRequest req, ContextEngine engine, SqliteConnectionFactory factory, IWorkspaceRepository repo, IContextPackageRepository ctxRepo) =>
{
    var ws = await repo.FindByIdAsync(WorkspaceId.Parse(req.WorkspaceId));
    if (ws is null) return Results.NotFound(ErrorEnvelope.From(ErrorCode.WorkspaceNotFound, "Workspace not found"));

    var activeSnapshot = await GetActiveSnapshotIdAsync(factory, req.WorkspaceId);
    if (activeSnapshot is null)
        return Results.BadRequest(ErrorEnvelope.From(ErrorCode.IndexNotFound, "No active index snapshot. Run 'cachehub index build' first."));

    var activeSnapshotId = activeSnapshot.Value.SnapshotId;
    var indexedFiles = await GetIndexedFilesAsync(factory, req.WorkspaceId);

    // V7-W02 / V8-P0-01: Stale detection — default: reject build when stale
    var staleResult = await CacheHub.Core.Indexing.StaleDetector.CheckAsync(
        ws.RootPath, activeSnapshot.Value.WorkspaceFingerprint,
        fileFilter: CreateDesktopFingerprintFilter(ws.RootPath, indexedFiles.Select(f => f.Path)));
    if (!staleResult.IsFresh)
    {
        // V8-P0-01: Desktop API returns 409 Conflict with CONTEXT_STALE error code
        return Results.Json(ErrorEnvelope.From(ErrorCode.ContextStale,
            $"Workspace state has changed since last index build. {staleResult.Message} Run index refresh to update."),
            statusCode: 409);
    }

    var manifest = engine.Build(
        new ContextBuildRequest
        {
            WorkspaceId = ws.Id,
            IndexSnapshotId = activeSnapshotId,
            Task = req.Task,
            RepositoryCommit = activeSnapshot.Value.RepositoryCommit,
            Branch = activeSnapshot.Value.Branch,
            IsDirty = activeSnapshot.Value.IsDirty,
            WorkspaceFingerprint = activeSnapshot.Value.WorkspaceFingerprint,
            CurrentWorkspaceFingerprint = staleResult.CurrentFingerprint, // V8-P0-01
        },
        () => indexedFiles,
        path =>
        {
            var fullPath = ResolvePathSafe(ws.RootPath, path);
            return fullPath is not null && File.Exists(fullPath) ? File.ReadAllTextAsync(fullPath).GetAwaiter().GetResult() : "";
        },
        path => ResolveFileHashFromDb(factory, activeSnapshotId, path, ws.RootPath),
        ftsSearch: keyword =>
        {
            var querySvc = new CacheHub.Storage.Query.SqliteIndexQueryService(factory);
            var results = querySvc.SearchFtsAsync(activeSnapshotId, keyword, 50).GetAwaiter().GetResult();
            return results.Select(r => new CacheHub.Context.Recall.FtsMatch(r.Path, r.Language, r.Snippet, r.RankScore, r.HitLine)).ToList();
        },
        symbolSearch: symbol =>
        {
            var querySvc = new CacheHub.Storage.Query.SqliteIndexQueryService(factory);
            var results = querySvc.SearchSymbolsAsync(activeSnapshotId, symbol).GetAwaiter().GetResult();
            return results.Select(r => r.NormalizedPath).ToList();
        },
        importSearch: symbol =>
        {
            var querySvc = new CacheHub.Storage.Query.SqliteIndexQueryService(factory);
            var results = querySvc.GetFilesByImportedSymbolAsync(activeSnapshotId, symbol).GetAwaiter().GetResult();
            return results.ToList();
        },
        symbolSearchDetailed: symbol =>
        {
            var querySvc = new CacheHub.Storage.Query.SqliteIndexQueryService(factory);
            var results = querySvc.SearchSymbolsAsync(activeSnapshotId, symbol).GetAwaiter().GetResult();
            return results.Select(r => new CacheHub.Context.Recall.SymbolHit
            {
                NormalizedPath = r.NormalizedPath,
                Name = r.Name,
                Kind = r.Kind,
                StartLine = r.StartLine,
                EndLine = r.EndLine,
                ExactMatch = r.ExactMatch,
            }).ToList();
        },
        relationSearch: filePath =>
        {
            var querySvc = new CacheHub.Storage.Query.SqliteIndexQueryService(factory);
            var results = querySvc.GetFileRelationsAsync(activeSnapshotId, filePath).GetAwaiter().GetResult();
            return results.Select(r => new CacheHub.Context.Recall.RelationHit
            {
                TargetName = r.TargetName,
                RelationType = r.RelationType,
                Relation = r.Relation,
                Confidence = r.Confidence,
            }).ToList();
        },
        semanticSearch: DesktopSemanticHelper.CreateSemanticSearch(ws.Id.Value),
        reverseRelationSearch: target =>
        {
            var revQuerySvc = new CacheHub.Storage.Query.SqliteIndexQueryService(factory);
            var revResults = revQuerySvc.GetFilesByRelationTargetAsync(activeSnapshotId, target).GetAwaiter().GetResult();
            return revResults.Select(r => new CacheHub.Context.Recall.RelationHit
            {
                TargetName = r.TargetName,
                RelationType = r.RelationType,
                Relation = r.Relation,
                Confidence = r.Confidence,
                SourcePath = r.NormalizedPath,
            }).ToList();
        });

    await ctxRepo.SaveAsync(manifest);

    return Results.Ok(manifest);
});

app.MapGet("/api/v1/context/{id}", async (string id, IContextPackageRepository ctxRepo) =>
{
    var manifest = await ctxRepo.FindByIdAsync(ContextPackageId.Parse(id));
    if (manifest is null) return Results.NotFound(ErrorEnvelope.From(ErrorCode.ContextPackageNotFound, "Context package not found"));
    return Results.Ok(manifest);
});

app.MapPost("/api/v1/context/{id}/expand", async (string id, ExpandApiRequest req, IContextPackageRepository ctxRepo, IWorkspaceRepository wsRepo) =>
{
    var manifest = await ctxRepo.FindByIdAsync(ContextPackageId.Parse(id));
    if (manifest is null) return Results.NotFound(ErrorEnvelope.From(ErrorCode.ContextPackageNotFound, "Context package not found"));

    var ws = await wsRepo.FindByIdAsync(manifest.WorkspaceId);
    if (ws is null) return Results.NotFound(ErrorEnvelope.From(ErrorCode.WorkspaceNotFound, "Workspace not found"));

    // V8-audit-32: Verify workspace hasn't changed since the parent context package was built.
    // Prevents creating cross-version child revision chains (parent on snapshot A, disk now B).
    if (!string.IsNullOrEmpty(manifest.DirtyStateHash))
    {
        var expandStaleResult = await CacheHub.Core.Indexing.StaleDetector.CheckAsync(
            ws.RootPath, manifest.DirtyStateHash,
            fileFilter: CreateDesktopFingerprintFilter(ws.RootPath,
                (await GetIndexedFilesAsync(factory, ws.Id.Value)).Select(f => f.Path)));
        if (!expandStaleResult.IsFresh)
        {
            return Results.Json(ErrorEnvelope.From(ErrorCode.ContextStale,
                $"Workspace state has changed since this context package was built. {expandStaleResult.Message} Run index refresh and rebuild context."),
                statusCode: 409);
        }
    }

    var targetPath = req.File ?? req.Symbol ?? "";
    var fullPath = ResolvePathSafe(ws.RootPath, targetPath);

    if (fullPath is null)
        return Results.BadRequest(ErrorEnvelope.From(ErrorCode.InvalidArgument, "Invalid file path"));

    if (!File.Exists(fullPath))
        return Results.NotFound(ErrorEnvelope.From(ErrorCode.InvalidArgument, $"File not found: {targetPath}"));

    var content = await File.ReadAllTextAsync(fullPath);
    var expander = new ContextExpander();
    var result = expander.ExpandByFile(id, targetPath, content, req.Reason ?? "API expand");

    // Create and persist child revision package
    var revision = expander.CreateRevision(manifest, result, path =>
    {
        var revisionPath = ResolvePathSafe(ws.RootPath, path);
        return revisionPath is not null && File.Exists(revisionPath)
            ? File.ReadAllText(revisionPath)
            : "";
    });
    await ctxRepo.SaveAsync(revision);

    return Results.Ok(new
    {
        contextId = revision.Id.Value,
        parentContextId = id,
        addedItems = result.AddedItems.Select(i => new { path = i.Path, mode = i.Mode.ToString(), content = i.Content.Length }),
        additionalTokens = result.AdditionalTokens,
        cumulativeTokens = revision.Budget.ActualEstimate,
        reason = result.Reason,
    });
});

app.MapPost("/api/v1/context/{id}/feedback", async (string id, FeedbackApiRequest req, IFeedbackRepository fbRepo, IContextPackageRepository ctxRepo) =>
{
    var feedback = new ContextFeedback
    {
        ContextPackageId = id,
        ClientId = req.ClientId,
        FilesActuallyRead = req.FilesActuallyRead ?? [],
        TaskCompleted = req.TaskCompleted,
        MissingContextReported = req.MissingContextReported,
    };

    await fbRepo.SaveAsync(feedback);

    // V5-W09: Record semantic reference on successful task completion
    if (req.TaskCompleted)
    {
        try
        {
            var manifest = await ctxRepo.FindByIdAsync(ContextPackageId.Parse(id));
            if (manifest is not null)
            {
                DesktopSemanticHelper.RecordReference(
                    manifest.Task.OriginalText,
                    manifest.WorkspaceId.Value,
                    manifest.Task.OriginalText,
                    manifest.IndexSnapshotId.Value,
                    selectedFiles: manifest.SelectedFiles.Select(f => f.Path).ToList(),
                    filesActuallyRead: req.FilesActuallyRead,
                    taskCompleted: true);
            }
        }
        catch { /* best effort — don't fail the feedback if semantic recording fails */ }
    }

    return Results.Ok(new { received = true, saved = true, contextId = id, clientId = feedback.ClientId });
});

// === Context List ===
app.MapGet("/api/v1/workspaces/{id}/contexts", async (string id, IContextPackageRepository ctxRepo) =>
{
    var list = await ctxRepo.ListByWorkspaceAsync(WorkspaceId.Parse(id));
    return Results.Ok(list.Select(m => new
    {
        id = m.Id.Value,
        task = m.Task.OriginalText,
        budget = m.Budget.ActualEstimate,
        engine = m.ContextEngineVersion,
        createdAt = m.CreatedAt,
    }));
});

// === Context Explain ===
app.MapGet("/api/v1/context/{id}/explain", async (string id, IContextPackageRepository ctxRepo) =>
{
    var manifest = await ctxRepo.FindByIdAsync(ContextPackageId.Parse(id));
    if (manifest is null) return Results.NotFound(ErrorEnvelope.From(ErrorCode.ContextPackageNotFound, "Context package not found"));

    var explanations = ContextExplainer.Explain(manifest);
    var misses = ContextExplainer.DetectPotentialMisses(manifest);
    var budgetSummary = ContextExplainer.BudgetSummary(manifest);

    return Results.Ok(new
    {
        contextId = id,
        explanations = explanations.Select(e => new
        {
            path = e.Path,
            selected = e.Selected,
            score = e.Score,
            reasons = e.Reasons,
            exclusionReason = e.ExclusionReason,
        }),
        potentialMisses = misses,
        budgetSummary,
    });
});

// === File Export ===
app.MapPost("/api/v1/workspaces/{id}/export", async (string id, IWorkspaceRepository repo) =>
{
    var ws = await repo.FindByIdAsync(WorkspaceId.Parse(id));
    if (ws is null) return Results.NotFound(ErrorEnvelope.From(ErrorCode.WorkspaceNotFound, "Workspace not found"));

    var appData = new AppDataDirectory();
    var exportDir = Path.Combine(appData.Root, "exports", id);
    Directory.CreateDirectory(exportDir);

    var workspaceJson = new { id = ws.Id.Value, name = ws.Name, rootPath = ws.RootPath, status = ws.Status.ToString() };
    var jsonPath = Path.Combine(exportDir, "workspace.json");
    await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(workspaceJson, new JsonSerializerOptions { WriteIndented = true }));

    var repomapPath = Path.Combine(exportDir, "repomap.md");
    // Generate real repo map from indexed files
    var repomapContent = await GenerateRepoMapMarkdown(ws);
    await File.WriteAllTextAsync(repomapPath, repomapContent);

    return Results.Ok(new
    {
        workspaceId = id,
        exportedTo = exportDir,
        files = new[] { "workspace.json", "repomap.md" },
    });
});

// === Search ===
app.MapGet("/api/v1/search", async (string? q, string? workspace, int? limit, SqliteConnectionFactory factory) =>
{
    if (string.IsNullOrWhiteSpace(q))
        return Results.BadRequest(ErrorEnvelope.From(ErrorCode.InvalidArgument, "Query parameter 'q' is required"));

    var wsId = workspace ?? "";
    IndexSnapshotId? snapshotId = null;

    if (!string.IsNullOrEmpty(wsId))
    {
        await using var conn = factory.CreateOpenConnection();
        using var snapCmd = conn.CreateCommand();
        snapCmd.CommandText = "SELECT id FROM index_snapshots WHERE workspace_id = $ws AND status IN ('Active', 'ActiveDegraded') LIMIT 1;";
        snapCmd.Parameters.AddWithValue("$ws", wsId);
        await using var snapReader = await snapCmd.ExecuteReaderAsync();
        if (await snapReader.ReadAsync())
            snapshotId = IndexSnapshotId.Parse(snapReader.GetString(0));
    }

    if (snapshotId is null)
        return Results.Ok(new { results = Array.Empty<object>(), message = "No active index snapshot found" });

    var fts = new Fts5Index(factory);
    var results = await fts.SearchAsync(snapshotId, q, limit ?? 50);

    return Results.Ok(new
    {
        query = q,
        count = results.Count,
        results = results.Select(r => new { path = r.Path, language = r.Language, snippet = r.Snippet }),
    });
});

// === Outline === (SECURITY: workspace-scoped, no arbitrary absolute paths)
app.MapGet("/api/v1/outline", async (string workspaceId, string path, IWorkspaceRepository wsRepo) =>
{
    if (string.IsNullOrWhiteSpace(workspaceId))
        return Results.BadRequest(ErrorEnvelope.From(ErrorCode.InvalidArgument, "Query parameter 'workspaceId' is required"));
    if (string.IsNullOrWhiteSpace(path))
        return Results.BadRequest(ErrorEnvelope.From(ErrorCode.InvalidArgument, "Query parameter 'path' (relative file path) is required"));

    var ws = await wsRepo.FindByIdAsync(WorkspaceId.Parse(workspaceId));
    if (ws is null)
        return Results.NotFound(ErrorEnvelope.From(ErrorCode.WorkspaceNotFound, "Workspace not found"));

    var resolver = new SafePathResolver(ws.RootPath);
    var resolvedPath = resolver.ResolveFile(path);
    if (resolvedPath is null)
        return Results.BadRequest(ErrorEnvelope.From(ErrorCode.PathTraversalDetected, "File not found within workspace or path traversal detected"));

    var content = File.ReadAllText(resolvedPath);
    var ext = Path.GetExtension(resolvedPath).ToLowerInvariant();

    CacheHub.Core.Parsing.ICodeParser? parser = ext switch
    {
        ".cs" => new CacheHub.Indexing.Parsing.CSharpRegexParser(),
        ".ts" or ".tsx" or ".js" or ".jsx" => new CacheHub.Indexing.Parsing.TypeScriptRegexParser(),
        ".py" => new CacheHub.Indexing.Parsing.PythonRegexParser(),
        ".md" or ".markdown" => new CacheHub.Indexing.Parsing.MarkdownParser(),
        _ => new CacheHub.Indexing.Parsing.TextParser(),
    };

    var result = parser.Parse(content, path);
    var outline = CacheHub.Core.Parsing.Outline.DeterministicOutlineGenerator.Generate(result, path);

    return Results.Ok(new
    {
        file = outline.FilePath,
        language = outline.Language,
        parser = outline.ParserId,
        symbols = outline.Symbols.Select(s => new
        {
            name = s.Name,
            kind = s.Kind.ToString(),
            startLine = s.StartLine,
            endLine = s.EndLine,
            modifier = s.Modifier,
        }),
        imports = outline.Imports.Select(i => new { module = i.Module, name = i.ImportedName, line = i.Line }),
    });
});

// === Stats (V6: unified dashboard data — workspace stats + usage stats) ===
app.MapGet("/api/v1/stats", async (SqliteConnectionFactory factory, UsageStatsService usageStats) =>
{
    var wsRepo = new SqliteWorkspaceRepository(factory);
    var ctxRepo = new SqliteContextPackageRepository(factory);

    var workspaces = await wsRepo.ListAllAsync();
    var totalContexts = 0;
    var totalTokens = 0;

    foreach (var ws in workspaces)
    {
        var contexts = await ctxRepo.ListByWorkspaceAsync(ws.Id);
        totalContexts += contexts.Count;
        totalTokens += contexts.Sum(c => c.Budget.ActualEstimate);
    }

    var usage = usageStats.GetStats();

    // V7-W14: Read Gateway persistent stats (cross-process: Codex → Gateway → stats.db → Desktop)
    var gatewayStatsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CacheHub", "gateway", "stats.db");
    long gwTotalRequests = 0, gwCacheHits = 0, gwPromptTokens = 0, gwCompletionTokens = 0, gwCachedSaved = 0;
    double gwCacheHitRate = 0, gwAvgLatency = 0;
    if (File.Exists(gatewayStatsPath))
    {
        try
        {
            using var gwConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={gatewayStatsPath}");
            gwConn.Open();
            using var gwCmd = gwConn.CreateCommand();
            gwCmd.CommandText = """
                SELECT
                    COUNT(*),
                    SUM(CASE WHEN cached = 1 THEN 1 ELSE 0 END),
                    SUM(prompt_tokens),
                    SUM(completion_tokens),
                    SUM(CASE WHEN cached = 1 THEN prompt_tokens ELSE 0 END),
                    AVG(latency_ms)
                FROM gateway_stats;
                """;
            using var gwReader = gwCmd.ExecuteReader();
            if (gwReader.Read() && !gwReader.IsDBNull(0))
            {
                gwTotalRequests = gwReader.GetInt64(0);
                gwCacheHits = gwReader.IsDBNull(1) ? 0 : gwReader.GetInt64(1);
                gwPromptTokens = gwReader.IsDBNull(2) ? 0 : gwReader.GetInt64(2);
                gwCompletionTokens = gwReader.IsDBNull(3) ? 0 : gwReader.GetInt64(3);
                gwCachedSaved = gwReader.IsDBNull(4) ? 0 : gwReader.GetInt64(4);
                gwAvgLatency = gwReader.IsDBNull(5) ? 0 : gwReader.GetDouble(5);
                gwCacheHitRate = gwTotalRequests > 0 ? (double)gwCacheHits / gwTotalRequests : 0;
            }
        }
        catch { /* non-fatal: Gateway may not have written stats yet */ }
    }

    // Merge Desktop + Gateway stats
    var mergedRequests = usage.TotalRequests + gwTotalRequests;
    var mergedCacheHits = usage.CacheHits + gwCacheHits;
    var mergedPromptTokens = usage.TotalPromptTokens + gwPromptTokens;
    var mergedCompletionTokens = usage.TotalCompletionTokens + gwCompletionTokens;
    var mergedCachedSaved = usage.ActualCacheTokensSaved + gwCachedSaved;
    var mergedCacheHitRate = mergedRequests > 0 ? (double)mergedCacheHits / mergedRequests : 0;

    return Results.Ok(new
    {
        // Usage stats (Desktop + Gateway merged, for Dashboard)
        totalRequests = mergedRequests,
        cacheHits = mergedCacheHits,
        cacheHitRate = mergedCacheHitRate,
        totalPromptTokens = mergedPromptTokens,
        totalCompletionTokens = mergedCompletionTokens,
        // V7-W12: Separate estimated context savings from actual cache token savings
        estimatedContextTokensSaved = usage.EstimatedContextSaved,
        actualCacheTokensSaved = mergedCachedSaved,
        avgLatencyMs = gwAvgLatency > 0 ? gwAvgLatency : usage.AvgLatencyMs,
        // V7-W14: Breakdown for transparency
        desktopRequests = usage.TotalRequests,
        gatewayRequests = gwTotalRequests,
        // Workspace stats
        workspaces = workspaces.Count,
        contextPackages = totalContexts,
        totalEstimatedTokens = totalTokens,
        statuses = workspaces.GroupBy(w => w.Status.ToString())
            .Select(g => new { status = g.Key, count = g.Count() }),
    });
});

// === Payload ===
app.MapGet("/api/v1/context/{id}/payload", async (string id, IContextPackageRepository ctxRepo, IWorkspaceRepository wsRepo) =>
{
    var manifest = await ctxRepo.FindByIdAsync(ContextPackageId.Parse(id));
    if (manifest is null) return Results.NotFound(ErrorEnvelope.From(ErrorCode.ContextPackageNotFound, "Context package not found"));

    var ws = await wsRepo.FindByIdAsync(manifest.WorkspaceId);
    if (ws is null) return Results.NotFound(ErrorEnvelope.From(ErrorCode.WorkspaceNotFound, "Workspace not found"));

    var generator = new PayloadGenerator();
    var (_, payloadEnforcer) = CacheHub.Core.Security.SecurityPolicyResolver.CreateEnforcer();

    // Pre-pass: identify blocked/approval-required files before generating payload
    var blockedFiles = new List<object>();
    foreach (var file in manifest.SelectedFiles)
    {
        var fullPath = ResolvePathSafe(ws.RootPath, file.Path);
        if (fullPath is null || !File.Exists(fullPath)) continue;
        var content = await File.ReadAllTextAsync(fullPath);
        var decision = payloadEnforcer.EvaluateFile(file.Path, content);
        if (!decision.IsAllowed)
        {
            blockedFiles.Add(new
            {
                path = file.Path,
                approvalRequired = decision.IsApprovalRequired,
                reason = decision.Reason ?? "Blocked by security policy",
            });
        }
    }

    ContextPackagePayload payload;
    try
    {
        payload = generator.Generate(manifest, path =>
        {
            var fullPath = ResolvePathSafe(ws.RootPath, path);
            return fullPath is not null && File.Exists(fullPath) ? File.ReadAllTextAsync(fullPath).GetAwaiter().GetResult() : "";
        }, payloadEnforcer);
    }
    catch (ContextVersionMismatchException ex)
    {
        // V8-P0-02: Content has changed since Context Package was built
        return Results.Json(ErrorEnvelope.From(ErrorCode.ContextVersionMismatch, ex.Message),
            statusCode: 409);
    }

    return Results.Ok(new
    {
        contextPackageId = payload.ContextPackageId,
        format = payload.Format.ToString(),
        totalEstimatedTokens = payload.TotalEstimatedTokens,
        items = payload.Items.Select(i => new
        {
            path = i.Path,
            mode = i.Mode.ToString(),
            content = i.Content,
            startLine = i.StartLine,
            endLine = i.EndLine,
        }),
        blockedFiles = blockedFiles.Count > 0 ? blockedFiles : null,
    });
});

// Print access token to console for user
Console.WriteLine("============================================================");
Console.WriteLine("CacheHub Local API started on http://127.0.0.1:5099");
Console.WriteLine($"Access Token: {accessToken}");
Console.WriteLine($"Launch URL: http://127.0.0.1:5099/?nonce={launchNonce}");
Console.WriteLine("All API requests require: Authorization: Bearer <token>");
Console.WriteLine("============================================================");

static ICodeParser SelectParser(string filePath)
{
    var ext = Path.GetExtension(filePath).ToLowerInvariant();
    return ext switch
    {
        ".cs" => new CSharpRegexParser(),
        ".ts" or ".tsx" or ".js" or ".jsx" => new TypeScriptRegexParser(),
        ".py" => new PythonRegexParser(),
        ".go" => new GoRegexParser(),
        ".rs" => new RustRegexParser(),
        ".java" => new JavaRegexParser(),
        ".c" or ".h" or ".cpp" or ".hpp" or ".cc" or ".cxx" => new CppRegexParser(),
        ".php" => new PhpRegexParser(),
        ".rb" => new RubyRegexParser(),
        ".kt" or ".kts" => new KotlinRegexParser(),
        ".swift" => new SwiftRegexParser(),
        ".md" or ".markdown" => new MarkdownParser(),
        _ => new TextParser(),
    };
}

static async Task<string> GenerateRepoMapMarkdown(Workspace ws)
{
    var sb = new StringBuilder();
    sb.AppendLine($"# Repository Map: {ws.Name}");
    sb.AppendLine();
    sb.AppendLine($"Root: `{ws.RootPath}`");
    sb.AppendLine();

    try
    {
        var ignoreEngine = new CacheHub.Indexing.IgnoreRules.IgnoreRuleEngine()
            .WithDefaults()
            .WithGitIgnore(System.IO.Path.Combine(ws.RootPath, ".gitignore"))
            .WithCacheHubIgnore(System.IO.Path.Combine(ws.RootPath, ".cachehubignore"));

        var enumerator = new CacheHub.Indexing.Scanning.DirectoryEnumerator();
        var directories = new SortedSet<string>(StringComparer.Ordinal);
        var fileCount = 0;

        sb.AppendLine("## Directory Structure");
        sb.AppendLine();
        sb.AppendLine("```");

        var rootName = System.IO.Path.GetFileName(ws.RootPath.TrimEnd(System.IO.Path.DirectorySeparatorChar));
        sb.AppendLine($"{rootName}/");

        var fileEntries = new List<(string relativePath, long size, string language)>();

        await foreach (var file in enumerator.EnumerateAsync(ws.RootPath))
        {
            if (file.IsDirectory) continue;
            var relativePath = CacheHub.Core.Paths.PathNormalizer.GetRelativePath(ws.RootPath, file.Path);
            if (ignoreEngine.IsIgnored(relativePath)) continue;

            var typeInfo = CacheHub.Indexing.Detection.FileTypeDetector.Detect(file.Path, file.Size);
            if (!typeInfo.ShouldIndex) continue;

            fileEntries.Add((relativePath, file.Size, typeInfo.Language));
            fileCount++;

            var dir = System.IO.Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? "";
            if (!string.IsNullOrEmpty(dir))
                directories.Add(dir);
        }

        // Print directory tree
        foreach (var dir in directories)
        {
            var depth = dir.Split('/').Length;
            var indent = new string(' ', depth * 2);
            var dirName = dir.Split('/').Last();
            sb.AppendLine($"{indent}{dirName}/");
        }

        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine($"**Total indexed files**: {fileCount}");
        sb.AppendLine();

        // Language breakdown
        var langGroups = fileEntries.GroupBy(f => f.language).OrderByDescending(g => g.Count());
        if (langGroups.Any())
        {
            sb.AppendLine("## Language Breakdown");
            sb.AppendLine();
            sb.AppendLine("| Language | Files |");
            sb.AppendLine("|----------|-------|");
            foreach (var group in langGroups)
            {
                sb.AppendLine($"| {group.Key} | {group.Count()} |");
            }
        }
    }
    catch (Exception ex)
    {
        sb.AppendLine($"> Error generating repo map: {ex.Message}");
    }

    return sb.ToString();
}

// === Unified Workflow: Contextual Completion ===
app.MapPost("/api/v1/workflows/contextual-completion", async (ContextualCompletionApiRequest req, ContextEngine engine, IWorkspaceRepository wsRepo, IContextPackageRepository ctxRepo, SqliteConnectionFactory factory, UsageStatsService usageStats) =>
{
    if (string.IsNullOrEmpty(req.WorkspaceId) || string.IsNullOrEmpty(req.Task))
        return Results.BadRequest(ErrorEnvelope.From(ErrorCode.InvalidArgument, "WorkspaceId and Task are required"));

    var workspace = await wsRepo.FindByIdAsync(WorkspaceId.Parse(req.WorkspaceId));
    if (workspace is null)
        return Results.NotFound(ErrorEnvelope.From(ErrorCode.WorkspaceNotFound, "Workspace not found"));

    var querySvc = new CacheHub.Storage.Query.SqliteIndexQueryService(factory);
    var activeSnapshot = await querySvc.GetActiveSnapshotWithGitStateAsync(workspace.Id.Value);
    if (activeSnapshot is null)
        return Results.BadRequest(ErrorEnvelope.From(ErrorCode.IndexNotFound, "No active index snapshot. Run index build first."));

    var activeSnapshotId = activeSnapshot.SnapshotId;
    var indexedFiles = await querySvc.GetIndexedFilesBySnapshotAsync(activeSnapshotId);

    // V7-W02 / V8-P0-01: Stale detection — default: reject build when stale
    var staleResult = await CacheHub.Core.Indexing.StaleDetector.CheckAsync(
        workspace.RootPath, activeSnapshot.WorkspaceFingerprint,
        fileFilter: CreateDesktopFingerprintFilter(workspace.RootPath, indexedFiles.Select(f => f.Path)));
    if (!staleResult.IsFresh)
    {
        return Results.Json(ErrorEnvelope.From(ErrorCode.ContextStale,
            $"Workspace state has changed since last index build. {staleResult.Message} Run index refresh to update."),
            statusCode: 409);
    }

    // V5-W02 (P0): Use unified SecurityPolicyResolver
    var (secPolicy, secEnforcer) = CacheHub.Core.Security.SecurityPolicyResolver.CreateEnforcer();

    var indexedFileInfos = indexedFiles.Select(f => new CacheHub.Context.Recall.IndexedFileInfo
    {
        Path = f.NormalizedPath,
        NormalizedPath = f.NormalizedPath,
        Language = f.Language,
        Size = f.Size,
        ContentHash = f.ContentHash,
    }).ToList();

    var buildRequest = new ContextBuildRequest
    {
        WorkspaceId = workspace.Id,
        IndexSnapshotId = activeSnapshotId,
        Task = req.Task,
        ModelId = req.ModelId,
        CurrentFile = req.CurrentFile,
        SecurityPolicyVersion = secPolicy.Version,
        RepositoryCommit = activeSnapshot.RepositoryCommit,
        Branch = activeSnapshot.Branch,
        IsDirty = activeSnapshot.IsDirty,
        WorkspaceFingerprint = activeSnapshot.WorkspaceFingerprint,
        CurrentWorkspaceFingerprint = staleResult.CurrentFingerprint, // V8-P0-01
    };

    var manifest = engine.Build(
        buildRequest,
        () => indexedFileInfos,
        path =>
        {
            var fullPath = ResolvePathSafe(workspace.RootPath, path);
            return fullPath is not null && File.Exists(fullPath) ? File.ReadAllTextAsync(fullPath).GetAwaiter().GetResult() : "";
        },
        path => ResolveFileHashFromDb(factory, activeSnapshotId, path, workspace.RootPath),
        ftsSearch: keyword =>
        {
            var results = querySvc.SearchFtsAsync(activeSnapshotId, keyword, 50).GetAwaiter().GetResult();
            return results.Select(r => new CacheHub.Context.Recall.FtsMatch(r.Path, r.Language, r.Snippet, r.RankScore, r.HitLine)).ToList();
        },
        symbolSearch: symbol =>
        {
            var results = querySvc.SearchSymbolsAsync(activeSnapshotId, symbol).GetAwaiter().GetResult();
            return results.Select(r => r.NormalizedPath).ToList();
        },
        importSearch: symbol =>
        {
            var results = querySvc.GetFilesByImportedSymbolAsync(activeSnapshotId, symbol).GetAwaiter().GetResult();
            return results.ToList();
        },
        symbolSearchDetailed: symbol =>
        {
            var results = querySvc.SearchSymbolsAsync(activeSnapshotId, symbol).GetAwaiter().GetResult();
            return results.Select(r => new CacheHub.Context.Recall.SymbolHit
            {
                NormalizedPath = r.NormalizedPath,
                Name = r.Name,
                Kind = r.Kind,
                StartLine = r.StartLine,
                EndLine = r.EndLine,
                ExactMatch = r.ExactMatch,
            }).ToList();
        },
        relationSearch: filePath =>
        {
            var results = querySvc.GetFileRelationsAsync(activeSnapshotId, filePath).GetAwaiter().GetResult();
            return results.Select(r => new CacheHub.Context.Recall.RelationHit
            {
                TargetName = r.TargetName,
                RelationType = r.RelationType,
                Relation = r.Relation,
                Confidence = r.Confidence,
            }).ToList();
        },
        semanticSearch: DesktopSemanticHelper.CreateSemanticSearch(workspace.Id.Value),
        fileSymbolsProvider: path =>
        {
            var results = querySvc.GetFileSymbolsAsync(activeSnapshotId, path).GetAwaiter().GetResult();
            return results.Select(r => new CacheHub.Context.Recall.SymbolHit
            {
                NormalizedPath = path,
                Name = r.Name,
                Kind = r.Kind,
                StartLine = r.StartLine,
                EndLine = r.EndLine,
                ExactMatch = true,
            }).ToList();
        },
        reverseRelationSearch: target =>
        {
            var revResults = querySvc.GetFilesByRelationTargetAsync(activeSnapshotId, target).GetAwaiter().GetResult();
            return revResults.Select(r => new CacheHub.Context.Recall.RelationHit
            {
                TargetName = r.TargetName,
                RelationType = r.RelationType,
                Relation = r.Relation,
                Confidence = r.Confidence,
                SourcePath = r.NormalizedPath,
            }).ToList();
        });

    await ctxRepo.SaveAsync(manifest);

    // Assemble prompt
    var promptAssembly = new PromptAssemblyService();
    var payloadGenerator = new PayloadGenerator();
    string payloadContent;
    try
    {
        payloadContent = payloadGenerator.GenerateMarkdown(manifest, path =>
        {
            var fullPath = ResolvePathSafe(workspace.RootPath, path);
            return fullPath is not null && File.Exists(fullPath) ? File.ReadAllTextAsync(fullPath).GetAwaiter().GetResult() : "";
        }, secEnforcer);
    }
    catch (ContextVersionMismatchException ex)
    {
        // V8-P0-02: Content has changed since Context Package was built
        return Results.Json(ErrorEnvelope.From(ErrorCode.ContextVersionMismatch, ex.Message),
            statusCode: 409);
    }
    var (systemPrompt, userContent) = promptAssembly.Assemble(manifest, payloadContent);

    // Call Gateway if requested
    string? modelResponse = null;
    var gatewayCalled = false;
    var modelPromptTokens = 0;
    var modelCompletionTokens = 0;
    var modelTotalTokens = 0;
    var gatewayLatencyMs = 0L;  // V7-W12: real latency tracking
    var gatewayCacheHit = false;  // V7-W12: real cache hit from Gateway response header

    if (req.CallGateway && !string.IsNullOrEmpty(req.ModelId))
    {
        // V5-W02 (P0): Hard-block gateway call if security policy is Offline
        if (!secEnforcer.IsCloudSendAllowed())
        {
            modelResponse = "Gateway call blocked: security policy is Offline mode.";
            gatewayCalled = false;
        }
        else
        {
            var gatewayUrl = req.GatewayUrl ?? "http://127.0.0.1:5218";
            var gatewayToken = req.GatewayToken
                ?? Environment.GetEnvironmentVariable("CACHEHUB_GATEWAY_TOKEN")
                ?? "";

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                var requestBody = System.Text.Json.JsonSerializer.Serialize(new
                {
                    model = req.ModelId,
                    messages = new[]
                    {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userContent },
                },
                });

                using var msg = new HttpRequestMessage(HttpMethod.Post, $"{gatewayUrl.TrimEnd('/')}/v1/chat/completions");
                msg.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
                if (!string.IsNullOrEmpty(gatewayToken))
                    msg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gatewayToken);

                // V7-W12: Track real latency
                var gwSw = System.Diagnostics.Stopwatch.StartNew();
                var resp = await http.SendAsync(msg);
                var body = await resp.Content.ReadAsStringAsync();
                gwSw.Stop();
                gatewayLatencyMs = gwSw.ElapsedMilliseconds;

                // V7-W12: Check for cache hit header from Gateway
                if (resp.Headers.TryGetValues("X-CacheHub-Cache-Hit", out var cacheHitValues))
                {
                    var hitValue = cacheHitValues.FirstOrDefault();
                    gatewayCacheHit = string.Equals(hitValue, "true", StringComparison.OrdinalIgnoreCase);
                }

                if (resp.IsSuccessStatusCode)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    modelResponse = doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString();
                    gatewayCalled = true;

                    if (doc.RootElement.TryGetProperty("usage", out var usage))
                    {
                        modelPromptTokens = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;
                        modelCompletionTokens = usage.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0;
                        modelTotalTokens = usage.TryGetProperty("total_tokens", out var tt) ? tt.GetInt32() : (modelPromptTokens + modelCompletionTokens);
                    }
                }
                else
                {
                    modelResponse = $"Gateway error ({resp.StatusCode}): {body}";
                }
            }
            catch (Exception ex)
            {
                modelResponse = $"Gateway call failed: {ex.Message}";
            }
        } // end else (cloud send allowed)
    }

    // V6: Fix tokensSaved — use estimated baseline (total indexed file tokens) vs actual model prompt
    var estimatedBaselineTokens = indexedFileInfos.Sum(f => f.Size) / 4; // ~4 chars per token
    var estimatedContextSaved = Math.Max(0, estimatedBaselineTokens - modelPromptTokens);

    // V6: Record usage stats for Dashboard
    // V7-W12: Use real cache hit + latency from Gateway response
    // V8-audit-41: When Gateway is called, Gateway records model tokens to gateway/stats.db.
    // Desktop should NOT double-count model tokens — only record estimated context saved (unique to Desktop).
    if (req.CallGateway)
    {
        // Only record estimated context saved — Gateway is the single source of truth for model token stats
        usageStats.RecordRequest(
            0,  // Don't count prompt tokens here — Gateway already recorded them
            0,  // Don't count completion tokens here — Gateway already recorded them
            false,  // Cache hit is tracked by Gateway
            (int)estimatedContextSaved,
            0);  // Latency is tracked by Gateway
    }

    return Results.Ok(new
    {
        manifest = new
        {
            id = manifest.Id.Value,
            workspaceId = manifest.WorkspaceId.Value,
            task = manifest.Task.OriginalText,
            selectedFiles = manifest.SelectedFiles.Count,
            actualTokens = manifest.Budget.ActualEstimate,
            targetTokens = manifest.Budget.ContextTarget,
        },
        systemPrompt,
        userContent,
        gatewayCalled,
        modelResponse,
        // V5-W10: No double-counting — model total already includes context
        contextSelectedTokens = manifest.Budget.ActualEstimate,
        modelPromptTokens,
        modelCompletionTokens,
        modelTotalTokens,
        // V6: Fixed — estimated baseline (all indexed files) vs actual model prompt
        estimatedBaselineTokens,
        tokensSaved = estimatedContextSaved,
    });
});

app.Run();

record ImportRequest(string Path, string? Name);
record BootstrapApiRequest(string Url, string? Dest = null, string? Name = null);
record ContextBuildApiRequest(string WorkspaceId, string Task);
record ExpandApiRequest(string? Symbol, string? File, string? Reason);
record FeedbackApiRequest(string? ClientId, bool TaskCompleted, bool MissingContextReported, IReadOnlyList<string>? FilesActuallyRead);
record ContextualCompletionApiRequest(string WorkspaceId, string Task, string? ModelId, string? CurrentFile, bool CallGateway, string? GatewayUrl = null, string? GatewayToken = null);
record ProviderConfigApiRequest(bool? Enabled, int? Port, string? ProviderUrl, bool? EnableCache, bool? EnableSingleFlight);

// Make Program class accessible for integration testing
public partial class Program { }

/// <summary>
/// Desktop semantic reference helper: provides a semanticSearch callback
/// using a persistent vector store and local hash embedding.
/// Reference-only: provides historical task/error context, does not reuse model answers.
/// </summary>
internal static class DesktopSemanticHelper
{
    private static readonly LocalHashEmbeddingProvider _embedding = new();
    private static PersistentVectorStore? _store;

    private static PersistentVectorStore GetStore()
    {
        if (_store is not null) return _store;
        var appData = new AppDataDirectory();
        var dir = Path.Combine(appData.Root, "semantic");
        Directory.CreateDirectory(dir);
        _store = new PersistentVectorStore(Path.Combine(dir, "references.json"));
        return _store;
    }

    public static Func<string, IReadOnlyList<CacheHub.Context.Recall.SemanticHit>>? CreateSemanticSearch(string workspaceId)
    {
        var store = GetStore();
        if (store.Count == 0) return null;

        return queryText =>
        {
            var recall = new SemanticReferenceRecall(store, _embedding);
            var results = recall.RecallAsync(queryText, workspaceId, topK: 5, minSimilarity: 0.1)
                .GetAwaiter().GetResult();
            return results.Select(r => new CacheHub.Context.Recall.SemanticHit
            {
                Content = r.Reference.Content,
                Similarity = r.Similarity,
                ReferenceType = r.Reference.Type.ToString(),
                TaskDescription = r.Reference.TaskDescription,
                HistoricalFiles = [.. r.Reference.SelectedFiles, .. r.Reference.FilesActuallyRead],
            }).ToList();
        };
    }

    public static void RecordReference(string content, string? workspaceId,
        string? taskDescription, string? snapshotId,
        IReadOnlyList<string>? selectedFiles = null,
        IReadOnlyList<string>? filesActuallyRead = null,
        bool? taskCompleted = null)
    {
        var store = GetStore();
        var recall = new SemanticReferenceRecall(store, _embedding);
        recall.RecordAsync(content, SemanticReferenceType.Task,
            workspaceId, taskDescription, taskCompleted,
            snapshotId, workspaceContentHash: null,
            selectedFiles, filesActuallyRead).GetAwaiter().GetResult();
    }
}
