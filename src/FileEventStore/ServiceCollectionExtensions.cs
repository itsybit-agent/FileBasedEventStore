using FileEventStore.Aggregates;
using FileEventStore.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace FileEventStore;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds FileEventStore services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="rootPath">The root path for event storage.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddFileEventStore(this IServiceCollection services, string? rootPath)
    {
        rootPath ??= Path.Combine(Directory.GetCurrentDirectory(), "data");
        services.AddSingleton<IEventSerializer, JsonEventSerializer>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IEventStore>(sp =>
        {
            var serializer = sp.GetRequiredService<IEventSerializer>();
            var clock = sp.GetRequiredService<IClock>();
            return new FileEventStore(rootPath, serializer, clock);
        });
        services.AddTransient(typeof(AggregateRepository<>));

        return services;
    }

    /// <summary>
    /// Adds FileEventStore services to the dependency injection container with custom configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure the options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddFileEventStore(this IServiceCollection services, Action<FileEventStoreOptions> configure)
    {
        var options = new FileEventStoreOptions();
        configure(options);

        if (string.IsNullOrWhiteSpace(options.RootPath))
            throw new ArgumentException("RootPath must be specified.", nameof(configure));

        services.AddSingleton<IEventSerializer, JsonEventSerializer>();
        services.AddSingleton<IClock>(options.Clock ?? SystemClock.Instance);
        services.AddSingleton<IEventStore>(sp =>
        {
            var serializer = sp.GetRequiredService<IEventSerializer>();
            var clock = sp.GetRequiredService<IClock>();
            return new FileEventStore(options.RootPath, serializer, clock);
        });
        services.AddTransient(typeof(AggregateRepository<>));

        return services;
    }
}

public class FileEventStoreOptions
{
    public string RootPath { get; set; } = "";
    public IClock? Clock { get; set; }
}
