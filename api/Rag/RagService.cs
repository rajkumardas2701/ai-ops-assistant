using AiOps.Api.Models;

namespace AiOps.Api.Rag;

/// <summary>
/// Orchestrates one RAG turn: embed the question, retrieve the top-K nearest chunks,
/// then ask the chat provider to answer grounded in those chunks. Returns citations so
/// the caller can show exactly which sources the answer came from.
/// </summary>
public sealed class RagService(IEmbeddingProvider embeddings, IChatProvider chat, InMemoryVectorStore store)
{
    public async Task<ChatResponse> AskAsync(string question, int topK, CancellationToken ct = default)
    {
        var queryVector = await embeddings.EmbedAsync(question, ct);
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

        return new ChatResponse(answer, citations, $"{embeddings.Name}+{chat.Name}", context.Count);
    }

    private static string Snippet(string content)
    {
        var t = content.Replace('\n', ' ').Trim();
        return t.Length > 200 ? t[..200] + "…" : t;
    }
}
