using AiOps.Api.Models;

namespace AiOps.Api.Rag;

/// <summary>
/// Generates an answer grounded in the retrieved context. The local implementation is
/// extractive (no LLM); the Azure OpenAI implementation synthesizes a real answer.
/// </summary>
public interface IChatProvider
{
    string Name { get; }
    Task<string> CompleteAsync(string question, IReadOnlyList<DocumentChunk> context, CancellationToken ct = default);
}
