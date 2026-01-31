namespace FileEventStore;

public record StoredEvent(
    long StreamVersion,
    string StreamId,
    string? StreamType,
    string EventType,
    string ClrType,
    DateTimeOffset Timestamp,
    object Data
);
