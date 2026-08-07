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

        // Determine the actual source language: TypeScript only if there is TS evidence.
        var language = DetectLanguage(rootPath, content);

        return new DetectedComponent
        {
            Id = "node-" + Path.GetFileName(rootPath),
            Path = rootPath,
            Language = language,
            Framework = framework,
            BuildSystem = "npm-scripts",
            PackageManager = packageManager,
            Evidence = evidence,
        };
    }

    private static string DetectLanguage(string rootPath, string packageJsonContent)
    {
        // TypeScript evidence: tsconfig.json / typescript dependency / .ts|.tsx files present.
        if (File.Exists(Path.Combine(rootPath, "tsconfig.json")) ||
            packageJsonContent.Contains("\"typescript\"", StringComparison.OrdinalIgnoreCase) ||
            Directory.Exists(rootPath) && (Directory.EnumerateFiles(rootPath, "*.ts", SearchOption.AllDirectories).Any() ||
                                           Directory.EnumerateFiles(rootPath, "*.tsx", SearchOption.AllDirectories).Any()))
            return "typescript";
        return "javascript";
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
/// Framework is determined from .csproj content, not assumed.
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
        string? framework = null;

        if (triggerFileContents.ContainsKey("global.json")) evidence.Add("global.json");
        if (triggerFileContents.Keys.Any(k => k.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))) evidence.Add("*.sln");

        // Parse .csproj to detect framework (not assume ASP.NET Core)
        var csprojKey = triggerFileContents.Keys.FirstOrDefault(k => k.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
        if (csprojKey is not null)
        {
            evidence.Add(csprojKey);
            var csprojContent = triggerFileContents[csprojKey];

            // Detect framework from SDK/PackageReference
            if (csprojContent.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase))
                framework = "ASP.NET Core";
            else if (csprojContent.Contains("Microsoft.NET.Sdk.Worker", StringComparison.OrdinalIgnoreCase))
                framework = ".NET Worker Service";
            else if (csprojContent.Contains("Microsoft.NET.Sdk.BlazorWebAssembly", StringComparison.OrdinalIgnoreCase))
                framework = "Blazor WebAssembly";
            else if (csprojContent.Contains("Microsoft.NET.Sdk.Maui", StringComparison.OrdinalIgnoreCase))
                framework = ".NET MAUI";
            else if (csprojContent.Contains("Microsoft.NET.Sdk", StringComparison.OrdinalIgnoreCase))
                framework = ".NET (class library or console)";
            // If no SDK match, leave framework null — don't guess
        }

        if (evidence.Count == 0) return null;

        return new DetectedComponent
        {
            Id = "dotnet-" + Path.GetFileName(rootPath),
            Path = rootPath,
            Language = "csharp",
            Framework = framework, // null if unknown — don't guess
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

// === V5-W13: Additional detectors for broader project coverage ===

/// <summary>
/// Detects C/C++ projects using CMake.
/// </summary>
public sealed class CMakeDetector : IProjectDetector
{
    public string Id => "cmake";
    public IReadOnlySet<string> TriggerFiles => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "CMakeLists.txt", "CMakePresets.json"
    };

    public DetectedComponent? Detect(string rootPath, IReadOnlyDictionary<string, string> triggerFileContents)
    {
        var evidence = new List<string>();
        if (triggerFileContents.ContainsKey("CMakeLists.txt")) evidence.Add("CMakeLists.txt");
        if (triggerFileContents.ContainsKey("CMakePresets.json")) evidence.Add("CMakePresets.json");
        if (evidence.Count == 0) return null;

        var language = "cpp";
        if (!Directory.EnumerateFiles(rootPath, "*.cpp", SearchOption.TopDirectoryOnly).Any() &&
            !Directory.EnumerateFiles(rootPath, "*.cc", SearchOption.TopDirectoryOnly).Any() &&
            Directory.EnumerateFiles(rootPath, "*.c", SearchOption.TopDirectoryOnly).Any())
            language = "c";

        return new DetectedComponent
        {
            Id = "cmake-" + Path.GetFileName(rootPath),
            Path = rootPath,
            Language = language,
            BuildSystem = "CMake",
            PackageManager = "none",
            Evidence = evidence,
        };
    }
}

