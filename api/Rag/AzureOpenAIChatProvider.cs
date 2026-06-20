using System.ClientModel;
using System.Text;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using AiOps.Api.Models;

namespace AiOps.Api.Rag;

/// <summary>
/// Real answer synthesis via Azure OpenAI chat completions. Used when AI_PROVIDER=azureopenai.
/// The system prompt forces grounding ("answer only from context") and inline [n] citations,
/// which is what keeps a RAG assistant trustworthy.
/// </summary>
public sealed class AzureOpenAIChatProvider : IChatProvider
{
    private readonly ChatClient _client;
    public string Name => "azure-openai-chat";

    public AzureOpenAIChatProvider(IOptions<AzureOpenAIOptions> options)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Endpoint))
            throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is required when AI_PROVIDER=azureopenai.");

        var azure = string.IsNullOrWhiteSpace(o.ApiKey)
            ? new AzureOpenAIClient(new Uri(o.Endpoint), new DefaultAzureCredential())
            : new AzureOpenAIClient(new Uri(o.Endpoint), new ApiKeyCredential(o.ApiKey));

        _client = azure.GetChatClient(o.ChatDeployment);
    }

    public async Task<string> CompleteAsync(string question, IReadOnlyList<DocumentChunk> context, CancellationToken ct = default)
    {
        if (context.Count == 0)
            return "I couldn't find anything relevant in the indexed runbooks to answer that.";

        var ctx = new StringBuilder();
        for (int i = 0; i < context.Count; i++)
            ctx.AppendLine($"[{i + 1}] (source: {context[i].Source})\n{context[i].Content}\n");

        const string system =
            "You are an operations assistant for on-call engineers. Answer ONLY using the provided context. " +
            "Cite sources inline like [1], [2]. If the context is insufficient, say so plainly. Be concise and actionable.";
        var user = $"Context:\n{ctx}\nQuestion: {question}";

        var resp = await _client.CompleteChatAsync(
            [new SystemChatMessage(system), new UserChatMessage(user)],
            cancellationToken: ct);

        return resp.Value.Content.Count > 0 ? resp.Value.Content[0].Text : string.Empty;
    }
}
