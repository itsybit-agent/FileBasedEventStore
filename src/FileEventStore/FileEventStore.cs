using FileEventStore.Serialization;

namespace FileEventStore;

public class FileEventStore : IEventStore
{
    private readonly string _rootPath;
    private readonly IEventSerializer _serializer;
    private readonly IClock _clock;

    public FileEventStore(string rootPath, IEventSerializer serializer, IClock? clock = null)
    {
        _rootPath = rootPath;
        _serializer = serializer;
        _clock = clock ?? SystemClock.Instance;
        Directory.CreateDirectory(GetStreamsPath());
    }

    public FileEventStore(string rootPath) : this(rootPath, new JsonEventSerializer(), null)
    {
    }

    private string GetStreamsPath() => Path.Combine(_rootPath, "streams");
    private string GetStreamPath(string streamId) => Path.Combine(GetStreamsPath(), streamId);
    private string GetEventFilePath(string streamId, long version) =>
        Path.Combine(GetStreamPath(streamId), $"{version:D6}.json");

    public Task<long> StartStreamAsync(StreamId streamId, string? streamType, IEnumerable<IStoreableEvent> events)
        => AppendInternalAsync(streamId, streamType, events, ExpectedVersion.None);

    public Task<long> StartStreamAsync(StreamId streamId, IEnumerable<IStoreableEvent> events)
        => StartStreamAsync(streamId, null, events);

    public Task<long> StartStreamAsync(StreamId streamId, IStoreableEvent evt)
        => StartStreamAsync(streamId, null, [evt]);

    public Task<long> AppendToStreamAsync(StreamId streamId, IStoreableEvent evt, ExpectedVersion expectedVersion)
        => AppendToStreamAsync(streamId, null, [evt], expectedVersion);

    public Task<long> AppendToStreamAsync(StreamId streamId, IEnumerable<IStoreableEvent> events, ExpectedVersion expectedVersion)
        => AppendInternalAsync(streamId, null, events, expectedVersion);

    public Task<long> AppendToStreamAsync(StreamId streamId, string? streamType, IEnumerable<IStoreableEvent> events, ExpectedVersion expectedVersion)
        => AppendInternalAsync(streamId, streamType, events, expectedVersion);

    public Task<long> AppendToStreamAsync(StreamId streamId, string? streamType, IStoreableEvent evt, ExpectedVersion expectedVersion)
        => AppendInternalAsync(streamId, streamType, [evt], expectedVersion);

    private async Task<long> AppendInternalAsync(StreamId streamId, string? streamType, IEnumerable<IStoreableEvent> events, ExpectedVersion expectedVersion)
    {
        var streamPath = GetStreamPath(streamId);
        var currentVersion = GetCurrentStreamVersion(streamPath);

        if (expectedVersion.Value == ExpectedVersion.None.Value)
        {
            if (currentVersion > 0)
                throw new ConcurrencyException(streamId, -1, currentVersion);
        }
        else if (expectedVersion.Value >= 0)
        {
            if (currentVersion != expectedVersion.Value)
                throw new ConcurrencyException(streamId, expectedVersion.Value, currentVersion);
        }

        Directory.CreateDirectory(streamPath);

        var version = currentVersion;

        foreach (var evt in events)
        {
            version++;
            var timestamp = _clock.UtcNow;
            evt.TimestampUtc = timestamp.ToString("O");

            var eventType = evt.GetType();
            var stored = new StoredEvent(
                StreamVersion: version,
                StreamId: streamId,
                StreamType: streamType,
                EventType: eventType.Name,
                ClrType: eventType.AssemblyQualifiedName!,
                Timestamp: timestamp,
                Data: evt
            );

            var filePath = GetEventFilePath(streamId, version);
            var json = _serializer.Serialize(stored);

            await using var fs = new FileStream(
                filePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);

            await using var writer = new StreamWriter(fs);
            await writer.WriteAsync(json);
        }

        return version;
    }

    public async Task<IReadOnlyList<IStoreableEvent>> FetchEventsAsync(StreamId streamId)
    {
        var stored = await FetchStreamAsync(streamId);
        return stored.Select(e => e.Data).ToList();
    }

    public async Task<IReadOnlyList<StoredEvent>> FetchStreamAsync(StreamId streamId)
    {
        var streamPath = GetStreamPath(streamId);

        if (!Directory.Exists(streamPath))
            return Array.Empty<StoredEvent>();

        var files = Directory.GetFiles(streamPath, "*.json")
            .OrderBy(f => f)
            .ToList();

        var events = new List<StoredEvent>(files.Count);

        foreach (var file in files)
        {
            var json = await File.ReadAllTextAsync(file);
            events.Add(_serializer.Deserialize(json));
        }

        return events;
    }

    public Task<long> GetStreamVersionAsync(StreamId streamId)
    {
        var streamPath = GetStreamPath(streamId);
        return Task.FromResult(GetCurrentStreamVersion(streamPath));
    }

    public Task<bool> StreamExistsAsync(StreamId streamId)
    {
        var streamPath = GetStreamPath(streamId);
        return Task.FromResult(Directory.Exists(streamPath) && Directory.GetFiles(streamPath, "*.json").Any());
    }

    private long GetCurrentStreamVersion(string streamPath)
    {
        if (!Directory.Exists(streamPath))
            return 0;

        var files = Directory.GetFiles(streamPath, "*.json");
        if (files.Length == 0)
            return 0;

        return files
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Select(name => long.TryParse(name, out var v) ? v : 0)
            .Max();
    }
}
