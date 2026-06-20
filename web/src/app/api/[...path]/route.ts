// Server-side proxy: forwards /api/* from the browser to the real API, read from
// API_BASE_URL at request time. This keeps the API on internal-only ingress (never
// exposed publicly) and sidesteps CORS, since the browser only ever talks to this origin.

const API_BASE = process.env.API_BASE_URL ?? "http://localhost:7071";

export const dynamic = "force-dynamic";

async function handler(
  req: Request,
  ctx: { params: Promise<{ path: string[] }> },
): Promise<Response> {
  const { path } = await ctx.params;
  const search = new URL(req.url).search;
  const target = `${API_BASE}/api/${path.join("/")}${search}`;

  const init: RequestInit = {
    method: req.method,
    headers: { "content-type": req.headers.get("content-type") ?? "application/json" },
    cache: "no-store",
  };
  if (req.method !== "GET" && req.method !== "HEAD") {
    init.body = await req.text();
  }

  try {
    const res = await fetch(target, init);
    const body = await res.text();
    const headers = new Headers({
      "content-type": res.headers.get("content-type") ?? "application/json",
    });
    // Pass through observability headers from the API (cache/rate-limit/budget signals).
    for (const h of ["x-cache", "x-ratelimit-remaining", "x-budget-remaining", "retry-after"]) {
      const v = res.headers.get(h);
      if (v) headers.set(h, v);
    }
    return new Response(body, { status: res.status, headers });
  } catch {
    return new Response(
      JSON.stringify({ error: "Upstream API unreachable." }),
      { status: 502, headers: { "content-type": "application/json" } },
    );
  }
}

export { handler as GET, handler as POST };
