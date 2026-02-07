using FileEventStore.Aggregates;

namespace FileEventStore.Session;

/// <summary>
/// File-based implementation of IEventSession.
/// Provides Unit of Work pattern with both aggregate and raw stream operations.
/// Note: Each stream is committed independently; there is no cross-aggregate atomicity.
/// </summary>
public class FileEventSession : IEventSession
{
    private readonly IEventStore _store;
    private readonly Dictionary<string, AggregateEntry> _trackedAggregates = new();
    private readonly List<PendingStreamOperation> _pendingStreamOps = new();
    private bool _disposed;

    public FileEventSession(IEventStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public bool HasChanges => 
        _trackedAggregates.Values.Any(e => e.Aggregate.UncommittedEvents.Count > 0) ||
        _pendingStreamOps.Count > 0;

    // =========================================================================
    // AGGREGATE OPERATIONS
    // =========================================================================

    public async Task<T?> AggregateStreamAsync<T>(string id) where T : Aggregate, new()
    {
        ThrowIfDisposed();
        ValidateAggregateId(id);
        
        var key = GetKey<T>(id);
        
        // Return cached instance if already loaded
        if (_trackedAggregates.TryGetValue(key, out var entry))
        {
            return entry.Aggregate as T;
        }

        // Load from store
        var streamId = GetStreamId<T>(id);
        var events = await _store.FetchStreamAsync(streamId);

        if (events.Count == 0)
            return null;

        var aggregate = new T();
        aggregate.Load(events);
        
        // Track it with the version at load time
        _trackedAggregates[key] = new AggregateEntry(aggregate, typeof(T), aggregate.Version);
        
        return aggregate;
    }

    public async Task<T> AggregateStreamOrCreateAsync<T>(string id) where T : Aggregate, new()
    {
        ThrowIfDisposed();
        ValidateAggregateId(id);
        
        var aggregate = await AggregateStreamAsync<T>(id);
        if (aggregate is not null)
            return aggregate;

        // Create new and track it (version 0 = new aggregate)
        aggregate = new T();
        var key = GetKey<T>(id);
        _trackedAggregates[key] = new AggregateEntry(aggregate, typeof(T), 0);
        
        return aggregate;
    }

    public void Track<T>(T aggregate) where T : Aggregate
    {
        ThrowIfDisposed();
        
        if (aggregate is null)
            throw new ArgumentNullException(nameof(aggregate));

        // Use runtime type in case aggregate is held as base type
        var aggregateType = aggregate.GetType();
        var key = $"{aggregateType.Name}:{aggregate.Id}";
        _trackedAggregates[key] = new AggregateEntry(aggregate, aggregateType, aggregate.Version);
    }

    // =========================================================================
    // STREAM OPERATIONS
    // =========================================================================

    public void StartStream(StreamId streamId, params IStoreableEvent[] events)
    {
        ThrowIfDisposed();
        _pendingStreamOps.Add(new PendingStreamOperation(streamId, null, events, isNew: true));
    }

    public void StartStream<T>(string id, params IStoreableEvent[] events) where T : Aggregate
    {
        ThrowIfDisposed();
        var streamId = GetStreamId<T>(id);
        _pendingStreamOps.Add(new PendingStreamOperation(streamId, typeof(T).Name, events, isNew: true));
    }

    public void Append(StreamId streamId, params IStoreableEvent[] events)
    {
        ThrowIfDisposed();
        _pendingStreamOps.Add(new PendingStreamOperation(streamId, null, events, isNew: false));
    }

    public Task<IReadOnlyList<StoredEvent>> FetchStreamAsync(StreamId streamId)
    {
        ThrowIfDisposed();
        return _store.FetchStreamAsync(streamId);
    }

    // =========================================================================
    // UNIT OF WORK
    // =========================================================================

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var exceptions = new List<Exception>();

        // Save pending stream operations first
        foreach (var op in _pendingStreamOps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                if (op.IsNew)
                {
                    await _store.StartStreamAsync(op.StreamId, op.StreamType, op.Events);
                }
                else
                {
                    await _store.AppendToStreamAsync(op.StreamId, op.StreamType, op.Events, ExpectedVersion.Any);
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }
        _pendingStreamOps.Clear();

        // Save tracked aggregates
        var toSave = _trackedAggregates.Values
            .Where(e => e.Aggregate.UncommittedEvents.Count > 0)
            .ToList();

        foreach (var entry in toSave)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                var streamId = GetStreamId(entry.AggregateType, entry.Aggregate.Id);
                
                var expectedVersion = entry.LoadedVersion == 0
                    ? ExpectedVersion.None
                    : ExpectedVersion.Exactly(entry.LoadedVersion);

                await _store.AppendToStreamAsync(
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
            throw new AggregateException("One or more streams failed to save", exceptions);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        _trackedAggregates.Clear();
        _pendingStreamOps.Clear();
        
        return ValueTask.CompletedTask;
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FileEventSession));
    }

    private static string GetKey<T>(string id) => $"{typeof(T).Name}:{id}";

    private static StreamId GetStreamId<T>(string id) => 
        StreamId.From($"{typeof(T).Name.ToLowerInvariant()}-{id}");
    
    private static StreamId GetStreamId(Type type, string id) => 
        StreamId.From($"{type.Name.ToLowerInvariant()}-{id}");

    private static void ValidateAggregateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Aggregate id cannot be null or empty.", nameof(id));
        
        if (id.Contains(".."))
            throw new ArgumentException("Aggregate id cannot contain '..' (path traversal).", nameof(id));
        
        if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Aggregate id contains invalid characters.", nameof(id));
    }

    // =========================================================================
    // INTERNAL TYPES
    // =========================================================================

    private class AggregateEntry
    {
        public Aggregate Aggregate { get; }
        public Type AggregateType { get; }
        public long LoadedVersion { get; }

        public AggregateEntry(Aggregate aggregate, Type aggregateType, long loadedVersion)
        {
            Aggregate = aggregate;
            AggregateType = aggregateType;
            LoadedVersion = loadedVersion;
        }
    }

    private class PendingStreamOperation
    {
        public StreamId StreamId { get; }
        public string? StreamType { get; }
        public IStoreableEvent[] Events { get; }
        public bool IsNew { get; }

        public PendingStreamOperation(StreamId streamId, string? streamType, IStoreableEvent[] events, bool isNew)
        {
            StreamId = streamId;
            StreamType = streamType;
            Events = events;
            IsNew = isNew;
        }
    }
}
