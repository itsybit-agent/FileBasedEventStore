using System.IO;
using Microsoft.Extensions.DependencyInjection;
using FileEventStore;
using FileEventStore.Aggregates;

var services = new ServiceCollection();

// Single line to register all FileEventStore dependencies
services.AddFileEventStore(Path.Combine(Directory.GetCurrentDirectory(), "data"));

var provider = services.BuildServiceProvider();

var store = provider.GetRequiredService<IEventStore>();

await Run();

async Task Run()
{
    var ev = new OrderCreated("order-1", 19.99m);

    // Uses simplified overload - no streamType needed
    // EventType and ClrType are resolved automatically from the CLR type
    await store.AppendAsync("orders-order-1", ev, ExpectedVersion.Any);

    var stored = await store.LoadStreamAsync("orders-order-1");
    Console.WriteLine($"Loaded {stored.Count} events");
    Console.WriteLine($"Event type: {stored[0].EventType}");
    Console.WriteLine($"Order ID: {((OrderCreated)stored[0].Data).OrderId}");
}

// Events implement IStoreableEvent as a marker interface
public record OrderCreated(string OrderId, decimal Amount) : IStoreableEvent;

public class Order : Aggregate
{
    protected override void Apply(object evt)
    {
        // Handle events here
    }
}