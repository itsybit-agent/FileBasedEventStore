namespace FileEventStore;

public interface IEventStore
{
    Task<long> AppendAsync(string streamId, string? streamType, IEnumerable<object> events, ExpectedVersion expectedVersion);
    Task<long> AppendAsync(string streamId, string? streamType, object evt, ExpectedVersion expectedVersion);
    Task<long> AppendAsync(string streamId, IEnumerable<object> events, ExpectedVersion expectedVersion);
    Task<long> AppendAsync(string streamId, object evt, ExpectedVersion expectedVersion);
    Task<IReadOnlyList<StoredEvent>> LoadStreamAsync(string streamId);
    Task<long> GetCurrentVersionAsync(string streamId);
    Task<bool> StreamExistsAsync(string streamId);
}
