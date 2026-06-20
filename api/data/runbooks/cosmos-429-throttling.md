# Cosmos DB 429 Throttling (Request Rate Too Large)

## Symptoms
- Clients receive HTTP 429 responses with a substatus and an x-ms-retry-after-ms header.
- Application Insights dependency calls to Cosmos DB show failures and rising latency.
- Throughput consumption (RU/s) is at or near the provisioned ceiling for the container.

## Immediate Triage
1. Check the Normalized RU Consumption metric for the container. If it is pinned at 100%, you are rate limited.
2. Confirm the SDK is honoring retry-after. The .NET and Java SDKs retry 429s automatically up to a limit; make sure that limit has not been lowered.
3. Identify whether the throttling is global or limited to one logical partition. A single hot partition key throttles even when total RU looks healthy.

## Root Cause Investigation
- A hot partition means the partition key has low cardinality or a skewed access pattern. Review the partition key design.
- Cross-partition queries and large scans consume disproportionate RU. Check for missing indexes or SELECT * queries.
- A traffic spike or a backfill job can temporarily exceed provisioned throughput.

## Resolution
- Short term: increase provisioned throughput, or enable autoscale so RU/s scales with demand.
- For a hot partition: choose a higher-cardinality partition key, or add a synthetic key suffix to spread writes.
- For expensive queries: add composite indexes and select only required fields to reduce RU per query.
- For backfills: throttle the job client-side and run it during off-peak hours.

## Prevention
- Enable autoscale throughput sized to peak demand plus headroom.
- Add an alert when Normalized RU Consumption stays above 80% for 10 minutes.
- Partition by tenantId for multi-tenant workloads to keep per-partition load even.
