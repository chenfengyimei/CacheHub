using System.Text.Json;
using AiKv.Core.Feedback;

namespace AiKv.Tests;

public class FeedbackProtocolTests
{
    [Fact]
    public void ContextFeedback_CanBeCreated_WithRequiredFields()
    {
        var fb = new ContextFeedback
        {
            ContextPackageId = "ctx_001",
            ClientId = "generic-agent",
            TaskCompleted = true,
        };

        Assert.Equal("ctx_001", fb.ContextPackageId);
        Assert.True(fb.TaskCompleted);
        Assert.Empty(fb.FilesActuallyRead);
    }

    [Fact]
    public void ContextFeedback_CanSerializeToJson()
    {
        var fb = new ContextFeedback
        {
            ContextPackageId = "ctx_001",
            ClientId = "test-agent",
            FilesActuallyRead = ["src/auth.ts", "src/token.ts"],
            AdditionalFilesRequested = ["src/utils.ts"],
            TaskCompleted = true,
            MissingContextReported = false,
            TotalWorkflowInputTokens = 15000,
            TotalWorkflowOutputTokens = 3000,
        };

        var json = JsonSerializer.Serialize(fb);
        var deserialized = ContextFeedback.ParseJson(json);

        Assert.NotNull(deserialized);
        Assert.Equal("ctx_001", deserialized!.ContextPackageId);
        Assert.Equal(2, deserialized.FilesActuallyRead.Count);
        Assert.Single(deserialized.AdditionalFilesRequested);
        Assert.True(deserialized.TaskCompleted);
        Assert.Equal(15000, deserialized.TotalWorkflowInputTokens);
    }

    [Fact]
    public void ContextFeedback_ParseJson_ShouldHandleInvalidInput()
    {
        // Empty JSON {} should deserialize but ContextPackageId will be null (required not enforced by deserializer)
        Assert.Throws<JsonException>(() => ContextFeedback.ParseJson("invalid json"));
    }

    [Fact]
    public void ContextFeedback_DefaultCollections_ShouldBeEmpty()
    {
        var fb = new ContextFeedback { ContextPackageId = "ctx_test" };

        Assert.Empty(fb.FilesActuallyRead);
        Assert.Empty(fb.AdditionalFilesRequested);
        Assert.Empty(fb.SelectedFilesUsed);
        Assert.Empty(fb.SelectedFilesIgnored);
        Assert.Empty(fb.PatchFiles);
        Assert.Empty(fb.TestsRun);
    }
}
