using AiKv.Core.LanguageServers;

namespace AiKv.Tests;

public class LspTests
{
    [Fact]
    public void LspCapabilities_ShouldDefaultAllFalse()
    {
        var caps = new LspCapabilities();

        Assert.False(caps.SupportsDefinition);
        Assert.False(caps.SupportsReferences);
        Assert.False(caps.SupportsDiagnostics);
    }

    [Fact]
    public void LspLifecycle_Initialize_ShouldTransitionToReady()
    {
        var config = new LspServerConfig
        {
            ServerId = "ts-ls",
            Command = "typescript-language-server",
            WorkingDirectory = "/tmp/project",
        };
        var lifecycle = new LspLifecycle(config);

        lifecycle.Initialize();

        Assert.Equal(LspState.Ready, lifecycle.State);
        Assert.True(lifecycle.IsReady);
    }

    [Fact]
    public void LspLifecycle_Disable_ShouldTransitionToDisabled()
    {
        var config = new LspServerConfig
        {
            ServerId = "test",
            Command = "test-server",
            WorkingDirectory = "/tmp",
        };
        var lifecycle = new LspLifecycle(config);
        lifecycle.Initialize();

        lifecycle.Disable();

        Assert.Equal(LspState.Disabled, lifecycle.State);
        Assert.False(lifecycle.IsReady);
    }

    [Fact]
    public void LspLifecycle_ReportCrash_ShouldAutoRestart_WhenEnabled()
    {
        var config = new LspServerConfig
        {
            ServerId = "test",
            Command = "test-server",
            WorkingDirectory = "/tmp",
            AutoRestart = true,
            MaxRestarts = 3,
        };
        var lifecycle = new LspLifecycle(config);
        lifecycle.Initialize();

        lifecycle.ReportCrash();

        Assert.Equal(LspState.Ready, lifecycle.State);
        Assert.Equal(1, lifecycle.RestartCount);
        Assert.NotNull(lifecycle.LastCrashAt);
    }

    [Fact]
    public void LspLifecycle_ReportCrash_ShouldNotRestart_WhenMaxExceeded()
    {
        var config = new LspServerConfig
        {
            ServerId = "test",
            Command = "test-server",
            WorkingDirectory = "/tmp",
            AutoRestart = true,
            MaxRestarts = 2,
        };
        var lifecycle = new LspLifecycle(config);
        lifecycle.Initialize();

        lifecycle.ReportCrash(); // restart 1
        lifecycle.ReportCrash(); // restart 2
        lifecycle.ReportCrash(); // exceeds max

        Assert.Equal(LspState.Crashed, lifecycle.State);
        Assert.Equal(3, lifecycle.RestartCount);
    }

    [Fact]
    public void LspLifecycle_ReportCrash_ShouldNotRestart_WhenAutoRestartDisabled()
    {
        var config = new LspServerConfig
        {
            ServerId = "test",
            Command = "test-server",
            WorkingDirectory = "/tmp",
            AutoRestart = false,
        };
        var lifecycle = new LspLifecycle(config);
        lifecycle.Initialize();

        lifecycle.ReportCrash();

        Assert.Equal(LspState.Crashed, lifecycle.State);
    }

    [Fact]
    public void LspLocation_ShouldStorePosition()
    {
        var loc = new LspLocation
        {
            FilePath = "src/app.ts",
            StartLine = 10,
            StartCharacter = 5,
            EndLine = 10,
            EndCharacter = 20,
        };

        Assert.Equal("src/app.ts", loc.FilePath);
        Assert.Equal(10, loc.StartLine);
    }

    [Fact]
    public void LspDiagnostic_ShouldStoreSeverity()
    {
        var diag = new LspDiagnostic
        {
            Line = 5,
            Character = 10,
            Message = "Type 'string' is not assignable to type 'number'",
            Severity = LspSeverity.Error,
            Source = "typescript",
        };

        Assert.Equal(LspSeverity.Error, diag.Severity);
        Assert.Contains("typescript", diag.Source);
    }

    [Fact]
    public void LspServerConfig_ShouldStoreEnvironment()
    {
        var config = new LspServerConfig
        {
            ServerId = "csharp-ls",
            Command = "dotnet",
            Args = ["OmniSharp"],
            WorkingDirectory = "/project",
            Environment = new Dictionary<string, string> { ["DOTNET_ROOT"] = "/usr/share/dotnet" },
        };

        Assert.Single(config.Args);
        Assert.NotNull(config.Environment);
    }
}
