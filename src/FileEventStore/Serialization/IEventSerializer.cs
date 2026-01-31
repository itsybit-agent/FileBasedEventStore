using System.Reflection;

namespace FileEventStore.Serialization;

public interface IEventSerializer
{
    string Serialize(StoredEvent evt);
    StoredEvent Deserialize(string json);
}
