namespace FileEventStore.Aggregates;

/// <summary>
/// Value object representing a validated aggregate identifier.
/// Ensures callers pass a raw aggregate id — not a stream id — to load operations.
/// </summary>
public readonly struct AggregateId : IEquatable<AggregateId>
{
    public string Value { get; }

    private AggregateId(string value)
    {
        Value = value;
    }

    public static AggregateId From(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Aggregate id cannot be null or empty.", nameof(id));

        if (id.Contains(".."))
            throw new ArgumentException("Aggregate id cannot contain '..' (path traversal).", nameof(id));

        if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Aggregate id contains invalid characters.", nameof(id));

        return new AggregateId(id);
    }

    public override string ToString() => Value;

    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    public override bool Equals(object? obj) => obj is AggregateId other && Equals(other);

    public bool Equals(AggregateId other) => Value == other.Value;

    public static bool operator ==(AggregateId left, AggregateId right) => left.Equals(right);

    public static bool operator !=(AggregateId left, AggregateId right) => !left.Equals(right);

    public static implicit operator string(AggregateId id) => id.Value;

    public static implicit operator AggregateId(string id) => From(id);
}
