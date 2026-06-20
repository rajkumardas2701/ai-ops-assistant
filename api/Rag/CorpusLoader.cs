using AiOps.Api.Models;
using Microsoft.Extensions.Logging;

namespace AiOps.Api.Rag;

/// <summary>
/// Reads markdown docs from the corpus folder, chunks them, embeds the chunks, and loads
/// them into the vector store. This is the "derived data" pipeline of the system (DDIA Ch.3/11):
/// the search index is a materialized view derived from the source documents.
/// </summary>
public sealed class CorpusLoader(IEmbeddingProvider embeddings, InMemoryVectorStore store, ILogger<CorpusLoader> logger)
{
    public string DefaultCorpusPath => Path.Combine(AppContext.BaseDirectory, "data", "runbooks");

    public async Task<(int docs, int chunks)> LoadAsync(string? path = null, bool reset = true, CancellationToken ct = default)
    {
        path ??= DefaultCorpusPath;
        if (!Directory.Exists(path))
        {
            logger.LogWarning("Corpus path not found: {Path}", path);
            return (0, 0);
        }

        if (reset) store.Clear();

        var files = Directory.GetFiles(path, "*.md", SearchOption.AllDirectories);
        var allChunks = new List<DocumentChunk>();

        foreach (var file in files)
        {
            var text = await File.ReadAllTextAsync(file, ct);
            var docId = Path.GetFileNameWithoutExtension(file);
            var title = ExtractTitle(text, docId);
            var source = Path.GetFileName(file);
            allChunks.AddRange(DocumentChunker.Chunk(docId, title, source, text));
        }

        if (allChunks.Count > 0)
        {
            var vectors = await embeddings.EmbedBatchAsync(allChunks.Select(c => c.Content).ToList(), ct);
            for (int i = 0; i < allChunks.Count; i++) allChunks[i].Embedding = vectors[i];
            store.Add(allChunks);
        }

        logger.LogInformation("Ingested {Docs} docs / {Chunks} chunks from {Path}", files.Length, allChunks.Count, path);
        return (files.Length, allChunks.Count);
    }

    private static string ExtractTitle(string text, string fallback)
    {
        foreach (var line in text.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("# ")) return t[2..].Trim();
        }
        return fallback;
    }
}
