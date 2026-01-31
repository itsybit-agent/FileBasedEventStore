using Xunit;
using FileEventStore;
using FileEventStore.Serialization;
using FileEventStore.Aggregates;
using Microsoft.Extensions.DependencyInjection;
using EventStore = global::FileEventStore.FileEventStore;

namespace FileEventStore.Tests;

public class ServiceCollectionExtensionsTests : IDisposable
{
    private readonly string _tmp;

    public ServiceCollectionExtensionsTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "fes-di-tests-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_tmp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch { }
    }

    [Fact]
    public void AddFileEventStore_RegistersAllServices()
    {
        var services = new ServiceCollection();

        services.AddFileEventStore(_tmp);

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IEventSerializer>());
        Assert.NotNull(provider.GetService<IClock>());
        Assert.NotNull(provider.GetService<IEventStore>());
    }

    [Fact]
    public void AddFileEventStore_RegistersCorrectImplementations()
    {
        var services = new ServiceCollection();

        services.AddFileEventStore(_tmp);

        var provider = services.BuildServiceProvider();

        Assert.IsType<JsonEventSerializer>(provider.GetService<IEventSerializer>());
        Assert.IsType<SystemClock>(provider.GetService<IClock>());
        Assert.IsType<EventStore>(provider.GetService<IEventStore>());
    }

    [Fact]
    public void AddFileEventStore_WithOptions_UsesCustomClock()
    {
        var services = new ServiceCollection();
        var customClock = new TestClock { UtcNow = new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero) };

        services.AddFileEventStore(options =>
        {
            options.RootPath = _tmp;
            options.Clock = customClock;
        });

        var provider = services.BuildServiceProvider();

        Assert.Same(customClock, provider.GetService<IClock>());
    }

    [Fact]
    public void AddFileEventStore_WithOptions_ThrowsIfRootPathNotSet()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() =>
            services.AddFileEventStore(options => { }));
    }

    [Fact]
    public async Task AddFileEventStore_EventStoreIsFullyFunctional()
    {
        var services = new ServiceCollection();
        services.AddFileEventStore(_tmp);

        var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IEventStore>();

        await store.AppendAsync("test-stream", new TestEvent { Message = "hello" }, ExpectedVersion.None);

        var loaded = await store.LoadStreamAsync("test-stream");
        Assert.Single(loaded);
        Assert.Equal("hello", ((TestEvent)loaded[0].Data).Message);
    }

    [Fact]
    public void AddFileEventStore_ReturnsServiceCollectionForChaining()
    {
        var services = new ServiceCollection();

        var result = services.AddFileEventStore(_tmp);

        Assert.Same(services, result);
    }
}
