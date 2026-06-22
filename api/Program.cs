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
using StackExchange.Redis;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

var config = builder.Configuration;

// AI_PROVIDER is the default for both seams; EMBEDDING_PROVIDER / CHAT_PROVIDER can override each
// independently (e.g. real Azure OpenAI embeddings while chat stays local/extractive). Values: "local" | "azureopenai".
var provider = config["AI_PROVIDER"] ?? "local";
var embeddingProvider = config["EMBEDDING_PROVIDER"] ?? provider;
var chatProvider = config["CHAT_PROVIDER"] ?? provider;

builder.Services.Configure<AzureOpenAIOptions>(o =>
{
    o.Endpoint = config["AZURE_OPENAI_ENDPOINT"] ?? "";
    o.ApiKey = config["AZURE_OPENAI_KEY"] ?? "";
    o.ChatDeployment = config["AZURE_OPENAI_CHAT_DEPLOYMENT"] ?? "gpt-4o";
    o.EmbeddingDeployment = config["AZURE_OPENAI_EMBEDDING_DEPLOYMENT"] ?? "text-embedding-3-small";
    if (int.TryParse(config["AZURE_OPENAI_EMBEDDING_DIMENSIONS"], out var dims)) o.EmbeddingDimensions = dims;
});

if (string.Equals(embeddingProvider, "azureopenai", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IEmbeddingProvider, AzureOpenAIEmbeddingProvider>();
else
    builder.Services.AddSingleton<IEmbeddingProvider, LocalEmbeddingProvider>();

if (string.Equals(chatProvider, "azureopenai", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IChatProvider, AzureOpenAIChatProvider>();
else
    builder.Services.AddSingleton<IChatProvider, LocalChatProvider>();

// VECTOR_STORE selects the retrieval index behind the IVectorStore seam (ADR-001):
//   "memory" (default) — in-process brute-force cosine, per-replica, rebuilt at startup.
//   "azuresearch"      — Azure AI Search: durable, shared across replicas, ANN (HNSW) retrieval.
var vectorStore = config["VECTOR_STORE"] ?? "memory";
if (string.Equals(vectorStore, "azuresearch", StringComparison.OrdinalIgnoreCase))
{
    var searchEndpoint = config["SEARCH_ENDPOINT"]
        ?? throw new InvalidOperationException("VECTOR_STORE=azuresearch requires SEARCH_ENDPOINT.");
    var searchIndex = config["SEARCH_INDEX"] ?? "runbooks";
    builder.Services.AddSingleton<IVectorStore>(sp => new AzureAISearchVectorStore(
        searchEndpoint,
        searchIndex,
        sp.GetRequiredService<IEmbeddingProvider>().Dimensions,
        sp.GetRequiredService<ILogger<AzureAISearchVectorStore>>()));
}
else
{
    builder.Services.AddSingleton<IVectorStore, InMemoryVectorStore>();
}

// DOCUMENT_STORE selects the system-of-record behind the IDocumentStore seam (Stage C):
//   "memory" (default) — in-process, per-replica: fine for local dev / tests.
//   "cosmos"           — Azure Cosmos DB, partitioned by /tenantId: durable and isolated per tenant.
var documentStore = config["DOCUMENT_STORE"] ?? "memory";
if (string.Equals(documentStore, "cosmos", StringComparison.OrdinalIgnoreCase))
{
    var cosmosEndpoint = config["COSMOS_ENDPOINT"]
        ?? throw new InvalidOperationException("DOCUMENT_STORE=cosmos requires COSMOS_ENDPOINT.");
    var cosmosDatabase = config["COSMOS_DATABASE"] ?? "aiops";
    var cosmosContainer = config["COSMOS_CONTAINER"] ?? "documents";
    builder.Services.AddSingleton<IDocumentStore>(_ =>
        new CosmosDocumentStore(cosmosEndpoint, cosmosDatabase, cosmosContainer));
}
else
{
    builder.Services.AddSingleton<IDocumentStore, InMemoryDocumentStore>();
}

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

// Semantic cache + per-user rate limiter + daily token budget.
// STATE_STORE selects where this state lives, behind the same seams:
//   "memory" (default) — in-memory, per-replica: correct only at a single replica.
//   "redis"            — Azure Cache for Redis, shared across replicas: lets the API scale out
//                        while keeping rate limits and budgets globally consistent (Stage 3-B).
var stateStore = config["STATE_STORE"] ?? "memory";
if (string.Equals(stateStore, "redis", StringComparison.OrdinalIgnoreCase))
{
    var redisConn = config["REDIS_CONNECTION"]
        ?? throw new InvalidOperationException("STATE_STORE=redis requires a REDIS_CONNECTION connection string.");
    var redisOptions = ConfigurationOptions.Parse(redisConn);
    redisOptions.AbortOnConnectFail = false; // connect lazily and reconnect, instead of crashing on boot

    builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));
    builder.Services.AddSingleton<ISemanticCache, RedisSemanticCache>();
    builder.Services.AddSingleton<IRateLimiter, RedisRateLimiter>();
    builder.Services.AddSingleton<ITokenBudget, RedisTokenBudget>();
}
else
{
    builder.Services.AddSingleton<ISemanticCache, InMemorySemanticCache>();
    builder.Services.AddSingleton<IRateLimiter, TokenBucketRateLimiter>();
    builder.Services.AddSingleton<ITokenBudget, InMemoryTokenBudget>();
}

var host = builder.Build();

// Auto-ingest the sample corpus at startup so the demo works out of the box. With a durable index
// (Azure AI Search) this is skipped when the index is already populated, avoiding needless re-embedding.
try
{
    using var scope = host.Services.CreateScope();
    var store = scope.ServiceProvider.GetRequiredService<IVectorStore>();
    if (await store.CountAsync() == 0)
    {
        var loader = scope.ServiceProvider.GetRequiredService<CorpusLoader>();
        await loader.LoadAsync();
    }
}
catch (Exception ex)
{
    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    logger.LogError(ex, "Startup corpus ingestion failed. Call POST /api/ingest manually once configured.");
}

host.Run();
