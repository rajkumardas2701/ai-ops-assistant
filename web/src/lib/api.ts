// Typed client for the AI Ops Assistant API. The browser always calls the Next.js
// server on the same origin (/api/*); a server-side proxy route forwards to the real
// API. This keeps the API private (internal ingress) and avoids CORS entirely.

export interface Citation {
  docId: string;
  title: string;
  source: string;
  score: number;
  snippet: string;
}

export interface ChatResponse {
  answer: string;
  citations: Citation[];
  provider: string;
  contextChunks: number;
}

export interface HealthResponse {
  status: string;
  indexedChunks: number;
  embeddingProvider: string;
  chatProvider: string;
}

export async function ask(question: string, topK = 4): Promise<ChatResponse> {
  const res = await fetch(`/api/chat`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ question, topK }),
  });
  if (!res.ok) {
    const text = await res.text();
    throw new Error(`Chat request failed (${res.status}): ${text}`);
  }
  return (await res.json()) as ChatResponse;
}

export async function getHealth(): Promise<HealthResponse> {
  const res = await fetch(`/api/health`, { cache: "no-store" });
  if (!res.ok) throw new Error(`Health check failed (${res.status})`);
  return (await res.json()) as HealthResponse;
}
