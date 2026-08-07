using CacheHub.Core.Ecosystem;

namespace CacheHub.Tests;

/// <summary>
/// Tests for R9: Cross-platform release and ecosystem.
/// Plugin security, auto-update, enterprise policy, team shared index.
/// </summary>
public class EcosystemR9Tests
{
    // === Plugin Security Manager ===

    [Fact]
    public void PluginSecurity_ValidatePlugin_SignedWithSignature_Passes()
    {
        var mgr = new PluginSecurityManager();
        var manifest = new PluginManifest
        {
            Id = "test-plugin",
            Name = "Test Plugin",
            Version = "1.0.0",
            Author = "test",
            IsSigned = true,
            Signature = "abc123",
            IsEnabled = true,
            Permissions = [PluginAccess.ReadFiles],
        };

        var (isValid, issues) = mgr.ValidatePlugin(manifest);
        Assert.True(isValid);
        Assert.Empty(issues);
    }

    [Fact]
    public void PluginSecurity_ValidatePlugin_SignedButNoSignature_Fails()
    {
        var mgr = new PluginSecurityManager();
        var manifest = new PluginManifest
        {
            Id = "test",
            Name = "Test",
            Version = "1.0",
            Author = "test",
            IsSigned = true,
            Signature = null,
            IsEnabled = true,
            Permissions = [],
        };

        var (isValid, issues) = mgr.ValidatePlugin(manifest);
        Assert.False(isValid);
        Assert.Contains(issues, i => i.Contains("no signature"));
    }

    [Fact]
    public void PluginSecurity_ValidatePlugin_DangerousCombo_Fails()
    {
        var mgr = new PluginSecurityManager();
        var manifest = new PluginManifest
        {
            Id = "dangerous",
            Name = "Dangerous",
            Version = "1.0",
            Author = "test",
            IsSigned = false,
            IsEnabled = true,
            Permissions = [PluginAccess.ExecuteProcess, PluginAccess.AccessCredentials],
        };

        var (isValid, issues) = mgr.ValidatePlugin(manifest);
        Assert.False(isValid);
        Assert.Contains(issues, i => i.Contains("ExecuteProcess") && i.Contains("AccessCredentials"));
    }

    [Fact]
    public void PluginSecurity_HasPermission_ReturnsCorrectValue()
    {
        var mgr = new PluginSecurityManager();
        mgr.RegisterPlugin(new PluginManifest
        {
            Id = "p1",
            Name = "P1",
            Version = "1.0",
            Author = "test",
            IsSigned = false,
            IsEnabled = true,
            Permissions = [PluginAccess.ReadFiles, PluginAccess.WriteFiles],
        });

        Assert.True(mgr.HasPermission("p1", PluginAccess.ReadFiles));
        Assert.False(mgr.HasPermission("p1", PluginAccess.NetworkAccess));
    }

    // === Update Manager ===

    [Fact]
    public void UpdateManager_CheckForUpdate_DifferentVersion_Available()
    {
        var mgr = new UpdateManager(new UpdateConfig { Channel = UpdateChannel.Stable }, "/tmp");
        var result = mgr.CheckForUpdate("1.0.0", "1.1.0");

        Assert.True(result.UpdateAvailable);
        Assert.Equal("1.0.0", result.CurrentVersion);
        Assert.Equal("1.1.0", result.LatestVersion);
    }

    [Fact]
    public void UpdateManager_CheckForUpdate_SameVersion_NotAvailable()
    {
        var mgr = new UpdateManager(new UpdateConfig { Channel = UpdateChannel.Stable }, "/tmp");
        var result = mgr.CheckForUpdate("1.0.0", "1.0.0");

        Assert.False(result.UpdateAvailable);
    }

    [Fact]
    public void UpdateManager_CreateBackup_ReturnsBackupWithId()
    {
        var mgr = new UpdateManager(new UpdateConfig { Channel = UpdateChannel.Stable }, "/tmp");
        var backup = mgr.CreateBackup("1.0.0");

        Assert.NotEmpty(backup.BackupId);
        Assert.Equal("1.0.0", backup.Version);
        Assert.Contains(mgr.GetBackups(), b => b.BackupId == backup.BackupId);
    }

    [Fact]
    public void UpdateManager_Rollback_ExistingBackup_ReturnsTrue()
    {
        var mgr = new UpdateManager(new UpdateConfig { Channel = UpdateChannel.Stable }, "/tmp");
        var backup = mgr.CreateBackup("1.0.0");

        Assert.True(mgr.Rollback(backup.BackupId));
    }

    [Fact]
    public void UpdateManager_Rollback_NonExistentBackup_ReturnsFalse()
    {
        var mgr = new UpdateManager(new UpdateConfig { Channel = UpdateChannel.Stable }, "/tmp");
        Assert.False(mgr.Rollback("non-existent"));
    }

    // === Enterprise Policy Enforcer ===

