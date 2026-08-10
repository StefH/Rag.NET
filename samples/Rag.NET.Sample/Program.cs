using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using OpenAI.Embeddings;
using Rag.NET.Abstractions;
using Rag.NET.AzureAISearch;
using Rag.NET.Chunking;
using Rag.NET.DataProviders;
using Rag.NET.DependencyInjection;
using Rag.NET.Models;
using Rag.NET.Models.Options;
using Rag.NET.Parsers.Pdf;
using System.Runtime.CompilerServices;
using ZeroAlloc.Results;

AzureOpenAIClient azureClient = new(
    new Uri(Environment.GetEnvironmentVariable("AZURE_OPENAI_URL2")!),
    new AzureKeyCredential(Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY2")!));

IChatClient chatClient = azureClient.GetChatClient("gpt-5")
    .AsIChatClient();

var services = new ServiceCollection();
services.AddChatClient(chatClient);

EmbeddingClient embeddingClient = azureClient.GetEmbeddingClient("text-embedding-3-small");

services.AddEmbeddingGenerator(embeddingClient.AsIEmbeddingGenerator());

services.AddSingleton<IPromptObserver, PromptDump>();

// Configure Rag.NET
services
    .AddRagNet(static rag => rag
        .UseChunkingStrategy<RecursiveChunkingStrategy>(static options =>
        {
            //options.MaxChunkSize = 1000;
            //options.Overlap = 50;
        })
        .UseAzureAISearch(
            new Uri(Environment.GetEnvironmentVariable("AZURE_AI_SEARCH_URL")!),
            //"field-guide-to-data-science",
            "combined-index",
            new AzureKeyCredential(Environment.GetEnvironmentVariable("AZURE_AI_SEARCH_KEY")!)
        )
        .AddPdfParser()
    );

var provider = services.BuildServiceProvider();
var vectorStore = (AzureAISearchVectorStore) provider.GetRequiredService<IVectorStore>();
await vectorStore.InitializeAsync();

var pipeline = provider.GetRequiredService<IRagPipeline>();

var progress = new Progress<IngestionProgress>(static p => Console.WriteLine($"{p.DocumentId} {p.Stage} {p.Message}"));

//var metadata = new DocumentMetadata
//{
//    DocumentId = new DocumentId("field-guide-to-data-science"),
//    FileName = "2015-field-guide-to-data-science-160211215115.pdf",
//    ContentType = "application/pdf",
//    CreatedAt = DateTime.Now,
//    UpdatedAt = DateTime.Now
//};

//await using var stream = File.OpenRead(@"c:\users\stefheyenrath\downloads\2015-field-guide-to-data-science-160211215115.pdf");

//var result = await pipeline.IngestAsync(stream, metadata, progress: progress);
//if (result.IsSuccess)
//{
//    Console.WriteLine($"Stored {result.Value.ChunksStored} chunks");
//}
//else
//{
//    Console.WriteLine($"Ingestion failed: {result.Error}");
//}


var myProvider = new FileContentProvider();

var baseMetadata = new DocumentMetadata
{
    DocumentId = new DocumentId("dummy"),
    FileName = "dummy.pdf",
    ContentType = "application/pdf"
};

var result = await pipeline.IngestFromProviderAsync(myProvider, new ProviderId("combined"),
    hashStore: new MyContentHashStore(true),
    progress: progress,
    baseMetadata: baseMetadata,
    cleanupMode: CleanupMode.Full);
Console.WriteLine($"Ingested: {result.Ingested}, Skipped: {result.Skipped}, Deleted: {result.Deleted}");

var o = new RagOptions
{
    SystemPrompt =
    """
        You are a helpful assistant that answers questions based on the provided context.
        When you cannot give a good answer based on the sources, return 'I cannot find any relevant information.'
    """,

    TopK = 5,
    MinScore = 0.1,
    UseHybridSearch = true,
    //Temperature = 0.4f
};


var azureResponse0 = await pipeline.AskAsync("Explain why the moon appears to change shape.", o);
Console.WriteLine("\r\n" + azureResponse0.Answer);


//if (false)
//{
//    var response0 = await pipeline.AskAsync("What is my address?", o);
//    Console.WriteLine("\r\n" + response0.Answer);

//    var response1 = await pipeline.AskAsync("What are fractals?");
//    Console.WriteLine("\r\n" + response1.Answer);
//    // Fractals are mathematical sets characterized by self-similar patterns: as you zoom in, the same patterns reappear at smaller scales.
//    // A common analogy is a stalk of broccoli, where each piece resembles the whole. [Source 1]

//    var response2 = await pipeline.AskAsync("What is the advice stage of data maturity?", o);
//    Console.WriteLine("\r\n" + response2.Answer);
//    // The Advise stage is the most mature stage in the data science maturity model.
//    // It's where analytics are conducted with the explicit intent to produce an output that advises-delivering true insights that drive decisions and competitive advantage.
//    // Few organizations operate at this level, and reaching it requires robust processes, people, culture, and an operating model, progressing through earlier stages (Collect, Describe, Discover, Predict) toward Advise.
//    // [Source 1], [Source 2], [Source 4], [Source 5]

//    var response3 = await pipeline.AskAsync("What is the collect stage of data maturity?", o);
//    Console.WriteLine("\r\n" + response3.Answer);
//    // The Collect stage is the early data maturity phase that begins when an organization decides to build a data science capability.
//    // Most effort is devoted to identifying what data exists or is needed and aggregating it-often focusing on collecting internal data (e.g., gathering sales records).
//    // As the organization matures, the proportion of effort spent on Collect declines but never disappears, since new questions and data sources require ongoing aggregation and preparation.
//    // [Source 1], [Source 2], [Source 3]

//    var response4 = await pipeline.AskAsync("What are examples of good data science teams?", o);
//    Console.WriteLine("\r\n" + response4.Answer);
//}

internal sealed class PromptDump : IPromptObserver
{
    public void OnPromptAssembled(IReadOnlyList<ChatMessage> messages)
    {
        foreach (var m in messages)
        {
            Console.WriteLine($"[{m.Role}] {m.Text}");
        }
    }
}

internal sealed class FileContentProvider : IFileContentProvider
{
    public async IAsyncEnumerable<Result<FileEntry, RagError>> GetFilesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var fileEntry1 = new FileEntry(
                new EntryId("Azure_Developer_Guide"),
                "Azure_Developer_Guide_eBook.pdf",
                static async ct =>
                {
                    var stream = File.OpenRead(@"c:\users\stefheyenrath\downloads\Azure_Developer_Guide_eBook.pdf");
                    return await Task.FromResult(stream);
                },
                ETag: "v2"
            );

        var fileEntry2 = new FileEntry(
            new EntryId("field-guide-to-data-science"),
            "2015-field-guide-to-data-science-160211215115.pdf",
            async ct =>
            {
                var stream = File.OpenRead(@"c:\users\stefheyenrath\downloads\2015-field-guide-to-data-science-160211215115.pdf");
                return await Task.FromResult(stream);
            },
            ETag: "v1"
        );        

        yield return Result<FileEntry, RagError>.Success(fileEntry1);
        yield return Result<FileEntry, RagError>.Success(fileEntry2);
    }
}

internal sealed class MyContentHashStore(bool hasIds) : IContentHashStore
{
    public Task<IReadOnlySet<EntryId>> GetAllIdsAsync(ProviderId providerId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult((IReadOnlySet<EntryId>)new HashSet<EntryId> { new EntryId("Azure_Developer_Guide"), new EntryId("field-guide-to-data-science") });
    }

    public Task<string?> GetETagAsync(ProviderId providerId, EntryId entryId, CancellationToken cancellationToken = default)
    {
        if (!hasIds)
        {
            return Task.FromResult<string?>("v0");
        }

        if (string.Equals(entryId.Value, "Azure_Developer_Guide", StringComparison.OrdinalIgnoreCase)) {
            return Task.FromResult<string?>("v2");
        }

        if (string.Equals(entryId.Value, "field-guide-to-data-science", StringComparison.OrdinalIgnoreCase)) {
            return Task.FromResult<string?>("v1");
        }

        throw new NotSupportedException();
    }

    public Task<string?> GetHashAsync(ProviderId providerId, EntryId entryId, CancellationToken cancellationToken = default)
    {
        if (string.Equals(entryId.Value, "Azure_Developer_Guide", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<string?>("hash1");
        }

        if (string.Equals(entryId.Value, "field-guide-to-data-science", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<string?>("hash2");
        }

        throw new NotSupportedException();
    }

    public Task RemoveAsync(ProviderId providerId, EntryId entryId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task SetAsync(ProviderId providerId, EntryId entryId, string? etag, string hash, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}