using System.Text.RegularExpressions;
using AiOps.Api.Models;
using Azure;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;

namespace AiOps.Api.Rag;

/// <summary>
/// Azure AI Search vector store (ADR-001). Moves the "index" out of process into a managed service:
/// it is durable (survives restarts), shared across all API replicas, and scales beyond a single
/// node — the limitations of <see cref="InMemoryVectorStore"/>. Retrieval is approximate-nearest-
/// neighbor (HNSW) rather than the brute-force scan, which is what makes it scale.
///
/// Auth is passwordless via <see cref="DefaultAzureCredential"/> (the API's managed identity in
/// Azure, your az login locally), so no keys are stored.
/// </summary>
public sealed partial class AzureAISearchVectorStore : IVectorStore
{
    private const string VectorField = "contentVector";
    private const string Profile = "vprofile";
    private const string Algorithm = "halgo";

    private readonly SearchIndexClient _indexClient;
    private readonly SearchClient _searchClient;
    private readonly string _indexName;
    private readonly int _dimensions;
    private readonly ILogger<AzureAISearchVectorStore> _logger;

    public string Name => "azure-ai-search";

    public AzureAISearchVectorStore(string endpoint, string indexName, int dimensions, ILogger<AzureAISearchVectorStore> logger)
    {
        var credential = new DefaultAzureCredential();
        var uri = new Uri(endpoint);
        _indexClient = new SearchIndexClient(uri, credential);
        _searchClient = new SearchClient(uri, indexName, credential);
        _indexName = indexName;
        _dimensions = dimensions;
        _logger = logger;
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _searchClient.GetDocumentCountAsync(ct);
            return (int)resp.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return 0; // index not created yet
        }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        // Cheapest reliable "delete all": drop and recreate the index definition.
        try
        {
            await _indexClient.DeleteIndexAsync(_indexName, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // nothing to delete
        }
        await CreateIndexAsync(ct);
    }

    public async Task AddAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken ct = default)
    {
        if (chunks.Count == 0) return;
        await EnsureIndexAsync(ct);

        var docs = chunks.Select(c => new SearchDocument
        {
            ["id"] = SanitizeKey(c.Id),
            ["docId"] = c.DocId,
            ["title"] = c.Title,
            ["source"] = c.Source,
            ["content"] = c.Content,
            [VectorField] = c.Embedding,
        });

        await _searchClient.MergeOrUploadDocumentsAsync(docs, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(float[] query, int topK, CancellationToken ct = default)
    {
        var options = new SearchOptions
        {
            Size = topK,
            VectorSearch = new VectorSearchOptions
            {
                Queries = { new VectorizedQuery(query) { KNearestNeighborsCount = topK, Fields = { VectorField } } },
            },
        };

        SearchResults<SearchDocument> results;
        try
        {
            results = (await _searchClient.SearchAsync<SearchDocument>(null, options, ct)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return [];
        }

        var hits = new List<SearchHit>(topK);
        await foreach (var r in results.GetResultsAsync())
        {
            var d = r.Document;
            var chunk = new DocumentChunk
            {
                Id = d.GetString("id") ?? "",
                DocId = d.GetString("docId") ?? "",
                Title = d.GetString("title") ?? "",
                Source = d.GetString("source") ?? "",
                Content = d.GetString("content") ?? "",
            };
            hits.Add(new SearchHit(chunk, r.Score ?? 0));
        }
        return hits;
    }

    private async Task EnsureIndexAsync(CancellationToken ct)
    {
        try
        {
            await _indexClient.GetIndexAsync(_indexName, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            await CreateIndexAsync(ct);
        }
    }

    private async Task CreateIndexAsync(CancellationToken ct)
    {
        var index = new SearchIndex(_indexName)
        {
            Fields =
            {
                new SimpleField("id", SearchFieldDataType.String) { IsKey = true },
                new SimpleField("docId", SearchFieldDataType.String) { IsFilterable = true },
                new SearchableField("title"),
                new SimpleField("source", SearchFieldDataType.String) { IsFilterable = true },
                new SearchableField("content"),
                new SearchField(VectorField, SearchFieldDataType.Collection(SearchFieldDataType.Single))
                {
                    IsSearchable = true,
                    VectorSearchDimensions = _dimensions,
                    VectorSearchProfileName = Profile,
                },
            },
            VectorSearch = new VectorSearch
            {
                Profiles = { new VectorSearchProfile(Profile, Algorithm) },
                Algorithms = { new HnswAlgorithmConfiguration(Algorithm) },
            },
        };

        await _indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: ct);
        _logger.LogInformation("Created Azure AI Search index {Index} ({Dimensions} dims)", _indexName, _dimensions);
    }

    // Search keys allow only letters, digits, dash, underscore and equals; chunk ids use "::".
    private static string SanitizeKey(string id) => KeyRegex().Replace(id, "_");

    [GeneratedRegex("[^A-Za-z0-9_\\-=]")]
    private static partial Regex KeyRegex();
}
