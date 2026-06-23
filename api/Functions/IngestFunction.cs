using AiOps.Api.Models;
using AiOps.Api.Rag;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace AiOps.Api.Functions;

/// <summary>
/// Stage D — asynchronous corpus ingestion via Durable Functions.
///
/// Re-embedding the whole corpus is slow and bursty (network calls to Azure OpenAI, writes to
/// Cosmos + Search), so doing it inline in an HTTP request blocks the caller and has no
/// retry/checkpointing. Instead <c>POST /api/ingest</c> now just <em>starts</em> an orchestration
/// and returns <c>202 Accepted</c> with a status URL; the work runs in the background with durable
/// checkpoints and is parallelised across tenants (fan-out/fan-in). Clients poll
/// <c>GET /api/ingest/{instanceId}</c> for progress.
/// </summary>
public sealed class IngestFunction(CorpusLoader loader, IEmbeddingProvider embeddings, ILogger<IngestFunction> logger)
{
    private const string OrchestratorName = "IngestOrchestrator";

    /// <summary>POST /api/ingest — start an async ingest run and return a 202 with a status URL.</summary>
    [Function("ingest")]
    public async Task<IActionResult> Start(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ingest")] HttpRequest req,
        [DurableClient] DurableTaskClient client)
    {
        var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(OrchestratorName);
        logger.LogInformation("Started ingest orchestration {InstanceId}", instanceId);

        var statusUri = $"/api/ingest/{instanceId}";
        return new AcceptedResult(statusUri, new IngestAccepted(instanceId, statusUri));
    }

    /// <summary>GET /api/ingest/{instanceId} — poll the status (and result) of an ingest run.</summary>
    [Function("ingest_status")]
    public async Task<IActionResult> Status(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ingest/{instanceId}")] HttpRequest req,
        string instanceId,
        [DurableClient] DurableTaskClient client)
    {
        var meta = await client.GetInstanceAsync(instanceId, getInputsAndOutputs: true);
        if (meta is null)
            return new NotFoundObjectResult(new IngestStatus(instanceId, "NotFound"));

        var status = meta.RuntimeStatus.ToString();
        var result = meta.RuntimeStatus == OrchestrationRuntimeStatus.Completed
            ? meta.ReadOutputAs<IngestSummary>()
            : null;
        var error = meta.RuntimeStatus == OrchestrationRuntimeStatus.Failed
            ? meta.FailureDetails?.ErrorMessage
            : null;

        return new OkObjectResult(new IngestStatus(instanceId, status, result, error));
    }

    /// <summary>
    /// Orchestrator: reset the index once, discover the tenants, then fan out one seed activity per
    /// tenant and fan in the results. Must be deterministic — all I/O happens in the activities.
    /// </summary>
    [Function(OrchestratorName)]
    public static async Task<IngestSummary> Orchestrate([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        await context.CallActivityAsync(nameof(ResetIndex));

        var plan = await context.CallActivityAsync<IngestPlan>(nameof(PlanIngest));

        // Activities can fail transiently (embedding/storage calls) — let Durable retry with backoff.
        var retry = TaskOptions.FromRetryPolicy(new RetryPolicy(
            maxNumberOfAttempts: 3,
            firstRetryInterval: TimeSpan.FromSeconds(5),
            backoffCoefficient: 2.0));

        var seeds = plan.Tenants
            .Select(t => context.CallActivityAsync<TenantIngestResult>(nameof(SeedTenant), t, retry))
            .ToList();
        var results = await Task.WhenAll(seeds);

        return new IngestSummary(
            results.Sum(r => r.Docs),
            results.Sum(r => r.Chunks),
            plan.Provider,
            results);
    }

    /// <summary>Activity: drop the derived index once before re-seeding.</summary>
    [Function(nameof(ResetIndex))]
    public async Task ResetIndex([ActivityTrigger] object? _)
    {
        await loader.ResetAsync();
        logger.LogInformation("Reset derived index for ingest run");
    }

    /// <summary>Activity: discover the tenants to seed and the embedding provider for the summary.</summary>
    [Function(nameof(PlanIngest))]
    public IngestPlan PlanIngest([ActivityTrigger] object? _)
        => new(embeddings.Name, loader.DiscoverTenants());

    /// <summary>Activity: seed a single tenant's corpus (the unit of parallel work).</summary>
    [Function(nameof(SeedTenant))]
    public async Task<TenantIngestResult> SeedTenant([ActivityTrigger] string tenantId)
    {
        var (docs, chunks) = await loader.LoadTenantAsync(tenantId);
        logger.LogInformation("Seeded tenant {Tenant}: {Docs} docs / {Chunks} chunks", tenantId, docs, chunks);
        return new TenantIngestResult(tenantId, docs, chunks);
    }
}
