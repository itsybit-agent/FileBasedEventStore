namespace FileEventStore;

public class TestClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;

    public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
}
