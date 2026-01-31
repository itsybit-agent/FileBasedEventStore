namespace FileEventStore;

public class ConcurrencyException : Exception
{
    public string StreamId { get; }
    public long ExpectedVersion { get; }
    public long ActualVersion { get; }

    public ConcurrencyException(string streamId, long expected, long actual)
        : base($"Stream '{streamId}' expected version {expected} but was {actual}")
    {
        StreamId = streamId;
        ExpectedVersion = expected;
        ActualVersion = actual;
    }
}
