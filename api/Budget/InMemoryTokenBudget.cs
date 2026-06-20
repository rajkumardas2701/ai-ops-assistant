using System.Collections.Concurrent;
using AiOps.Api.Support;
using Microsoft.Extensions.Options;

namespace AiOps.Api.Budget;

/// <summary>
/// Per-user daily token budget (cost control / quota enforcement). Tracks estimated tokens
/// consumed per user and resets at UTC midnight. Combined with the semantic cache — cache hits
/// do not draw down the budget — this caps the worst-case spend a single user can drive.
///
/// In-memory and per-replica; a shared store is the scale-out path for a global budget.
/// </summary>
public sealed class InMemoryTokenBudget(IOptions<ServiceLimitsOptions> options) : ITokenBudget
{
    private sealed class Usage
    {
        public int Used;
        public DateOnly Day;
    }

    private readonly ConcurrentDictionary<string, Usage> _usage = new();
    private readonly ServiceLimitsOptions _opt = options.Value;

    public int GetRemaining(string userId)
    {
        var u = Current(userId);
        lock (u) return Math.Max(0, _opt.DailyTokenBudget - u.Used);
    }

    public void Consume(string userId, int tokens)
    {
        var u = Current(userId);
        lock (u) u.Used += Math.Max(0, tokens);
    }

    private Usage Current(string userId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var u = _usage.GetOrAdd(userId, _ => new Usage { Used = 0, Day = today });
        lock (u)
        {
            if (u.Day != today)
            {
                u.Day = today;
                u.Used = 0;
            }
        }
        return u;
    }
}
