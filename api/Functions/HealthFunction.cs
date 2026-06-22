using AiOps.Api.Caching;
using AiOps.Api.Rag;
using AiOps.Api.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Options;

namespace AiOps.Api.Functions;

/// <summary>GET /api/health — liveness plus current index size, active providers, and Stage 2 limits.</summary>
public sealed class HealthFunction(
    IVectorStore store,
    IEmbeddingProvider embeddings,
    IChatProvider chat,
    ISemanticCache cache,
    IOptions<ServiceLimitsOptions> limits)
{
    [Function("health")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest req)
        => new OkObjectResult(new
        {
            status = "ok",
            indexedChunks = await store.CountAsync(req.HttpContext.RequestAborted),
            vectorStore = store.Name,
            embeddingProvider = embeddings.Name,
            chatProvider = chat.Name,
            cachedAnswers = cache.Count,
            limits = new
            {
                rateLimitPerMinute = limits.Value.RateLimitPerMinute,
                dailyTokenBudget = limits.Value.DailyTokenBudget,
                cacheSimilarityThreshold = limits.Value.CacheSimilarityThreshold,
            },
        });
}
