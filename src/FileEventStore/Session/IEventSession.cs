namespace FileEventStore.Session;

/// <summary>
/// Unit of Work for event sourcing.
/// Tracks loaded aggregates and coordinates saving changes.
/// Note: Each aggregate stream is committed independently; there is no cross-aggregate atomicity.
/// </summary>
public interface IEventSession : IAsyncDisposable
{
    /// <summary>
    /// Load an aggregate by id. Returns null if not found.
    /// Subsequent calls with the same id return the cached instance.
    /// </summary>
    Task<T?> LoadAsync<T>(string id) where T : Aggregates.Aggregate, new();

    /// <summary>
    /// Load an aggregate by id, creating a new one if not found.
    /// </summary>
    Task<T> LoadOrCreateAsync<T>(string id) where T : Aggregates.Aggregate, new();

    /// <summary>
    /// Mark an aggregate to be saved when SaveChangesAsync is called.
    /// Call this after modifying an aggregate that wasn't loaded through this session.
    /// Aggregates loaded via LoadAsync/LoadOrCreateAsync are tracked automatically.
    /// </summary>
    void Store<T>(T aggregate) where T : Aggregates.Aggregate;

    /// <summary>
    /// Commit all pending changes to the event store.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if there are any uncommitted changes.
    /// </summary>
    bool HasChanges { get; }
}
