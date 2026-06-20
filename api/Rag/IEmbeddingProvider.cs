namespace AiOps.Api.Rag;

/// <summary>
/// Turns text into vectors. Stage 1 ships a local, $0 implementation so the whole
/// pipeline runs offline; flip AI_PROVIDER=azureopenai to use real embeddings.
/// </summary>
public interface IEmbeddingProvider
{
    string Name { get; }
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
