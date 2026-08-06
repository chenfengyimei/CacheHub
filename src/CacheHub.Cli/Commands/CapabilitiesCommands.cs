using System.Text.Json;
using CacheHub.Core.Capabilities;

namespace CacheHub.Cli.Commands;

/// <summary>
/// Handles the `cachehub capabilities` command.
/// Outputs JSON to stdout; logs go to stderr.
/// </summary>
public static class CapabilitiesCommands
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };
    public static int Handle(string[] args)
    {
        var outputJson = args.Contains("--output=json", StringComparer.OrdinalIgnoreCase)
                         || args.Contains("--json", StringComparer.OrdinalIgnoreCase);

        var cd = new CapabilityDiscovery
        {
            Version = "0.2.0-prealpha",
            ProtocolVersion = "1.0",
            Capabilities = CapabilityFlags.With(
                Capability.WorkspaceImport,
                Capability.ContextBuild,
                Capability.ContextExpand,
                Capability.ContextExplain,
                Capability.FileExport),
            SchemaVersions = new Dictionary<string, int>
            {
                ["contextPackage"] = 1,
                ["capabilityDiscovery"] = 1,
                ["error"] = 1,
            },
            Limitations =
            [
                "No Semantic Search",
                "No LSP",
                "Tokenizer is rough estimate (chars/4)",
                "Gateway: experimental, no streaming, no real usage tracking",
                "Cache: in-memory only, not cross-session",
                "Context Build: no symbol recall, no FTS recall",
                "Incremental index: not implemented",
            ],
        };

        if (outputJson)
        {
            var json = JsonSerializer.Serialize(cd, _jsonOpts);
            Console.WriteLine(json);
        }
        else
        {
            Console.WriteLine($"CacheHub v{cd.Version} (Protocol {cd.ProtocolVersion})");
            Console.WriteLine();
            Console.WriteLine("Capabilities:");
            Console.WriteLine($"  WorkspaceImport:  {cd.Capabilities.WorkspaceImport}");
            Console.WriteLine($"  ContextBuild:     {cd.Capabilities.ContextBuild}");
            Console.WriteLine($"  ContextExpand:   {cd.Capabilities.ContextExpand}");
            Console.WriteLine($"  ContextExplain:  {cd.Capabilities.ContextExplain}");
            Console.WriteLine($"  FileExport:      {cd.Capabilities.FileExport}");
            Console.WriteLine($"  Cache:           {cd.Capabilities.Cache}");
            Console.WriteLine($"  Gateway:         {cd.Capabilities.Gateway}");
            Console.WriteLine($"  Semantic:        {cd.Capabilities.Semantic}");
            Console.WriteLine($"  LSP:              {cd.Capabilities.Lsp}");
            Console.WriteLine();
            Console.WriteLine("Schema Versions:");
            if (cd.SchemaVersions is not null)
                foreach (var kv in cd.SchemaVersions)
                    Console.WriteLine($"  {kv.Key}: v{kv.Value}");
            Console.WriteLine();
            Console.WriteLine("Limitations:");
            if (cd.Limitations is not null)
                foreach (var lim in cd.Limitations)
                    Console.WriteLine($"  - {lim}");
        }

        return 0;
    }
}
