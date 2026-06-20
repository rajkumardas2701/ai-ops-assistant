// Typed client for the AI Ops Assistant API. The base URL is injected at build time
// via NEXT_PUBLIC_API_BASE_URL so the same UI works against local Functions or Azure.

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

const BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:7071";

export async function ask(question: string, topK = 4): Promise<ChatResponse> {
  const res = await fetch(`${BASE_URL}/api/chat`, {
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
  const res = await fetch(`${BASE_URL}/api/health`, { cache: "no-store" });
  if (!res.ok) throw new Error(`Health check failed (${res.status})`);
  return (await res.json()) as HealthResponse;
}
