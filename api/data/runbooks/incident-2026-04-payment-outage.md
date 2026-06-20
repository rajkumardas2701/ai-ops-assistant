# Postmortem: Payment API Outage (2026-04-12)

## Summary
On 2026-04-12 the payment API returned elevated 500 errors for 38 minutes between 14:02 and 14:40 UTC. Roughly 4% of checkout attempts failed during the window. No payment data was lost; failed checkouts were retried successfully after recovery.

## Impact
- 4% of checkout requests failed with HTTP 500 for 38 minutes.
- Customer support saw a spike in failed-payment tickets.
- No double charges occurred because the payment provider call is idempotent on the order id.

## Timeline (UTC)
- 14:02 Error rate alert fires for the payment API.
- 14:08 On-call confirms 500s correlate with Cosmos DB 429 throttling on the orders container.
- 14:19 Throughput increased via autoscale ceiling change; errors begin to fall.
- 14:40 Error rate returns to baseline; incident downgraded.

## Root Cause
A marketing campaign drove a 6x traffic spike. The orders container used a partition key of orderStatus, which has very low cardinality, so nearly all writes landed on a single logical partition. That partition hit its RU limit and threw 429s, which the API surfaced as 500s instead of retrying.

## What Went Wrong
- Partition key design concentrated load on one partition (a hot partition).
- The API translated Cosmos 429 into a 500 rather than honoring retry-after and backing off.
- Autoscale maximum was set too low to absorb the spike.

## What Went Well
- Alerting fired within 2 minutes of the error spike.
- Idempotent payment calls prevented any double charges.

## Action Items
1. Repartition the orders container by orderId to spread writes evenly.
2. Map Cosmos 429 to a ret(retry with backoff) path in the API instead of returning 500.
3. Raise the autoscale maximum and load test against a 10x spike.
4. Add a hot-partition alert based on per-partition RU consumption.

## Lessons Learned
- Partition key cardinality is a reliability concern, not just a performance one.
- Translate downstream throttling into client retries, never into user-facing 500s.
