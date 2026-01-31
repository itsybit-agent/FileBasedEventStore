namespace FileEventStore;

/// <summary>
/// Marker interface for events that can be stored in the event store.
/// Implement this interface on your event types to enable automatic type resolution.
/// </summary>
public interface IStoreableEvent { }
