using System.Text.Json;
using AiOps.Api.Budget;
using AiOps.Api.Models;
using AiOps.Api.Rag;
using AiOps.Api.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AiOps.Api.Functions;

/// <summary>
/// POST /api/chat — ask a question, get a grounded answer with citations.
/// Stage 2 guards each request with a per-user rate limit and daily token budget, and serves
/// semantically cached answers when possible. The caller is identified by the X-User-Id header
/// (falling back to the forwarded client IP), so limits are per client without requiring auth.
/// </summary>
public sealed class ChatFunction(RagService rag, IRateLimiter limiter, ITokenBudget budget, ILogger<ChatFunction> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Function("chat")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "chat")] HttpRequest req)
    {
        ChatRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<ChatRequest>(
                req.Body, JsonOptions, req.HttpContext.RequestAborted);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new { error = "Invalid JSON body." });
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Question))
            return new BadRequestObjectResult(new { error = "'question' is required." });

        var userId = ResolveUser(req);
        var tenantId = TenantResolver.Resolve(req);
        req.HttpContext.Response.Headers["X-Tenant-Id"] = tenantId;

        // Backpressure: shed load per user before touching the expensive downstream.
        var rate = limiter.TryAcquire(userId);
        var headers = req.HttpContext.Response.Headers;
        headers["X-RateLimit-Remaining"] = rate.Remaining.ToString();
        if (!rate.Allowed)
        {
            headers["Retry-After"] = rate.RetryAfterSeconds.ToString();
            logger.LogWarning("Rate limit hit for {User} (retry in {Retry}s)", userId, rate.RetryAfterSeconds);
            return new ObjectResult(new { error = "Rate limit exceeded. Slow down.", retryAfterSeconds = rate.RetryAfterSeconds })
            {
                StatusCode = StatusCodes.Status429TooManyRequests,
            };
        }

        // Cost control: stop a user who has exhausted their daily token budget.
        if (budget.GetRemaining(userId) <= 0)
        {
            logger.LogWarning("Daily token budget exhausted for {User}", userId);
            return new ObjectResult(new { error = "Daily token budget exhausted. Try again tomorrow." })
            {
                StatusCode = StatusCodes.Status429TooManyRequests,
            };
        }

        var topK = Math.Clamp(body.TopK ?? 4, 1, 10);
        logger.LogInformation("Chat question (topK={TopK}) from {User} in tenant {Tenant}: {Question}", topK, userId, tenantId, body.Question);

        var result = await rag.AskAsync(body.Question, topK, tenantId, req.HttpContext.RequestAborted);

        // Cache hits cost nothing, so they do not draw down the user's budget.
        if (!result.Cached)
            budget.Consume(userId, result.TokensEstimated);

        headers["X-Cache"] = result.Cached ? "HIT" : "MISS";
        headers["X-Budget-Remaining"] = budget.GetRemaining(userId).ToString();
        return new OkObjectResult(result);
    }

    private static string ResolveUser(HttpRequest req)
    {
        if (req.Headers.TryGetValue("X-User-Id", out var id) && !string.IsNullOrWhiteSpace(id))
            return id.ToString();
        if (req.Headers.TryGetValue("X-Forwarded-For", out var fwd) && !string.IsNullOrWhiteSpace(fwd))
            return fwd.ToString().Split(',')[0].Trim();
        return req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
    }
}
