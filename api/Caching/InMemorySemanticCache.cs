using AiOps.Api.Models;
using AiOps.Api.Support;
using Microsoft.Extensions.Options;

namespace AiOps.Api.Caching;

/// <summary>
/// Stage 2 semantic cache (DDIA Ch.1 caching / Ch.11 derived data). Keyed by the meaning of a
/// question rather than its exact text: an incoming query vector is compared by cosine similarity
/// to previously answered queries, and a close-enough match returns the stored answer with no
/// embedding search or LLM call. This is the cheapest way to absorb repeated/near-duplicate load.
///
/// It is in-memory and per-replica, so it is only fully consistent at a single replica — exactly
/// the limitation that motivates a shared cache (Redis) at a later stage.
/// </summary>
public sealed class InMemorySemanticCache(IOptions<ServiceLimitsOptions> options) : ISemanticCache
{
    private sealed record Entry(float[] Vector, ChatResponse Response, DateTimeOffset ExpiresAt);

    private readonly List<Entry> _entries = [];
    private readonly Lock _lock = new();
    private readonly ServiceLimitsOptions _opt = options.Value;

    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    public bool TryGet(float[] queryVector, out ChatResponse? response)
    {
        response = null;
        var now = DateTimeOffset.UtcNow;

        lock (_lock)
        {
            _entries.RemoveAll(e => e.ExpiresAt <= now);

            double best = 0;
            Entry? hit = null;
            foreach (var e in _entries)
            {
                var sim = VectorMath.Cosine(queryVector, e.Vector);
                if (sim > best)
                {
                    best = sim;
                    hit = e;
                }
            }

            if (hit is not null && best >= _opt.CacheSimilarityThreshold)
            {
                response = hit.Response;
                return true;
            }
        }

        return false;
    }

    public void Set(float[] queryVector, ChatResponse response)
    {
        var entry = new Entry(queryVector, response, DateTimeOffset.UtcNow.AddMinutes(_opt.CacheTtlMinutes));
        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > _opt.CacheCapacity)
                _entries.RemoveAt(0); // evict oldest (FIFO)
        }
    }
}
