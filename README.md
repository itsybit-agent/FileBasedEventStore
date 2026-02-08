# FileEventStore

A simple, file-based event store for .NET applications. Perfect for local development, prototyping, and small-scale applications.

> **For home use only** 🤪

## Features

- 📁 File-based storage (one directory per stream, one JSON file per event)
- 🔄 Event sourcing with aggregates
- 🎯 Unit of Work pattern with sessions (Marten-inspired API)
- 💉 Built-in dependency injection support
- ⚡ Optimistic concurrency control (per-stream)

## Installation

```bash
# Add to your project
dotnet add package FileEventStore
```

Or reference the project directly in your solution.

## Quick Start

### 1. Register Services

```csharp
// Program.cs or Startup.cs
builder.Services.AddFileEventStore("./data/events");
```

### 2. Create an Aggregate

```csharp
public class HouseholdAggregate : Aggregate
{
    public string Name { get; private set; } = "";
    public List<string> Members { get; private set; } = new();

    public void Create(string id, string name, string creatorId)
    {
        if (!string.IsNullOrEmpty(Id))
            throw new InvalidOperationException("Household already exists");

        Emit(new HouseholdCreated(id, name, creatorId, DateTime.UtcNow));
    }

    public void AddMember(string userId, string displayName)
    {
        if (Members.Contains(userId))
            throw new InvalidOperationException("Already a member");

        Emit(new MemberJoined(Id, userId, displayName, DateTime.UtcNow));
    }

    // Apply handles both replay and new events
    protected override void Apply(IStoreableEvent evt)
    {
        switch (evt)
        {
            case HouseholdCreated e:
                Id = e.HouseholdId;
                Name = e.Name;
                Members.Add(e.CreatorId);
                break;
            case MemberJoined e:
                Members.Add(e.UserId);
                break;
        }
    }
}
```

### 3. Create Events

```csharp
public record HouseholdCreated(
    string HouseholdId,
    string Name,
    string CreatorId,
    DateTime CreatedAt
) : IStoreableEvent
{
    public string TimestampUtc { get; set; } = "";
}

public record MemberJoined(
    string HouseholdId,
    string UserId,
    string DisplayName,
    DateTime JoinedAt
) : IStoreableEvent
{
    public string TimestampUtc { get; set; } = "";
}
```

## Using Sessions (Unit of Work)

Sessions provide a Unit of Work pattern inspired by Marten's API. They support both aggregate-level and raw stream operations.

### Basic Usage

```csharp
public class JoinHouseholdHandler
{
    private readonly IEventSessionFactory _sessionFactory;

    public JoinHouseholdHandler(IEventSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    public async Task Handle(JoinHouseholdCommand cmd)
    {
        await using var session = _sessionFactory.OpenSession();

        // Load aggregates (automatically tracked)
        var invite = await session.AggregateStreamAsync<InviteAggregate>(cmd.InviteCode);
        if (invite is null)
            throw new InvalidOperationException("Invalid invite code");

        var household = await session.AggregateStreamAsync<HouseholdAggregate>(invite.HouseholdId);
        if (household is null)
            throw new InvalidOperationException("Household not found");

        // Make changes
        invite.MarkUsed();
        household.AddMember(cmd.UserId, cmd.DisplayName);

        // Commit all changes
        await session.SaveChangesAsync();
    }
}
```

### Aggregate Operations

```csharp
await using var session = _sessionFactory.OpenSession();

// Load aggregate (null if not found)
var household = await session.AggregateStreamAsync<HouseholdAggregate>("123");

// Load or create new
var invite = await session.AggregateStreamOrCreateAsync<InviteAggregate>(code);

// Manual tracking for externally-created aggregates
var newAggregate = new MyAggregate();
newAggregate.DoSomething();
session.Track(newAggregate);

await session.SaveChangesAsync();
```

### Raw Stream Operations

For non-aggregate event streams (logs, projections, etc.):

```csharp
await using var session = _sessionFactory.OpenSession();

// Start a new stream (fails if exists)
session.StartStream("audit-log-2024", new UserLoggedIn(...));

// Append to existing stream
session.Append("audit-log-2024", new UserLoggedOut(...));

// Fetch raw events
var events = await session.FetchStreamAsync("audit-log-2024");

await session.SaveChangesAsync();
```

### Identity Map

Loading the same aggregate twice returns the same instance:

