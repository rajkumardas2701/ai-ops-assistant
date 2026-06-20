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
    int ContextChunks);

/// <summary>A single indexed unit of a document. The embedding is the vector used for retrieval.</summary>
public sealed record DocumentChunk
{
    public required string Id { get; init; }
    public required string DocId { get; init; }
    public required string Title { get; init; }
    public required string Source { get; init; }
    public required string Content { get; init; }
    public float[] Embedding { get; set; } = [];
}

/// <summary>A retrieval result: a chunk and its similarity score to the query.</summary>
public record SearchHit(DocumentChunk Chunk, double Score);

/// <summary>Summary of an ingestion run.</summary>
public record IngestResponse(int Documents, int Chunks, string Provider);
