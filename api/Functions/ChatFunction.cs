using System.Text.Json;
using AiOps.Api.Models;
using AiOps.Api.Rag;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AiOps.Api.Functions;

/// <summary>POST /api/chat — ask a question, get a grounded answer with citations.</summary>
public sealed class ChatFunction(RagService rag, ILogger<ChatFunction> logger)
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

        var topK = Math.Clamp(body.TopK ?? 4, 1, 10);
        logger.LogInformation("Chat question (topK={TopK}): {Question}", topK, body.Question);

        var result = await rag.AskAsync(body.Question, topK, req.HttpContext.RequestAborted);
        return new OkObjectResult(result);
    }
}
