"use client";

import { useEffect, useRef, useState } from "react";
import { ask, getHealth, type Citation } from "@/lib/api";

interface Turn {
  role: "user" | "assistant";
  text: string;
  citations?: Citation[];
  provider?: string;
}

const SAMPLE_QUESTIONS = [
  "How do I fix Cosmos DB 429 throttling from a hot partition?",
  "App Service CPU is at 95% — what should I do?",
  "How do I roll back a bad deployment safely?",
  "Redis cache keeps evicting keys and timing out.",
];

export default function Home() {
  const [question, setQuestion] = useState("");
  const [turns, setTurns] = useState<Turn[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [health, setHealth] = useState<string>("connecting…");
  const endRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    getHealth()
      .then((h) =>
        setHealth(
          `${h.indexedChunks} chunks · ${h.embeddingProvider} / ${h.chatProvider}`,
        ),
      )
      .catch(() => setHealth("API offline — start the Functions host on :7071"));
  }, []);

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [turns, loading]);

  async function submit(q: string) {
    const trimmed = q.trim();
    if (!trimmed || loading) return;
    setError(null);
    setQuestion("");
    setTurns((t) => [...t, { role: "user", text: trimmed }]);
    setLoading(true);
    try {
      const res = await ask(trimmed);
      setTurns((t) => [
        ...t,
        {
          role: "assistant",
          text: res.answer,
          citations: res.citations,
          provider: res.provider,
        },
      ]);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Something went wrong.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <main className="mx-auto flex min-h-screen w-full max-w-3xl flex-col px-4 py-8">
      <header className="mb-6 border-b border-black/10 pb-4 dark:border-white/10">
        <h1 className="text-2xl font-semibold tracking-tight">
          AI Operations Assistant
        </h1>
        <p className="mt-1 text-sm opacity-70">
          Grounded answers over runbooks &amp; incidents, with citations.
        </p>
        <p className="mt-2 font-mono text-xs opacity-50">index: {health}</p>
      </header>

      {turns.length === 0 && (
        <div className="mb-6">
          <p className="mb-3 text-sm opacity-60">Try one of these:</p>
          <div className="flex flex-col gap-2">
            {SAMPLE_QUESTIONS.map((s) => (
              <button
                key={s}
                onClick={() => submit(s)}
                className="rounded-lg border border-black/10 px-3 py-2 text-left text-sm transition-colors hover:bg-black/5 dark:border-white/15 dark:hover:bg-white/5"
              >
                {s}
              </button>
            ))}
          </div>
        </div>
      )}

      <div className="flex flex-1 flex-col gap-4">
        {turns.map((turn, i) => (
          <div
            key={i}
            className={turn.role === "user" ? "self-end" : "self-start"}
          >
            <div
              className={
                turn.role === "user"
                  ? "max-w-prose rounded-2xl bg-blue-600 px-4 py-2 text-sm text-white"
                  : "max-w-prose rounded-2xl border border-black/10 px-4 py-3 text-sm dark:border-white/15"
              }
            >
              <p className="whitespace-pre-wrap leading-relaxed">{turn.text}</p>

              {turn.citations && turn.citations.length > 0 && (
                <div className="mt-3 border-t border-black/10 pt-2 dark:border-white/10">
                  <p className="mb-1 text-xs font-medium opacity-60">Sources</p>
                  <ol className="flex flex-col gap-1">
                    {turn.citations.map((c, idx) => (
                      <li key={c.docId + idx} className="text-xs opacity-75">
                        <span className="font-mono">[{idx + 1}]</span>{" "}
                        <span className="font-medium">{c.title}</span>{" "}
                        <span className="opacity-50">
                          ({c.source} · score {c.score})
                        </span>
                      </li>
                    ))}
                  </ol>
                </div>
              )}

              {turn.provider && (
                <p className="mt-2 font-mono text-[10px] opacity-40">
                  {turn.provider}
                </p>
              )}
            </div>
          </div>
        ))}

        {loading && (
          <div className="self-start rounded-2xl border border-black/10 px-4 py-3 text-sm opacity-60 dark:border-white/15">
            Thinking…
          </div>
        )}
        <div ref={endRef} />
      </div>

      {error && (
        <p className="mt-3 rounded-lg bg-red-500/10 px-3 py-2 text-sm text-red-600 dark:text-red-400">
          {error}
        </p>
      )}

      <form
        onSubmit={(e) => {
          e.preventDefault();
          submit(question);
        }}
        className="sticky bottom-0 mt-4 flex gap-2 bg-[var(--background)] py-3"
      >
        <input
          value={question}
          onChange={(e) => setQuestion(e.target.value)}
          placeholder="Ask about an incident, runbook, or alert…"
          className="flex-1 rounded-xl border border-black/15 bg-transparent px-4 py-2 text-sm outline-none focus:border-blue-500 dark:border-white/20"
        />
        <button
          type="submit"
          disabled={loading || !question.trim()}
          className="rounded-xl bg-blue-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-blue-500 disabled:opacity-40"
        >
          Ask
        </button>
      </form>
    </main>
  );
}
