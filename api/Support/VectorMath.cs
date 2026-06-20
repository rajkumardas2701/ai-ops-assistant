namespace AiOps.Api.Support;

/// <summary>Shared vector helpers used by both retrieval and the semantic cache.</summary>
public static class VectorMath
{
    /// <summary>Cosine similarity of two equal-length vectors; 0 if lengths differ or are empty.</summary>
    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        if (na == 0 || nb == 0) return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
