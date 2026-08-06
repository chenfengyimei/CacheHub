using System.Text.Json.Serialization;

namespace CacheHub.Core.LanguageServers;

/// <summary>
/// Language Intelligence Contract: definition/reference/type/diagnostic/call hierarchy.
/// </summary>
public interface ILanguageServer
{
    string Id { get; }
    string Version { get; }
    string Language { get; }
    Task<LspCapabilities> GetCapabilitiesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LspLocation>> GetDefinitionAsync(string filePath, int line, int character, CancellationToken ct = default);
    Task<IReadOnlyList<LspLocation>> GetReferencesAsync(string filePath, int line, int character, CancellationToken ct = default);
    Task<IReadOnlyList<LspDiagnostic>> GetDiagnosticsAsync(string filePath, CancellationToken ct = default);
}

/// <summary>
/// Capabilities negotiated with the LSP server.
/// </summary>
public sealed record LspCapabilities
{
    public bool SupportsDefinition { get; init; }
    public bool SupportsReferences { get; init; }
    public bool SupportsDiagnostics { get; init; }
    public bool SupportsCallHierarchy { get; init; }
    public bool SupportsHover { get; init; }
    public bool SupportsWorkspaceSymbol { get; init; }
}

/// <summary>
/// A location in a file.
/// </summary>
public sealed record LspLocation
{
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int StartCharacter { get; init; }
    public required int EndLine { get; init; }
    public required int EndCharacter { get; init; }
}

/// <summary>
/// Diagnostic message from LSP.
/// </summary>
public sealed record LspDiagnostic
{
    public required int Line { get; init; }
    public required int Character { get; init; }
    public required string Message { get; init; }
    public required LspSeverity Severity { get; init; }
    public string? Source { get; init; }
}

/// <summary>
/// Severity of a diagnostic.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LspSeverity
{
    Error,
    Warning,
    Information,
    Hint,
}

/// <summary>
/// LSP lifecycle state.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LspState
{
    NotStarted,
    Initializing,
    Ready,
    Crashed,
    Disabled,
}

/// <summary>
/// Configuration for an LSP server process.
/// </summary>
public sealed record LspServerConfig
{
    public required string ServerId { get; init; }
    public required string Command { get; init; }
    public IReadOnlyList<string> Args { get; init; } = [];
    public required string WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public bool AutoRestart { get; init; }
    public int MaxRestarts { get; init; } = 3;
}

/// <summary>
/// LSP server lifecycle manager.
/// </summary>
public sealed class LspLifecycle
{
    public LspState State { get; private set; } = LspState.NotStarted;
    public int RestartCount { get; private set; }
    public DateTimeOffset? LastCrashAt { get; private set; }
    private readonly LspServerConfig _config;

    public LspLifecycle(LspServerConfig config)
    {
        _config = config;
    }

    public void Initialize()
    {
        State = LspState.Initializing;
        // In a full implementation, this would start the LSP process.
        State = LspState.Ready;
    }

    public void Disable()
    {
        State = LspState.Disabled;
    }

    public void ReportCrash()
    {
        State = LspState.Crashed;
        LastCrashAt = DateTimeOffset.UtcNow;
        RestartCount++;

        if (_config.AutoRestart && RestartCount <= _config.MaxRestarts)
        {
            Initialize();
        }
    }

    public bool IsReady => State == LspState.Ready;
}
