using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiOps.Api.Models;
using AiOps.Api.Support;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AiOps.Api.Caching;

/// <summary>
/// Redis-backed answer cache shared by every API replica (DDIA Ch.1 shared state / Ch.5 replication).
///
/// Unlike <see cref="InMemorySemanticCache"/>, this keys on the *normalized question text* (a SHA-256
/// of the lowercased, whitespace-collapsed question) rather than vector similarity. Azure Cache for
/// Redis Basic/Standard has no vector index, so true semantic matching here would require RediSearch
/// (Azure Managed Redis / Enterprise). Exact-normalized keying is the simple, cheap distributed win;
/// vector search in Redis is a documented future upgrade (see roadmap "A").
/// </summary>
public sealed class RedisSemanticCache(IConnectionMultiplexer redis, IOptions<ServiceLimitsOptions> options) : ISemanticCache
{
    private readonly IDatabase _db = redis.GetDatabase();
    private readonly ServiceLimitsOptions _opt = options.Value;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Live entry count is not cheaply available in Redis (would need a SCAN). -1 signals "distributed".
    public int Count => -1;

    public bool TryGet(string tenantId, string question, float[] queryVector, out ChatResponse? response)
    {
        response = null;
        var value = _db.StringGet(Key(tenantId, question));
        if (value.IsNullOrEmpty) return false;

        response = JsonSerializer.Deserialize<ChatResponse>(value!, Json);
        return response is not null;
    }

    public void Set(string tenantId, string question, float[] queryVector, ChatResponse response)
    {
        var payload = JsonSerializer.Serialize(response, Json);
        _db.StringSet(Key(tenantId, question), payload, TimeSpan.FromMinutes(_opt.CacheTtlMinutes));
    }

    private static string Key(string tenantId, string question)
    {
        var normalized = string.Join(' ', question.ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"cache:{tenantId}:{Convert.ToHexString(hash)}";
    }
}
