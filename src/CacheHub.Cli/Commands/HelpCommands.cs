namespace CacheHub.Cli.Commands;

public static class HelpCommands
{
    public static int Handle(string[] args)
    {
        if (args.Length == 0)
        {
            PrintFullHelp();
            return 0;
        }

        return args[0] switch
        {
            "workspace" => PrintWorkspaceHelp(),
            "index" => PrintIndexHelp(),
            "context" => PrintContextHelp(),
            "detect" => PrintDetectHelp(),
            "gateway" => PrintGatewayHelp(),
            "config" => PrintConfigHelp(),
            "stats" => PrintStatsHelp(),
            "repo" => PrintRepoHelp(),
            "version" => PrintVersionHelp(),
            "capabilities" => PrintCapabilitiesHelp(),
            "integration" => PrintIntegrationHelp(),
            _ => PrintFullHelp(),
        };
    }

    private static int PrintFullHelp()
    {
        Console.WriteLine("""
            CacheHub — Local code context infrastructure
            Version 0.2.0-prealpha | Protocol 1.0 | MIT License

            USAGE:
                cachehub <command> [subcommand] [options]

            COMMANDS:
                capabilities          Show available capabilities
                workspace             Manage workspaces (import/list/status/remove)
                index                 Build and verify file index (build/status/verify)
                context               Build and manage context packages
                detect                Detect project type and generate init plan
                gateway               Start/stop the optional model gateway
                config                Manage configuration file
                stats                 Show usage statistics
                repo                  Git repository operations (inspect/clone/status/diff/pull)
                version               Show version information
                integration           Verify installation (verify)
                help                  Show this help or command-specific help

            GLOBAL OPTIONS:
                --output=json         Output in JSON format (most commands)
                --json                Alias for --output=json

            QUICK START:
                cachehub workspace import /path/to/project
                cachehub index build --id=<workspace-id>
                cachehub context build --workspace=<id> --task="Fix login bug" --output=json
                cachehub integration verify

            DOCUMENTATION:
                https://github.com/chenfengyimei/CacheHub
                AGENTS.md — Agent integration guide

            """);
        return 0;
    }

    private static int PrintWorkspaceHelp()
    {
        Console.WriteLine("""
            cachehub workspace — Manage workspaces

            SUBCOMMANDS:
                import <path> [--name=<name>]     Import a local directory as a workspace
                list                              List all registered workspaces
                status --id=<workspace-id>        Show workspace status
                remove --id=<workspace-id>        Remove workspace (CacheHub data only, not source)

            EXAMPLES:
                cachehub workspace import C:\projects\myapp --name="MyApp"
                cachehub workspace list
                cachehub workspace status --id=abc123
                cachehub workspace remove --id=abc123

            """);
        return 0;
    }

    private static int PrintIndexHelp()
    {
        Console.WriteLine("""
            cachehub index — Build and verify file index

            SUBCOMMANDS:
                build --id=<workspace-id>         Build a new index snapshot
                status --id=<workspace-id>        Show index status and active snapshot
                verify --id=<workspace-id>        Verify index consistency against disk

            EXAMPLES:
                cachehub index build --id=abc123
                cachehub index status --id=abc123
                cachehub index verify --id=abc123

            """);
        return 0;
    }

    private static int PrintContextHelp()
    {
        Console.WriteLine("""
            cachehub context — Build and manage context packages

            SUBCOMMANDS:
                build --workspace=<id> --task=<text> [--git-diff] [--model=<id>] [--output=json]
                        Build a context package and persist it
                inspect --id=<ctx-id> [--output=json]
                        Inspect a persisted context package
                list --workspace=<id> [--output=json]
                        List all context packages for a workspace
                export --id=<ctx-id> [--format=markdown|json|file]
                        Export context (markdown to stdout, json, or .cachehub/ directory)
                expand --id=<ctx-id> --symbol=<name> | --file=<path> [--reason=<text>]
                        Expand context with additional files
                feedback --id=<ctx-id> --file=<feedback.json>
                        Submit agent feedback for ranking improvement

            EXAMPLES:
                cachehub context build --workspace=abc123 --task="Fix token refresh" --git-diff --output=json
                cachehub context inspect --id=ctx001
                cachehub context list --workspace=abc123
                cachehub context export --id=ctx001 --format=markdown
                cachehub context expand --id=ctx001 --file=src/auth.ts --reason="Missing auth implementation"
                cachehub context feedback --id=ctx001 --file=feedback.json

            """);
        return 0;
    }