```csharp
var household1 = await session.AggregateStreamAsync<HouseholdAggregate>("123");
var household2 = await session.AggregateStreamAsync<HouseholdAggregate>("123");

// Same instance!
Debug.Assert(ReferenceEquals(household1, household2));
```

## Low-Level Store API

For advanced scenarios, use `IEventStore` directly:

```csharp
public class MyService
{
    private readonly IEventStore _store;

    public async Task WriteEvents()
    {
        // Start a new stream
        await _store.StartStreamAsync("orders-123", new OrderCreated(...));

        // Append with concurrency control
        await _store.AppendToStreamAsync(
            "orders-123",
            new OrderShipped(...),
            ExpectedVersion.Exactly(1));

        // Fetch events
        var events = await _store.FetchStreamAsync("orders-123");
        var version = await _store.GetStreamVersionAsync("orders-123");
    }
}
```

## Value Objects

### AggregateId

Aggregate load operations accept an `AggregateId` value object to prevent accidentally passing a full stream id (e.g. `"order-abc123"`) where a raw aggregate id (`"abc123"`) is expected.

```csharp
// Implicit conversion from string — existing code keeps working
var household = await session.AggregateStreamAsync<HouseholdAggregate>("abc123");

// Explicit construction
var id = AggregateId.From("abc123");
```

Validation rules:
- Not null or empty
- No path traversal (`..`)
- No filesystem-invalid characters

### StreamId

Stream IDs are validated automatically via the `StreamId` value object:

```csharp
// Implicit conversion from string validates automatically
StreamId id = "my-stream-123";  // OK

StreamId bad = "../etc/passwd";  // Throws ArgumentException (path traversal)
StreamId bad = "stream<>name";   // Throws ArgumentException (invalid chars)
```

Validation rules:
- Max 200 characters
- Alphanumeric with hyphens, underscores, dots
- No path traversal (`..`)
- No filesystem-invalid characters

## Samples

Two sample applications are included:

- **SampleApp** - Basic event store usage with direct `IEventStore` access
- **SessionSample** - Unit of Work pattern with multi-aggregate operations

```bash
dotnet run --project samples/SampleApp
dotnet run --project samples/SessionSample
```

## Storage Structure

Events are stored in directories per stream, with one JSON file per event:

```
data/streams/
├── householdaggregate-abc123/
│   ├── 000001.json
│   └── 000002.json
├── inviteaggregate-INV001/
│   └── 000001.json
```

## API Reference

### IEventSession (Unit of Work)

| Method | Description |
|--------|-------------|
| `AggregateStreamAsync<T>(AggregateId)` | Load and rebuild aggregate from events (null if not found) |
| `AggregateStreamOrCreateAsync<T>(AggregateId)` | Load or create new aggregate |
| `Track<T>(aggregate)` | Manually track an aggregate for saving |
| `StartStream(streamId, events)` | Queue events to start a new stream |
| `StartStream<T>(id, events)` | Queue events to start a new typed stream |
| `Append(streamId, events)` | Queue events to append to existing stream |
| `FetchStreamAsync(streamId)` | Fetch raw events (immediate read) |
| `SaveChangesAsync()` | Commit all pending changes |
| `HasChanges` | True if there are uncommitted events |

### IEventStore (Low-Level)

| Method | Description |
|--------|-------------|
| `StartStreamAsync(streamId, events)` | Start a new stream (fails if exists) |
| `AppendToStreamAsync(streamId, events, expectedVersion)` | Append events with concurrency check |
| `FetchStreamAsync(streamId)` | Fetch events with metadata |
| `FetchEventsAsync(streamId)` | Fetch just event data |
| `GetStreamVersionAsync(streamId)` | Get current stream version |
| `StreamExistsAsync(streamId)` | Check if stream exists |

## Limitations

- **Not for production at scale** — file I/O isn't optimized for high throughput
- **No cross-aggregate transactions** — each aggregate saves independently
- **No projections built-in** — implement your own read models
- **Single-process only** — no distributed locking

## Future: Storage Abstraction

The session interface is designed to be swappable. Future versions may include:

- `MartenEventSession` — backed by Marten/PostgreSQL
- `EventStoreDbSession` — backed by EventStoreDB

Your business code stays the same:

```csharp
await using var session = _sessionFactory.OpenSession();
var aggregate = await session.AggregateStreamAsync<MyAggregate>(id);
// ...
await session.SaveChangesAsync();
```

## License

MIT
