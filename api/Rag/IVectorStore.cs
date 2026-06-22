using AiOps.Api.Models;

namespace AiOps.Api.Rag;

/// <summary>
/// The retrieval index abstraction (DDIA Ch.3). Stage 1 keeps this in-process; Stage A swaps in
/// Azure AI Search — a dedicated, durable, shared index — behind the same seam (ADR-001). Methods
/// are async because a real index is an out-of-process service.
/// </summary>
public interface IVectorStore
{
    /// <summary>Identifies the active implementation (for diagnostics / health).</summary>
    string Name { get; }

    /// <summary>Number of indexed chunks (best-effort for remote stores).</summary>
    Task<int> CountAsync(CancellationToken ct = default);

    /// <summary>Removes all indexed chunks (used before a full re-ingest).</summary>
    Task ClearAsync(CancellationToken ct = default);

    /// <summary>Adds (or upserts) chunks with their embeddings.</summary>
    Task AddAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken ct = default);

    /// <summary>Returns the top-K nearest chunks to the query embedding.</summary>
    Task<IReadOnlyList<SearchHit>> SearchAsync(float[] query, int topK, CancellationToken ct = default);
}
