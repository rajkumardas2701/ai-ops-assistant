# Redis Cache: Evictions and Connection Timeouts

## Symptoms
- Cache hit rate drops while the used_memory metric sits near maxmemory.
- Clients log RedisTimeoutException or StackExchange.Redis timeout awaiting responses.
- Database load rises because requests miss the cache and fall through to the data store.

## Immediate Triage
1. Check the Used Memory and Evicted Keys metrics. Rising evictions mean the cache is too small for the working set.
2. Check Server Load (CPU) on the cache. Above 80% indicates the instance is undersized or running expensive commands.
3. Look for KEYS or other O(n) commands in the slow log; these block the single-threaded server.

## Root Cause Investigation
- Memory pressure: the working set exceeds maxmemory and the eviction policy is discarding hot keys.
- Connection churn: creating a new connection per request exhausts the connection pool and causes timeouts. Reuse a single multiplexer.
- Large values or unbounded lists inflate memory and serialization time.

## Resolution
- Scale the cache up to a tier with more memory, or scale out with clustering to spread the keyspace.
- Set an appropriate eviction policy (allkeys-lru for a pure cache) so cold keys are dropped first.
- Set TTLs on cached entries so stale data expires and memory is reclaimed.
- Reuse one connection multiplexer per process instead of opening connections per request.

## Prevention
- Alert when Used Memory exceeds 80% of maxmemory.
- Alert when Evicted Keys per minute crosses a baseline threshold.
- Cap value sizes and always set a TTL on cache writes.
