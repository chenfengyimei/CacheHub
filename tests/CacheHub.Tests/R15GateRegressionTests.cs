using CacheHub.Core.Ecosystem;

namespace CacheHub.Tests;

/// <summary>
/// R15 Gate: Long-term ecosystem 鈥?core works without enterprise, no cross-workspace leakage.
/// </summary>
public class R15GateRegressionTests
{
    // R15 Gate: Core works without enterprise services
    [Fact]
    public void Gate_CoreWorksWithoutEnterprise()
    {
        // Context Engine, Gateway, Storage all work without any enterprise configuration
        var engine = new CacheHub.Context.Engine.ContextEngine();
        Assert.NotNull(engine);

        using var server = new CacheHub.Gateway.Server.GatewayServer(new CacheHub.Gateway.GatewayConfig
        {
            ProviderBaseUrl = "https://api.example.com",
            ProviderApiKey = "test-key",
            Port = 15320,
        });
        Assert.NotEmpty(server.AccessToken);
    }

    // R15 Gate: Team shared index prevents cross-workspace leakage
    [Fact]
    public void Gate_TeamIndex_NoCrossWorkspaceLeakage()
    {
        var mgr = new TeamIndexManager();
        mgr.PublishIndex("ws-a", "user-1", "hash-a");
        mgr.PublishIndex("ws-b", "user-2", "hash-b");

        var list = mgr.ListSharedIndices();
        Assert.Equal(2, list.Count);

        // Remove one doesn't affect the other
        mgr.RemoveSharedIndex("ws-a");
        Assert.Single(mgr.ListSharedIndices());
    }

    // R15 Gate: Enterprise policy can disable third-party plugins
    [Fact]
    public void Gate_EnterprisePolicy_DisablesThirdPartyPlugins()
    {
        var enforcer = new EnterprisePolicyEnforcer(new EnterprisePolicy
        {
            PolicyId = "p1",
            DisableThirdPartyPlugins = true,
        });

        var plugin = new PluginManifest
        {
            Id = "third-party",
            Name = "Third Party",
            Version = "1.0",
            Author = "external",
            IsSigned = true,
            IsEnabled = true,
            Permissions = [],
        };

        Assert.False(enforcer.IsPluginAllowed(plugin));
    }

    // R15 Gate: Enterprise audit log records events
    [Fact]
    public void Gate_EnterprisePolicy_AuditLog_Records()
    {
        var enforcer = new EnterprisePolicyEnforcer(new EnterprisePolicy
        {
            PolicyId = "p1",
            ForceAuditLog = true,
        });

        enforcer.LogAudit("context.build", "Built context for ws-1", "user-1");
        enforcer.LogAudit("gateway.call", "Forwarded request", "user-2");

        var log = enforcer.GetAuditLog();
        Assert.Equal(2, log.Count);
    }

    // R15 Gate: Plugin signature required by enterprise policy
    [Fact]
    public void Gate_EnterprisePolicy_RequiresSignature()
    {
        var enforcer = new EnterprisePolicyEnforcer(new EnterprisePolicy
        {
            PolicyId = "p1",
            RequirePluginSignature = true,
        });

        var unsignedPlugin = new PluginManifest
        {
            Id = "unsigned",
            Name = "Unsigned",
            Version = "1.0",
            Author = "test",
            IsSigned = false,
            IsEnabled = true,
            Permissions = [],
        };

        Assert.False(enforcer.IsPluginAllowed(unsignedPlugin));
    }
}
