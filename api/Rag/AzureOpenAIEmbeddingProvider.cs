using System.ClientModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;

namespace AiOps.Api.Rag;

/// <summary>
/// Real embeddings via Azure OpenAI. Used when AI_PROVIDER=azureopenai. Auth precedence:
/// an explicit API key if provided, otherwise DefaultAzureCredential (managed identity locally
/// falls back to your az login). This is the production-bound implementation behind the same
/// IEmbeddingProvider seam the local one uses.
/// </summary>
public sealed class AzureOpenAIEmbeddingProvider : IEmbeddingProvider
{
    private readonly EmbeddingClient _client;
    public string Name => "azure-openai-embeddings";

    public AzureOpenAIEmbeddingProvider(IOptions<AzureOpenAIOptions> options)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Endpoint))
            throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is required when AI_PROVIDER=azureopenai.");

        var azure = string.IsNullOrWhiteSpace(o.ApiKey)
            ? new AzureOpenAIClient(new Uri(o.Endpoint), new DefaultAzureCredential())
            : new AzureOpenAIClient(new Uri(o.Endpoint), new ApiKeyCredential(o.ApiKey));

        _client = azure.GetEmbeddingClient(o.EmbeddingDeployment);
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var resp = await _client.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return resp.Value.ToFloats().ToArray();
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var resp = await _client.GenerateEmbeddingsAsync(texts, cancellationToken: ct);
        return resp.Value.Select(e => e.ToFloats().ToArray()).ToList();
    }
}
