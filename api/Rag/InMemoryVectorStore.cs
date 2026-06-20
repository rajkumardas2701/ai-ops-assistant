using AiOps.Api.Models;

namespace AiOps.Api.Rag;

/// <summary>
/// The "index" for Stage 1: an in-memory list scanned with brute-force cosine similarity.
/// This is deliberately the simplest thing that works so the mechanics of vector retrieval
/// are visible (DDIA Ch.3). It is O(n) per query and single-node — exactly the limitation
/// that later stages fix by swapping in Azure AI Search (see ADR-001).
/// </summary>
public sealed class InMemoryVectorStore
{
    private readonly List<DocumentChunk> _chunks = [];
    private readonly Lock _lock = new();

    public int Count
    {
        get { lock (_lock) return _chunks.Count; }
    }

    public void Clear()
    {
        lock (_lock) _chunks.Clear();
    }

    public void Add(IEnumerable<DocumentChunk> chunks)
    {
        lock (_lock) _chunks.AddRange(chunks);
    }

    public IReadOnlyList<SearchHit> Search(float[] query, int topK)
    {
        lock (_lock)
        {
            return _chunks
                .Select(c => new SearchHit(c, Cosine(query, c.Embedding)))
                .OrderByDescending(h => h.Score)
                .Take(topK)
                .ToList();
        }
    }

    private static double Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        if (na == 0 || nb == 0) return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
