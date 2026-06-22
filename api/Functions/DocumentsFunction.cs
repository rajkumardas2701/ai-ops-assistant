using AiOps.Api.Rag;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace AiOps.Api.Functions;

/// <summary>
/// GET /api/documents — lists the calling tenant's source documents straight from the
/// system-of-record (Cosmos). Demonstrates single-partition isolation: the tenant is taken
/// from the <c>X-Tenant-Id</c> header (or <c>?tenant=</c>) and the query never crosses tenants.
/// </summary>
public sealed class DocumentsFunction(IDocumentStore documents)
{
    [Function("documents")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "documents")] HttpRequest req)
    {
        var tenantId = TenantResolver.Resolve(req);
        var docs = await documents.ListAsync(tenantId, req.HttpContext.RequestAborted);
        return new OkObjectResult(new
        {
            tenant = tenantId,
            count = docs.Count,
            documents = docs.Select(d => new { d.DocId, d.Title, d.Source }),
        });
    }
}
