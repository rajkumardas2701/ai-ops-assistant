namespace AiOps.Api.Support;

/// <summary>
/// Approximates token counts without pulling in a real tokenizer. The ~4-characters-per-token
/// heuristic is accurate enough for budgeting/quota decisions; a real provider can later
/// report exact usage from its API response.
/// </summary>
public static class TokenEstimator
{
    public static int Estimate(string? text)
        => string.IsNullOrEmpty(text) ? 0 : Math.Max(1, text.Length / 4);
}
