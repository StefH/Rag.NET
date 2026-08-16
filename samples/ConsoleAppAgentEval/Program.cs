using AgentEval.Core;
using AgentEval.Metrics.RAG;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using OpenAI.Embeddings;
using Rag.NET.Abstractions;
using Rag.NET.AzureAISearch;
using Rag.NET.DependencyInjection;
using Rag.NET.Models.Options;


AzureOpenAIClient azureClient = new(
    new Uri(Environment.GetEnvironmentVariable("AZURE_OPENAI_URL2")!),
    new AzureKeyCredential(Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY2")!));

IChatClient chatClient = azureClient.GetChatClient("gpt-4.1")
    .AsIChatClient();

EmbeddingClient embeddingClient = azureClient.GetEmbeddingClient("text-embedding-3-small");

var services = new ServiceCollection();
services.AddEmbeddingGenerator(embeddingClient.AsIEmbeddingGenerator());
services.AddChatClient(chatClient);

services.AddSingleton<IPromptObserver, PromptDump>();

// Configure Rag.NET
services
    .AddRagNet(static rag => rag
        .UseAzureAISearch(
            new Uri(Environment.GetEnvironmentVariable("AZURE_AI_SEARCH_URL")!),
            "web-index",
            new AzureKeyCredential(Environment.GetEnvironmentVariable("AZURE_AI_SEARCH_KEY")!)
        )
    );

var o = new RagOptions
{
    SystemPrompt =
    """
        Je bent een behulpzame assistent die vragen beantwoordt.

        Volg deze richtlijnen bij het beantwoorden van vragen:
        - gebruik alleen verstrekte bronnen 
        - antwoord in het Nederlands
        - Zet geen bronnen in het antwoord.
        - Gebruik Taalniveau CEFR B1/B2. 
        - Geef duidelijke en beknopte antwoorden.
        - Als er een link in de bron staat zoals (/pensioen-bij-abp/pensioenreglement/uw-keuzes-als-u-met-pensioen-gaat), vervang deze dan door LINK_1, LINK_2, enzovoort.
        - Zet onderaan het antwoord een lijst van de gebruikte links met de echte complete URL. Bijvoorbeeld: 
          Links:
           - LINK_1: https://www.abp.nl/pensioen-bij-abp/pensioenreglement/uw-keuzes-als-u-met-pensioen-gaat
           - LINK_2: https://www.abp.nl/uw-situatie-verandert/relatie-en-prive/uit-elkaar-gaan
        - Wanneer je geen goed antwoord kunt geven op basis van de bronnen, geef dan 'Ik kan geen relevante informatie vinden.'
    """,

    TopK = 10,
    MinScore = 0.7,
    UseHybridSearch = true,
    //Temperature = 0.4f
};

var provider = services.BuildServiceProvider();
var pipeline = provider.GetRequiredService<IRagPipeline>();

var question = "Mijn partner en ik gaan uit elkaar. Wat is er dan met mijn pensioen, waar moet ik rekening mee houden?";
var ragResponse = await pipeline.AskAsync(question, o);

var context = new EvaluationContext
{
    Input = question,
    Output = ragResponse.Answer,
    Context = string.Join("\r\n", ragResponse.Sources.Select(s => s.Chunk.Text)),
    GroundTruth =
        """
        Als jij en je partner uit elkaar gaan, heeft dit gevolgen voor je pensioen. Je ouderdomspensioen en het partnerpensioen kunnen anders verdeeld worden. Je ex-partner kan recht hebben op een deel van het ouderdomspensioen en het partnerpensioen dat is opgebouwd tijdens de relatie.
        Als jullie getrouwd waren, een geregistreerd partnerschap hadden, of samenwoonden, dan krijgt je ex-partner partnerpensioen als jij overlijdt. Dit geldt voor het bedrag dat was opgebouwd tot het moment dat jullie uit elkaar gingen. Jullie kunnen er samen voor kiezen dat je ex-partner geen recht krijgt op het partnerpensioen, maar dan moeten jullie dit samen schriftelijk regelen en doorgeven.
        Als jullie het pensioen hebben gesplitst bij de scheiding, krijgt je ex-partner een eigen pensioen. In dat geval verandert er niets aan het partnerpensioen als jij overlijdt.
        Kortom: je moet rekening houden met een mogelijke verdeling van het ouderdomspensioen en het recht van je ex-partner op partnerpensioen, tenzij jullie samen anders afspreken.
        """,
};

var faithfulness = await new FaithfulnessMetric(chatClient).EvaluateAsync(context).ConfigureAwait(false);

var relevance = await new RelevanceMetric(chatClient).EvaluateAsync(context).ConfigureAwait(false);

var correctness = await new AnswerCorrectnessMetric(chatClient).EvaluateAsync(context).ConfigureAwait(false);

Console.WriteLine($"Question:     {question}");
Console.WriteLine($"Answer:       {ragResponse.Answer}");
Console.WriteLine($"Faithfulness: {faithfulness.Score}");
Console.WriteLine($"Relevance:    {relevance.Score}");
Console.WriteLine($"Correctness:  {correctness.Score}");
/*
Faithfulness: 85
Relevance:    90
Correctness:  95
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