using AiOps.Api.Rag;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace AiOps.Api.Functions;

/// <summary>GET /api/health — liveness plus current index size and active providers.</summary>
public sealed class HealthFunction(InMemoryVectorStore store, IEmbeddingProvider embeddings, IChatProvider chat)
{
    [Function("health")]
    public IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest req)
        => new OkObjectResult(new
        {
            status = "ok",
            indexedChunks = store.Count,
            embeddingProvider = embeddings.Name,
            chatProvider = chat.Name,
        });
}
