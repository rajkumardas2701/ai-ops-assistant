using AiOps.Api.Models;
using AiOps.Api.Rag;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace AiOps.Api.Functions;

/// <summary>POST /api/ingest — (re)build the in-memory index from the sample corpus.</summary>
public sealed class IngestFunction(CorpusLoader loader, IEmbeddingProvider embeddings)
{
    [Function("ingest")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ingest")] HttpRequest req)
    {
        var (docs, chunks) = await loader.LoadAsync(reset: true, ct: req.HttpContext.RequestAborted);
        return new OkObjectResult(new IngestResponse(docs, chunks, embeddings.Name));
    }
}
