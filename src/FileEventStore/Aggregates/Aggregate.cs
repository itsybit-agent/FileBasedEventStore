namespace FileEventStore.Aggregates
{

    public abstract class Aggregate
    {
        public string Id { get; protected set; } = "";
        public long Version { get; private set; } = 0;

        private readonly List<object> _uncommittedEvents = new();
        public IReadOnlyList<object> UncommittedEvents => _uncommittedEvents;

        protected void Emit(object evt)
        {
            Apply(evt);
            _uncommittedEvents.Add(evt);
        }

        public void Load(IEnumerable<StoredEvent> events)
        {
            foreach (var evt in events)
            {
                Apply(evt.Data);
                Version = evt.StreamVersion;
            }
        }

        public void ClearUncommittedEvents()
        {
            _uncommittedEvents.Clear();
        }

        protected abstract void Apply(object evt);
    }
}
