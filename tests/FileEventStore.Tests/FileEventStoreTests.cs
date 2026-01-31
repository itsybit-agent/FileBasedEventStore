using Xunit;
using FileEventStore;
using FileEventStore.Serialization;
using EventStore = global::FileEventStore.FileEventStore;

namespace FileEventStore.Tests;

public class FileEventStoreTests : IDisposable
{
    private readonly string _tmp;

    public FileEventStoreTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "fes-tests-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_tmp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch { }
    }

    [Fact]
    public async Task AppendAndLoadRoundTrip()
    {
        var serializer = new JsonEventSerializer();
        var store = new EventStore(_tmp, serializer, new TestClock());

        var ev = new TestEvent { Message = "hello" };
        await store.AppendAsync("stream-1", "TestStream", ev, ExpectedVersion.None);

        var loaded = await store.LoadStreamAsync("stream-1");
        Assert.Single(loaded);
        Assert.IsType<TestEvent>(loaded[0].Data);
        Assert.Equal("hello", ((TestEvent)loaded[0].Data).Message);
        Assert.Equal("TestStream", loaded[0].StreamType);
        Assert.Equal(typeof(TestEvent).AssemblyQualifiedName, loaded[0].ClrType);
    }

    [Fact]
    public async Task AppendWithoutStreamType_SetsStreamTypeToNull()
    {
        var serializer = new JsonEventSerializer();
        var store = new EventStore(_tmp, serializer, new TestClock());

        var ev = new TestEvent { Message = "hello" };
        await store.AppendAsync("stream-1", ev, ExpectedVersion.None);

        var loaded = await store.LoadStreamAsync("stream-1");
        Assert.Single(loaded);
        Assert.Null(loaded[0].StreamType);
        Assert.Equal("TestEvent", loaded[0].EventType);
    }

    [Fact]
    public async Task AppendMultipleEventsWithoutStreamType()
    {
        var serializer = new JsonEventSerializer();
        var store = new EventStore(_tmp, serializer, new TestClock());

        var events = new IStoreableEvent[]
        {
            new TestEvent { Message = "first" },
            new TestEvent { Message = "second" }
        };
        await store.AppendAsync("stream-1", events, ExpectedVersion.None);

        var loaded = await store.LoadStreamAsync("stream-1");
        Assert.Equal(2, loaded.Count);
        Assert.Equal("first", ((TestEvent)loaded[0].Data).Message);
        Assert.Equal("second", ((TestEvent)loaded[1].Data).Message);
    }

    [Fact]
    public async Task StreamVersioningAndFiles()
    {
        var serializer = new JsonEventSerializer();
        var store = new EventStore(_tmp, serializer, new TestClock());

        await store.AppendAsync("s1", new TestEvent { Message = "a" }, ExpectedVersion.None);
        await store.AppendAsync("s1", new TestEvent { Message = "b" }, ExpectedVersion.Exactly(1));

        var files = Directory.GetFiles(Path.Combine(_tmp, "streams", "s1"));
        Assert.Equal(2, files.Length);

        var current = await store.GetCurrentVersionAsync("s1");
        Assert.Equal(2, current);
    }

    [Fact]
    public async Task ConcurrencyCheck_ExpectedVersionNone_FailsIfStreamExists()
    {
        var store = new EventStore(_tmp);

        await store.AppendAsync("s1", new TestEvent { Message = "a" }, ExpectedVersion.None);

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            store.AppendAsync("s1", new TestEvent { Message = "b" }, ExpectedVersion.None));
    }

    [Fact]
    public async Task ConcurrencyCheck_ExpectedVersionExactly_FailsIfMismatch()
    {
        var store = new EventStore(_tmp);

        await store.AppendAsync("s1", new TestEvent { Message = "a" }, ExpectedVersion.None);

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            store.AppendAsync("s1", new TestEvent { Message = "b" }, ExpectedVersion.Exactly(5)));
    }

    [Fact]
    public async Task ConcurrencyCheck_ExpectedVersionAny_AlwaysSucceeds()
    {
        var store = new EventStore(_tmp);

        await store.AppendAsync("s1", new TestEvent { Message = "a" }, ExpectedVersion.Any);
        await store.AppendAsync("s1", new TestEvent { Message = "b" }, ExpectedVersion.Any);
        await store.AppendAsync("s1", new TestEvent { Message = "c" }, ExpectedVersion.Any);

        var loaded = await store.LoadStreamAsync("s1");
        Assert.Equal(3, loaded.Count);
    }

    [Fact]
    public async Task LoadStream_NonExistentStream_ReturnsEmptyList()
    {
        var store = new EventStore(_tmp);

        var loaded = await store.LoadStreamAsync("nonexistent");

        Assert.Empty(loaded);
    }

    [Fact]
    public async Task StreamExists_ReturnsTrueForExistingStream()
    {
        var store = new EventStore(_tmp);

        await store.AppendAsync("s1", new TestEvent { Message = "a" }, ExpectedVersion.None);

        Assert.True(await store.StreamExistsAsync("s1"));
        Assert.False(await store.StreamExistsAsync("s2"));
    }

    [Fact]
    public async Task ValidateStreamId_RejectsInvalidCharacters()
    {
        var store = new EventStore(_tmp);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.AppendAsync("stream/invalid", new TestEvent(), ExpectedVersion.None));
    }

    [Fact]
    public async Task ValidateStreamId_RejectsPathTraversal()
    {
        var store = new EventStore(_tmp);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.AppendAsync("../escape", new TestEvent(), ExpectedVersion.None));
    }

    [Fact]
    public async Task ClockInjection_UsesProvidedClock()
    {
        var clock = new TestClock { UtcNow = new DateTimeOffset(2025, 1, 15, 10, 30, 0, TimeSpan.Zero) };
        var store = new EventStore(_tmp, new JsonEventSerializer(), clock);

        await store.AppendAsync("s1", new TestEvent { Message = "a" }, ExpectedVersion.None);

        var loaded = await store.LoadStreamAsync("s1");
        Assert.Equal(clock.UtcNow, loaded[0].Timestamp);
    }

    [Fact]
    public async Task ClockInjection_TestClockAdvances()
    {
        var clock = new TestClock { UtcNow = new DateTimeOffset(2025, 1, 15, 10, 30, 0, TimeSpan.Zero) };
        var store = new EventStore(_tmp, new JsonEventSerializer(), clock);

        await store.AppendAsync("s1", new TestEvent { Message = "a" }, ExpectedVersion.None);
        clock.Advance(TimeSpan.FromHours(1));
        await store.AppendAsync("s1", new TestEvent { Message = "b" }, ExpectedVersion.Exactly(1));

        var loaded = await store.LoadStreamAsync("s1");
        Assert.Equal(TimeSpan.FromHours(1), loaded[1].Timestamp - loaded[0].Timestamp);
    }

    [Fact]
    public async Task EventType_IsSetFromClrTypeName()
    {
        var store = new EventStore(_tmp);

        await store.AppendAsync("s1", new TestEvent { Message = "a" }, ExpectedVersion.None);

        var loaded = await store.LoadStreamAsync("s1");
        Assert.Equal("TestEvent", loaded[0].EventType);
    }

    [Fact]
    public async Task ClrType_IsSetFromAssemblyQualifiedName()
    {
        var store = new EventStore(_tmp);

        await store.AppendAsync("s1", new TestEvent { Message = "a" }, ExpectedVersion.None);

        var loaded = await store.LoadStreamAsync("s1");
        Assert.Equal(typeof(TestEvent).AssemblyQualifiedName, loaded[0].ClrType);
    }

    [Fact]
    public async Task MultipleEventTypes_DeserializeCorrectly()
    {
        var store = new EventStore(_tmp);

        await store.AppendAsync("s1", new TestEvent { Message = "a" }, ExpectedVersion.None);
        await store.AppendAsync("s1", new AnotherEvent { Value = 42 }, ExpectedVersion.Exactly(1));

        var loaded = await store.LoadStreamAsync("s1");
        Assert.IsType<TestEvent>(loaded[0].Data);
        Assert.IsType<AnotherEvent>(loaded[1].Data);
        Assert.Equal(42, ((AnotherEvent)loaded[1].Data).Value);
    }
}

// Test event types
public class TestEvent : IStoreableEvent
{
    public string Message { get; set; } = "";
    public string TimestampUtc { get; set; } = "";
}

public class AnotherEvent : IStoreableEvent
{
    public int Value { get; set; }
    public string TimestampUtc { get; set; } = "";
}
