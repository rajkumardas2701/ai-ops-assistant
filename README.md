# AI Operations Assistant

A RAG (retrieval-augmented generation) assistant for cloud on-call engineers: ask natural-language
questions about runbooks, incidents, and operational knowledge and get **grounded answers with
citations**. Built as a hands-on study of *Designing Data-Intensive Applications* (DDIA) — the same
system is evolved through real scaling pressure, **10 → 10,000,000 users**.

## Stage 1 — "10 users: single team, single region" (current)

The simplest thing that works, end-to-end, fully local at **\$0**:

```
Next.js (TypeScript)  ──POST /api/chat──▶  Azure Functions (C#, .NET 9 isolated)
                                                │
                                   embed question (IEmbeddingProvider)
                                                │
                                   search top-K (InMemoryVectorStore, cosine)
                                                │
                                   answer grounded in context (IChatProvider)
                                                ▼
                                   { answer, citations[], provider }
```

### Design seams (so later stages are config flips, not rewrites)
- **`IEmbeddingProvider`** — `local-hash` (offline feature hashing, \$0) ↔ `azure-openai-embeddings`
- **`IChatProvider`** — `local-extractive` (no LLM, stitches + cites) ↔ `azure-openai-chat`
- **`InMemoryVectorStore`** — brute-force cosine over an in-memory list; the index you can *see*.
  This is the O(n), single-node limitation that motivates the move to Azure AI Search (ADR-001).

### DDIA concepts made tangible here
- *Reliability/maintainability* — the provider seams and a health endpoint.
- *Indexes & secondary indexes* (Ch.3) — the vector store **is** a hand-built index.
- *Derived data* (Ch.3/11) — the index is a materialized view derived from source docs (the ingest pipeline).

## Run it locally

**API** (terminal 1):
```powershell
cd api
func start --port 7071
```
The sample runbook corpus auto-ingests at startup. Endpoints:
- `GET  /api/health` — index size + active providers
- `POST /api/chat`   — `{ "question": "...", "topK": 4 }`
- `POST /api/ingest` — start an async re-ingest run; returns `202` + a status URL
- `GET  /api/ingest/{instanceId}` — poll an ingest run's status/result

**Web** (terminal 2):
```powershell
cd web
npm run dev   # http://localhost:3000
```

## Switch to real Azure OpenAI
In `api/local.settings.json` set:
```
"AI_PROVIDER": "azureopenai",
"AZURE_OPENAI_ENDPOINT": "https://<your-resource>.openai.azure.com/",
"AZURE_OPENAI_KEY": "<key, or leave empty to use az login / managed identity>",
"AZURE_OPENAI_CHAT_DEPLOYMENT": "gpt-4o",
"AZURE_OPENAI_EMBEDDING_DEPLOYMENT": "text-embedding-3-small"
```

## Scale journey (roadmap)
| Tier | Headline | Key changes |
|------|----------|-------------|
| 10 | Single team, single region ✅ | In-memory index, local providers, synchronous RAG |
| 1,000 | Caching + token budgeting ✅ | Semantic cache, per-user rate limiting, daily token budget |
| 100,000 | Shared state + multi-tenant isolation | Redis-backed cache/limits ✅, Azure AI Search ✅, real Azure OpenAI embeddings ✅, Cosmos partitioned by tenantId ✅, async ingestion ✅ |
| 1M | Front Door + autoscale | Premium/Container Apps, WAF, multi-deployment OpenAI router |
| 10M | Global multi-region | Active-active, Cosmos multi-write, AI gateway (APIM) |

## Stage 2 — reliability & cost (1,000 users)
Three in-memory mechanisms guard the expensive embedding + LLM path. Each is behind an
interface so it can later be swapped for a distributed (Redis) implementation:

- **Semantic cache** (`Caching/`) — reuses a previous answer when an incoming question is
  cosine-similar (≥ threshold) to one already answered. A hit skips retrieval and the LLM
  entirely and does not draw down the caller's budget.
- **Per-user rate limiting** (`RateLimiting/`) — a token bucket per caller (`RATE_LIMIT_PER_MINUTE`)
  sheds excess load with `429` + `Retry-After` (DDIA backpressure).
- **Daily token budget** (`Budget/`) — caps estimated tokens per caller per UTC day
  (`DAILY_TOKEN_BUDGET`), resetting at midnight.

The caller is identified by the `X-User-Id` header (falling back to the forwarded client IP).
Responses carry `X-Cache`, `X-RateLimit-Remaining`, and `X-Budget-Remaining` headers.

## Stage 3-B — shared state (scale-out)
The three mechanisms above are now selectable between an in-memory backend (per-replica) and a
shared **Redis** backend via the `STATE_STORE` env var (`memory` default, or `redis` with a
`REDIS_CONNECTION` string) — same interfaces, no call-site changes:

- **Rate limiter** — an atomic Lua token-bucket in Redis, so per-user limits stay correct even
  when requests land on different replicas. This is the correctness fix that lets the API scale out.
