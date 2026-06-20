using System.Globalization;
using AiOps.Api.Support;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AiOps.Api.RateLimiting;

/// <summary>
/// Redis token-bucket rate limiter shared across all replicas (DDIA Ch.1 shared state). The bucket
/// (tokens + last-refill timestamp) lives in Redis and is updated by an atomic Lua script, so
/// concurrent requests from the same user — even when load-balanced onto different replicas — see
/// one consistent limit. This is the correctness fix that lets us scale the API past a single replica.
/// </summary>
public sealed class RedisRateLimiter(IConnectionMultiplexer redis, IOptions<ServiceLimitsOptions> options) : IRateLimiter
{
    private readonly IDatabase _db = redis.GetDatabase();
    private readonly int _capacity = Math.Max(1, options.Value.RateLimitPerMinute);
    private readonly double _refillPerSec = Math.Max(1, options.Value.RateLimitPerMinute) / 60.0;

    // Atomic token-bucket refill+take. KEYS[1]=bucket; ARGV: capacity, refillPerSec, now(seconds), ttl(seconds).
    // Tokens are returned as a string because Redis truncates Lua numbers to integers over the wire.
    private const string Script = @"
local b = redis.call('HMGET', KEYS[1], 'tokens', 'ts')
local tokens = tonumber(b[1])
local ts = tonumber(b[2])
local cap = tonumber(ARGV[1])
local refill = tonumber(ARGV[2])
local now = tonumber(ARGV[3])
if tokens == nil then tokens = cap; ts = now end
local elapsed = now - ts
if elapsed < 0 then elapsed = 0 end
tokens = math.min(cap, tokens + elapsed * refill)
local allowed = 0
if tokens >= 1 then tokens = tokens - 1; allowed = 1 end
redis.call('HMSET', KEYS[1], 'tokens', tokens, 'ts', now)
redis.call('EXPIRE', KEYS[1], ARGV[4])
return {allowed, tostring(tokens)}
";

    public RateLimitResult TryAcquire(string userId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        var raw = (RedisResult[])_db.ScriptEvaluate(
            Script,
            new RedisKey[] { $"rl:{userId}" },
            new RedisValue[] { _capacity, _refillPerSec.ToString(CultureInfo.InvariantCulture), now.ToString(CultureInfo.InvariantCulture), 120 })!;

        var allowed = (long)raw[0] == 1;
        var tokens = double.Parse((string)raw[1]!, CultureInfo.InvariantCulture);
        var remaining = (int)Math.Floor(tokens);
        var retryAfter = allowed ? 0 : (int)Math.Ceiling((1 - tokens) / _refillPerSec);
        return new RateLimitResult(allowed, remaining, retryAfter);
    }
}
