using AiOps.Api.Caching;
using AiOps.Api.Models;
using AiOps.Api.Support;

namespace AiOps.Api.Rag;

/// <summary>
/// Orchestrates one RAG turn: embed the question, retrieve the top-K nearest chunks,
/// then ask the chat provider to answer grounded in those chunks. Returns citations so
/// the caller can show exactly which sources the answer came from.
///
/// Stage 2: before retrieval, a semantic cache is consulted on the query embedding. A close
/// enough previous question short-circuits the whole turn (no search, no LLM), which is the
/// main lever for absorbing repeated load cheaply.
/// </summary>
public sealed class RagService(IEmbeddingProvider embeddings, IChatProvider chat, InMemoryVectorStore store, ISemanticCache cache)
{
    public async Task<ChatResponse> AskAsync(string question, int topK, CancellationToken ct = default)
    {
        var queryVector = await embeddings.EmbedAsync(question, ct);

        if (cache.TryGet(queryVector, out var cached) && cached is not null)
            return cached with { Cached = true };

        var hits = store.Search(queryVector, topK);
        var context = hits.Select(h => h.Chunk).ToList();

        var answer = await chat.CompleteAsync(question, context, ct);

        var citations = hits
            .Select(h => new Citation(
                h.Chunk.DocId,
                h.Chunk.Title,
                h.Chunk.Source,
                Math.Round(h.Score, 4),
                Snippet(h.Chunk.Content)))
            .ToList();

        var tokens = TokenEstimator.Estimate(question)
            + TokenEstimator.Estimate(answer)
            + context.Sum(c => TokenEstimator.Estimate(c.Content));

        var response = new ChatResponse(
            answer, citations, $"{embeddings.Name}+{chat.Name}", context.Count, Cached: false, TokensEstimated: tokens);

        cache.Set(queryVector, response);
        return response;
    }

    private static string Snippet(string content)
    {
        var t = content.Replace('\n', ' ').Trim();
        return t.Length > 200 ? t[..200] + "…" : t;
    }
}
