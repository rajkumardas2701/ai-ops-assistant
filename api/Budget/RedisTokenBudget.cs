using AiOps.Api.Support;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AiOps.Api.Budget;

/// <summary>
/// Redis-backed per-user daily token budget shared across replicas (DDIA Ch.1 shared state). Usage is
/// an atomic counter (INCRBY) under a per-day key that expires at the end of the UTC day, so the budget
/// is enforced globally regardless of which replica served the request, and resets without a sweeper.
/// </summary>
public sealed class RedisTokenBudget(IConnectionMultiplexer redis, IOptions<ServiceLimitsOptions> options) : ITokenBudget
{
    private readonly IDatabase _db = redis.GetDatabase();
    private readonly int _daily = options.Value.DailyTokenBudget;

    public int GetRemaining(string userId)
    {
        var used = (long?)_db.StringGet(Key(userId)) ?? 0;
        return (int)Math.Max(0, _daily - used);
    }

    public void Consume(string userId, int tokens)
    {
        if (tokens <= 0) return;

        var key = Key(userId);
        _db.StringIncrement(key, tokens);
        // Set the expiry once, only if the key has none yet, so the counter clears at UTC midnight.
        _db.KeyExpire(key, SecondsUntilUtcMidnight(), ExpireWhen.HasNoExpiry);
    }

    private static string Key(string userId) => $"budget:{userId}:{DateTime.UtcNow:yyyyMMdd}";

    private static TimeSpan SecondsUntilUtcMidnight()
    {
        var now = DateTimeOffset.UtcNow;
        var nextMidnight = now.UtcDateTime.Date.AddDays(1);
        return nextMidnight - now.UtcDateTime;
    }
}
