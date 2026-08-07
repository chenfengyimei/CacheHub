using CacheHub.Core.LanguageServers;
using CacheHub.Core.LanguageServers.JsonRpc;
using CacheHub.Core.Parsing;

namespace CacheHub.Tests;

/// <summary>
/// R14 Gate: Tree-sitter, Roslyn, LSP — regex fallback always available, language service failure doesn't block.
/// </summary>
public class R14GateRegressionTests
{
    // R14 Gate: Regex parser is always available as fallback
    [Fact]
    public void Gate_RegexParser_AlwaysAvailable()
    {
        var csParser = new CacheHub.Indexing.Parsing.CSharpRegexParser();
        var tsParser = new CacheHub.Indexing.Parsing.TypeScriptRegexParser();
        var pyParser = new CacheHub.Indexing.Parsing.PythonRegexParser();

        Assert.NotEmpty(csParser.SupportedExtensions);
        Assert.NotEmpty(tsParser.SupportedExtensions);
        Assert.NotEmpty(pyParser.SupportedExtensions);

        // Parse should not throw
        var result = csParser.Parse("public class Test { }", "test.cs");
        Assert.NotEmpty(result.Symbols);
    }

    // R14 Gate: Parser version is explicitly marked as regex-baseline
    [Fact]
    public void Gate_ParserVersion_MarksRegexBaseline()
    {
        var csParser = new CacheHub.Indexing.Parsing.CSharpRegexParser();
        Assert.Contains("regex", csParser.Id, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("baseline", csParser.Id, StringComparison.OrdinalIgnoreCase);
    }

    // R14 Gate: LSP module can be disabled without affecting core
    [Fact]
    public void Gate_LSPDisabled_CoreStillWorks()
    {
        var lifecycle = new LspLifecycle(new LspServerConfig
        {
            ServerId = "test",
            Command = "test",
            WorkingDirectory = "/tmp",
        });

        lifecycle.Disable();
        Assert.Equal(LspState.Disabled, lifecycle.State);
        Assert.False(lifecycle.IsReady);

        // Core engine doesn't depend on LSP
        var engine = new CacheHub.Context.Engine.ContextEngine();
        Assert.NotNull(engine);
    }

    // R14 Gate: LSP approval model requires explicit approval
    [Fact]
    public void Gate_LSPApproval_RequiredBeforeStart()
    {
        var model = new LspApprovalModel();
        Assert.False(model.IsApproved("csharp-ls"));
        Assert.False(model.RequestApproval("csharp-ls"));

        model.GrantApproval("csharp-ls");
        Assert.True(model.IsApproved("csharp-ls"));
    }

    // R14 Gate: JSON-RPC framed reader works
    [Fact]
    public async Task Gate_JsonRpcFramedReader_ParsesMessages()
    {
        var body = """{"jsonrpc":"2.0","method":"test","params":{}}""";
        var bodyBytes = System.Text.Encoding.UTF8.GetBytes(body);
        var header = System.Text.Encoding.ASCII.GetBytes($"Content-Length: {bodyBytes.Length}\r\n\r\n");
        var fullMessage = header.Concat(bodyBytes).ToArray();

        using var stream = new MemoryStream(fullMessage);
        using var reader = new LspFramedReader(stream);
        var result = await reader.ReadMessageAsync();

        Assert.NotNull(result);
        Assert.Equal("test", result.Value.GetProperty("method").GetString());
    }

    // R14 Gate: Relation confidence distinguishes syntactic from heuristic
    [Fact]
    public void Gate_RelationConfidence_DistinguishesTypes()
    {
        var syntactic = new CodeRelation
        {
            RelationType = RelationType.Syntactic,
            Relation = "imports",
            TargetName = "System",
            Confidence = 1.0,
            Source = "csharp-regex-baseline",
        };

        var heuristic = new CodeRelation
        {
            RelationType = RelationType.Heuristic,
            Relation = "possible_call",
            TargetName = "DoSomething",
            Confidence = 0.5,
            Source = "csharp-regex-baseline",
        };

        Assert.True(syntactic.Confidence > heuristic.Confidence);
        Assert.NotEqual(syntactic.RelationType, heuristic.RelationType);
    }
}
