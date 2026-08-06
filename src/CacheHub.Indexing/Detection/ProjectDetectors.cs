using CacheHub.Core.Detection;

namespace CacheHub.Indexing.Detection;

/// <summary>
/// Detects Node.js projects: package.json, framework, package manager.
/// </summary>
public sealed class NodeDetector : IProjectDetector
{
    public string Id => "node";
    public IReadOnlySet<string> TriggerFiles => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "package.json" };

    public DetectedComponent? Detect(string rootPath, IReadOnlyDictionary<string, string> triggerFileContents)
    {
        if (!triggerFileContents.TryGetValue("package.json", out var content)) return null;

        var evidence = new List<string> { "package.json found" };
        string? framework = null;
        string? packageManager = null;

        if (content.Contains("\"next\"", StringComparison.OrdinalIgnoreCase)) { framework = "Next.js"; evidence.Add("next dependency"); }
        else if (content.Contains("\"react\"", StringComparison.OrdinalIgnoreCase)) { framework = "React"; evidence.Add("react dependency"); }
        else if (content.Contains("\"vue\"", StringComparison.OrdinalIgnoreCase)) { framework = "Vue"; evidence.Add("vue dependency"); }
        else if (content.Contains("\"express\"", StringComparison.OrdinalIgnoreCase)) { framework = "Express"; evidence.Add("express dependency"); }

        if (rootPath.Contains("pnpm-lock", StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(rootPath, "pnpm-lock.yaml")))
            packageManager = "pnpm";
        else if (File.Exists(Path.Combine(rootPath, "yarn.lock")))
            packageManager = "yarn";
        else
            packageManager = "npm";

        return new DetectedComponent
        {
            Id = "node-" + Path.GetFileName(rootPath),
            Path = rootPath,
            Language = "typescript",
            Framework = framework,
            BuildSystem = "npm-scripts",
            PackageManager = packageManager,
            Evidence = evidence,
        };
    }
}

/// <summary>
/// Detects Python projects: pyproject.toml, requirements.txt, setup.py.
/// </summary>
public sealed class PythonDetector : IProjectDetector
{
    public string Id => "python";
    public IReadOnlySet<string> TriggerFiles => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "pyproject.toml", "requirements.txt", "setup.py", "Pipfile"
    };

    public DetectedComponent? Detect(string rootPath, IReadOnlyDictionary<string, string> triggerFileContents)
    {
        var evidence = new List<string>();
        string? packageManager = null;

        if (triggerFileContents.ContainsKey("pyproject.toml")) { evidence.Add("pyproject.toml"); packageManager = "pip/poetry"; }
        if (triggerFileContents.ContainsKey("requirements.txt")) { evidence.Add("requirements.txt"); packageManager ??= "pip"; }
        if (triggerFileContents.ContainsKey("setup.py")) { evidence.Add("setup.py"); packageManager ??= "setuptools"; }
        if (triggerFileContents.ContainsKey("Pipfile")) { evidence.Add("Pipfile"); packageManager = "pipenv"; }

        if (evidence.Count == 0) return null;

        return new DetectedComponent
        {
            Id = "python-" + Path.GetFileName(rootPath),
            Path = rootPath,
            Language = "python",
            BuildSystem = packageManager,
            PackageManager = packageManager,
            Evidence = evidence,
        };
    }
}

/// <summary>
/// Detects .NET projects: *.sln, *.csproj, global.json.
/// </summary>
public sealed class DotNetDetector : IProjectDetector
{
    public string Id => "dotnet";
    public IReadOnlySet<string> TriggerFiles => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "global.json", "*.sln", "*.csproj"
    };

    public DetectedComponent? Detect(string rootPath, IReadOnlyDictionary<string, string> triggerFileContents)
    {
        var evidence = new List<string>();

        if (triggerFileContents.ContainsKey("global.json")) evidence.Add("global.json");
        if (triggerFileContents.Keys.Any(k => k.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))) evidence.Add("*.sln");
        if (triggerFileContents.Keys.Any(k => k.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))) evidence.Add("*.csproj");

        if (evidence.Count == 0) return null;

        return new DetectedComponent
        {
            Id = "dotnet-" + Path.GetFileName(rootPath),
            Path = rootPath,
            Language = "csharp",
            Framework = "ASP.NET Core",
            BuildSystem = "MSBuild",
            PackageManager = "NuGet",
            Evidence = evidence,
        };
    }
}

/// <summary>
/// Detects Go, Rust, Java/Kotlin, C/C++ projects.
/// </summary>
public sealed class GoDetector : IProjectDetector
{
    public string Id => "go";
    public IReadOnlySet<string> TriggerFiles => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "go.mod" };

    public DetectedComponent? Detect(string rootPath, IReadOnlyDictionary<string, string> triggerFileContents)
    {
        if (!triggerFileContents.ContainsKey("go.mod")) return null;
        return new DetectedComponent
        {
            Id = "go-" + Path.GetFileName(rootPath),
            Path = rootPath,
            Language = "go",
            BuildSystem = "Go Modules",
            PackageManager = "go",
            Evidence = ["go.mod found"],
        };
    }
}

