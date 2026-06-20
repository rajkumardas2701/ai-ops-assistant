using System.Text;
using AiOps.Api.Models;

namespace AiOps.Api.Rag;

/// <summary>
/// Offline, $0 "chat" that composes a grounded, extractive answer from retrieved context.
/// No LLM is involved — it stitches the most relevant chunks together with citation markers
/// so the full RAG flow (retrieve -> ground -> cite) runs locally. Switch AI_PROVIDER to
/// azureopenai for synthesized natural-language answers.
/// </summary>
public sealed class LocalChatProvider : IChatProvider
{
    public string Name => "local-extractive";

    public Task<string> CompleteAsync(string question, IReadOnlyList<DocumentChunk> context, CancellationToken ct = default)
    {
        if (context.Count == 0)
        {
            return Task.FromResult(
                "I couldn't find anything relevant in the indexed runbooks to answer that.");
        }

        var sb = new StringBuilder();
        sb.AppendLine("Based on the indexed operational docs, here's the most relevant guidance:");
        sb.AppendLine();

        for (int i = 0; i < context.Count; i++)
        {
            sb.AppendLine($"{i + 1}. {Summarize(context[i].Content)} [{i + 1}]");
        }

        sb.AppendLine();
        sb.Append("(Local extractive mode — set AI_PROVIDER=azureopenai for synthesized answers.)");
        return Task.FromResult(sb.ToString());
    }

    private static string Summarize(string content)
    {
        var trimmed = content.Replace('\n', ' ').Trim();
        var sentences = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var s = string.Join(". ", sentences.Take(2)).Trim();
        if (s.Length > 280) s = s[..280] + "…";
        else if (!s.EndsWith('.') && !s.EndsWith('…')) s += ".";
        return s;
    }
}
