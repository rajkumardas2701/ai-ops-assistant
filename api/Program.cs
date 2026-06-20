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
