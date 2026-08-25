using AgentGuard.Core.Abstractions;
using AgentGuard.Core.ChatClient;
using AgentGuard.Core.Rules.Secrets;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using OpenAI.Embeddings;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Rag.NET.Abstractions;
using Rag.NET.AzureAISearch;
using Rag.NET.Chunking;
using Rag.NET.DataProviders;
using Rag.NET.DataProviders.Web;
using Rag.NET.DependencyInjection;
using Rag.NET.Evaluation;
using Rag.NET.Evaluation.Ragas;
using Rag.NET.Models;
using Rag.NET.Models.Options;
using Rag.NET.Parsers.Html;
using Rag.NET.Pipeline;
using Rag.NET.Sample.Web;
using Rag.NET.Telemetry;

//ChatMessage lastUserMessage = [messages].LastOrDefault((Microsoft.Extensions.AI.ChatMessage m) => m.Role == ChatRole.User);

var ss = await new SecretsDetectionRule(new SecretsDetectionOptions { Action = SecretAction.Redact }).EvaluateAsync(new GuardrailContext
{
    Text = "Hi ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqr== XXXX",
    Phase = GuardrailPhase.Input
});

AzureOpenAIClient azureClient = new(
    new Uri(Environment.GetEnvironmentVariable("AZURE_OPENAI_URL2")!),
    new AzureKeyCredential(Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY2")!));

IChatClient azureChatClient = azureClient.GetChatClient("gpt-4.1")
    .AsIChatClient()
    .AsLoggingChatClient();

var guardedClient = azureChatClient
    .UseAgentGuard(g => g
    // Layer 1
    .NormalizeInput()              // decode base64/hex/unicode evasion tricks
    .DetectSecrets(SecretAction.Redact)               // block API keys, tokens, connection strings
    .LimitInputTokens(2000)
    .BlockPromptInjection()        // regex-based injection detection
    

    // Layer 2
    //.RedactPii(piio)
    //.BlockPromptInjectionWithLlm(llmJudgeClient)
    //.EnforceTopicBoundaryWithLlm(llmJudgeClient, "uitkering", "pensioen")

    //.AddRule(new PiiRule2(piio, analyzer: engine))
    .GuardRetrieval()              // filter poisoned RAG chunks

    .OnViolation(v => v.RejectWithMessage("Ik kan alleen helpen met pensioen vragen."))

//.GuardToolCalls()              // inspect tool call arguments for injection
//.GuardToolResults()            // detect indirect injection in tool results
);

var services = new ServiceCollection();
services.AddChatClient(guardedClient);

EmbeddingClient embeddingClient = azureClient.GetEmbeddingClient("text-embedding-3-small");
var embeddingGenerator = embeddingClient.AsIEmbeddingGenerator();

services.AddEmbeddingGenerator(embeddingGenerator);

services.AddSingleton<IPromptObserver, PromptDump>();

const string name = "bouw";
const string indexName = "bouw-index";
const string url = "https://www.bpfbouw.nl/sitemap.xml";

// Configure Rag.NET
services.AddRagNetInstrumentation()
    .WithLogging()
    .WithTracing(t => t.AddConsoleExporter(options => options.Targets = OpenTelemetry.Exporter.ConsoleExporterOutputTargets.Console))
    .WithMetrics(m => m.AddConsoleExporter(options => options.Targets = OpenTelemetry.Exporter.ConsoleExporterOutputTargets.Console));

services
    .AddRagNet(name, static rag => rag
        .UseChunkingStrategy<RecursiveChunkingStrategy>(static options =>
        {
            options.MaxChunkSize = 1000;
            options.Overlap = 100;
        })
        .UseAzureAISearch(
            endpoint: new Uri(Environment.GetEnvironmentVariable("AZURE_AI_SEARCH_URL")!),
            indexName: indexName,
            credential: new AzureKeyCredential(Environment.GetEnvironmentVariable("AZURE_AI_SEARCH_KEY")!)
        )
        .AddHtmlParser()
    );

services.AddClass1Cache<Class1>();


var provider = services.BuildServiceProvider();

var cla = provider.GetRequiredService<IClass1>();
cla.X();
cla.X();

var pipelineFactory = provider.GetRequiredService<IRagPipelineFactory>();
var pipeline = (RagPipeline) pipelineFactory.Get(name);

var vectorStore = (AzureAISearchVectorStore)pipelineFactory.GetVectorStore(name);
await vectorStore.InitializeAsync();



var progress = new Progress<IngestionProgress>(static p => Console.WriteLine($"{p.DocumentId} {p.Stage} {p.Message}"));

var httpClient = new HttpClient();
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
var mySiteMap = new MySitemapDataProvider(url, httpClient, excludedUrls);

var baseMetadata = new DocumentMetadata
{
    DocumentId = new DocumentId(name),
    FileName = $"{name}.html",
    ContentType = "text/html"
};

var hashStorePath = $@"c:\users\stefheyenrath\downloads\{indexName}.json";
var hashStore = new JsonFileContentHashStore(hashStorePath);

var result = await pipeline.IngestFromProviderAsync(mySiteMap, new ProviderId(name),
    hashStore: hashStore,
    progress: progress,
    baseMetadata: baseMetadata,
    cleanupMode: CleanupMode.Full);
Console.WriteLine($"Ingested: {result.Ingested}, Skipped: {result.Skipped}, Deleted: {result.Deleted}");
foreach (var error in result.Errors)
{
    Console.WriteLine($"Error: {error}");
}

var o = new RagOptions
{
    SystemPrompt =
    $"""
        Je bent een behulpzame assistent die vragen beantwoordt.

        Volg deze richtlijnen bij het beantwoorden van vragen:
        - antwoord in het Nederlands
        - Gebruik Taalniveau CEFR B1/B2. 
        - Geef duidelijke en beknopte antwoorden.
        - gebruik alleen verstrekte bronnen
        - Zet geen bronnen in het antwoord.
        - Als er een link in de bron staat zoals (/pensioen-bij-abp/pensioenreglement/uw-keuzes-als-u-met-pensioen-gaat), vervang deze dan door LINK_1, LINK_2, enzovoort.
        - Zet onderaan het antwoord een lijst van de gebruikte links met de echte complete URL. Bijvoorbeeld: 
          Links:
           - LINK_1: {url}/pensioen-bij-abp/pensioenreglement/uw-keuzes-als-u-met-pensioen-gaat
           - LINK_2: {url}/uw-situatie-verandert/relatie-en-prive/uit-elkaar-gaan
        - Wanneer je geen goed antwoord kunt geven op basis van de bronnen, geef dan 'Ik kan geen relevante informatie vinden.'
    """,
    TopK = 10,
    MinScore = 0.7,
    UseHybridSearch = true,
    //Temperature = 0.4f
};

//var vraag = "Mijn partner en ik gaan uit elkaar. Wat is er dan met mijn pensioen, waar moet ik rekening mee houden?";
var vraag = "Hello ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqr== !";
var ragResponse = await pipeline.AskAsync(vraag, o);

Console.WriteLine($"{new string('-', 80)}\r\n{ragResponse.Answer}");

var sources = ragResponse.Sources
    .Select(static (x, index) => new { Index = index + 1, x.Chunk })
    .ToDictionary(static x => $"[Bron {x.Index}]", static x => x.Chunk.Metadata["url"].StringValue, StringComparer.OrdinalIgnoreCase);


Console.WriteLine("\r\n");
Console.WriteLine("\r\n");

sources.Values.Distinct(StringComparer.OrdinalIgnoreCase)
    .ToList()
    .ForEach(url => Console.WriteLine($"URL: {url}"));

var suite = new RagasEvaluationSuiteBuilder(azureChatClient, embeddingGenerator)
    .AddFaithfulness()
    .AddAnswerRelevance()
    .AddContextPrecision()
    .AddContextRecall()
    .Build();

var samples = new[]
{
    new EvaluationSample
    (
        Question:        vraag,
        PredictedAnswer: ragResponse.Answer,
        ReferenceAnswer:
        """
        Als jij en je partner uit elkaar gaan, heeft dit gevolgen voor je pensioen. Je ouderdomspensioen en het partnerpensioen kunnen anders verdeeld worden. Je ex-partner kan recht hebben op een deel van het ouderdomspensioen en het partnerpensioen dat is opgebouwd tijdens de relatie.
        Als jullie getrouwd waren, een geregistreerd partnerschap hadden, of samenwoonden, dan krijgt je ex-partner partnerpensioen als jij overlijdt. Dit geldt voor het bedrag dat was opgebouwd tot het moment dat jullie uit elkaar gingen. Jullie kunnen er samen voor kiezen dat je ex-partner geen recht krijgt op het partnerpensioen, maar dan moeten jullie dit samen schriftelijk regelen en doorgeven.
        Als jullie het pensioen hebben gesplitst bij de scheiding, krijgt je ex-partner een eigen pensioen. In dat geval verandert er niets aan het partnerpensioen als jij overlijdt.
        Kortom: je moet rekening houden met een mogelijke verdeling van het ouderdomspensioen en het recht van je ex-partner op partnerpensioen, tenzij jullie samen anders afspreken.
        """,
        SourceChunks: ragResponse.Sources.Select(static s => s.Chunk.Text).ToList()
    ),
};

var report = await suite.EvaluateAsync(samples);

Console.WriteLine($"OverallScore score: {report.OverallScore:F4}");

Console.WriteLine($"Faithfulness: {report.Faithfulness:F2}");

Console.WriteLine($"ContextPrecision: {report.ContextPrecision:F2}");

Console.WriteLine($"ContextRecall: {report.ContextRecall:F2}");

Console.WriteLine($"AnswerRelevance: {report.AnswerRelevance:F2}");

/*
OverallScore score: 0,8901
Faithfulness: 1,00
ContextPrecision: 0,78
ContextRecall: 1,00
AnswerRelevance: 0,78

Die andere geeft:
Faithfulness: 85
Relevance:    90
Correctness:  95
*/


//var replaced = azureResponse0.Answer;

//int idx = 1;
//foreach (var source in azureResponse0.Sources)
//{
//    replaced = replaced.Replace($"[Bron {idx++}]", source.Chunk.Metadata["url"].StringValue, StringComparison.OrdinalIgnoreCase);
//}

var messages = new List<ChatMessage>
{
    new(ChatRole.System,
        $"""
            Je bent een behulpzame assistent die een 5 mogelijk vervolgvragen teruggeeft op die een gebruiker verder nog zou kunnen stellen.
            Dit is gebaseerd op de vraag:
            ```
            {vraag}
            ```

            En het gegeven antwoord:
            ```
            {ragResponse.Answer}
            ```

            Geef de vervolgvragen in een genummerde lijst van 5 vragen, zonder verdere uitleg.
        """),
};

var x = await guardedClient.GetResponseAsync(messages);
Console.WriteLine("\r\nVervolgvragen:\r\n" + x.Text);


internal sealed class PromptDump : IPromptObserver
{
    public void OnPromptAssembled(IReadOnlyList<ChatMessage> messages)
    {
        foreach (var m in messages)
        {
            Console.WriteLine($"PromptDump [{m.Role}] {m.Text}");
        }
    }
}