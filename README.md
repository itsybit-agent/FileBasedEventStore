# FileEventStore

A simple, file-based event store for .NET applications. Perfect for local development, prototyping, and small-scale applications.

> **For home use only** 🤪

## Features

- 📁 File-based storage (one directory per stream, one JSON file per event)
- 🔄 Event sourcing with aggregates
- 🎯 Unit of Work pattern with sessions
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

    // Apply handles both replay (Load) and new events (Emit)
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
) : IStoreableEvent;

public record MemberJoined(
    string HouseholdId,
    string UserId,
    string DisplayName,
    DateTime JoinedAt
) : IStoreableEvent;
```

## Using Sessions (Unit of Work)

Sessions provide a Unit of Work pattern for working with multiple aggregates. Changes are tracked and saved in a single call, but each aggregate stream is committed independently (see Limitations).

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
        // Open a session
        await using var session = _sessionFactory.OpenSession();

        // Load aggregates (automatically tracked)
        var invite = await session.LoadAsync<InviteAggregate>(cmd.InviteCode);
        if (invite is null)
            throw new InvalidOperationException("Invalid invite code");

        var household = await session.LoadAsync<HouseholdAggregate>(invite.HouseholdId);
        if (household is null)
            throw new InvalidOperationException("Household not found");

        // Make changes
        invite.MarkUsed();
        household.AddMember(cmd.UserId, cmd.DisplayName);

        // Commit all changes together
        await session.SaveChangesAsync();
    }
}
```

### Session Features

#### Identity Map

Loading the same aggregate twice returns the same instance:

```csharp
await using var session = _sessionFactory.OpenSession();

var household1 = await session.LoadAsync<HouseholdAggregate>("123");
var household2 = await session.LoadAsync<HouseholdAggregate>("123");

// Same instance!
Debug.Assert(ReferenceEquals(household1, household2));
```

#### Load or Create

Create a new aggregate if it doesn't exist:

```csharp
await using var session = _sessionFactory.OpenSession();

// Returns existing or new empty aggregate
var household = await session.LoadOrCreateAsync<HouseholdAggregate>("new-id");

if (string.IsNullOrEmpty(household.Id))
{
    // It's new, initialize it
    household.Create("new-id", "My Household", userId);
}

await session.SaveChangesAsync();
```

#### Manual Tracking

Track aggregates created outside the session:

```csharp
await using var session = _sessionFactory.OpenSession();

var newHousehold = new HouseholdAggregate();
newHousehold.Create(Guid.NewGuid().ToString(), "New Home", userId);

// Manually track it
session.Store(newHousehold);

await session.SaveChangesAsync();
```

#### Check for Changes

```csharp
await using var session = _sessionFactory.OpenSession();

var household = await session.LoadAsync<HouseholdAggregate>("123");
household.AddMember(userId, "New Member");

if (session.HasChanges)
{
    await session.SaveChangesAsync();
}
```

### Session vs Repository

You can still use `AggregateRepository<T>` for simple single-aggregate operations:

```csharp
// Simple: single aggregate, immediate save
public class CreateHouseholdHandler
{
    private readonly AggregateRepository<HouseholdAggregate> _repo;

    public async Task Handle(CreateHouseholdCommand cmd)
    {
        var household = new HouseholdAggregate();
        household.Create(cmd.Id, cmd.Name, cmd.UserId);
        await _repo.SaveAsync(household);
    }
}

// Complex: multiple aggregates, coordinated save
public class JoinHouseholdHandler
{
    private readonly IEventSessionFactory _sessionFactory;

    public async Task Handle(JoinHouseholdCommand cmd)
    {
        await using var session = _sessionFactory.OpenSession();
        // ... load multiple aggregates, make changes ...
        await session.SaveChangesAsync();
    }
}
```

## Samples

Two sample applications are included:

- **SampleApp** - Basic event store usage with direct `IEventStore` access
- **SessionSample** - Unit of Work pattern with multi-aggregate operations

Run them with:

```bash
dotnet run --project samples/SampleApp
dotnet run --project samples/SessionSample
```

## Configuration

### Custom Options

```csharp
builder.Services.AddFileEventStore(options =>
{
    options.RootPath = "./data/events";
    options.Clock = new FakeClock(); // For testing
});
```

### Storage Structure

Events are stored in directories per stream, with one JSON file per event:

```
data/streams/
├── householdaggregate-abc123/
│   ├── 000001.json
│   └── 000002.json
├── householdaggregate-def456/
│   └── 000001.json
└── inviteaggregate-INV001/
    ├── 000001.json
    └── 000002.json
```

Each file contains a single event with metadata:

```json
[
  {
    "version": 1,
    "timestamp": "2024-02-07T09:00:00Z",
    "eventType": "HouseholdCreated",
    "data": {
      "householdId": "abc123",
      "name": "The Smiths",
      "creatorId": "user-1",
      "createdAt": "2024-02-07T09:00:00Z"
    }
  }
]
```

## API Reference

### IEventSession

| Method | Description |
|--------|-------------|
| `LoadAsync<T>(id)` | Load aggregate by id (null if not found) |
| `LoadOrCreateAsync<T>(id)` | Load or create new aggregate |
| `Store<T>(aggregate)` | Manually track an aggregate |
| `SaveChangesAsync()` | Commit all pending changes |
| `HasChanges` | True if there are uncommitted events |

### IEventStore

| Method | Description |
|--------|-------------|
| `AppendAsync(...)` | Append events to a stream |
| `LoadEventsAsync(streamId)` | Load events from a stream |
| `LoadStreamAsync(streamId)` | Load events with metadata |
| `GetCurrentVersionAsync(streamId)` | Get stream version |
| `StreamExistsAsync(streamId)` | Check if stream exists |

## Limitations

- **Not for production at scale** — file I/O isn't optimized for high throughput
- **No cross-aggregate transactions** — each aggregate saves independently
- **No projections built-in** — implement your own read models
- **Single-process only** — no distributed locking

## Future: Storage Abstraction

The session interface (`IEventSession`) is designed to be swappable. Future versions may include:

- `MartenEventSession` — backed by Marten/PostgreSQL
- `EventStoreDbSession` — backed by EventStoreDB

Your business code stays the same:

```csharp
// Works with any implementation
await using var session = _sessionFactory.OpenSession();
var aggregate = await session.LoadAsync<MyAggregate>(id);
// ...
await session.SaveChangesAsync();
```

## License

MIT
