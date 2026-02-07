using System.IO;
using FileEventStore.Aggregates;

namespace FileEventStore.Session;

/// <summary>
/// File-based implementation of IEventSession.
/// Provides Unit of Work pattern for event sourcing, coordinating commits for tracked aggregates.
/// Note: Each aggregate stream is committed independently; there is no cross-aggregate atomicity.
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
        ValidateAggregateId(id);
        
        var key = GetKey<T>(id);
        
        // Return cached instance if already loaded
        if (_trackedAggregates.TryGetValue(key, out var entry))
        {
            return entry.Aggregate as T;
        }

        // Load from store (StreamId.From validates the full stream id)
        var streamId = GetStreamId<T>(id);
        var events = await _store.LoadStreamAsync(streamId);

        if (events.Count == 0)
            return null;

        var aggregate = new T();
        aggregate.Load(events);
        
        // Track it with the version at load time
        _trackedAggregates[key] = new AggregateEntry(aggregate, typeof(T), aggregate.Version);
        
        return aggregate;
    }

    public async Task<T> LoadOrCreateAsync<T>(string id) where T : Aggregate, new()
    {
        ThrowIfDisposed();
        ValidateAggregateId(id);
        
        var aggregate = await LoadAsync<T>(id);
        if (aggregate is not null)
            return aggregate;

        // Create new and track it (version 0 = new aggregate)
        aggregate = new T();
        var key = GetKey<T>(id);
        _trackedAggregates[key] = new AggregateEntry(aggregate, typeof(T), 0);
        
        return aggregate;
    }

    public void Store<T>(T aggregate) where T : Aggregate
    {
        ThrowIfDisposed();
        
        if (aggregate is null)
            throw new ArgumentNullException(nameof(aggregate));

        // Use runtime type, not compile-time type, in case aggregate is held as base type
        var aggregateType = aggregate.GetType();
        var key = $"{aggregateType.Name}:{aggregate.Id}";
        _trackedAggregates[key] = new AggregateEntry(aggregate, aggregateType, aggregate.Version);
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
                
                // Expected version is the version at load time (entry.LoadedVersion)
                // For new aggregates (never loaded), expect stream doesn't exist
                var expectedVersion = entry.LoadedVersion == 0
                    ? ExpectedVersion.None
                    : ExpectedVersion.Exactly(entry.LoadedVersion);

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
}
