namespace FileEventStore.Aggregates
{
    public class AggregateRepository<T> where T : Aggregate, new()
    {
        private readonly IEventStore _store;
        private readonly Func<string, string> _streamIdFromAggregateId;

        public AggregateRepository(IEventStore store, Func<string, string>? streamIdFromAggregateId = null)
        {
            _store = store;
            _streamIdFromAggregateId = streamIdFromAggregateId ?? (id => $"{typeof(T).Name.ToLowerInvariant()}-{id}");
        }

        public async Task<T?> LoadAsync(string id)
        {
            var streamId = _streamIdFromAggregateId(id);
            var events = await _store.FetchStreamAsync(streamId);

            if (events.Count == 0)
                return null;

            var aggregate = new T();
            aggregate.Load(events);
            return aggregate;
        }

        public async Task<T> LoadOrCreateAsync(string id)
        {
            return await LoadAsync(id) ?? new T();
        }

        public async Task SaveAsync(T aggregate)
        {
            if (aggregate.UncommittedEvents.Count == 0)
                return;

            var streamId = _streamIdFromAggregateId(aggregate.Id);
            var expectedVersion = aggregate.Version == 0
                ? ExpectedVersion.None
                : ExpectedVersion.Exactly(aggregate.Version - aggregate.UncommittedEvents.Count);

            await _store.AppendToStreamAsync(streamId, typeof(T).Name, aggregate.UncommittedEvents, expectedVersion);
            aggregate.ClearUncommittedEvents();
        }
    }
}
