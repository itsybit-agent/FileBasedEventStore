using FileEventStore.Aggregates;

namespace FileEventStore.Session;

/// <summary>
/// File-based implementation of IEventSession.
/// Provides Unit of Work pattern for event sourcing with atomic commits.
/// </summary>
public class FileEventSession : IEventSession
{
    private readonly IEventStore _store;
    private readonly Dictionary<string, AggregateEntry> _trackedAggregates = new();
    private bool _disposed;

    public FileEventSession(IEventStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public bool HasChanges => _trackedAggregates.Values.Any(e => e.Aggregate.UncommittedEvents.Count > 0);

    public async Task<T?> LoadAsync<T>(string id) where T : Aggregate, new()
    {
        ThrowIfDisposed();
        
        var key = GetKey<T>(id);
        
        // Return cached instance if already loaded
        if (_trackedAggregates.TryGetValue(key, out var entry))
        {
            return entry.Aggregate as T;
        }

        // Load from store
        var streamId = GetStreamId<T>(id);
        var events = await _store.LoadStreamAsync(streamId);

        if (events.Count == 0)
            return null;

        var aggregate = new T();
        aggregate.Load(events);
        
        // Track it
        _trackedAggregates[key] = new AggregateEntry(aggregate, typeof(T));
        
        return aggregate;
    }

    public async Task<T> LoadOrCreateAsync<T>(string id) where T : Aggregate, new()
    {
        var aggregate = await LoadAsync<T>(id);
        if (aggregate is not null)
            return aggregate;

        // Create new and track it
        aggregate = new T();
        var key = GetKey<T>(id);
        _trackedAggregates[key] = new AggregateEntry(aggregate, typeof(T));
        
        return aggregate;
    }

    public void Store<T>(T aggregate) where T : Aggregate
    {
        ThrowIfDisposed();
        
        if (aggregate is null)
            throw new ArgumentNullException(nameof(aggregate));

        var key = GetKey<T>(aggregate.Id);
        _trackedAggregates[key] = new AggregateEntry(aggregate, typeof(T));
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Get all aggregates with uncommitted events
        var toSave = _trackedAggregates.Values
            .Where(e => e.Aggregate.UncommittedEvents.Count > 0)
            .ToList();

        if (toSave.Count == 0)
            return;

        // Save each aggregate
        // Note: FileEventStore writes to separate files per stream, so this is
        // effectively atomic per-aggregate. True cross-aggregate atomicity would
        // require a transaction log, which is future work.
        var exceptions = new List<Exception>();
        
        foreach (var entry in toSave)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                var streamId = GetStreamId(entry.AggregateType, entry.Aggregate.Id);
                var expectedVersion = entry.Aggregate.Version == 0
                    ? ExpectedVersion.None
                    : ExpectedVersion.Exactly(entry.Aggregate.Version - entry.Aggregate.UncommittedEvents.Count);

                await _store.AppendAsync(
                    streamId,
                    entry.AggregateType.Name,
                    entry.Aggregate.UncommittedEvents,
                    expectedVersion);

                entry.Aggregate.ClearUncommittedEvents();
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        if (exceptions.Count > 0)
        {
            throw new AggregateException("One or more aggregates failed to save", exceptions);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        _trackedAggregates.Clear();
        
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FileEventSession));
    }

    private static string GetKey<T>(string id) => $"{typeof(T).Name}:{id}";

    private static string GetStreamId<T>(string id) => $"{typeof(T).Name.ToLowerInvariant()}-{id}";
    
    private static string GetStreamId(Type type, string id) => $"{type.Name.ToLowerInvariant()}-{id}";

    private class AggregateEntry
    {
        public Aggregate Aggregate { get; }
        public Type AggregateType { get; }

        public AggregateEntry(Aggregate aggregate, Type aggregateType)
        {
            Aggregate = aggregate;
            AggregateType = aggregateType;
        }
    }
}
