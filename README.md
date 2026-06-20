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
- `POST /api/ingest` — rebuild the index from `api/data/runbooks/*.md`

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
| 1,000 | Caching + token budgeting | Semantic cache, per-user rate limiting |
| 100,000 | Multi-tenant isolation | Azure AI Search, Cosmos partitioned by tenantId, async ingestion |
| 1M | Front Door + autoscale | Premium/Container Apps, WAF, multi-deployment OpenAI router |
| 10M | Global multi-region | Active-active, Cosmos multi-write, AI gateway (APIM) |

## Project layout
```
api/   Azure Functions (C#) — RAG core, providers, sample corpus
web/   Next.js (TypeScript) — chat UI with citations
```
