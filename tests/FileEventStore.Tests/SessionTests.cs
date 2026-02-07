using FileEventStore;
using FileEventStore.Aggregates;
using FileEventStore.Session;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileEventStore.Tests;

public class SessionTests : IDisposable
{
    private readonly string _testPath;
    private readonly ServiceProvider _provider;
    private readonly IEventSessionFactory _sessionFactory;

    public SessionTests()
    {
        _testPath = Path.Combine(Path.GetTempPath(), $"session-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testPath);

        var services = new ServiceCollection();
        services.AddFileEventStore(_testPath);
        _provider = services.BuildServiceProvider();
        _sessionFactory = _provider.GetRequiredService<IEventSessionFactory>();
    }

    public void Dispose()
    {
        _provider.Dispose();
        if (Directory.Exists(_testPath))
            Directory.Delete(_testPath, true);
    }

    // =========================================================================
    // Loading Behavior
    // =========================================================================

    [Fact]
    public async Task LoadAsync_returns_null_when_aggregate_does_not_exist()
    {
        await using var session = _sessionFactory.OpenSession();

        var result = await session.LoadAsync<TestAggregate>("non-existent");

        Assert.Null(result);
    }

    [Fact]
    public async Task LoadAsync_returns_aggregate_with_state_from_stored_events()
    {
        // Arrange: create and save an aggregate
        var id = Guid.NewGuid().ToString();
        await using (var session = _sessionFactory.OpenSession())
        {
            var aggregate = await session.LoadOrCreateAsync<TestAggregate>(id);
            aggregate.Create(id, "Test Name");
            await session.SaveChangesAsync();
        }

        // Act: load in a new session
        await using (var session = _sessionFactory.OpenSession())
        {
            var loaded = await session.LoadAsync<TestAggregate>(id);

            // Assert
            Assert.NotNull(loaded);
            Assert.Equal(id, loaded.Id);
            Assert.Equal("Test Name", loaded.Name);
        }
    }

    [Fact]
    public async Task LoadOrCreateAsync_returns_new_aggregate_when_not_exists()
    {
        await using var session = _sessionFactory.OpenSession();

        var aggregate = await session.LoadOrCreateAsync<TestAggregate>("new-id");

        Assert.NotNull(aggregate);
        Assert.Equal("", aggregate.Id); // Not initialized yet
    }

    [Fact]
    public async Task LoadOrCreateAsync_returns_existing_aggregate_when_exists()
    {
        var id = Guid.NewGuid().ToString();
        
        // Create first
        await using (var session = _sessionFactory.OpenSession())
        {
            var aggregate = await session.LoadOrCreateAsync<TestAggregate>(id);
            aggregate.Create(id, "Original");
            await session.SaveChangesAsync();
        }

        // Load via LoadOrCreate
        await using (var session = _sessionFactory.OpenSession())
        {
            var loaded = await session.LoadOrCreateAsync<TestAggregate>(id);

            Assert.Equal("Original", loaded.Name);
        }
    }

    // =========================================================================
    // Identity Map Behavior
    // =========================================================================

    [Fact]
    public async Task Loading_same_aggregate_twice_returns_same_instance()
    {
        var id = Guid.NewGuid().ToString();
        
        await using (var session = _sessionFactory.OpenSession())
        {
            var aggregate = await session.LoadOrCreateAsync<TestAggregate>(id);
            aggregate.Create(id, "Test");
            await session.SaveChangesAsync();
        }

        await using (var session = _sessionFactory.OpenSession())
        {
            var first = await session.LoadAsync<TestAggregate>(id);
            var second = await session.LoadAsync<TestAggregate>(id);

            Assert.Same(first, second);
        }
    }

    [Fact]
    public async Task Changes_to_loaded_aggregate_are_visible_on_second_load()
    {
        var id = Guid.NewGuid().ToString();
        
        await using (var session = _sessionFactory.OpenSession())
        {
            var aggregate = await session.LoadOrCreateAsync<TestAggregate>(id);
            aggregate.Create(id, "Original");
            await session.SaveChangesAsync();
        }

        await using (var session = _sessionFactory.OpenSession())
        {
            var first = await session.LoadAsync<TestAggregate>(id);
            first!.Rename("Updated");

            var second = await session.LoadAsync<TestAggregate>(id);

            Assert.Equal("Updated", second!.Name);
        }
    }

    // =========================================================================
    // Saving Behavior
    // =========================================================================

    [Fact]
    public async Task SaveChangesAsync_persists_new_aggregate()
    {
        var id = Guid.NewGuid().ToString();

        await using (var session = _sessionFactory.OpenSession())
        {
            var aggregate = await session.LoadOrCreateAsync<TestAggregate>(id);
            aggregate.Create(id, "New Aggregate");
            await session.SaveChangesAsync();
        }

        // Verify in new session
        await using (var session = _sessionFactory.OpenSession())
        {
            var loaded = await session.LoadAsync<TestAggregate>(id);
            Assert.NotNull(loaded);
            Assert.Equal("New Aggregate", loaded.Name);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_persists_changes_to_existing_aggregate()
    {
        var id = Guid.NewGuid().ToString();

        // Create
        await using (var session = _sessionFactory.OpenSession())
        {
            var aggregate = await session.LoadOrCreateAsync<TestAggregate>(id);
            aggregate.Create(id, "Original");
            await session.SaveChangesAsync();
        }

        // Modify
        await using (var session = _sessionFactory.OpenSession())
        {
            var aggregate = await session.LoadAsync<TestAggregate>(id);
            aggregate!.Rename("Modified");
            await session.SaveChangesAsync();
        }

        // Verify
        await using (var session = _sessionFactory.OpenSession())
        {
            var loaded = await session.LoadAsync<TestAggregate>(id);
            Assert.Equal("Modified", loaded!.Name);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_persists_multiple_aggregates_together()
    {
        var id1 = Guid.NewGuid().ToString();
        var id2 = Guid.NewGuid().ToString();

        await using (var session = _sessionFactory.OpenSession())
        {
            var agg1 = await session.LoadOrCreateAsync<TestAggregate>(id1);
            var agg2 = await session.LoadOrCreateAsync<TestAggregate>(id2);

            agg1.Create(id1, "First");
            agg2.Create(id2, "Second");

            await session.SaveChangesAsync();
        }

        // Verify both
        await using (var session = _sessionFactory.OpenSession())
        {
            var loaded1 = await session.LoadAsync<TestAggregate>(id1);
            var loaded2 = await session.LoadAsync<TestAggregate>(id2);

            Assert.Equal("First", loaded1!.Name);
            Assert.Equal("Second", loaded2!.Name);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_does_nothing_when_no_changes()
    {
        var id = Guid.NewGuid().ToString();

        await using (var session = _sessionFactory.OpenSession())
        {
            var aggregate = await session.LoadOrCreateAsync<TestAggregate>(id);
            aggregate.Create(id, "Test");
            await session.SaveChangesAsync();
        }

        // Load but don't modify
        await using (var session = _sessionFactory.OpenSession())
        {
            var aggregate = await session.LoadAsync<TestAggregate>(id);
            
            Assert.False(session.HasChanges);
            await session.SaveChangesAsync(); // Should not throw
        }
    }

    // =========================================================================
    // HasChanges Behavior
    // =========================================================================

    [Fact]
    public async Task HasChanges_is_false_initially()
    {
        await using var session = _sessionFactory.OpenSession();

        Assert.False(session.HasChanges);
    }

    [Fact]
    public async Task HasChanges_is_false_after_loading_without_modifications()
    {
        var id = Guid.NewGuid().ToString();

        await using (var session = _sessionFactory.OpenSession())
        {
            var aggregate = await session.LoadOrCreateAsync<TestAggregate>(id);
            aggregate.Create(id, "Test");
            await session.SaveChangesAsync();
        }

        await using (var session = _sessionFactory.OpenSession())
        {
            await session.LoadAsync<TestAggregate>(id);

            Assert.False(session.HasChanges);
        }
    }

    [Fact]
    public async Task HasChanges_is_true_after_emitting_event()
    {
        await using var session = _sessionFactory.OpenSession();

        var aggregate = await session.LoadOrCreateAsync<TestAggregate>("test");
        aggregate.Create("test", "Name");

        Assert.True(session.HasChanges);
    }

    [Fact]
    public async Task HasChanges_is_false_after_saving()
    {
        await using var session = _sessionFactory.OpenSession();

        var aggregate = await session.LoadOrCreateAsync<TestAggregate>("test");
        aggregate.Create("test", "Name");

        await session.SaveChangesAsync();

        Assert.False(session.HasChanges);
    }

    // =========================================================================
    // Concurrency Behavior
    // =========================================================================

    [Fact]
    public async Task Concurrent_modification_throws_concurrency_exception()
    {
        var id = Guid.NewGuid().ToString();

        // Create initial
        await using (var session = _sessionFactory.OpenSession())
        {
            var aggregate = await session.LoadOrCreateAsync<TestAggregate>(id);
            aggregate.Create(id, "Original");
            await session.SaveChangesAsync();
        }

        // Load in two sessions
        await using var session1 = _sessionFactory.OpenSession();
        await using var session2 = _sessionFactory.OpenSession();

        var agg1 = await session1.LoadAsync<TestAggregate>(id);
        var agg2 = await session2.LoadAsync<TestAggregate>(id);

        // Modify both
        agg1!.Rename("From Session 1");
        agg2!.Rename("From Session 2");

        // First save succeeds
        await session1.SaveChangesAsync();

        // Second save should fail
        await Assert.ThrowsAsync<AggregateException>(() => session2.SaveChangesAsync());
    }

    // =========================================================================
    // Store Behavior
    // =========================================================================

    [Fact]
    public async Task Store_tracks_externally_created_aggregate()
    {
        var id = Guid.NewGuid().ToString();

        await using (var session = _sessionFactory.OpenSession())
        {
            var aggregate = new TestAggregate();
            aggregate.Create(id, "External");

            session.Store(aggregate);
            await session.SaveChangesAsync();
        }

        // Verify
        await using (var session = _sessionFactory.OpenSession())
        {
            var loaded = await session.LoadAsync<TestAggregate>(id);
            Assert.Equal("External", loaded!.Name);
        }
    }
}

// =============================================================================
// Test Aggregate
// =============================================================================

public class TestAggregate : Aggregate
{
    public string Name { get; private set; } = "";

    public void Create(string id, string name)
    {
        Emit(new TestCreated(id, name));
    }

    public void Rename(string newName)
    {
        Emit(new TestRenamed(Id, newName));
    }

    protected override void Apply(IStoreableEvent evt)
    {
        switch (evt)
        {
            case TestCreated e:
                Id = e.Id;
                Name = e.Name;
                break;
            case TestRenamed e:
                Name = e.NewName;
                break;
        }
    }
}

public record TestCreated(string Id, string Name) : IStoreableEvent
{
    public string TimestampUtc { get; set; } = "";
}

public record TestRenamed(string Id, string NewName) : IStoreableEvent
{
    public string TimestampUtc { get; set; } = "";
}
