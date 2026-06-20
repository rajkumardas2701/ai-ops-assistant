using AiOps.Api.Models;

namespace AiOps.Api.Caching;

/// <summary>
/// Caches chat answers by the *meaning* of the question. Implementations compare an incoming
/// query embedding against previously answered ones and return a stored answer when close enough.
/// </summary>
public interface ISemanticCache
{
    /// <summary>Returns true and the cached response when a matching answer exists.</summary>
    /// <remarks>
    /// Both the question text and its embedding are supplied so implementations can key however they
    /// like: the in-memory cache compares <paramref name="queryVector"/> by cosine similarity, while the
    /// Redis cache keys on the normalized <paramref name="question"/> text.
    /// </remarks>
    bool TryGet(string question, float[] queryVector, out ChatResponse? response);

    /// <summary>Stores an answer keyed by its question text and/or query embedding.</summary>
    void Set(string question, float[] queryVector, ChatResponse response);

    /// <summary>Number of live cached entries.</summary>
    int Count { get; }
}
