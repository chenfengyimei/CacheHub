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
                Capability.ContextFeedback,
                Capability.FileExport,
                Capability.Cache,
                Capability.Gateway),
            SchemaVersions = new Dictionary<string, int>
            {
                ["contextPackage"] = 1,
                ["capabilityDiscovery"] = 1,
                ["error"] = 1,
            },
            Limitations =
            [
                "Semantic Search: lexical similarity (FNV-1a hash embedding), wired as reference, bound to Snapshot/ContentHash",
                "LSP: scaffold only, regex-based parsing fallback",
                "Tokenizer: BPE model tokenizers (cl100k_base + o200k_base via Microsoft.ML.Tokenizers) + CodeTokenizer fallback",
                "Gateway: multi-provider fallback, Responses API streaming, SSE Usage parsing, persistent SqliteCacheStore",
                "Cache: ContextPackageCache wired (in-memory + persistent SqliteCacheStore) with complete CacheKey, Gateway raw cache persistent via SqliteCacheStore",
                "Benchmark: retrieval (Recall@10 + TokenReduction) via real ContextEngine + Agent Benchmark (task→model→patch→cost) via Gateway",
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
            Console.WriteLine("Parser Coverage (12 languages):");
            Console.WriteLine("  C# / TypeScript / JavaScript / Python / Go / Rust");
            Console.WriteLine("  Java / C / C++ / PHP / Ruby / Kotlin / Swift + Markdown");
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
            Console.WriteLine();
            Console.WriteLine("Diagnostic: Run 'cachehub doctor' to check environment");
        }

        return 0;
    }
}
