using CacheHub.Core.Configuration;

namespace CacheHub.Tests;

public class ConfigManagerTests
{
    [Fact]
    public void ConfigManager_Load_ShouldReturnDefault_WhenNotExists()
    {
        using var temp = new TempDir();
        var manager = new ConfigManager(temp.Path);

        var config = manager.Load();

        Assert.Equal("1", config.Version);
        Assert.Null(config.DefaultModel);
    }

    [Fact]
    public void ConfigManager_SaveAndLoad_ShouldRoundTrip()
    {
        using var temp = new TempDir();
        var manager = new ConfigManager(temp.Path);

        var config = new CacheHubConfig
        {
            DefaultModel = "gpt-4",
            DefaultBudget = new BudgetConfig { ModelContextWindow = 64000, SafetyMargin = 5000 },
            Security = new SecurityConfig { Mode = CacheHub.Core.Security.ExfiltrationMode.Restricted },
            Gateway = new GatewayConfigFile { Enabled = true, Port = 8080, ProviderUrl = "https://api.test.com" },
        };

        manager.Save(config);
        Assert.True(manager.Exists);

        var loaded = manager.Load();

        Assert.Equal("gpt-4", loaded.DefaultModel);
        Assert.Equal(64000, loaded.DefaultBudget!.ModelContextWindow);
        Assert.Equal(CacheHub.Core.Security.ExfiltrationMode.Restricted, loaded.Security!.Mode);
        Assert.True(loaded.Gateway!.Enabled);
        Assert.Equal(8080, loaded.Gateway.Port);
    }

    [Fact]
    public void ConfigManager_Exists_ShouldBeFalse_WhenNotCreated()
    {
        using var temp = new TempDir();
        var manager = new ConfigManager(temp.Path);

        Assert.False(manager.Exists);
    }

    [Fact]
    public void CacheHubConfig_Default_ShouldHaveVersion1()
    {
        var config = new CacheHubConfig();
        Assert.Equal("1", config.Version);
    }

    [Fact]
    public void BudgetConfig_Default_ShouldHaveStandardValues()
    {
        var budget = new BudgetConfig();
        Assert.Equal(128000, budget.ModelContextWindow);
        Assert.Equal(10000, budget.SafetyMargin);
    }

    [Fact]
    public void SecurityConfig_Default_ShouldBeStandard()
    {
        var sec = new SecurityConfig();
        Assert.Equal(CacheHub.Core.Security.ExfiltrationMode.Standard, sec.Mode);
        Assert.True(sec.EnableSecretScan);
    }

    [Fact]
    public void GatewayConfigFile_Default_ShouldNotBeEnabled()
    {
        var gw = new GatewayConfigFile();
        Assert.False(gw.Enabled);
        Assert.Equal(5218, gw.Port);
    }

    [Fact]
    public void IndexingConfig_Default_ShouldHaveReasonableLimits()
    {
        var idx = new IndexingConfig();
        Assert.Equal(50, idx.MaxDepth);
        Assert.Equal(500000, idx.MaxFileCount);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cachehub_cfg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
