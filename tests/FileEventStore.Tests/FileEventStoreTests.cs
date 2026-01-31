using Xunit;
using FileEventStore.Serialization;
using System.IO;
using System.Threading.Tasks;
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
        var store = new FileEventStore.FileEventStore(_tmp, serializer, new TestClock());

        var ev = new SimpleEvent { Message = "hello" };
        await store.AppendAsync("stream-1", "TestStream", ev, ExpectedVersion.None);

        var loaded = await store.LoadStreamAsync("stream-1");
        Assert.Single(loaded);
        Assert.IsType<SimpleEvent>(loaded[0].Data);
        Assert.Equal("hello", ((SimpleEvent)loaded[0].Data).Message);
        Assert.Equal("TestStream", loaded[0].StreamType);
        Assert.Equal(typeof(SimpleEvent).AssemblyQualifiedName, loaded[0].ClrType);
    }


    [Fact]
    public async Task StreamVersioningAndFiles()
    {
        var serializer = new JsonEventSerializer();
        var store = new FileEventStore.FileEventStore(_tmp, serializer, new TestClock());

        await store.AppendAsync("s1", "TestStream", new SimpleEvent { Message = "a" }, ExpectedVersion.None);
        await store.AppendAsync("s1", "TestStream", new SimpleEvent { Message = "b" }, ExpectedVersion.Exactly(1));

        var files = Directory.GetFiles(Path.Combine(_tmp, "streams", "s1"));
        Assert.Equal(2, files.Length);

        var current = await store.GetCurrentVersionAsync("s1");
        Assert.Equal(2, current);
    }

    private class SimpleEvent { public string Message { get; set; } = ""; }
}
