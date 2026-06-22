using AiOps.Api.Models;
using Microsoft.Extensions.Logging;

namespace AiOps.Api.Rag;

/// <summary>
/// Seeds the multi-tenant corpus (Stage C). For each tenant it writes the source documents to the
/// system-of-record (<see cref="IDocumentStore"/>, partitioned by tenant) and derives the search
/// index (<see cref="IVectorStore"/>, tagged with the tenant) — the classic system-of-record vs
/// derived-data split (DDIA Ch.3/11), now isolated per tenant.
///
/// Tenant layout under the app's <c>data/</c> folder:
/// <list type="bullet">
///   <item><c>data/runbooks</c> → the shared <c>default</c> tenant (preserves the prior single-tenant corpus).</item>
///   <item><c>data/tenants/&lt;tenantId&gt;</c> → one folder per additional tenant.</item>
/// </list>
/// </summary>
public sealed class CorpusLoader(
    IEmbeddingProvider embeddings,
    IVectorStore store,
    IDocumentStore documents,
    ILogger<CorpusLoader> logger)
{
    public async Task<(int docs, int chunks)> LoadAsync(bool reset = true, CancellationToken ct = default)
    {
        // A full reset drops the whole derived index once, then every tenant is re-seeded below.
        if (reset) await store.ClearAsync(ct);

        int totalDocs = 0, totalChunks = 0;
        foreach (var (tenantId, dir) in DiscoverTenantCorpora())
        {
            var (docs, chunks) = await LoadTenantAsync(tenantId, dir, ct);
            totalDocs += docs;
            totalChunks += chunks;
        }
        return (totalDocs, totalChunks);
    }

    private async Task<(int docs, int chunks)> LoadTenantAsync(string tenantId, string dir, CancellationToken ct)
    {
        if (!Directory.Exists(dir))
        {
            logger.LogWarning("Corpus path not found for tenant {Tenant}: {Path}", tenantId, dir);
            return (0, 0);
        }

        var files = Directory.GetFiles(dir, "*.md", SearchOption.AllDirectories);
        var sources = new List<SourceDocument>(files.Length);
        var chunks = new List<DocumentChunk>();

        foreach (var file in files)
        {
            var text = await File.ReadAllTextAsync(file, ct);
            var docId = Path.GetFileNameWithoutExtension(file);
            var title = ExtractTitle(text, docId);
            var source = Path.GetFileName(file);

            sources.Add(new SourceDocument
            {
                Id = $"{tenantId}::{docId}",
                TenantId = tenantId,
                DocId = docId,
                Title = title,
                Source = source,
                Content = text,
            });
            chunks.AddRange(DocumentChunker.Chunk(tenantId, docId, title, source, text));
        }

        // System-of-record first, then the derived index.
        if (sources.Count > 0) await documents.UpsertAsync(sources, ct);

        if (chunks.Count > 0)
        {
            var vectors = await embeddings.EmbedBatchAsync(chunks.Select(c => c.Content).ToList(), ct);
            for (int i = 0; i < chunks.Count; i++) chunks[i].Embedding = vectors[i];
            await store.AddAsync(chunks, ct);
        }

        logger.LogInformation("Seeded tenant {Tenant}: {Docs} docs / {Chunks} chunks from {Path}",
            tenantId, files.Length, chunks.Count, dir);
        return (files.Length, chunks.Count);
    }

    private static IEnumerable<(string tenantId, string dir)> DiscoverTenantCorpora()
    {
        var baseDir = AppContext.BaseDirectory;

        // The original corpus is the shared "default" tenant so existing behavior is preserved.
        yield return ("default", Path.Combine(baseDir, "data", "runbooks"));

        var tenantsRoot = Path.Combine(baseDir, "data", "tenants");
        if (Directory.Exists(tenantsRoot))
            foreach (var dir in Directory.GetDirectories(tenantsRoot))
                yield return (Path.GetFileName(dir), dir);
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
