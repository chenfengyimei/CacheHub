using CacheHub.Core.Detection;
using CacheHub.Indexing.Detection;

namespace CacheHub.Tests;

public class ProjectDetectionTests
{
    [Fact]
    public void NodeDetector_ShouldDetectNextJs()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "package.json"),
            """{"dependencies":{"next":"14.0.0","react":"18.0.0"}}""");

        var engine = new ProjectDetectionEngine();
        var result = engine.Detect(temp.Path);

        Assert.Single(result.Components);
        Assert.Equal("javascript", result.Components[0].Language);
        Assert.Equal("Next.js", result.Components[0].Framework);
        Assert.Equal("npm", result.Components[0].PackageManager);
    }

    [Fact]
    public void NodeDetector_ShouldDetectTypeScript_WhenTsEvidencePresent()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "package.json"),
            """{"dependencies":{"next":"14.0.0","typescript":"5.4.0"}}""");

        var engine = new ProjectDetectionEngine();
        var result = engine.Detect(temp.Path);

        Assert.Single(result.Components);
        Assert.Equal("typescript", result.Components[0].Language);
    }

    [Fact]
    public void PythonDetector_ShouldDetectPyproject()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "pyproject.toml"), "[project]\nname = 'test'\n");

        var engine = new ProjectDetectionEngine();
        var result = engine.Detect(temp.Path);

        Assert.Single(result.Components);
        Assert.Equal("python", result.Components[0].Language);
    }

    [Fact]
    public void DotNetDetector_ShouldDetectCsproj()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        var engine = new ProjectDetectionEngine();
        var result = engine.Detect(temp.Path);

        Assert.Single(result.Components);
        Assert.Equal("csharp", result.Components[0].Language);
        Assert.Equal("MSBuild", result.Components[0].BuildSystem);
    }

    [Fact]
    public void GoDetector_ShouldDetectGoMod()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "go.mod"), "module test\n\ngo 1.21\n");

        var engine = new ProjectDetectionEngine();
        var result = engine.Detect(temp.Path);

        Assert.Single(result.Components);
        Assert.Equal("go", result.Components[0].Language);
    }

    [Fact]
    public void RustDetector_ShouldDetectCargoToml()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "Cargo.toml"), "[package]\nname = 'test'\n");

        var engine = new ProjectDetectionEngine();
        var result = engine.Detect(temp.Path);

        Assert.Single(result.Components);
        Assert.Equal("rust", result.Components[0].Language);
    }

    [Fact]
    public void JavaDetector_ShouldDetectMaven()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "pom.xml"), "<project></project>");

        var engine = new ProjectDetectionEngine();
        var result = engine.Detect(temp.Path);

        Assert.Single(result.Components);
        Assert.Equal("java", result.Components[0].Language);
        Assert.Equal("Maven", result.Components[0].BuildSystem);
    }

    [Fact]
    public void FlutterDetector_ShouldDetectPubspec()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "pubspec.yaml"), "name: test\n");

        var engine = new ProjectDetectionEngine();
        var result = engine.Detect(temp.Path);

        Assert.Single(result.Components);
        Assert.Equal("dart", result.Components[0].Language);
        Assert.Equal("Flutter", result.Components[0].Framework);
    }

    [Fact]
    public void DockerDetector_ShouldDetectDockerfile()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "Dockerfile"), "FROM node:18\n");

        var engine = new ProjectDetectionEngine();
        var result = engine.Detect(temp.Path);

        Assert.Contains(result.Components, c => c.Language == "dockerfile");
    }

    [Fact]
    public void Detect_ShouldHandleUnknownProject()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "readme.txt"), "hello");

        var engine = new ProjectDetectionEngine();
        var result = engine.Detect(temp.Path);

        Assert.Empty(result.Components);
    }

    [Fact]
    public void Detect_ShouldIdentifyMonorepo()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "frontend"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "backend"));
        File.WriteAllText(Path.Combine(temp.Path, "frontend", "package.json"), """{"dependencies":{}}""");
        File.WriteAllText(Path.Combine(temp.Path, "backend", "go.mod"), "module test\n");

        var engine = new ProjectDetectionEngine();
        var result = engine.Detect(temp.Path);

        Assert.True(result.IsMonorepo);
        Assert.Equal(2, result.Components.Count);
    }

    [Fact]
    public void GeneratePlan_ShouldCreateActions()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "package.json"), """{"dependencies":{}}""");

        var engine = new ProjectDetectionEngine();
        var result = engine.Detect(temp.Path);
        var plan = engine.GeneratePlan(result);

        Assert.NotEmpty(plan.Actions);
        Assert.Equal("npm install", plan.Actions[0].Command);
        Assert.True(plan.Actions[0].RequiresNetwork);
        Assert.True(plan.Actions[0].MayRunScripts);
        Assert.Equal(ApprovalLevel.EveryTimeApproval, plan.Actions[0].Approval);
    }

    [Fact]
    public void GeneratePlan_ShouldMarkRisks()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "package.json"), """{"dependencies":{}}""");

        var engine = new ProjectDetectionEngine();
        var result = engine.Detect(temp.Path);
        var plan = engine.GeneratePlan(result);

        Assert.NotEmpty(plan.Actions[0].Risks);
        Assert.Contains(plan.Actions[0].Risks, r => r.Contains("postinstall"));
    }

    [Fact]
    public void GeneratePlan_DotNet_ShouldUseDotnetRestore()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "App.csproj"), "<Project></Project>");

        var engine = new ProjectDetectionEngine();
        var result = engine.Detect(temp.Path);
        var plan = engine.GeneratePlan(result);

        Assert.NotEmpty(plan.Actions);
        Assert.Equal("dotnet restore", plan.Actions[0].Command);
        Assert.False(plan.Actions[0].MayRunScripts);
        Assert.Equal(ApprovalLevel.OneTimeApproval, plan.Actions[0].Approval);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cachehub_detect_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
