using CacheHub.Core.Security;

namespace CacheHub.Cli.Commands;

public static class ScanCommands
{
    public static int Handle(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: cachehub scan <file|directory> [--output=json]");
            return 1;
        }

        var target = args[0];
        var outputJson = args.Contains("--output=json", StringComparer.OrdinalIgnoreCase) ||
                         args.Contains("--json", StringComparer.OrdinalIgnoreCase);

        var scanner = new SecretScanner();
        var findings = new List<(string file, SecurityFinding finding)>();

        if (File.Exists(target))
        {
            var content = File.ReadAllText(target);
            var result = scanner.Scan(target, content);
            foreach (var f in result.Findings)
                findings.Add((target, f));
        }
        else if (Directory.Exists(target))
        {
            foreach (var file in Directory.EnumerateFiles(target, "*.*", SearchOption.AllDirectories))
            {
                // Skip binary/sensitive by extension
                if (SecretScanner.IsSensitiveFile(file))
                {
                    findings.Add((file, new SecurityFinding
                    {
                        Type = SecurityFindingType.Certificate,
                        FilePath = file,
                        Line = 0,
                        Description = "Sensitive file detected by name pattern",
                    }));
                    continue;
                }

                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".exe" or ".dll" or ".png" or ".jpg" or ".zip" or ".gz") continue;

                string content;
                try { content = File.ReadAllText(file); }
                catch { continue; }

                var result = scanner.Scan(file, content);
                foreach (var f in result.Findings)
                    findings.Add((file, f));
            }
        }
        else
        {
            Console.Error.WriteLine($"Error: File or directory not found: {target}");
            return 1;
        }

        if (outputJson)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                target,
                totalFindings = findings.Count,
                findings = findings.Select(f => new
                {
                    file = f.file,
                    type = f.finding.Type.ToString(),
                    line = f.finding.Line,
                    description = f.finding.Description,
                }),
            }, _jsonOpts));
        }
        else
        {
            Console.WriteLine($"Security scan: {target}");
            Console.WriteLine($"Scanner: {SecretScanner.Version}");
            Console.WriteLine($"Findings: {findings.Count}");
            Console.WriteLine();

            if (findings.Count == 0)
            {
                Console.WriteLine("✅ No security issues found.");
            }
            else
            {
                Console.WriteLine("⚠ Security findings:");
                foreach (var (file, finding) in findings)
                {
                    Console.WriteLine($"  [{finding.Type}] {file}:{finding.Line}");
                    Console.WriteLine($"    {finding.Description}");
                }
            }
        }

        return findings.Count > 0 ? 1 : 0;
    }

    private static readonly System.Text.Json.JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };
}