public sealed class RustDetector : IProjectDetector
{
    public string Id => "rust";
    public IReadOnlySet<string> TriggerFiles => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Cargo.toml" };

    public DetectedComponent? Detect(string rootPath, IReadOnlyDictionary<string, string> triggerFileContents)
    {
        if (!triggerFileContents.ContainsKey("Cargo.toml")) return null;
        return new DetectedComponent
        {
            Id = "rust-" + Path.GetFileName(rootPath),
            Path = rootPath,
            Language = "rust",
            BuildSystem = "Cargo",
            PackageManager = "cargo",
            Evidence = ["Cargo.toml found"],
        };
    }
}

public sealed class JavaDetector : IProjectDetector
{
    public string Id => "java";
    public IReadOnlySet<string> TriggerFiles => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "pom.xml", "build.gradle", "build.gradle.kts", "settings.gradle"
    };

    public DetectedComponent? Detect(string rootPath, IReadOnlyDictionary<string, string> triggerFileContents)
    {
        var evidence = new List<string>();
        string? buildSystem = null;

        if (triggerFileContents.ContainsKey("pom.xml")) { evidence.Add("pom.xml"); buildSystem = "Maven"; }
        if (triggerFileContents.ContainsKey("build.gradle") || triggerFileContents.ContainsKey("build.gradle.kts"))
        { evidence.Add("build.gradle"); buildSystem ??= "Gradle"; }

        if (evidence.Count == 0) return null;

        return new DetectedComponent
        {
            Id = "java-" + Path.GetFileName(rootPath),
            Path = rootPath,
            Language = "java",
            BuildSystem = buildSystem,
            PackageManager = buildSystem,
            Evidence = evidence,
        };
    }
}

/// <summary>
/// Detects Unity, Unreal, Flutter projects.
/// </summary>
public sealed class UnityDetector : IProjectDetector
{
    public string Id => "unity";
    public IReadOnlySet<string> TriggerFiles => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ProjectSettings/ProjectVersion.txt"
    };

    public DetectedComponent? Detect(string rootPath, IReadOnlyDictionary<string, string> triggerFileContents)
    {
        var key = triggerFileContents.Keys.FirstOrDefault(k =>
            k.Replace('\\', '/').Contains("ProjectSettings/ProjectVersion.txt", StringComparison.OrdinalIgnoreCase));
        if (key is null) return null;

        var version = "unknown";
        if (triggerFileContents[key].Contains("m_EditorVersion:", StringComparison.OrdinalIgnoreCase))
        {
            var idx = triggerFileContents[key].IndexOf("m_EditorVersion:");
            version = triggerFileContents[key][(idx + 17)..].Trim().Split('\n')[0].Trim();
        }

        return new DetectedComponent
        {
            Id = "unity-" + Path.GetFileName(rootPath),
            Path = rootPath,
            Language = "csharp",
            Framework = $"Unity {version}",
            BuildSystem = "Unity Editor",
            Evidence = ["ProjectVersion.txt found"],
        };
    }
}

public sealed class FlutterDetector : IProjectDetector
{
    public string Id => "flutter";
    public IReadOnlySet<string> TriggerFiles => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pubspec.yaml" };

    public DetectedComponent? Detect(string rootPath, IReadOnlyDictionary<string, string> triggerFileContents)
    {
        if (!triggerFileContents.ContainsKey("pubspec.yaml")) return null;
        return new DetectedComponent
        {
            Id = "flutter-" + Path.GetFileName(rootPath),
            Path = rootPath,
            Language = "dart",
            Framework = "Flutter",
            BuildSystem = "pub",
            Evidence = ["pubspec.yaml found"],
        };
    }
}

/// <summary>
/// Detects Docker, Terraform, CI config files.
/// </summary>
public sealed class DockerDetector : IProjectDetector
{
    public string Id => "docker";
    public IReadOnlySet<string> TriggerFiles => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Dockerfile", "docker-compose.yml", "docker-compose.yaml", "compose.yaml"
    };

    public DetectedComponent? Detect(string rootPath, IReadOnlyDictionary<string, string> triggerFileContents)
    {
        var evidence = triggerFileContents.Keys
            .Where(k => TriggerFiles.Contains(k))
            .Select(k => $"{k} found")
            .ToList();

        if (evidence.Count == 0) return null;

        return new DetectedComponent
        {
            Id = "docker-" + Path.GetFileName(rootPath),
            Path = rootPath,
            Language = "dockerfile",
            BuildSystem = "Docker",
            Confidence = 0.8,
            Evidence = evidence,
        };
    }
}
