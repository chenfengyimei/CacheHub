using CacheHub.Core.Repository;

namespace CacheHub.Cli.Commands;

public static class RepoCommands
{
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
}
