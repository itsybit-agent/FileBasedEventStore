namespace FileEventStore;

public interface IEventStore
{
    Task<long> AppendAsync(StreamId streamId, string? streamType, IEnumerable<IStoreableEvent> events, ExpectedVersion expectedVersion);
    Task<long> AppendAsync(StreamId streamId, string? streamType, IStoreableEvent evt, ExpectedVersion expectedVersion);
    Task<long> AppendAsync(StreamId streamId, IEnumerable<IStoreableEvent> events, ExpectedVersion expectedVersion);
    Task<long> AppendAsync(StreamId streamId, IStoreableEvent evt, ExpectedVersion expectedVersion);

    /// <summary>
    /// Loads events from a stream, returning just the event data.
    /// </summary>
    Task<IReadOnlyList<IStoreableEvent>> LoadEventsAsync(StreamId streamId);

    /// <summary>
    /// Loads events from a stream with full metadata (version, timestamp, etc.).
    /// </summary>
    Task<IReadOnlyList<StoredEvent>> LoadStreamAsync(StreamId streamId);

    Task<long> GetCurrentVersionAsync(StreamId streamId);
    Task<bool> StreamExistsAsync(StreamId streamId);
}
