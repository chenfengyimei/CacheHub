namespace CacheHub.Core.Identifiers;

/// <summary>
/// Base for strongly-typed identifiers.
/// Prevents mixing different ID types (WorkspaceId, FileId, etc.) at compile time.
/// </summary>
public abstract record StrongId(string Value) : IComparable<StrongId>
{
    public string Value { get; } = !string.IsNullOrWhiteSpace(Value)
        ? Value
        : throw new ArgumentException("ID value cannot be null or whitespace.", nameof(Value));

    public int CompareTo(StrongId? other) =>
        other is null ? 1 : string.Compare(Value, other.Value, StringComparison.Ordinal);

    public override string ToString() => Value;
}
