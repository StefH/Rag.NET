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
using Rag.NET.Sample.Web;

AzureOpenAIClient azureClient = new(
    new Uri(Environment.GetEnvironmentVariable("AZURE_OPENAI_URL2")!),
    new AzureKeyCredential(Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY2")!));

IChatClient chatClient = azureClient.GetChatClient("gpt-4.1")
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
foreach (var error in result.Errors)
{
    Console.WriteLine($"Error: {error}");
}

var o = new RagOptions
{
    //SystemPrompt =
    //"""
    //    You are a helpful assistant that answers questions based on the provided context.
    //    When you cannot give a good answer based on the sources, return 'I cannot find any relevant information.'
    //""",

    SystemPrompt =
    """
        Je bent een behulpzame assistent die vragen beantwoordt. Maar alleen op basis van de verstrekte context in het Nederlands.
        Vertaal "[Source 1]" naar "[Bron 1]", "[Source 2]" naar "[Bron 2]", enzovoort. En zet de bronnen in een lijst.
        Gebruik Taalniveau CEFR B1/B2. Geef duidelijke en beknopte antwoorden.
        Wanneer je geen goed antwoord kunt geven op basis van de bronnen, geef dan 'Ik kan geen relevante informatie vinden.'
    """,

    TopK = 5,
    MinScore = 0.7,
    UseHybridSearch = true,
    //Temperature = 0.4f
};

var vraag = "Ik ben 66 en kan volgend jaar met pensioen, maar mijn partner pas over 5 jaar, wat is handig om te doen in mijn situatie?";
var azureResponse0 = await pipeline.AskAsync(vraag, o);
/*
 * Op basis van de bronnen is het handig om te weten dat je bij pensioenstart keuzes kunt maken:

- Als je geen partner hebt, kun je het partnerpensioen ruilen voor een hoger ouderdomspensioen.
- Heb je wel een partner, dan kun je mogelijk ouderdomspensioen ruilen voor een hoger partnerpensioen wanneer jouw pensioen ingaat.
- Het is belangrijk om te kijken naar de datum waarop je pensioen is opgebouwd en wanneer je een partnerrelatie hebt, omdat dit invloed heeft op het recht op partnerpensioen. Bijvoorbeeld: als je pensioenopbouw vóór 1 januari 2015 is gestopt en de partnerrelatie is gestart na je 65e, kan je partner géén recht hebben op partnerpensioen bij jouw overlijden.

Advies: Bekijk goed wanneer je pensioen hebt opgebouwd en wanneer je partnerrelatie is gestart. Je kunt ervoor kiezen het partnerpensioen te verhogen als je wilt dat je partner later meer inkomen krijgt. Neem bij twijfel contact op met ABP en bekijk samen de beste optie voor jullie situatie.

Bronnen:
https://www.abp.nl/pensioen-bij-abp/pensioenreglement/meer-of-minder-pensioen
https://www.abp.nl/pensioen-bij-abp/pensioenreglement/overgangsbepalingen/partnerpensioen-over-pensioenopbouw-voor-1-januari-2018-bij-overlijden-op-of-na-65-jaar
*/

Console.WriteLine();

var replaced = azureResponse0.Answer;

int idx = 1;
foreach (var source in azureResponse0.Sources)
{
    //Console.WriteLine(source.Chunk.Metadata["url"].StringValue);
    replaced = replaced.Replace($"[Bron {idx++}]", source.Chunk.Metadata["url"].StringValue, StringComparison.OrdinalIgnoreCase);
}

Console.WriteLine("\r\n" + replaced);

var messages = new List<ChatMessage>
{
    new(ChatRole.System,
        $"""
            Je bent een behulpzame assistent die een 5 mogelijk vervolgvragen teruggeeft op die een gebruiker zou kunnen stellen.
            Dit is gebaseerd op de vraag:
            ```
            {vraag}
            ```

            En het gegeven antwoord:
            ```
            {replaced}
            ```

            Geef de vervolgvragen in een genummerde lijst van 5 vragen, zonder verdere uitleg.
        """),
    //new(ChatRole.User, "Explain dependency injection in .NET.")
};

var x = await chatClient.GetResponseAsync(messages);
Console.WriteLine("\r\nVervolgvragen:\r\n" + x.Text);


// -- 50 pages of ABP.nl
/*
In jouw situatie heb je de mogelijkheid om bij je pensioenkeuze het ouderdomspensioen en partnerpensioen te ruilen. Je kunt ervoor kiezen om ouderdomspensioen te ruilen voor een hoger partnerpensioen. Dit kan voordelig zijn als je wilt dat jouw partner meer pensioen krijgt als jij eerder overlijdt, vooral omdat je partner pas over vijf jaar met pensioen gaat. Je kunt ook kiezen om het partnerpensioen juist om te zetten in een hoger eigen ouderdomspensioen, vooral als je verwacht dat je partner later geen partnerpensioen nodig heeft.

Let op: Als je voor 1 januari 2018 in dienst was, kunnen er voor jou extra regels gelden. Het is verstandig om de overgangsbepalingen goed te bekijken die voor jou van toepassing zijn.

Samenvattend:
- Wil je een hoger partnerpensioen voor je partner? Ruil dan een deel van je ouderdomspensioen hiervoor.
- Wil je zelf meer ouderdomspensioen ontvangen? Dan kun je partnerpensioen omzetten in extra ouderdomspensioen.

Het beste overleg je jouw keuzes en situatie met het pensioenfonds of een adviseur.

Bronnen:

1. https://www.abp.nl/pensioen-bij-abp/pensioenreglement/meer-of-minder-pensioen
2. https://www.abp.nl/pensioen-bij-abp/pensioenreglement/overgangsbepalingen/ruilen-van-ouderdomspensioen-voor-een-hoger-partnerpensioen-bij
3. https://www.abp.nl/pensioen-bij-abp/pensioenreglement/premie-en-pensioenberekeningen

Vervolgvragen:
1. Wat gebeurt er met het partnerpensioen als ik besluit eerder met pensioen te gaan dan mijn partner?
2. Hoeveel extra partnerpensioen kan ik krijgen als ik een deel van mijn ouderdomspensioen ruil?
3. Zijn er fiscale gevolgen als ik kies voor het ruilen van ouderdoms- en partnerpensioen?
4. Kan ik mijn pensioen later nog aanpassen als mijn situatie verandert, bijvoorbeeld als mijn partner eerder stopt met werken?
5. Welke overgangsbepalingen gelden precies voor mij als ik voor 1 januari 2018 in dienst was?
*/


// -- 100 pages of ABP.nl
/*
Op basis van de bronnen zijn er enkele dingen waar u rekening mee kunt houden:

- U kunt het partnerpensioen ruilen voor een hoger ouderdomspensioen of andersom. Dit kan handig zijn als u verwacht dat uw partner meer of minder inkomen nodig heeft als u met pensioen gaat. Dit regelen ze voor u op het moment dat u met pensioen gaat. U moet dan bevestigen wat uw keuze is. Let op: was u in dienst voor 1 januari 2018? Dan gelden er extra regels. Bekijk de overgangsbepalingen die voor u van toepassing zijn. https://www.abp.nl/pensioen-bij-abp/pensioenreglement/meer-of-minder-pensioen

- Uw partner komt in aanmerking voor partnerpensioen als u overlijdt op of na uw 65e, zolang aan bepaalde voorwaarden is voldaan. Bijvoorbeeld: als uw pensioenopbouw bij ABP is begonnen vóór 1 januari 2018 en u bent voor 1 januari 2015 niet gestopt met opbouwen, en uw partnerrelatie is ontstaan vóór uw 65ste, heeft uw partner recht op partnerpensioen. https://www.abp.nl/pensioen-bij-abp/pensioenreglement/overgangsbepalingen/partnerpensioen-over-pensioenopbouw-voor-1-januari-2018-bij-overlijden-op-of-na-65-jaar

Wat handig is in uw situatie, hangt af van uw financiële wensen en die van uw partner. Wilt u vooral een hoger pensioen nu, of wilt u meer zekerheid voor uw partner later? U kunt bij het pensioenmoment samen met ABP kiezen wat het beste past.

Bronnen:
1. https://www.abp.nl/pensioen-bij-abp/pensioenreglement/meer-of-minder-pensioen
2. https://www.abp.nl/pensioen-bij-abp/pensioenreglement/overgangsbepalingen/partnerpensioen-over-pensioenopbouw-voor-1-januari-2018-bij-overlijden-op-of-na-65-jaar

Vervolgvragen:
1. Hoe bereken ik wat het verschil is tussen partnerpensioen en ouderdomspensioen als ik ga ruilen?
2. Is het mogelijk om mijn pensioenuitkering te laten ingaan op het moment dat mijn partner met pensioen gaat?
3. Wat zijn de fiscale gevolgen als ik kies voor een hoger ouderdomspensioen en minder partnerpensioen?
4. Kunnen we het pensioen ook gespreid laten uitkeren over de jaren totdat mijn partner met pensioen gaat?
5. Wat gebeurt er met het partnerpensioen als mijn partner en ik niet getrouwd zijn, maar samenwonen?
*/











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
