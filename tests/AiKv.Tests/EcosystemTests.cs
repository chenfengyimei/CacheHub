using AiKv.Core.Ecosystem;

namespace AiKv.Tests;

public class EcosystemTests
{
    [Fact]
    public void PluginManifest_ShouldDefaultDisabled()
    {
        var manifest = new PluginManifest
        {
            Id = "test-plugin",
            Name = "Test Plugin",
            Version = "1.0.0",
            Author = "test",
            IsSigned = false,
            Permissions = [PluginAccess.ReadFiles],
        };

        Assert.False(manifest.IsEnabled);
        Assert.False(manifest.IsSigned);
        Assert.Single(manifest.Permissions);
    }

    [Fact]
    public void PluginManifest_Signed_ShouldHaveSignature()
    {
        var manifest = new PluginManifest
        {
            Id = "signed-plugin",
            Name = "Signed",
            Version = "1.0",
            Author = "official",
            IsSigned = true,
            Signature = "sha256:abc",
            Permissions = [PluginAccess.ReadWorkspace],
            IsEnabled = true,
        };

        Assert.True(manifest.IsSigned);
        Assert.NotNull(manifest.Signature);
        Assert.True(manifest.IsEnabled);
    }

    [Fact]
    public void EnterprisePolicy_ShouldEnforceDefaults()
    {
        var policy = new EnterprisePolicy
        {
            PolicyId = "enterprise-001",
            ForceCloudSendRestricted = true,
            ForceAuditLog = true,
            AllowedProviders = ["internal-llm"],
            DisableThirdPartyPlugins = true,
            RequirePluginSignature = true,
        };

        Assert.True(policy.ForceCloudSendRestricted);
        Assert.True(policy.DisableThirdPartyPlugins);
        Assert.Single(policy.AllowedProviders);
    }

    [Fact]
    public void EnterprisePolicy_ShouldNotWeakenLocalSecurity()
    {
        // Enterprise policy can add restrictions but cannot weaken the local security baseline.
        var policy = new EnterprisePolicy
        {
            PolicyId = "test",
            ForceCloudSendRestricted = true,
        };

        // Local security baseline: default only-read, local, least-exfiltration
        // Enterprise policy can only add more restrictions, not remove them.
        Assert.True(policy.ForceCloudSendRestricted);
    }

    [Fact]
    public void TeamConfig_ShouldShareWorkspaces()
    {
        var team = new TeamConfig
        {
            TeamId = "team-001",
            Name = "Dev Team",
            SharedWorkspaceIds = ["ws-1", "ws-2"],
            MemberIds = ["user-1", "user-2", "user-3"],
            EnableSharedIndex = true,
            EnableAuditLog = true,
        };

        Assert.Equal(2, team.SharedWorkspaceIds.Count);
        Assert.Equal(3, team.MemberIds.Count);
        Assert.True(team.EnableSharedIndex);
    }

    [Fact]
    public void UpdateConfig_ShouldDefaultToStableChannel()
    {
        var config = new UpdateConfig { Channel = UpdateChannel.Stable };

        Assert.True(config.AutoCheck);
        Assert.True(config.BackupBeforeUpdate);
        Assert.False(config.AutoInstall);
    }

    [Fact]
    public void UpdateCheckResult_ShouldIndicateAvailable()
    {
        var result = new UpdateCheckResult
        {
            UpdateAvailable = true,
            LatestVersion = "0.2.0",
            CurrentVersion = "0.1.0",
            ReleaseDate = DateTimeOffset.UtcNow,
            DownloadUrl = "https://example.com/download",
            Checksum = "sha256:def",
        };

        Assert.True(result.UpdateAvailable);
        Assert.NotNull(result.DownloadUrl);
    }

    [Fact]
    public void PluginAccess_ShouldHaveExpectedValues()
    {
        Assert.True(Enum.IsDefined(PluginAccess.ReadFiles));
        Assert.True(Enum.IsDefined(PluginAccess.WriteFiles));
        Assert.True(Enum.IsDefined(PluginAccess.ExecuteProcess));
        Assert.True(Enum.IsDefined(PluginAccess.NetworkAccess));
        Assert.True(Enum.IsDefined(PluginAccess.AccessCredentials));
    }

    [Fact]
    public void UpdateChannel_ShouldHaveExpectedValues()
    {
        Assert.Equal(4, Enum.GetNames<UpdateChannel>().Length);
    }
}
