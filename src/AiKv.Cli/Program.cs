using AiKv.Cli.Commands;

if (args.Length == 0)
{
    Console.WriteLine("AI_KV - Local code context infrastructure");
    Console.WriteLine();
    Console.WriteLine("Usage: aikv <command> [options]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  capabilities              Show available capabilities");
    Console.WriteLine("  workspace import <path>   Import a local directory as a workspace");
    Console.WriteLine("  workspace list            List all workspaces");
    Console.WriteLine("  workspace status --id=<id> Show workspace status");
    Console.WriteLine("  workspace remove --id=<id> Remove a workspace");
    return 1;
}

return await WorkspaceCommands.HandleAsync(args);
