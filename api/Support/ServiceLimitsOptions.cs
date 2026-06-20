namespace AiOps.Api.Support;

/// <summary>
/// Tunable limits for the Stage 2 reliability/cost features. All values are read from
/// environment configuration in Program.cs so they can be changed without a code change.
/// </summary>
public sealed class ServiceLimitsOptions
{
    /// <summary>Minimum cosine similarity for a cached answer to be reused (0–1).</summary>
    public double CacheSimilarityThreshold { get; set; } = 0.95;

    /// <summary>How long a cached answer stays valid.</summary>
    public int CacheTtlMinutes { get; set; } = 60;

    /// <summary>Maximum number of cached answers kept in memory (FIFO eviction).</summary>
    public int CacheCapacity { get; set; } = 500;

    /// <summary>Allowed requests per user per minute (token-bucket capacity + refill).</summary>
    public int RateLimitPerMinute { get; set; } = 20;

    /// <summary>Estimated tokens a single user may consume per UTC day.</summary>
    public int DailyTokenBudget { get; set; } = 100_000;
}
