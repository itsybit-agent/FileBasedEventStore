namespace FileEventStore;

public interface IEventStore
{
    Task<long> AppendAsync(string streamId, string? streamType, IEnumerable<IStoreableEvent> events, ExpectedVersion expectedVersion);
    Task<long> AppendAsync(string streamId, string? streamType, IStoreableEvent evt, ExpectedVersion expectedVersion);
    Task<long> AppendAsync(string streamId, IEnumerable<IStoreableEvent> events, ExpectedVersion expectedVersion);
    Task<long> AppendAsync(string streamId, IStoreableEvent evt, ExpectedVersion expectedVersion);

    /// <summary>
    /// Loads events from a stream, returning just the event data.
    /// </summary>
    Task<IReadOnlyList<IStoreableEvent>> LoadEventsAsync(string streamId);

    /// <summary>
    /// Loads events from a stream with full metadata (version, timestamp, etc.).
    /// </summary>
    Task<IReadOnlyList<StoredEvent>> LoadStreamAsync(string streamId);

    Task<long> GetCurrentVersionAsync(string streamId);
    Task<bool> StreamExistsAsync(string streamId);
}
