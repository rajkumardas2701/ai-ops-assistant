using System.Collections.Concurrent;
using AiOps.Api.Support;
using Microsoft.Extensions.Options;

namespace AiOps.Api.RateLimiting;

/// <summary>
/// Per-user token bucket (DDIA backpressure / load shedding). Each user gets a bucket holding up
/// to RateLimitPerMinute permits that refills continuously over time. A request consumes one
/// permit; when the bucket is empty the caller is told how many seconds to wait (Retry-After).
/// This protects the expensive downstream (embeddings + LLM) from a single noisy client.
///
/// In-memory and per-replica — a shared store (Redis) is needed for a global limit across replicas.
/// </summary>
public sealed class TokenBucketRateLimiter(IOptions<ServiceLimitsOptions> options) : IRateLimiter
{
    private sealed class Bucket
    {
        public double Tokens;
        public DateTimeOffset Last;
    }

    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();
    private readonly ServiceLimitsOptions _opt = options.Value;

    public RateLimitResult TryAcquire(string userId)
    {
        var capacity = Math.Max(1, _opt.RateLimitPerMinute);
        var refillPerSec = capacity / 60.0;

        var bucket = _buckets.GetOrAdd(userId, _ => new Bucket
        {
            Tokens = capacity,
            Last = DateTimeOffset.UtcNow,
        });

        lock (bucket)
        {
            var now = DateTimeOffset.UtcNow;
            var elapsed = (now - bucket.Last).TotalSeconds;
            bucket.Tokens = Math.Min(capacity, bucket.Tokens + elapsed * refillPerSec);
            bucket.Last = now;

            if (bucket.Tokens >= 1)
            {
                bucket.Tokens -= 1;
                return new RateLimitResult(true, (int)Math.Floor(bucket.Tokens), 0);
            }

            var retry = (int)Math.Ceiling((1 - bucket.Tokens) / refillPerSec);
            return new RateLimitResult(false, 0, Math.Max(1, retry));
        }
    }
}
