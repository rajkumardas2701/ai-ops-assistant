using AiOps.Api.Rag;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

var config = builder.Configuration;

// AI_PROVIDER selects the RAG implementations behind the IEmbeddingProvider / IChatProvider
// seams. Default "local" runs fully offline at $0; "azureopenai" uses real models.
var provider = config["AI_PROVIDER"] ?? "local";

builder.Services.Configure<AzureOpenAIOptions>(o =>
{
    o.Endpoint = config["AZURE_OPENAI_ENDPOINT"] ?? "";
    o.ApiKey = config["AZURE_OPENAI_KEY"] ?? "";
    o.ChatDeployment = config["AZURE_OPENAI_CHAT_DEPLOYMENT"] ?? "gpt-4o";
    o.EmbeddingDeployment = config["AZURE_OPENAI_EMBEDDING_DEPLOYMENT"] ?? "text-embedding-3-small";
});

if (string.Equals(provider, "azureopenai", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IEmbeddingProvider, AzureOpenAIEmbeddingProvider>();
    builder.Services.AddSingleton<IChatProvider, AzureOpenAIChatProvider>();
}
else
{
    builder.Services.AddSingleton<IEmbeddingProvider, LocalEmbeddingProvider>();
    builder.Services.AddSingleton<IChatProvider, LocalChatProvider>();
}

builder.Services.AddSingleton<InMemoryVectorStore>();
builder.Services.AddSingleton<CorpusLoader>();
builder.Services.AddSingleton<RagService>();

var host = builder.Build();

// Auto-ingest the sample corpus at startup so the demo works out of the box.
try
{
    using var scope = host.Services.CreateScope();
    var loader = scope.ServiceProvider.GetRequiredService<CorpusLoader>();
    await loader.LoadAsync();
}
catch (Exception ex)
{
    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    logger.LogError(ex, "Startup corpus ingestion failed. Call POST /api/ingest manually once configured.");
}

host.Run();
