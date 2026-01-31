namespace FileEventStore;

public class SystemClock : IClock
{
    public static readonly IClock Instance = new SystemClock();
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
