using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rag.NET.Abstractions;

namespace Rag.NET.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRagNet(
        this IServiceCollection services,
        string name,
        Action<RagBuilder>? configure = null,
        Action<IngestionPipelineBuilder>? ingestion = null,
        Action<RetrievalPipelineBuilder>? retrieval = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);

        services.TryAddSingleton<IRagPipelineFactory, RagPipelineFactory>();

        var snapshot = services
            .Where(static d => d.ServiceType != typeof(IRagPipelineFactory)
                               && d.ServiceType != typeof(NamedRagRegistration))
            .ToArray();

        services.AddSingleton(new NamedRagRegistration(name, snapshot, configure, ingestion, retrieval));

        return services;
    }
}

public interface IRagPipelineFactory
{
    IRagPipeline Get(string name);

    IVectorStore GetVectorStore(string name);
}

internal sealed class RagPipelineFactory(IEnumerable<NamedRagRegistration> registrations) : IRagPipelineFactory, IDisposable
{
    private readonly Dictionary<string, Lazy<NamedPipeline>> _pipelines = BuildPipelineMap(registrations);

    public IRagPipeline Get(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (_pipelines.TryGetValue(name, out var lazyPipeline))
        {
            return lazyPipeline.Value.Pipeline;
        }

        throw new InvalidOperationException($"No RAG pipeline named '{name}' is registered.");
    }

    public IVectorStore GetVectorStore(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (_pipelines.TryGetValue(name, out var lazyPipeline))
        {
            return lazyPipeline.Value.VectorStore;
        }

        throw new InvalidOperationException($"No RAG pipeline named '{name}' is registered.");
    }

    public void Dispose()
    {
        foreach (var pair in _pipelines.Values)
        {
            if (pair.IsValueCreated)
            {
                pair.Value.Provider.Dispose();
            }
        }
    }

    private static Dictionary<string, Lazy<NamedPipeline>> BuildPipelineMap(IEnumerable<NamedRagRegistration> registrations)
    {
        var map = new Dictionary<string, Lazy<NamedPipeline>>(StringComparer.Ordinal);

        foreach (var registration in registrations)
        {
            if (!map.TryAdd(registration.Name,
                    new Lazy<NamedPipeline>(() => BuildNamedPipeline(registration),
                        LazyThreadSafetyMode.ExecutionAndPublication)))
            {
                throw new InvalidOperationException($"A RAG pipeline named '{registration.Name}' is already registered.");
            }
        }

        return map;
    }

    private static NamedPipeline BuildNamedPipeline(NamedRagRegistration registration)
    {
        var innerServices = new ServiceCollection();

        foreach (var descriptor in registration.ServiceSnapshot)
        {
            innerServices.Add(descriptor);
        }

        innerServices.AddRagNet(registration.Configure, registration.Ingestion, registration.Retrieval);

        var provider = innerServices.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IRagPipeline>();
        var vectorStore = provider.GetRequiredService<IVectorStore>();

        return new NamedPipeline(provider, pipeline, vectorStore);
    }

    private sealed record NamedPipeline(ServiceProvider Provider, IRagPipeline Pipeline, IVectorStore VectorStore);
}

internal sealed record NamedRagRegistration(
    string Name,
    IReadOnlyList<ServiceDescriptor> ServiceSnapshot,
    Action<RagBuilder>? Configure,
    Action<IngestionPipelineBuilder>? Ingestion,
    Action<RetrievalPipelineBuilder>? Retrieval);
