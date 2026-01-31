using System.Text.Json;

namespace FileEventStore.Serialization;

public class JsonEventSerializer : IEventSerializer
{
    private readonly JsonSerializerOptions _options;

    public JsonEventSerializer()
    {
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }

    public string Serialize(StoredEvent evt)
    {
        var envelope = new JsonEventEnvelope
        {
            StreamVersion = evt.StreamVersion,
            StreamId = evt.StreamId,
            StreamType = evt.StreamType,
            EventType = evt.EventType,
            ClrType = evt.ClrType,
            Timestamp = evt.Timestamp,
            Data = JsonSerializer.SerializeToElement(evt.Data, _options)
        };

        return JsonSerializer.Serialize(envelope, _options);
    }

    public StoredEvent Deserialize(string json)
    {
        var envelope = JsonSerializer.Deserialize<JsonEventEnvelope>(json, _options)
            ?? throw new InvalidOperationException("Failed to deserialize event");

        // Try ClrType first, fall back to EventType for older events
        var eventType = ResolveType(envelope.ClrType, envelope.EventType)
            ?? throw new InvalidOperationException($"Could not load type: {envelope.ClrType} (EventType: {envelope.EventType})");

        var data = envelope.Data.Deserialize(eventType, _options)
            ?? throw new InvalidOperationException($"Failed to deserialize event data for {envelope.EventType}");

        return new StoredEvent(
            envelope.StreamVersion,
            envelope.StreamId,
            envelope.StreamType,
            envelope.EventType,
            envelope.ClrType,
            envelope.Timestamp,
            data
        );
    }

    private static Type? ResolveType(string clrType, string eventTypeName)
    {
        // Try ClrType first if it's not empty
        if (!string.IsNullOrWhiteSpace(clrType))
        {
            // Try direct resolution first (works for types in calling assembly or mscorlib)
            var type = Type.GetType(clrType);
            if (type != null)
                return type;

            // Extract the type name without assembly info for searching loaded assemblies
            var typeName = clrType.Contains(',')
                ? clrType[..clrType.IndexOf(',')]
                : clrType;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName);
                if (type != null)
                    return type;
            }
        }

        // Fall back to searching by EventType name (for older events without ClrType)
        if (!string.IsNullOrWhiteSpace(eventTypeName))
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == eventTypeName);
                if (type != null)
                    return type;
            }
        }

        return null;
    }

    private class JsonEventEnvelope
    {
        public long StreamVersion { get; set; }
        public string StreamId { get; set; } = "";
        public string? StreamType { get; set; }
        public string EventType { get; set; } = "";
        public string ClrType { get; set; } = "";
        public DateTimeOffset Timestamp { get; set; }
        public JsonElement Data { get; set; }
    }
}

