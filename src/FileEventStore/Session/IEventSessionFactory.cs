namespace FileEventStore.Session;

/// <summary>
/// Factory for creating event sessions.
/// Inject this into your handlers/services to get sessions.
/// </summary>
public interface IEventSessionFactory
{
    /// <summary>
    /// Create a new event session.
    /// Dispose the session when done to release resources.
    /// </summary>
    IEventSession OpenSession();
}
