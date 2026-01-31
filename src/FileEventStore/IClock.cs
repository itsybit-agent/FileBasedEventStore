namespace FileEventStore;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
