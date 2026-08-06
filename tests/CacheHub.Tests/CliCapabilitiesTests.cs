using CacheHub.Cli.Commands;

namespace CacheHub.Tests;

public class CliCapabilitiesTests
{
    [Fact]
    public void Capabilities_TextMode_ShouldPrintVersionAndCapabilities()
    {
        // Capture stdout
        var sw = new StringWriter();
        var oldOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            var exit = CapabilitiesCommands.Handle([]);
            Assert.Equal(0, exit);
            var output = sw.ToString();
            Assert.Contains("0.1.0-alpha", output);
            Assert.Contains("WorkspaceImport", output);
            Assert.Contains("ContextBuild", output);
            Assert.Contains("Limitations", output);
        }
        finally { Console.SetOut(oldOut); }
    }

    [Fact]
    public void Capabilities_JsonMode_ShouldOutputValidJson()
    {
        var sw = new StringWriter();
        var oldOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            var exit = CapabilitiesCommands.Handle(["--output=json"]);
            Assert.Equal(0, exit);
            var output = sw.ToString();
            Assert.Contains("\"version\"", output);
            Assert.Contains("\"protocolVersion\"", output);
            Assert.Contains("\"capabilities\"", output);
            Assert.Contains("\"schemaVersions\"", output);
            Assert.Contains("\"limitations\"", output);
        }
        finally { Console.SetOut(oldOut); }
    }

    [Fact]
    public void Capabilities_JsonFlag_ShouldAlsoWork()
    {
        var sw = new StringWriter();
        var oldOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            var exit = CapabilitiesCommands.Handle(["--json"]);
            Assert.Equal(0, exit);
            var output = sw.ToString();
            Assert.Contains("\"version\"", output);
        }
        finally { Console.SetOut(oldOut); }
    }
}
