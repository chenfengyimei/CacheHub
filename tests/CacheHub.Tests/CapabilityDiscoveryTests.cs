using System.Text.Json;
using CacheHub.Core.Capabilities;

namespace CacheHub.Tests;

public class CapabilityDiscoveryTests
{
    [Fact]
    public void CapabilityDiscovery_CanBeCreated_WithRequiredFields()
    {
        var cd = new CapabilityDiscovery
        {
            Version = "0.1.0-alpha",
            ProtocolVersion = "1.0",
            Capabilities = CapabilityFlags.With(Capability.WorkspaceImport, Capability.ContextBuild),
        };

        Assert.Equal("0.1.0-alpha", cd.Version);
        Assert.Equal("1.0", cd.ProtocolVersion);
        Assert.True(cd.Capabilities.WorkspaceImport);
        Assert.True(cd.Capabilities.ContextBuild);
        Assert.False(cd.Capabilities.Gateway);
        Assert.False(cd.Capabilities.Semantic);
    }

    [Fact]
    public void CapabilityFlags_Default_AllFalse()
    {
        var flags = new CapabilityFlags();

        Assert.False(flags.WorkspaceImport);
        Assert.False(flags.ContextBuild);
        Assert.False(flags.Gateway);
        Assert.False(flags.Lsp);
    }

    [Fact]
    public void CapabilityDiscovery_CanSerializeToJson()
    {
        var cd = new CapabilityDiscovery
        {
            Version = "0.1.0",
            ProtocolVersion = "1.0",
            Capabilities = CapabilityFlags.With(Capability.ContextBuild, Capability.ContextExpand),
            SchemaVersions = new Dictionary<string, int>
            {
                ["contextPackage"] = 1,
                ["capabilityDiscovery"] = 1,
            },
            Limitations = ["No GUI", "CLI only"],
        };

        var json = JsonSerializer.Serialize(cd);
        var deserialized = JsonSerializer.Deserialize<CapabilityDiscovery>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("0.1.0", deserialized.Version);
        Assert.True(deserialized.Capabilities.ContextBuild);
        Assert.True(deserialized.Capabilities.ContextExpand);
        Assert.False(deserialized.Capabilities.Gateway);
        Assert.NotNull(deserialized.SchemaVersions);
        Assert.Equal(1, deserialized.SchemaVersions["contextPackage"]);
        Assert.NotNull(deserialized.Limitations);
        Assert.Equal(2, deserialized.Limitations.Count);
    }

    [Fact]
    public void CapabilityFlags_IsEnabled_ChecksCorrectFlag()
    {
        var flags = CapabilityFlags.With(Capability.Cache, Capability.FileExport);

        Assert.True(flags.IsEnabled(Capability.Cache));
        Assert.True(flags.IsEnabled(Capability.FileExport));
        Assert.False(flags.IsEnabled(Capability.Gateway));
    }
}
