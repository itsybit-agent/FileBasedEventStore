namespace FileEventStore;

/// <summary>
/// Low-level event store operations for working with streams directly.
/// For aggregate-based workflows, use IEventSession instead.
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Start a new stream. Fails if stream already exists.
    /// </summary>
    Task<long> StartStreamAsync(StreamId streamId, string? streamType, IEnumerable<IStoreableEvent> events);
    
    /// <summary>
    /// Start a new stream. Fails if stream already exists.
    /// </summary>
    Task<long> StartStreamAsync(StreamId streamId, IEnumerable<IStoreableEvent> events);
    
    /// <summary>
    /// Start a new stream with a single event. Fails if stream already exists.
    /// </summary>
    Task<long> StartStreamAsync(StreamId streamId, IStoreableEvent evt);

    /// <summary>
    /// Append events to an existing stream.
    /// </summary>
    Task<long> AppendToStreamAsync(StreamId streamId, IEnumerable<IStoreableEvent> events, ExpectedVersion expectedVersion);
    
    /// <summary>
    /// Append a single event to an existing stream.
    /// </summary>
    Task<long> AppendToStreamAsync(StreamId streamId, IStoreableEvent evt, ExpectedVersion expectedVersion);
    
    /// <summary>
    /// Append events to an existing stream with stream type metadata.
    /// </summary>
    Task<long> AppendToStreamAsync(StreamId streamId, string? streamType, IEnumerable<IStoreableEvent> events, ExpectedVersion expectedVersion);
    
    /// <summary>
    /// Append a single event to an existing stream with stream type metadata.
    /// </summary>
    Task<long> AppendToStreamAsync(StreamId streamId, string? streamType, IStoreableEvent evt, ExpectedVersion expectedVersion);

    /// <summary>
    /// Fetch events from a stream with full metadata (version, timestamp, etc.).
    /// </summary>
    Task<IReadOnlyList<StoredEvent>> FetchStreamAsync(StreamId streamId);
    
    /// <summary>
    /// Fetch just the event data from a stream.
    /// </summary>
    Task<IReadOnlyList<IStoreableEvent>> FetchEventsAsync(StreamId streamId);

    /// <summary>
    /// Get the current version of a stream (0 if doesn't exist).
    /// </summary>
    Task<long> GetStreamVersionAsync(StreamId streamId);
    
    /// <summary>
    /// Check if a stream exists.
    /// </summary>
    Task<bool> StreamExistsAsync(StreamId streamId);
}