- **Token budget** — an atomic `INCRBY` counter under a per-day key that expires at UTC midnight.
- **Semantic cache** — keyed on the normalized question text (exact match) in Redis; true vector
  similarity in Redis needs RediSearch (Azure Managed Redis / Enterprise) and is a future upgrade.

Redis itself runs as a single internal (TCP, non-public) Container App in the same environment, so
the API now scales to **1–3 replicas** (`infra/main.bicep`). For production, swap the self-hosted
Redis for Azure Managed Redis.

## Stage A — durable vector store + real embeddings (ADR-001)
The brute-force `InMemoryVectorStore` (O(n), single-node) is now one implementation behind a new
`IVectorStore` seam. The production path is **`AzureAISearchVectorStore`**, which holds the corpus
in an HNSW vector index in **Azure AI Search** — durable, shared across replicas, and sub-linear at
query time. Selected via the `VECTOR_STORE` env var (`memory` default, or `azuresearch` with a
`SEARCH_ENDPOINT`); the index name comes from `SEARCH_INDEX` (default `runbooks`).

Embedding and chat providers are now **decoupled** (`EMBEDDING_PROVIDER` / `CHAT_PROVIDER`), so each
stage can adopt managed services independently. Stage A flips embeddings to real
**Azure OpenAI `text-embedding-3-small`** (1536-dim) while chat stays local-extractive — retrieval
quality improves without an LLM bill. `IEmbeddingProvider` exposes `Dimensions`, which the Search
index is created from, so the vector schema always matches the embedder.

All access is passwordless: the API's user-assigned managed identity holds *Cognitive Services
OpenAI User* on the OpenAI account and *Search Service/Index Data Contributor* on the search service
(`DefaultAzureCredential` resolves it via `AZURE_CLIENT_ID`). Azure OpenAI is provisioned in
`eastus2` because the model SKU isn't offered in the app's primary region.

## Stage C — multi-tenant isolation with Cosmos DB
Tenants are now first-class. Each request resolves a tenant from the `X-Tenant-Id` header (or a
`?tenant=` query fallback), defaulting to `default` so the original single-tenant UX is unchanged.
Isolation is enforced in two layers:

- **System of record** — a new `IDocumentStore` seam backed by **`CosmosDocumentStore`**. Documents
  live in a single Cosmos container partitioned by **`/tenantId`**, so every read/write targets one
  logical partition and tenants never share data. Selected via `DOCUMENT_STORE` (`memory` default,
  or `cosmos` with `COSMOS_ENDPOINT` / `COSMOS_DATABASE` / `COSMOS_CONTAINER`).
- **Derived index** — the Azure AI Search index gains a filterable `tenantId` field, and vector
  (KNN) retrieval is **hard-filtered** to the caller's tenant. The semantic cache is also keyed by
  tenant, so one tenant can never be served another's cached answer.

Ingestion seeds one tenant per corpus folder: `api/data/runbooks` → the shared `default` tenant, and
`api/data/tenants/<tenantId>` → one folder per additional tenant (the demo ships an `acme` tenant).
`GET /api/documents` lists the calling tenant's documents straight from Cosmos. Access is passwordless:
the app's managed identity holds the **Cosmos DB Built-in Data Contributor** data-plane role; local
auth on the account is disabled. `scripts/verify-tenant-isolation.ps1` proves the boundary live.

## Stage D — asynchronous ingestion with Durable Functions
Re-embedding the corpus is slow and bursty (network calls to Azure OpenAI, writes to Cosmos +
Search). Doing it inline in `POST /api/ingest` blocked the caller for the whole run and had no
retries — a half-failed run left the index inconsistent. Ingestion is now an **orchestration**:

- `POST /api/ingest` only *starts* the run and returns **`202 Accepted`** with a status URL.
- The **orchestrator** resets the index once, then **fans out one activity per tenant** and fans the
  per-tenant results back in — tenants are seeded in parallel instead of serially.
- Each `SeedTenant` activity runs under a **retry policy** (3 attempts, exponential backoff), and
  Durable's checkpointing means a replica restart resumes the run instead of restarting it. This is
  safe across the API's 1–3 replicas because Durable serialises each orchestration via its control
  queues (backed by the existing `AzureWebJobsStorage` account — no new infrastructure).
- `GET /api/ingest/{instanceId}` returns the run's status and, once `Completed`, the summary
  (`{ documents, chunks, provider, tenants[] }`). The status route lives under `/api` so the web
  proxy forwards it (the API's ingress is internal-only, so Durable's built-in management webhooks
  aren't reachable from the browser).

The synchronous startup seed is unchanged — it still runs once on a cold boot when the index is
empty, so the demo works out of the box; explicit re-ingestion is what became asynchronous.

## Project layout
```
api/   Azure Functions (C#) — RAG core, providers, sample corpus
web/   Next.js (TypeScript) — chat UI with citations
```