    private static int PrintDetectHelp()
    {
        Console.WriteLine("""
            cachehub detect — Detect project type and generate initialization plan

            USAGE:
                cachehub detect <path> [--plan] [--output=json]

            OPTIONS:
                --plan             Include initialization actions in output
                --output=json      Output in JSON format

            EXAMPLES:
                cachehub detect C:\projects\myapp
                cachehub detect C:\projects\monorepo --plan --output=json

            """);
        return 0;
    }

    private static int PrintGatewayHelp()
    {
        Console.WriteLine("""
            cachehub gateway — Optional model API gateway

            SUBCOMMANDS:
                start --provider-url=<url> [--provider-key=<key>] [--port=<port>]
                        Start the gateway on loopback (default port 5218)
                status  Check gateway status
                stop    Stop the running gateway (send Ctrl+C to the process)

            FEATURES:
                - OpenAI-compatible API forwarding
                - Raw Exact Cache (safe requests only)
                - SingleFlight (concurrent request deduplication)
                - Usage statistics

            EXAMPLES:
                cachehub gateway start --provider-url=https://api.openai.com --provider-key=sk-xxx
                cachehub gateway status

            """);
        return 0;
    }

    private static int PrintConfigHelp()
    {
        Console.WriteLine("""
            cachehub config — Manage configuration file

            SUBCOMMANDS:
                show                  Show current configuration
                init                  Create default .cachehub-config.json
                set <key> <value>     Set a configuration value

            SETTABLE KEYS:
                defaultModel          Default model ID for context build
                security.mode         Standard|Restricted|PreviewRequired|Offline
                gateway.enabled       true|false
                gateway.port          Port number
                gateway.providerUrl   Provider API URL

            EXAMPLES:
                cachehub config init
                cachehub config show
                cachehub config set defaultModel gpt-4
                cachehub config set security.mode Restricted

            """);
        return 0;
    }

    private static int PrintStatsHelp()
    {
        Console.WriteLine("""
            cachehub stats — Show usage statistics

            USAGE:
                cachehub stats [--output=json]

            OUTPUT:
                - Number of workspaces
                - Number of context packages
                - Total estimated tokens
                - Workspace status distribution
                - Data directory path

            """);
        return 0;
    }

    private static int PrintRepoHelp()
    {
        Console.WriteLine("""
            cachehub repo — Git repository operations

            SUBCOMMANDS:
                inspect <url>                        Parse and inspect a Git URL
                clone <url> <dest> [--depth N]       Clone (no submodules/LFS/hooks by default)
                status [path]                        Show git status (porcelain)
                diff [path]                          Show changed file names
                pull [path]                          Pull with --ff-only (safe, no merge)

            SAFETY:
                - Never auto-merges, rebases, or resets
                - Never executes hooks or install scripts
                - Stops on local changes or diverged branches

            EXAMPLES:
                cachehub repo inspect https://github.com/user/repo.git
                cachehub repo clone https://github.com/user/repo.git ./local-repo --depth 1
                cachehub repo status C:\projects\myapp
                cachehub repo pull C:\projects\myapp

            """);
        return 0;
    }

    private static int PrintVersionHelp()
    {
        Console.WriteLine("""
            cachehub version — Show version information

            USAGE:
                cachehub version [--output=json]

            OUTPUT:
                - CacheHub version
                - Protocol version
                - .NET SDK version
                - OS information
                - Machine name
                - Current timestamp

            """);
        return 0;
    }

    private static int PrintCapabilitiesHelp()
    {
        Console.WriteLine("""
            cachehub capabilities — Show available capabilities

            USAGE:
                cachehub capabilities [--output=json]

            OUTPUT:
                - Version and protocol version
                - Enabled capabilities (workspaceImport, contextBuild, etc.)
                - Schema versions
                - Current limitations

            """);
        return 0;
    }

    private static int PrintIntegrationHelp()
    {
        Console.WriteLine("""
            cachehub integration — Verify installation

            USAGE:
                cachehub integration verify

            CHECKS:
                1. Data directory accessible
                2. Database and migrations applied
                3. Workspace repository accessible
                4. CLI capabilities functional
                5. Rollback capability (safe remove)

            """);
        return 0;
    }
}
