# Session Sample

Demonstrates the **Unit of Work pattern** with `IEventSession`.

## What It Shows

1. **Creating a household** - Simple single-aggregate operation
2. **Generating an invite** - Loads one aggregate, creates another
3. **Joining a household** - **Multi-aggregate operation**: modifies both the invite and household, commits together
4. **Verifying state** - Reloads aggregates to confirm changes persisted

## Key Concepts

### Session Factory

Inject `IEventSessionFactory` and create sessions per request/operation:

```csharp
await using var session = factory.OpenSession();
```

### Loading Aggregates

```csharp
// Returns null if not found
var existing = await session.LoadAsync<MyAggregate>(id);

// Creates new if not found
var aggregate = await session.LoadOrCreateAsync<MyAggregate>(id);
```

### Identity Map

Loading the same aggregate twice returns the same instance:

```csharp
var a1 = await session.LoadAsync<MyAggregate>("123");
var a2 = await session.LoadAsync<MyAggregate>("123");
// a1 and a2 are the same object!
```

### Atomic Commits

All changes are committed together:

```csharp
invite.MarkUsed(userId);
household.AddMember(userId, displayName);

await session.SaveChangesAsync(); // Both saved
```

## Running

```bash
dotnet run --project samples/SessionSample
```
