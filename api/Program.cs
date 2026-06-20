using AiOps.Api.Budget;
using AiOps.Api.Caching;
using AiOps.Api.RateLimiting;
using AiOps.Api.Rag;
using AiOps.Api.Support;
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

// Stage 2 (1,000 users): tunable reliability/cost limits, read from environment config.
builder.Services.Configure<ServiceLimitsOptions>(o =>
{
    if (double.TryParse(config["CACHE_SIMILARITY_THRESHOLD"], out var threshold)) o.CacheSimilarityThreshold = threshold;
    if (int.TryParse(config["CACHE_TTL_MINUTES"], out var ttl)) o.CacheTtlMinutes = ttl;
    if (int.TryParse(config["CACHE_CAPACITY"], out var capacity)) o.CacheCapacity = capacity;
    if (int.TryParse(config["RATE_LIMIT_PER_MINUTE"], out var rate)) o.RateLimitPerMinute = rate;
    if (int.TryParse(config["DAILY_TOKEN_BUDGET"], out var daily)) o.DailyTokenBudget = daily;
});

// Semantic cache + per-user rate limiter + daily token budget. Singletons so their in-memory
// state is shared across all requests on this replica (correct at a single replica; a shared
// store such as Redis is the multi-replica scale-out path).
builder.Services.AddSingleton<ISemanticCache, InMemorySemanticCache>();
builder.Services.AddSingleton<IRateLimiter, TokenBucketRateLimiter>();
builder.Services.AddSingleton<ITokenBudget, InMemoryTokenBudget>();

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
