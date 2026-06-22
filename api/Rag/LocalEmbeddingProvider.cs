using System.Text;

namespace AiOps.Api.Rag;

/// <summary>
/// Deterministic, offline, $0 embedding using signed feature hashing (the "hashing trick").
/// Each token is hashed into a fixed-dimension vector; cosine similarity then approximates
/// term overlap. It is intentionally simple — the point is to make vector retrieval tangible
/// (DDIA Ch.3: indexes) without any external service. Swap in real embeddings later.
/// </summary>
public sealed class LocalEmbeddingProvider : IEmbeddingProvider
{
    private const int Dim = 384;
    public int Dimensions => Dim;
    public string Name => "local-hash";

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        => Task.FromResult(Embed(text));

    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        => Task.FromResult((IReadOnlyList<float[]>)texts.Select(Embed).ToList());

    private static float[] Embed(string text)
    {
        var vec = new float[Dim];
        foreach (var token in Tokenize(text))
        {
            var h = Fnv1a(token);
            var idx = (int)(h % Dim);
            var sign = ((h >> 31) & 1) == 0 ? 1f : -1f; // signed hashing reduces collision bias
            vec[idx] += sign;
        }
        Normalize(vec);
        return vec;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (sb.Length > 0)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    private static uint Fnv1a(string s)
    {
        uint hash = 2166136261;
        foreach (var ch in s)
        {
            hash ^= ch;
            hash *= 16777619;
        }
        return hash;
    }

    private static void Normalize(float[] v)
    {
        double sum = 0;
        foreach (var x in v) sum += x * x;
        var norm = Math.Sqrt(sum);
        if (norm < 1e-9) return;
        for (int i = 0; i < v.Length; i++) v[i] = (float)(v[i] / norm);
    }
}