    [Fact]
    public void EnterprisePolicy_ForceRestricted_BlocksCloudSend()
    {
        var enforcer = new EnterprisePolicyEnforcer(new EnterprisePolicy
        {
            PolicyId = "p1",
            ForceCloudSendRestricted = true,
        });

        Assert.False(enforcer.IsCloudSendAllowed());
    }

    [Fact]
    public void EnterprisePolicy_AllowedProviders_FiltersCorrectly()
    {
        var enforcer = new EnterprisePolicyEnforcer(new EnterprisePolicy
        {
            PolicyId = "p1",
            AllowedProviders = ["openai", "anthropic"],
        });

        Assert.True(enforcer.IsProviderAllowed("openai"));
        Assert.False(enforcer.IsProviderAllowed("unknown"));
    }

    [Fact]
    public void EnterprisePolicy_DisablePlugins_BlocksAllPlugins()
    {
        var enforcer = new EnterprisePolicyEnforcer(new EnterprisePolicy
        {
            PolicyId = "p1",
            DisableThirdPartyPlugins = true,
        });

        var plugin = new PluginManifest
        {
            Id = "p",
            Name = "P",
            Version = "1.0",
            Author = "test",
            IsSigned = true,
            IsEnabled = true,
            Permissions = [],
        };

        Assert.False(enforcer.IsPluginAllowed(plugin));
    }

    [Fact]
    public void EnterprisePolicy_RequireSignature_BlocksUnsigned()
    {
        var enforcer = new EnterprisePolicyEnforcer(new EnterprisePolicy
        {
            PolicyId = "p1",
            RequirePluginSignature = true,
        });

        var unsignedPlugin = new PluginManifest
        {
            Id = "p",
            Name = "P",
            Version = "1.0",
            Author = "test",
            IsSigned = false,
            IsEnabled = true,
            Permissions = [],
        };

        Assert.False(enforcer.IsPluginAllowed(unsignedPlugin));
    }

    [Fact]
    public void EnterprisePolicy_BudgetExceeded_DetectsCorrectly()
    {
        var enforcer = new EnterprisePolicyEnforcer(new EnterprisePolicy
        {
            PolicyId = "p1",
            MaxDailyBudgetUsd = 10m,
        });

        Assert.False(enforcer.IsBudgetExceeded(5m));
        Assert.True(enforcer.IsBudgetExceeded(10m));
    }

    [Fact]
    public void EnterprisePolicy_AuditLog_RecordsEvents()
    {
        var enforcer = new EnterprisePolicyEnforcer(new EnterprisePolicy
        {
            PolicyId = "p1",
            ForceAuditLog = true,
        });

        enforcer.LogAudit("context.build", "Built context for ws-1", "user-1");
        enforcer.LogAudit("context.export", "Exported manifest", "user-2");

        var log = enforcer.GetAuditLog();
        Assert.Equal(2, log.Count);
        Assert.Contains(log, e => e.Action == "context.build");
        Assert.Contains(log, e => e.Action == "context.export");
    }

    [Fact]
    public void EnterprisePolicy_NoAuditLog_DoesNotRecord()
    {
        var enforcer = new EnterprisePolicyEnforcer(new EnterprisePolicy
        {
            PolicyId = "p1",
            ForceAuditLog = false,
        });

        enforcer.LogAudit("test");
        Assert.Empty(enforcer.GetAuditLog());
    }

    // === Team Index Manager ===

    [Fact]
    public void TeamIndex_PublishAndRetrieve_Works()
    {
        var mgr = new TeamIndexManager();

        mgr.PublishIndex("ws-1", "user-1", "hash-abc");
        var index = mgr.GetSharedIndex("ws-1");

        Assert.NotNull(index);
        Assert.Equal("ws-1", index.WorkspaceId);
        Assert.Equal("user-1", index.PublisherId);
        Assert.Equal("hash-abc", index.IndexHash);
    }

    [Fact]
    public void TeamIndex_DownloadCount_Increments()
    {
        var mgr = new TeamIndexManager();
        mgr.PublishIndex("ws-1", "user-1", "hash");

        // Two downloads
        mgr.GetSharedIndex("ws-1");
        mgr.GetSharedIndex("ws-1");

        // Third call inside the assertion also increments
        var index = mgr.GetSharedIndex("ws-1");
        Assert.NotNull(index);
        Assert.Equal(3, index.DownloadCount);
    }

    [Fact]
    public void TeamIndex_Remove_DeletesIndex()
    {
        var mgr = new TeamIndexManager();
        mgr.PublishIndex("ws-1", "user-1", "hash");

        Assert.True(mgr.RemoveSharedIndex("ws-1"));
        Assert.Null(mgr.GetSharedIndex("ws-1"));
    }

    [Fact]
    public void TeamIndex_List_ReturnsAllIndices()
    {
        var mgr = new TeamIndexManager();
        mgr.PublishIndex("ws-1", "u1", "h1");
        mgr.PublishIndex("ws-2", "u2", "h2");

        var list = mgr.ListSharedIndices();
        Assert.Equal(2, list.Count);
    }
}
