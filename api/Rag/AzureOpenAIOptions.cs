namespace AiOps.Api.Rag;

/// <summary>Configuration for the Azure OpenAI providers, bound from app settings.</summary>
public sealed class AzureOpenAIOptions
{
    public string Endpoint { get; set; } = "";

    /// <summary>Optional. If empty, DefaultAzureCredential (managed identity / az login) is used.</summary>
    public string ApiKey { get; set; } = "";

    public string ChatDeployment { get; set; } = "gpt-4o";

    public string EmbeddingDeployment { get; set; } = "text-embedding-3-small";

    /// <summary>Vector size produced by the embedding deployment (text-embedding-3-small = 1536).</summary>
    public int EmbeddingDimensions { get; set; } = 1536;
}
