namespace CampusGrid.Configuration;

public class LLMOptions
{
    public const string SectionName = "LLM";

    public string Provider { get; set; } = "OpenAI"; // "OpenAI", "AzureOpenAI", "Local", "FallbackOnly"
    public string? ApiKey { get; set; }
    public string Endpoint { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-4o-mini";
    public string? DeploymentName { get; set; }
    public string ApiVersion { get; set; } = "2024-02-15-preview";
    public int TimeoutSeconds { get; set; } = 15;
    public bool EnableSemanticFallback { get; set; } = true;
}
