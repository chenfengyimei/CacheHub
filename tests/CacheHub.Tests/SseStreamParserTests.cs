using CacheHub.Gateway.Streaming;

namespace CacheHub.Tests;

public class SseStreamParserTests
{
    [Fact]
    public void ParseChunk_ShouldExtractDeltaContent()
    {
        var data = """{"choices":[{"delta":{"content":"Hello"},"finish_reason":null}]}""";

        var chunk = SseStreamParser.ParseChunk(data);

        Assert.NotNull(chunk);
        Assert.Equal("Hello", chunk!.Delta);
        Assert.Null(chunk.FinishReason);
    }

    [Fact]
    public void ParseChunk_ShouldExtractFinishReason()
    {
        var data = """{"choices":[{"delta":{},"finish_reason":"stop"}]}""";

        var chunk = SseStreamParser.ParseChunk(data);

        Assert.NotNull(chunk);
        Assert.Equal("", chunk!.Delta);
        Assert.Equal("stop", chunk.FinishReason);
    }

    [Fact]
    public void ParseChunk_ShouldExtractUsage()
    {
        var data = """{"choices":[{"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":10,"completion_tokens":5,"total_tokens":15}}""";

        var chunk = SseStreamParser.ParseChunk(data);

        Assert.NotNull(chunk);
        Assert.NotNull(chunk!.Usage);
        Assert.Equal(10, chunk.Usage!.PromptTokens);
        Assert.Equal(15, chunk.Usage.TotalTokens);
    }

    [Fact]
    public void ParseChunk_ShouldReturnNullForDone()
    {
        var chunk = SseStreamParser.ParseChunk("[DONE]");

        Assert.Null(chunk);
    }

    [Fact]
    public void ParseChunk_ShouldReturnNullForEmpty()
    {
        Assert.Null(SseStreamParser.ParseChunk(""));
        Assert.Null(SseStreamParser.ParseChunk("   "));
    }

    [Fact]
    public void ParseChunk_ShouldReturnNullForInvalidJson()
    {
        Assert.Null(SseStreamParser.ParseChunk("not json"));
    }

    [Fact]
    public void ParseStream_ShouldConcatenateDeltas()
    {
        var sseBody = """
            data: {"choices":[{"delta":{"content":"Hello"},"finish_reason":null}]}

            data: {"choices":[{"delta":{"content":" "},"finish_reason":null}]}

            data: {"choices":[{"delta":{"content":"World"},"finish_reason":null}]}

            data: {"choices":[{"delta":{},"finish_reason":"stop"}]}

            data: [DONE]

            """;

        var result = SseStreamParser.ParseStream(sseBody);

        Assert.Equal("Hello World", result.Content);
        Assert.Equal("stop", result.FinishReason);
    }

    [Fact]
    public void ParseStream_ShouldExtractUsageFromFinalChunk()
    {
        var sseBody = """
            data: {"choices":[{"delta":{"content":"Hi"},"finish_reason":null}]}

            data: {"choices":[{"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":5,"completion_tokens":1,"total_tokens":6}}

            data: [DONE]

            """;

        var result = SseStreamParser.ParseStream(sseBody);

        Assert.NotNull(result.Usage);
        Assert.Equal(6, result.Usage!.TotalTokens);
    }

    [Fact]
    public void ParseStream_ShouldHandleEmptyStream()
    {
        var result = SseStreamParser.ParseStream("");

        Assert.Equal("", result.Content);
        Assert.Null(result.FinishReason);
        Assert.Null(result.Usage);
    }

    [Fact]
    public void SseFrame_ToSseString_ShouldFormatCorrectly()
    {
        var frame = new SseFrame { Event = "message", Data = """{"content":"hi"}""", Id = "1" };

        var sse = frame.ToSseString();

        Assert.Contains("event: message", sse);
        Assert.Contains("id: 1", sse);
        Assert.Contains("data: ", sse);
        Assert.EndsWith("\n\n", sse);
    }

    [Fact]
    public void SseFrame_ToSseString_ShouldWorkWithoutEventOrId()
    {
        var frame = new SseFrame { Event = "", Data = """{"content":"hi"}""" };

        var sse = frame.ToSseString();

        Assert.DoesNotContain("event:", sse);
        Assert.DoesNotContain("id:", sse);
        Assert.Contains("data: ", sse);
    }
}
