using AiOps.Api.Models;

namespace AiOps.Api.Rag;

/// <summary>
/// The durable system-of-record for source documents, partitioned by tenant (DDIA Ch.6).
/// Where <see cref="IVectorStore"/> is a derived index optimized for retrieval, this is the
/// authoritative per-tenant document store. Stage C backs it with Azure Cosmos DB (partition
/// key <c>/tenantId</c>) so each tenant's data is isolated and the store scales horizontally.
/// </summary>
public interface IDocumentStore
{
    /// <summary>Identifies the active implementation (for diagnostics / health).</summary>
    string Name { get; }

    /// <summary>Upserts source documents. Each carries its own <see cref="SourceDocument.TenantId"/>.</summary>
    Task UpsertAsync(IReadOnlyList<SourceDocument> documents, CancellationToken ct = default);

    /// <summary>Counts documents for a single tenant (a single-partition query).</summary>
    Task<int> CountAsync(string tenantId, CancellationToken ct = default);

    /// <summary>Lists a tenant's documents (a single-partition query — never crosses tenants).</summary>
    Task<IReadOnlyList<SourceDocument>> ListAsync(string tenantId, CancellationToken ct = default);
}
