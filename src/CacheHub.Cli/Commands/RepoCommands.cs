using System.Text.Json;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Repository;
using CacheHub.Core.Workspaces;
using CacheHub.Indexing.Detection;
using CacheHub.Storage;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Repositories;

namespace CacheHub.Cli.Commands;

public static class RepoCommands
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    public static async Task<int> HandleAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: cachehub repo <inspect|clone|status|diff|pull> [options]");
            return 1;
        }

        var git = new GitProcessWrapper();

        return args[0] switch
        {
            "inspect" => Inspect(args.AsSpan(1).ToArray()),
            "clone" => await CloneAsync(args.AsSpan(1).ToArray(), git),
            "status" => await StatusAsync(args.AsSpan(1).ToArray(), git),
            "diff" => await DiffAsync(args.AsSpan(1).ToArray(), git),
            "pull" => await PullAsync(args.AsSpan(1).ToArray(), git),
            "bootstrap" => await BootstrapAsync(args.AsSpan(1).ToArray(), git),
            _ => 1,
        };
    }

    private static int Inspect(string[] args)
    {
        var url = args.FirstOrDefault();
        if (string.IsNullOrEmpty(url))
        {
            Console.Error.WriteLine("Error: URL is required");
            Console.Error.WriteLine("Usage: cachehub repo inspect <url>");
            return 1;
        }

        var parsed = RepositoryUrlParser.Parse(url);

        Console.WriteLine($"URL:         {parsed.OriginalUrl}");
        Console.WriteLine($"Normalized:  {parsed.NormalizedUrl}");
        Console.WriteLine($"Source:      {parsed.Source}");
        if (parsed.Host is not null) Console.WriteLine($"Host:        {parsed.Host}");
        if (parsed.Owner is not null) Console.WriteLine($"Owner:       {parsed.Owner}");
        if (parsed.RepoName is not null) Console.WriteLine($"Repository:  {parsed.RepoName}");

        return 0;
    }

    private static async Task<int> CloneAsync(string[] args, GitProcessWrapper git)
    {
        var url = args.FirstOrDefault();
        var dest = args.Skip(1).FirstOrDefault();
        var depth = args.SkipWhile(a => a != "--depth").Skip(1).FirstOrDefault();

        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(dest))
        {
            Console.Error.WriteLine("Error: URL and destination are required");
            Console.Error.WriteLine("Usage: cachehub repo clone <url> <destination> [--depth N]");
            return 1;
        }

        var parsed = RepositoryUrlParser.Parse(url);
        Console.Error.WriteLine($"Cloning {parsed.Owner}/{parsed.RepoName} from {parsed.Source}...");
        Console.Error.WriteLine($"  Destination: {dest}");
        Console.Error.WriteLine($"  Submodules: disabled (default)");
        Console.Error.WriteLine($"  LFS: disabled (default)");
        Console.Error.WriteLine($"  Hooks: not executed (default)");

        var plan = new ClonePlan
        {
            Url = url,
            Destination = dest,
            Depth = depth is not null && int.TryParse(depth, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null,
            IncludeSubmodules = false,
            IncludeLfs = false,
            Risks = ["Clone writes to disk", "Network access required"],
        };

        var result = await git.CloneAsync(plan);

        if (result.Success)
        {
            Console.Error.WriteLine("Clone completed successfully.");
            Console.WriteLine($"{{ \"cloned\": true, \"destination\": \"{dest.Replace('\\', '/')}\" }}");
            return 0;
        }
        else
        {
            Console.Error.WriteLine($"Clone failed: {result.ErrorMessage}");
            return 1;
        }
    }

    private static async Task<int> StatusAsync(string[] args, GitProcessWrapper git)
    {
        var path = args.FirstOrDefault() ?? Environment.CurrentDirectory;

        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"Error: Directory not found: {path}");
            return 1;
        }

        var result = await git.StatusAsync(path);

        if (result.Success)
        {
            if (string.IsNullOrWhiteSpace(result.Output))
            {
                Console.WriteLine("Working tree clean. No changes.");
            }
            else
            {
                Console.WriteLine("Changes:");
                Console.WriteLine(result.Output);
            }
            return 0;
        }
        else
        {
            Console.Error.WriteLine($"Git status failed: {result.ErrorMessage}");
            return 1;
        }
    }

    private static async Task<int> DiffAsync(string[] args, GitProcessWrapper git)
    {
        var path = args.FirstOrDefault() ?? Environment.CurrentDirectory;

        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"Error: Directory not found: {path}");
            return 1;
        }

        var result = await git.DiffAsync(path);

        if (result.Success)
        {
            var files = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (files.Length == 0)
            {
                Console.WriteLine("No changed files.");
            }
            else
            {
                Console.WriteLine($"Changed files ({files.Length}):");
                foreach (var file in files)
                    Console.WriteLine($"  {file.Trim()}");
            }
            return 0;
        }
        else
        {
            Console.Error.WriteLine($"Git diff failed: {result.ErrorMessage}");
            return 1;
        }
    }

    private static async Task<int> PullAsync(string[] args, GitProcessWrapper git)
    {
        var path = args.FirstOrDefault() ?? Environment.CurrentDirectory;

        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"Error: Directory not found: {path}");
            return 1;
        }

        Console.Error.WriteLine("Pulling with --ff-only (safe, no merge/rebase/reset)...");

        var result = await git.FfOnlyPullAsync(path);

        if (result.Success)
        {
            Console.Error.WriteLine("Pull completed (fast-forward).");
            Console.WriteLine("""{"pulled": true, "strategy": "ff-only"}""");
            return 0;
        }
        else
        {
            Console.Error.WriteLine($"Pull failed: {result.ErrorMessage}");
            Console.Error.WriteLine("  Possible reasons: local changes, diverged branches.");
            Console.Error.WriteLine("  CacheHub does not auto-merge, rebase, or reset. Resolve manually.");
            return 1;
        }
    }

    /// <summary>
    /// V5-W14+: `cachehub repo bootstrap` — one-step URL→clone→detect→import→index.
    /// Chains all existing capabilities into a single command so users don't need to run 5 separate CLI commands.
    /// </summary>
    private static async Task<int> BootstrapAsync(string[] args, GitProcessWrapper git)
    {
        var url = args.FirstOrDefault();
        if (string.IsNullOrEmpty(url))
        {
            Console.Error.WriteLine("Error: URL is required");
            Console.Error.WriteLine("Usage: cachehub repo bootstrap <url> [--dest <path>] [--name <name>]");
            Console.Error.WriteLine("  Chains: inspect → clone → detect → import → index");
            return 1;
        }

        // Parse optional arguments
        var dest = args.SkipWhile(a => a != "--dest").Skip(1).FirstOrDefault();
        var name = args.SkipWhile(a => a != "--name").Skip(1).FirstOrDefault();

        // Step 1: Inspect URL
        var parsed = RepositoryUrlParser.Parse(url);
        Console.Error.WriteLine($"[1/4] Inspecting URL: {parsed.Owner}/{parsed.RepoName} ({parsed.Source})");

        // Step 2: Clone
        dest ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            "CacheHub", "repos", parsed.RepoName ?? "repo");

        if (Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any())
        {
            // V6: Security — verify .git remote matches the requested URL before continuing
            var gitDir = Path.Combine(dest, ".git");
            if (Directory.Exists(gitDir))
            {
                Console.Error.WriteLine($"  Destination already exists with a .git directory: {dest}");
                var remoteResult = await git.ExecuteAsync(dest, ["config", "--get", "remote.origin.url"]);
                if (remoteResult.Success)
                {
                    var existingRemote = remoteResult.Output.Trim();
                    var normalizedExisting = existingRemote.Replace(".git", "", StringComparison.OrdinalIgnoreCase);
                    var normalizedRequested = url.Replace(".git", "", StringComparison.OrdinalIgnoreCase);
                    if (!string.Equals(normalizedExisting, normalizedRequested, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Error.WriteLine($"  ERROR: Destination .git remote URL does not match requested URL!");
                        Console.Error.WriteLine($"    Existing: {existingRemote}");
                        Console.Error.WriteLine($"    Requested: {url}");
                        Console.Error.WriteLine("  Use a different --dest or remove the existing directory.");
                        return 1;
                    }
                    Console.Error.WriteLine("  Remote URL matches. Continuing with existing clone.");
                }
                else
                {
                    Console.Error.WriteLine("  Warning: Could not read remote URL from existing .git. Proceeding with caution.");
                }
            }
            else
            {
                Console.Error.WriteLine($"  Destination already exists and is not empty (no .git): {dest}");
                Console.Error.WriteLine("  Skipping clone. Use a different --dest or remove the directory.");
            }
        }
        else
        {
            Console.Error.WriteLine($"[2/4] Cloning to: {dest}");
            var clonePlan = new ClonePlan
            {
                Url = url,
                Destination = dest,
                Depth = 1, // shallow clone for speed
                IncludeSubmodules = false,
                IncludeLfs = false,
                Risks = ["Clone writes to disk", "Network access required"],
            };
            var cloneResult = await git.CloneAsync(clonePlan);
            if (!cloneResult.Success)
            {
                Console.Error.WriteLine($"  Clone failed: {cloneResult.ErrorMessage}");
                return 1;
            }
            Console.Error.WriteLine("  Clone completed.");
        }

        // Step 3: Detect + Generate Init Plan
        Console.Error.WriteLine($"[3/4] Detecting project type...");
        var detectEngine = new ProjectDetectionEngine();
        var detection = detectEngine.Detect(dest);
        var initPlan = detectEngine.GeneratePlan(detection);

        if (detection.Components.Count > 0)
        {
            Console.Error.WriteLine($"  Found {detection.Components.Count} component(s):");
            foreach (var comp in detection.Components)
                Console.Error.WriteLine($"    - {comp.Language}" + (comp.Framework is not null ? $" ({comp.Framework})" : "") + $" at {comp.Path}");
        }
        else
        {
            Console.Error.WriteLine("  No recognized project components found (files will still be indexed).");
        }

        // V6: Output Init Plan (missing tools, recommended actions, requires approval)
        if (initPlan.Actions.Count > 0)
        {
            Console.Error.WriteLine($"  Init Plan ({initPlan.Actions.Count} action(s)):");
            foreach (var action in initPlan.Actions)
            {
                Console.Error.WriteLine($"    [{action.Approval}] {action.Command} — {action.Purpose}");
                if (action.Risks.Count > 0)
                    Console.Error.WriteLine($"      Risks: {string.Join(", ", action.Risks)}");
            }
            if (initPlan.MissingTools.Count > 0)
                Console.Error.WriteLine($"  Missing tools: {string.Join(", ", initPlan.MissingTools)}");
            Console.Error.WriteLine("  ⚠ Do NOT auto-execute init plan commands without user approval.");
        }

        // Step 4: Import workspace
        name ??= parsed.RepoName ?? new DirectoryInfo(dest).Name;
        Console.Error.WriteLine($"[4/4] Importing workspace '{name}'...");
        var appData = new AppDataDirectory();
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

        var wsRepo = new SqliteWorkspaceRepository(factory);
        var workspace = Workspace.CreateValidated(name, dest);
        await wsRepo.InsertAsync(workspace);
        Console.Error.WriteLine($"  Workspace imported: {workspace.Id.Value}");

        // Step 5: Build index
        Console.Error.WriteLine("  Building index...");
        var exitCode = await IndexCommands.HandleAsync(["build", $"--id={workspace.Id.Value}"]);

        if (exitCode == 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Bootstrap complete!");
            Console.Error.WriteLine($"  Workspace: {workspace.Id.Value}");
            Console.Error.WriteLine($"  Path:      {dest}");
            Console.Error.WriteLine($"  Components: {detection.Components.Count}");
            Console.Error.WriteLine($"  IsMonorepo: {detection.IsMonorepo}");
            Console.Error.WriteLine($"  Recommended actions: {initPlan.Actions.Count}");
            Console.Error.WriteLine($"  Missing tools: {initPlan.MissingTools.Count}");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Next steps:");
            Console.Error.WriteLine($"  cachehub context build --workspace={workspace.Id.Value} --task=\"<your task>\"");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                bootstrapped = true,
                workspaceId = workspace.Id.Value,
                path = dest.Replace('\\', '/'),
                components = detection.Components.Count,
                isMonorepo = detection.IsMonorepo,
                missingTools = initPlan.MissingTools,
                recommendedActions = initPlan.Actions.Select(a => new { command = a.Command, purpose = a.Purpose, risks = a.Risks }).ToList(),
                requiresApproval = initPlan.Actions.Where(a => a.MayRunScripts || a.WritesToDisk).Select(a => a.Command).ToList(),
            }, _jsonOpts));
        }

        return exitCode;
    }
}
