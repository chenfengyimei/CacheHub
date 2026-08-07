using CacheHub.Core.Detection;

namespace CacheHub.Indexing.Detection;

/// <summary>
/// Runs all registered detectors against a directory.
/// Collects evidence, computes language stats, and detects monorepo structure.
/// </summary>
public sealed class ProjectDetectionEngine
{
    private readonly List<IProjectDetector> _detectors;

    public ProjectDetectionEngine(IEnumerable<IProjectDetector>? detectors = null)
    {
        _detectors = detectors?.ToList() ??
        [
            new NodeDetector(),
            new PythonDetector(),
            new DotNetDetector(),
            new GoDetector(),
            new RustDetector(),
            new AndroidDetector(), // before JavaDetector — Android projects also use build.gradle
            new JavaDetector(),
            new UnityDetector(),
            new FlutterDetector(),
            new DockerDetector(),
            // V5-W13: expanded detection coverage
            new CMakeDetector(),
            new SwiftDetector(),
            new PhpDetector(),
            new RubyDetector(),
            new TerraformDetector(),
            new UnrealDetector(),
            new GenericDetector(), // must be last — catches unstructured source dirs
        ];
    }

    /// <summary>
    /// Detects all components in a directory tree. Read-only — never executes scripts.
    /// </summary>
    public DetectionResult Detect(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException($"Directory not found: {rootPath}");

        var components = new List<DetectedComponent>();
        var languageStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // V5-W13: Scan root + up to 3 levels deep for monorepo component roots
        var searchDirs = CollectSearchDirectories(rootPath, maxDepth: 3);

        foreach (var dir in searchDirs.Distinct())
        {
            var triggerFiles = CollectTriggerFiles(dir);
            if (triggerFiles.Count == 0) continue;

            foreach (var detector in _detectors)
            {
                var component = detector.Detect(dir, triggerFiles);
                if (component is not null)
                {
                    components.Add(component);
                    var lang = component.Language;
                    languageStats[lang] = languageStats.GetValueOrDefault(lang) + 1;
                    break; // First match wins per directory
                }
            }
        }

        // Also count files by extension for language stats
        if (languageStats.Count == 0)
        {
            foreach (var file in Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories)
                .Take(500))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                var lang = ext switch
                {
                    ".cs" => "csharp",
                    ".ts" or ".tsx" => "typescript",
                    ".js" or ".jsx" => "javascript",
                    ".py" => "python",
                    ".go" => "go",
                    ".rs" => "rust",
                    ".java" => "java",
                    ".cpp" or ".cc" or ".cxx" => "cpp",
                    ".c" => "c",
                    ".rb" => "ruby",
                    ".php" => "php",
                    _ => null,
                };
                if (lang is not null)
                    languageStats[lang] = languageStats.GetValueOrDefault(lang) + 1;
            }
        }

        // V6: A monorepo is any repo with multiple independent component roots,
        // regardless of whether they use the same language (e.g. apps/web + apps/admin + packages/ui all TypeScript).
        var isMonorepo = components.Count > 1;

        return new DetectionResult
        {
            RootPath = rootPath,
            Components = components,
            LanguageStats = languageStats,
            IsMonorepo = isMonorepo,
        };
    }

    /// <summary>
    /// Generates an initialization plan from detection results.
    /// All actions require approval — detection is read-only.
    /// </summary>
    public InitializationPlan GeneratePlan(DetectionResult result)
    {
        var actions = new List<InitAction>();

        foreach (var comp in result.Components)
        {
            var (cmd, purpose, needsNet, writes, runsScripts, risks) = comp.PackageManager switch
            {
                "npm" => ("npm install", "Install Node.js dependencies", true, true, true,
                    ["May execute postinstall scripts", "Writes to node_modules"]),
                "pnpm" => ("pnpm install", "Install Node.js dependencies (pnpm)", true, true, true,
                    ["May execute postinstall scripts", "Writes to node_modules"]),
                "yarn" => ("yarn install", "Install Node.js dependencies (yarn)", true, true, true,
                    ["May execute postinstall scripts", "Writes to node_modules"]),
                "pip" => ("pip install -r requirements.txt", "Install Python dependencies", true, true, false,
                    ["Writes to site-packages"]),
                "pip/poetry" => ("poetry install", "Install Python dependencies (Poetry)", true, true, false,
                    ["Writes to virtual environment"]),
                "NuGet" => ("dotnet restore", "Restore NuGet packages", true, true, false,
                    ["Writes to obj/ directory"]),
                "go" => ("go mod download", "Download Go modules", true, true, false, []),
                "cargo" => ("cargo fetch", "Download Rust crates", true, true, false, []),
                "Maven" => ("mvn install", "Install Maven dependencies", true, true, false,
                    ["May compile code", "Writes to target/"]),
                "Gradle" => ("./gradlew build", "Build Gradle project", true, true, true,
                    ["May execute Gradle scripts", "Writes to build/"]),
                "pub" => ("flutter pub get", "Install Flutter dependencies", true, true, false, []),
                "Composer" => ("composer install", "Install PHP dependencies", true, true, false,
                    ["Writes to vendor/"]),
                "Bundler" => ("bundle install", "Install Ruby gems", true, true, false,
                    ["Writes to bundle path"]),
                "SPM" => ("swift build", "Build Swift package", true, true, false,
                    ["Writes to .build/"]),
                "none" => (null, "No package manager detected", false, false, false, Array.Empty<string>()),
                _ => ((string?)null, (string?)null, false, false, false, Array.Empty<string>()),
            };

            if (cmd is not null)
            {
                actions.Add(new InitAction
                {
                    Command = cmd,
                    Purpose = purpose!,
                    WorkingDirectory = comp.Path,
                    RequiresNetwork = needsNet,
                    WritesToDisk = writes,
                    MayRunScripts = runsScripts,
                    Approval = runsScripts ? ApprovalLevel.EveryTimeApproval : ApprovalLevel.OneTimeApproval,
                    Risks = risks,
                });
            }
        }

        return new InitializationPlan
        {
            RootPath = result.RootPath,
            Actions = actions,
            DetectedComponents = result.Components.Select(c => $"{c.Language} ({c.Framework ?? c.BuildSystem ?? "unknown"}) at {c.Path}").ToList(),
            MissingTools = result.MissingTools,
        };
    }

    /// <summary>
    /// V5-W13: Collects directories to search, supporting deep Monorepo structure (up to maxDepth levels).
    /// Skips common non-source directories (node_modules, .git, bin, obj, etc.).
    /// </summary>
    private static List<string> CollectSearchDirectories(string rootPath, int maxDepth)
    {
        var result = new List<string> { rootPath };
        var queue = new Queue<(string dir, int depth)>();
        queue.Enqueue((rootPath, 0));

        while (queue.Count > 0)
        {
            var (dir, depth) = queue.Dequeue();
            if (depth >= maxDepth) continue;

            foreach (var sub in Directory.GetDirectories(dir, "*", SearchOption.TopDirectoryOnly))
            {
                if (ShouldSkipDir(sub)) continue;
                result.Add(sub);
                queue.Enqueue((sub, depth + 1));
            }
        }

        return result.Distinct().ToList();
    }

    private static Dictionary<string, string> CollectTriggerFiles(string dir)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            result[name] = File.Exists(file) ? File.ReadAllText(file) : "";
        }

        // Check for nested trigger files like ProjectSettings/ProjectVersion.txt
        var projectSettings = Path.Combine(dir, "ProjectSettings", "ProjectVersion.txt");
        if (File.Exists(projectSettings))
            result["ProjectSettings/ProjectVersion.txt"] = File.ReadAllText(projectSettings);

        // Check for *.sln and *.csproj
        foreach (var sln in Directory.EnumerateFiles(dir, "*.sln", SearchOption.TopDirectoryOnly))
            result[Path.GetFileName(sln)] = File.ReadAllText(sln);
        foreach (var csproj in Directory.EnumerateFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly))
            result[Path.GetFileName(csproj)] = File.ReadAllText(csproj);

        return result;
    }

    private static bool ShouldSkipDir(string dir)
    {
        var name = Path.GetFileName(dir);
        return name.StartsWith('.') || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("target", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("dist", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("build", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("vendor", StringComparison.OrdinalIgnoreCase);
    }
}
