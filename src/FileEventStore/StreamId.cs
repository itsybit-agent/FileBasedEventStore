using System.Text.RegularExpressions;

namespace FileEventStore;

/// <summary>
/// Value object representing a validated stream identifier.
/// Protects against path traversal and invalid filesystem characters.
/// </summary>
public readonly partial struct StreamId : IEquatable<StreamId>
{
    private static readonly Regex ValidPattern = GeneratedValidPattern();
    
    // Allowed: alphanumeric, hyphen, underscore, dot (but not leading/trailing dots or ..)
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_\-\.]*[a-zA-Z0-9]$|^[a-zA-Z0-9]$", RegexOptions.Compiled)]
    private static partial Regex GeneratedValidPattern();

    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars()
        .Concat(new[] { '/', '\\' })
        .Distinct()
        .ToArray();

    public string Value { get; }

    private StreamId(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Create a StreamId from a string, validating it.
    /// </summary>
    /// <exception cref="ArgumentException">If the id is invalid.</exception>
    public static StreamId From(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Stream id cannot be null or empty.", nameof(id));

        if (id.Length > 200)
            throw new ArgumentException("Stream id cannot exceed 200 characters.", nameof(id));

        // Check for path traversal
        if (id.Contains(".."))
            throw new ArgumentException("Stream id cannot contain '..' (path traversal).", nameof(id));

        // Check for invalid filesystem characters
        if (id.IndexOfAny(InvalidChars) >= 0)
            throw new ArgumentException($"Stream id contains invalid characters.", nameof(id));

        // Check for leading/trailing dots or spaces
        if (id.StartsWith('.') || id.EndsWith('.') || id.StartsWith(' ') || id.EndsWith(' '))
            throw new ArgumentException("Stream id cannot start or end with dots or spaces.", nameof(id));

        // Validate pattern (alphanumeric with hyphens, underscores, dots)
        if (!ValidPattern.IsMatch(id))
            throw new ArgumentException("Stream id must be alphanumeric with hyphens, underscores, or dots.", nameof(id));

        return new StreamId(id);
    }

    /// <summary>
    /// Try to create a StreamId, returning false if invalid.
    /// </summary>
    public static bool TryFrom(string id, out StreamId streamId)
    {
        try
        {
            streamId = From(id);
            return true;
        }
        catch (ArgumentException)
        {
            streamId = default;
            return false;
        }
    }

    public override string ToString() => Value;
    
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;
    
    public override bool Equals(object? obj) => obj is StreamId other && Equals(other);
    
    public bool Equals(StreamId other) => Value == other.Value;

    public static bool operator ==(StreamId left, StreamId right) => left.Equals(right);
    
    public static bool operator !=(StreamId left, StreamId right) => !left.Equals(right);

    /// <summary>
    /// Implicit conversion from StreamId to string.
    /// </summary>
    public static implicit operator string(StreamId id) => id.Value;
    
    /// <summary>
    /// Implicit conversion from string to StreamId.
    /// Validates the string and throws ArgumentException if invalid.
    /// </summary>
    public static implicit operator StreamId(string id) => From(id);
}
