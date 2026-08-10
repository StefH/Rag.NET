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
using Rag.NET.DataProviders.Web;
using Rag.NET.DependencyInjection;
using Rag.NET.Models;
using Rag.NET.Models.Options;
using Rag.NET.Parsers.Html;
using Rag.NET.Parsers.Pdf;
using Rag.NET.Sample.Web;

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
            options.MaxChunkSize = 2000;
            options.Overlap = 200;
        })
        .UseAzureAISearch(
            new Uri(Environment.GetEnvironmentVariable("AZURE_AI_SEARCH_URL")!),
            //"field-guide-to-data-science",
            "web-index",
            new AzureKeyCredential(Environment.GetEnvironmentVariable("AZURE_AI_SEARCH_KEY")!)
        )
        .AddHtmlParser()
    );

var provider = services.BuildServiceProvider();
var vectorStore = (AzureAISearchVectorStore)provider.GetRequiredService<IVectorStore>();
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

var httpClient = new HttpClient
{
    //BaseAddress = new Uri("https://www.abp.nl")
};
//var myProvider = new WebCrawlerDataProvider("https://www.abp.nl", httpClient, new WebCrawlerOptions
//{
//    MaxDepth = 3,
//    MaxPages = 100,
//    SameDomain = true,
//    RespectRobotsTxt = false
//});

var excludedUrls = new List<string>
{
    "https://www.abp.nl/werkgevers",
    "https://www.abp.nl/over-abp/over-de-organisatie",
    "https://www.abp.nl/over-abp/onze-financiele-situatie",
    "https://www.abp.nl/nieuws-en-pers",
    "https://www.abp.nl/contact/u-bent-het-ergens-niet-mee-eens/commissie-van-beroep",
    "https://www.abp.nl/english",
    "https://www.abp.nl/militair",
    "https://www.abp.nl/videos",
    "https://www.abp.nl/over-deze-site"
};
var mySiteMap = new MySitemapDataProvider("https://www.abp.nl/sitemap.xml", httpClient, excludedUrls);

var baseMetadata = new DocumentMetadata
{
    DocumentId = new DocumentId("dummy"),
    FileName = "dummy.pdf",
    ContentType = "text/html"
};

var hashStorePath = @"c:\users\stefheyenrath\downloads\rag-content-hashes.json";
var hashStore = new JsonFileContentHashStore(hashStorePath);

var result = await pipeline.IngestFromProviderAsync(mySiteMap, new ProviderId("web"),
    hashStore: hashStore,
    progress: progress,
    baseMetadata: baseMetadata,
    cleanupMode: CleanupMode.Full);
Console.WriteLine($"Ingested: {result.Ingested}, Skipped: {result.Skipped}, Deleted: {result.Deleted}");

var o = new RagOptions
{
    //SystemPrompt =
    //"""
    //    You are a helpful assistant that answers questions based on the provided context.
    //    When you cannot give a good answer based on the sources, return 'I cannot find any relevant information.'
    //""",

    SystemPrompt =
    """
        Je bent een behulpzame assistent die vragen beantwoordt op basis van de verstrekte context.
        Gebruik Taalniveau CEFR B1/B2. Geef duidelijke en beknopte antwoorden.
        Wanneer je geen goed antwoord kunt geven op basis van de bronnen, geef dan 'Ik kan geen relevante informatie vinden.'
    """,

    TopK = 5,
    MinScore = 0.8,
    UseHybridSearch = true,
    //Temperature = 0.4f
};


var azureResponse0 = await pipeline.AskAsync("Ik ben 66 en kan volgend jaar met pensioen, maar mijn partner pas over 5 jaar, wat is handig om te doen in mijn situatie?", o);
Console.WriteLine("\r\n" + azureResponse0.Answer);


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
