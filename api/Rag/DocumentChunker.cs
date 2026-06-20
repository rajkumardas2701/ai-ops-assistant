using System.Text;
using AiOps.Api.Models;

namespace AiOps.Api.Rag;

/// <summary>
/// Splits a document into overlapping, size-bounded chunks. Chunking matters for RAG:
/// chunks too large dilute relevance and blow the token budget; too small lose context.
/// We pack paragraphs up to a char budget and carry a small overlap so ideas that straddle
/// a boundary are still retrievable.
/// </summary>
public static class DocumentChunker
{
    public static IReadOnlyList<DocumentChunk> Chunk(
        string docId, string title, string source, string text, int maxChars = 800, int overlap = 100)
    {
        var chunks = new List<DocumentChunk>();
        // Normalize line endings so paragraph splitting works regardless of OS (\r\n vs \n).
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var paragraphs = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var buffer = new StringBuilder();
        int idx = 0;

        void Flush()
        {
            if (buffer.Length == 0) return;
            chunks.Add(new DocumentChunk
            {
                Id = $"{docId}::{idx}",
                DocId = docId,
                Title = title,
                Source = source,
                Content = buffer.ToString().Trim(),
            });
            idx++;
        }

        foreach (var p in paragraphs)
        {
            if (buffer.Length + p.Length > maxChars && buffer.Length > 0)
            {
                var tail = buffer.ToString();
                Flush();
                buffer.Clear();
                if (overlap > 0 && tail.Length > overlap)
                    buffer.Append(tail[^overlap..]).Append("\n\n");
            }
            buffer.Append(p).Append("\n\n");
        }
        Flush();

        return chunks;
    }
}
