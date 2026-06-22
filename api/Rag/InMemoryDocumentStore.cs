using System.Collections.Concurrent;
using AiOps.Api.Models;

namespace AiOps.Api.Rag;

/// <summary>
/// In-memory system-of-record used for local development and tests. Keyed by tenant so it
/// mirrors the isolation guarantees of <see cref="CosmosDocumentStore"/> without requiring
/// Cosmos data-plane RBAC on a developer machine.
/// </summary>
public sealed class InMemoryDocumentStore : IDocumentStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SourceDocument>> _byTenant = new();

    public string Name => "in-memory";

    public Task UpsertAsync(IReadOnlyList<SourceDocument> documents, CancellationToken ct = default)
    {
        foreach (var doc in documents)
        {
            var tenant = _byTenant.GetOrAdd(doc.TenantId, _ => new());
            tenant[doc.Id] = doc;
        }
        return Task.CompletedTask;
    }

    public Task<int> CountAsync(string tenantId, CancellationToken ct = default)
        => Task.FromResult(_byTenant.TryGetValue(tenantId, out var t) ? t.Count : 0);

    public Task<IReadOnlyList<SourceDocument>> ListAsync(string tenantId, CancellationToken ct = default)
    {
        IReadOnlyList<SourceDocument> docs = _byTenant.TryGetValue(tenantId, out var t)
            ? t.Values.OrderBy(d => d.DocId).ToList()
            : [];
        return Task.FromResult(docs);
    }
}