/// <summary>
/// Detects Android projects (Gradle with Android plugin or .gradle with android namespace).
/// </summary>
public sealed class AndroidDetector : IProjectDetector
{
    public string Id => "android";
    public IReadOnlySet<string> TriggerFiles => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "build.gradle", "build.gradle.kts"
    };

    public DetectedComponent? Detect(string rootPath, IReadOnlyDictionary<string, string> triggerFileContents)
    {
        // Only trigger if this looks like an Android project (has android {} block or AndroidManifest.xml)
        var hasAndroidManifest = File.Exists(Path.Combine(rootPath, "src", "main", "AndroidManifest.xml")) ||
                                  Directory.GetFiles(rootPath, "AndroidManifest.xml", SearchOption.AllDirectories).Length > 0;

        string? gradleContent = null;
        if (triggerFileContents.TryGetValue("build.gradle", out var c1)) gradleContent = c1;
        else if (triggerFileContents.TryGetValue("build.gradle.kts", out var c2)) gradleContent = c2;

        if (gradleContent is null) return null;

        var isAndroid = hasAndroidManifest ||
                        gradleContent.Contains("com.android.application", StringComparison.OrdinalIgnoreCase) ||
                        gradleContent.Contains("com.android.library", StringComparison.OrdinalIgnoreCase);
        if (!isAndroid) return null;

        var language = gradleContent.Contains("kotlin", StringComparison.OrdinalIgnoreCase) ||
                       Directory.Exists(Path.Combine(rootPath, "src", "main", "kotlin"))
            ? "kotlin"
            : "java";

        return new DetectedComponent
        {
            Id = "android-" + Path.GetFileName(rootPath),
            Path = rootPath,
            Language = language,
            Framework = "Android",
            BuildSystem = "Gradle (Android)",
            PackageManager = "Gradle",
            Evidence = ["Android Gradle project detected"],
        };
    }
}

/// <summary>
/// Detects Swift / Xcode projects.
/// </summary>
public sealed class SwiftDetector : IProjectDetector
{
    public string Id => "swift";
    public IReadOnlySet<string> TriggerFiles => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Package.swift", "Package.resolved"
    };

    public DetectedComponent? Detect(string rootPath, IReadOnlyDictionary<string, string> triggerFileContents)
    {
        if (!triggerFileContents.ContainsKey("Package.swift") && !triggerFileContents.ContainsKey("Package.resolved"))
            return null;

        var evidence = new List<string>();
        if (triggerFileContents.ContainsKey("Package.swift")) evidence.Add("Package.swift");
        if (triggerFileContents.ContainsKey("Package.resolved")) evidence.Add("Package.resolved");

        return new DetectedComponent
        {
            Id = "swift-" + Path.GetFileName(rootPath),
            Path = rootPath,
            Language = "swift",
            BuildSystem = "Swift Package Manager",
            PackageManager = "SPM",
            Evidence = evidence,
        };
    }
}

/// <summary>
/// Detects PHP projects (composer.json).
/// </summary>
public sealed class PhpDetector : IProjectDetector
{
    public string Id => "php";
    public IReadOnlySet<string> TriggerFiles => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "composer.json", "artisan"
    };

    public DetectedComponent? Detect(string rootPath, IReadOnlyDictionary<string, string> triggerFileContents)
    {
        var evidence = new List<string>();
        string? framework = null;

        if (triggerFileContents.ContainsKey("composer.json"))
        {
            evidence.Add("composer.json");
            var content = triggerFileContents["composer.json"];
            if (content.Contains("laravel", StringComparison.OrdinalIgnoreCase)) framework = "Laravel";
            else if (content.Contains("symfony", StringComparison.OrdinalIgnoreCase)) framework = "Symfony";
        }
        if (triggerFileContents.ContainsKey("artisan")) { evidence.Add("artisan"); framework ??= "Laravel"; }

        if (evidence.Count == 0) return null;

        return new DetectedComponent
        {
            Id = "php-" + Path.GetFileName(rootPath),
            Path = rootPath,
            Language = "php",
            Framework = framework,
            BuildSystem = "Composer",
            PackageManager = "Composer",
            Evidence = evidence,
        };
    }
}

/// <summary>
/// Detects Ruby projects (Gemfile, Rakefile, *.gemspec).
/// </summary>
public sealed class RubyDetector : IProjectDetector
{
    public string Id => "ruby";
    public IReadOnlySet<string> TriggerFiles => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Gemfile", "Rakefile", "*.gemspec"
    };

    public DetectedComponent? Detect(string rootPath, IReadOnlyDictionary<string, string> triggerFileContents)
    {
        var evidence = new List<string>();
        if (triggerFileContents.ContainsKey("Gemfile")) evidence.Add("Gemfile");
        if (triggerFileContents.ContainsKey("Rakefile")) evidence.Add("Rakefile");
        if (triggerFileContents.Keys.Any(k => k.EndsWith(".gemspec", StringComparison.OrdinalIgnoreCase)))
            evidence.Add("*.gemspec");

        if (evidence.Count == 0) return null;

        return new DetectedComponent
        {
            Id = "ruby-" + Path.GetFileName(rootPath),
            Path = rootPath,
            Language = "ruby",
            BuildSystem = "Bundler",
            PackageManager = "Bundler",
            Evidence = evidence,
        };
    }
}

