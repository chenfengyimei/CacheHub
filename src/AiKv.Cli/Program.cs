using AiKv.Cli.Commands;

if (args.Length == 0)
{
    Console.WriteLine("AI_KV - Local code context infrastructure");
    Console.WriteLine();
    Console.WriteLine("Usage: aikv <command> [options]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  capabilities [--output=json]      Show available capabilities");
    Console.WriteLine("  workspace import <path>           Import a local directory as a workspace");
    Console.WriteLine("  workspace list                     List all workspaces");
    Console.WriteLine("  workspace status --id=<id>         Show workspace status");
    Console.WriteLine("  workspace remove --id=<id>         Remove a workspace");
    Console.WriteLine("  index build --id=<id>              Build a new index snapshot");
    Console.WriteLine("  index status --id=<id>             Show index status");
    Console.WriteLine("  index verify --id=<id>             Verify index consistency");
    Console.WriteLine("  context build --workspace=<id> --task=<text> [--output=json]");
    Console.WriteLine("  context inspect --id=<ctx-id>      Inspect a context package");
    Console.WriteLine("  context export --id=<ctx-id> [--format=markdown|json]");
    Console.WriteLine("  context expand --id=<ctx-id> --symbol=<name>");
    Console.WriteLine("  context feedback --id=<ctx-id> --file=<path>");
    Console.WriteLine("  detect <path> [--plan] [--output=json]  Detect project type");
    Console.WriteLine("  gateway start --provider-url=<url> [--provider-key=<key>] [--port=<port>]");
    Console.WriteLine("  gateway status                     Show gateway status");
    Console.WriteLine("  config show                        Show current configuration");
    Console.WriteLine("  config init                        Create default config file");
    Console.WriteLine("  config set <key> <value>           Set a configuration value");
    Console.WriteLine("  stats [--output=json]              Show usage statistics");
    Console.WriteLine("  repo inspect <url>                  Parse and inspect a Git URL");
    Console.WriteLine("  repo clone <url> <dest> [--depth N] Clone a repository (safe defaults)");
    Console.WriteLine("  repo status [path]                  Show Git status");
    Console.WriteLine("  repo diff [path]                    Show changed files");
    Console.WriteLine("  repo pull [path]                    Pull with --ff-only (safe)");
    Console.WriteLine("  integration verify                 Verify installation and integration");
    return 1;
}

return args[0] switch
{
    "workspace" => await WorkspaceCommands.HandleAsync(args.AsSpan(1).ToArray()),
    "index" => await IndexCommands.HandleAsync(args.AsSpan(1).ToArray()),
    "context" => await ContextCommands.HandleAsync(args.AsSpan(1).ToArray()),
    "capabilities" => CapabilitiesCommands.Handle(args.AsSpan(1).ToArray()),
    "integration" => await IntegrationCommands.HandleAsync(args.AsSpan(1).ToArray()),
    "detect" => DetectCommands.Handle(args.AsSpan(1).ToArray()),
    "gateway" => await GatewayCommands.HandleAsync(args.AsSpan(1).ToArray()),
    "config" => ConfigCommands.Handle(args.AsSpan(1).ToArray()),
    "stats" => await StatsCommands.HandleAsync(args.AsSpan(1).ToArray()),
    "repo" => await RepoCommands.HandleAsync(args.AsSpan(1).ToArray()),
    _ => 1,
};
