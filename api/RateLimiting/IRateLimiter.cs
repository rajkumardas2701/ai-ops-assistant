namespace AiOps.Api.RateLimiting;

/// <summary>Outcome of a rate-limit check.</summary>
public readonly record struct RateLimitResult(bool Allowed, int Remaining, int RetryAfterSeconds);

/// <summary>Decides whether a given user may make another request right now.</summary>
public interface IRateLimiter
{
    RateLimitResult TryAcquire(string userId);
}
