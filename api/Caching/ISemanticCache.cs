using AiOps.Api.Models;

namespace AiOps.Api.Caching;

/// <summary>
/// Caches chat answers by the *meaning* of the question. Implementations compare an incoming
/// query embedding against previously answered ones and return a stored answer when close enough.
/// </summary>
public interface ISemanticCache
{
    /// <summary>Returns true and the cached response when a semantically similar answer exists.</summary>
    bool TryGet(float[] queryVector, out ChatResponse? response);

    /// <summary>Stores an answer keyed by its query embedding.</summary>
    void Set(float[] queryVector, ChatResponse response);

    /// <summary>Number of live cached entries.</summary>
    int Count { get; }
}
