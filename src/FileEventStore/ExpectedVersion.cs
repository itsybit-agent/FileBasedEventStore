namespace FileEventStore;

public class ExpectedVersion
{
    public static readonly ExpectedVersion None = new(-1);
    public static readonly ExpectedVersion Any = new(-2);

    public static ExpectedVersion Exactly(long version) => new(version);

    public long Value { get; }
    private ExpectedVersion(long value) => Value = value;
}
