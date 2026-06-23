namespace AiOps.Api.Models;

/// <summary>Incoming chat question. TopK controls how many context chunks to retrieve.</summary>
public record ChatRequest(string Question, int? TopK);

/// <summary>A source reference returned alongside an answer so users can verify it.</summary>
public record Citation(string DocId, string Title, string Source, double Score, string Snippet);

/// <summary>The grounded answer plus its citations and which providers produced it.</summary>
public record ChatResponse(
    string Answer,
    IReadOnlyList<Citation> Citations,
    string Provider,
    int ContextChunks,
    bool Cached = false,
    int TokensEstimated = 0);

/// <summary>A single indexed unit of a document. The embedding is the vector used for retrieval.</summary>
public sealed record DocumentChunk
{
    public required string Id { get; init; }
    /// <summary>Owning tenant — every chunk is isolated to one tenant (Stage C).</summary>
    public required string TenantId { get; init; }
    public required string DocId { get; init; }
    public required string Title { get; init; }
    public required string Source { get; init; }
    public required string Content { get; init; }
    public float[] Embedding { get; set; } = [];
}

/// <summary>
/// A source document in the system-of-record (Cosmos), partitioned by <see cref="TenantId"/>.
/// This is the authoritative copy; <see cref="DocumentChunk"/>s are the derived, indexed form.
/// </summary>
public sealed record SourceDocument
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public required string DocId { get; init; }
    public required string Title { get; init; }
    public required string Source { get; init; }
    public required string Content { get; init; }
}

/// <summary>A retrieval result: a chunk and its similarity score to the query.</summary>
public record SearchHit(DocumentChunk Chunk, double Score);

/// <summary>Summary of an ingestion run.</summary>
public record IngestResponse(int Documents, int Chunks, string Provider);

/// <summary>
/// 202 response when an ingestion run is started asynchronously (Stage D, Durable Functions).
/// The caller polls <see cref="StatusUri"/> for progress instead of blocking on the work.
/// </summary>
public record IngestAccepted(string InstanceId, string StatusUri, string Status = "Accepted");

/// <summary>Per-tenant outcome of a fan-out ingest activity.</summary>
public record TenantIngestResult(string TenantId, int Docs, int Chunks);

/// <summary>The plan an orchestration fans out over: the embedding provider and the tenants to seed.</summary>
public record IngestPlan(string Provider, string[] Tenants);

/// <summary>Aggregated result of an orchestrated ingest run (fan-in).</summary>
public record IngestSummary(int Documents, int Chunks, string Provider, IReadOnlyList<TenantIngestResult> Tenants);

/// <summary>Polled status of an ingestion orchestration.</summary>
public record IngestStatus(string InstanceId, string Status, IngestSummary? Result = null, string? Error = null);
