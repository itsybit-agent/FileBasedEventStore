using FileEventStore.Aggregates;

namespace FileEventStore.Session;

/// <summary>
/// Unit of Work for event sourcing.
/// Provides both aggregate-based and raw stream operations.
/// Note: Each aggregate stream is committed independently; there is no cross-aggregate atomicity.
/// </summary>
public interface IEventSession : IAsyncDisposable
{
    // =========================================================================
    // AGGREGATE OPERATIONS
    // =========================================================================

    /// <summary>
    /// Load and rebuild an aggregate from its event stream.
    /// Returns null if stream doesn't exist.
    /// Subsequent calls with the same id return the cached instance.
    /// </summary>
    Task<T?> AggregateStreamAsync<T>(AggregateId id) where T : Aggregate, new();

    /// <summary>
    /// Load and rebuild an aggregate, creating a new one if stream doesn't exist.
    /// </summary>
    Task<T> AggregateStreamOrCreateAsync<T>(AggregateId id) where T : Aggregate, new();

    /// <summary>
    /// Track an externally-created aggregate for saving.
    /// Aggregates loaded via AggregateStreamAsync are tracked automatically.
    /// </summary>
    void Track<T>(T aggregate) where T : Aggregate;

    // =========================================================================
    // STREAM OPERATIONS (raw access)
    // =========================================================================
    
    /// <summary>
    /// Queue events to start a new stream. Fails on SaveChangesAsync if stream exists.
    /// </summary>
    void StartStream(StreamId streamId, params IStoreableEvent[] events);
    
    /// <summary>
    /// Queue events to start a new stream with aggregate type. Fails on SaveChangesAsync if stream exists.
    /// </summary>
    void StartStream<T>(string id, params IStoreableEvent[] events) where T : Aggregate;
    
    /// <summary>
    /// Queue events to append to an existing stream.
    /// </summary>
    void Append(StreamId streamId, params IStoreableEvent[] events);
    
    /// <summary>
    /// Fetch raw events from a stream (not cached, immediate read).
    /// </summary>
    Task<IReadOnlyList<StoredEvent>> FetchStreamAsync(StreamId streamId);

    // =========================================================================
    // UNIT OF WORK
    // =========================================================================
    
    /// <summary>
    /// Commit all pending changes (aggregates + queued stream operations).
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if there are any uncommitted changes.
    /// </summary>
    bool HasChanges { get; }
}
