namespace FileEventStore.Session;

/// <summary>
/// Factory for creating FileEventSession instances.
/// </summary>
public class FileEventSessionFactory : IEventSessionFactory
{
    private readonly IEventStore _store;

    public FileEventSessionFactory(IEventStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public IEventSession OpenSession()
    {
        return new FileEventSession(_store);
    }
}