/// <summary>
/// Detects Terraform infrastructure projects.
/// </summary>
public sealed class TerraformDetector : IProjectDetector
{
    public string Id => "terraform";
    public IReadOnlySet<string> TriggerFiles => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "*.tf", "*.tfvars", "terraform.tfstate"
    };

    public DetectedComponent? Detect(string rootPath, IReadOnlyDictionary<string, string> triggerFileContents)
    {
        var evidence = new List<string>();
        if (triggerFileContents.Keys.Any(k => k.EndsWith(".tf", StringComparison.OrdinalIgnoreCase)))
            evidence.Add("*.tf files found");
        if (triggerFileContents.ContainsKey("terraform.tfstate")) evidence.Add("terraform.tfstate");

        if (evidence.Count == 0) return null;

        return new DetectedComponent
        {
            Id = "terraform-" + Path.GetFileName(rootPath),
            Path = rootPath,
            Language = "hcl",
            BuildSystem = "Terraform",
            PackageManager = "none",
            Confidence = 0.7,
            Evidence = evidence,
        };
    }
}

/// <summary>
/// Detects Unreal Engine projects.
/// </summary>
public sealed class UnrealDetector : IProjectDetector
{
    public string Id => "unreal";
    public IReadOnlySet<string> TriggerFiles => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "*.uproject", "*.uplugin"
    };

    public DetectedComponent? Detect(string rootPath, IReadOnlyDictionary<string, string> triggerFileContents)
    {
        var uprojectKey = triggerFileContents.Keys.FirstOrDefault(k => k.EndsWith(".uproject", StringComparison.OrdinalIgnoreCase));
        var upluginKey = triggerFileContents.Keys.FirstOrDefault(k => k.EndsWith(".uplugin", StringComparison.OrdinalIgnoreCase));

        if (uprojectKey is null && upluginKey is null) return null;

        var evidence = new List<string>();
        if (uprojectKey is not null) evidence.Add($"{uprojectKey} found");
        if (upluginKey is not null) evidence.Add($"{upluginKey} found");

        return new DetectedComponent
        {
            Id = "unreal-" + Path.GetFileName(rootPath),
            Path = rootPath,
            Language = "cpp",
            Framework = "Unreal Engine",
            BuildSystem = "Unreal Build Tool",
            PackageManager = "none",
            Evidence = evidence,
        };
    }
}

/// <summary>
/// Generic fallback detector: identifies a directory with source code but no recognized build system.
/// </summary>
public sealed class GenericDetector : IProjectDetector
{
    public string Id => "generic";
    public IReadOnlySet<string> TriggerFiles => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "*.cs", "*.ts", "*.js", "*.py", "*.go", "*.rs", "*.java", "*.cpp", "*.c", "*.rb", "*.php", "*.swift", "*.kt"
    };

    public DetectedComponent? Detect(string rootPath, IReadOnlyDictionary<string, string> triggerFileContents)
    {
        // Only triggers if no other detector matched — should be last in the list.
        // Check if any of the actual files match our source code patterns.
        var sourceExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".go", ".rs", ".java", ".cpp", ".cc", ".c", ".rb", ".php", ".swift", ".kt" };

        var matchingFiles = triggerFileContents.Keys
            .Where(k => sourceExtensions.Contains(Path.GetExtension(k)))
            .ToList();

        if (matchingFiles.Count == 0) return null;

        var evidence = new List<string> { $"Source files found ({matchingFiles.Count}), no recognized build system" };
        var language = "unknown";

        // Determine language from file extensions
        if (matchingFiles.Any(k => k.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))) language = "csharp";
        else if (matchingFiles.Any(k => k.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) || k.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))) language = "typescript";
        else if (matchingFiles.Any(k => k.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || k.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase))) language = "javascript";
        else if (matchingFiles.Any(k => k.EndsWith(".py", StringComparison.OrdinalIgnoreCase))) language = "python";
        else if (matchingFiles.Any(k => k.EndsWith(".go", StringComparison.OrdinalIgnoreCase))) language = "go";
        else if (matchingFiles.Any(k => k.EndsWith(".rs", StringComparison.OrdinalIgnoreCase))) language = "rust";
        else if (matchingFiles.Any(k => k.EndsWith(".java", StringComparison.OrdinalIgnoreCase))) language = "java";
        else if (matchingFiles.Any(k => k.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase) || k.EndsWith(".cc", StringComparison.OrdinalIgnoreCase) || k.EndsWith(".c", StringComparison.OrdinalIgnoreCase))) language = "cpp";

        return new DetectedComponent
        {
            Id = "generic-" + Path.GetFileName(rootPath),
            Path = rootPath,
            Language = language,
            BuildSystem = "none",
            PackageManager = "none",
            Confidence = 0.3,
            Evidence = evidence,
        };
    }
}
