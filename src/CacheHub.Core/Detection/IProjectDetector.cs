using System.Text.Json.Serialization;

namespace CacheHub.Core.Detection;

/// <summary>
/// Detected project component.
/// </summary>
public sealed record DetectedComponent
{
    public required string Id { get; init; }
    public required string Path { get; init; }
    public required string Language { get; init; }
    public string? Framework { get; init; }
    public string? BuildSystem { get; init; }
    public string? PackageManager { get; init; }
    public double Confidence { get; init; } = 1.0;
    public IReadOnlyList<string> Evidence { get; init; } = [];
}

/// <summary>
/// Result of project detection.
/// </summary>
public sealed record DetectionResult
{
    public required string RootPath { get; init; }
    public required IReadOnlyList<DetectedComponent> Components { get; init; }
    public required IReadOnlyDictionary<string, int> LanguageStats { get; init; }
    public required bool IsMonorepo { get; init; }
    public IReadOnlyList<string> MissingTools { get; init; } = [];
}

/// <summary>
/// Detector contract: evidence-based, read-only, with confidence.
/// </summary>
public interface IProjectDetector
{
    string Id { get; }
    IReadOnlySet<string> TriggerFiles { get; }
    DetectedComponent? Detect(string rootPath, IReadOnlyDictionary<string, string> triggerFileContents);
}

/// <summary>
/// Suggested initialization action (read-only, requires approval to execute).
/// </summary>
public sealed record InitAction
{
    public required string Command { get; init; }
    public required string Purpose { get; init; }
    public required string WorkingDirectory { get; init; }
    public required bool RequiresNetwork { get; init; }
    public required bool WritesToDisk { get; init; }
    public required bool MayRunScripts { get; init; }
    public required ApprovalLevel Approval { get; init; }
    public IReadOnlyList<string> Risks { get; init; } = [];
}

/// <summary>
/// Approval level for initialization actions.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ApprovalLevel
{
    Automatic,
    OneTimeApproval,
    EveryTimeApproval,
}

/// <summary>
/// Initialization plan: read-only detected, actions require approval.
/// </summary>
public sealed record InitializationPlan
{
    public required string RootPath { get; init; }
    public required IReadOnlyList<InitAction> Actions { get; init; }
    public required IReadOnlyList<string> DetectedComponents { get; init; }
    public required IReadOnlyList<string> MissingTools { get; init; }
}
