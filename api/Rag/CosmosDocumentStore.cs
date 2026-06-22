using System.Text.Json;
using AiOps.Api.Models;
using Azure.Identity;
using Microsoft.Azure.Cosmos;

namespace AiOps.Api.Rag;

/// <summary>
/// Azure Cosmos DB system-of-record (DDIA Ch.6 partitioning). Documents live in a single
/// container partitioned by <c>/tenantId</c>, so every read/write targets exactly one
/// partition and tenants never share a logical partition. Auth is passwordless via
/// <see cref="DefaultAzureCredential"/> + the Cosmos DB Built-in Data Contributor role
/// granted to the app's user-assigned managed identity (no keys in config).
/// </summary>
public sealed class CosmosDocumentStore : IDocumentStore
{
    private readonly Container _container;

    public string Name => "cosmos";

    public CosmosDocumentStore(string endpoint, string database, string container)
    {
        var options = new CosmosClientOptions
        {
            // Serialize C# records with the same camelCase contract the partition key expects
            // (TenantId -> tenantId, Id -> id) so /tenantId resolves and ids stay lowercase.
            UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web),
        };
        var client = new CosmosClient(endpoint, new DefaultAzureCredential(), options);
        _container = client.GetContainer(database, container);
    }

    public async Task UpsertAsync(IReadOnlyList<SourceDocument> documents, CancellationToken ct = default)
    {
        foreach (var doc in documents)
            await _container.UpsertItemAsync(doc, new PartitionKey(doc.TenantId), cancellationToken: ct);
    }

    public async Task<int> CountAsync(string tenantId, CancellationToken ct = default)
    {
        var query = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.tenantId = @tenant")
            .WithParameter("@tenant", tenantId);
        using var iterator = _container.GetItemQueryIterator<int>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenantId) });

        int count = 0;
        while (iterator.HasMoreResults)
            foreach (var n in await iterator.ReadNextAsync(ct))
                count += n;
        return count;
    }

    public async Task<IReadOnlyList<SourceDocument>> ListAsync(string tenantId, CancellationToken ct = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.tenantId = @tenant")
            .WithParameter("@tenant", tenantId);
        using var iterator = _container.GetItemQueryIterator<SourceDocument>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenantId) });

        var results = new List<SourceDocument>();
        while (iterator.HasMoreResults)
            results.AddRange(await iterator.ReadNextAsync(ct));
        return results;
    }
}
