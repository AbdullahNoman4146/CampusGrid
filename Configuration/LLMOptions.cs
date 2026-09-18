namespace CampusGrid.Configuration;

public class LLMOptions
{
    public const string SectionName = "LLM";

    public string Provider { get; set; } = "OpenAI"; // OpenAI-compatible, AzureOpenAI, or Local
    public string? ApiKey { get; set; }
    public string Endpoint { get; set; } = "https://api.groq.com/openai/v1";
    public string Model { get; set; } = "openai/gpt-oss-20b";
    public string? DeploymentName { get; set; }
    public string ApiVersion { get; set; } = "2024-02-15-preview";
    public int TimeoutSeconds { get; set; } = 20;
    public int RequestTimeoutSeconds { get; set; } = 28;
    public int MaxAttempts { get; set; } = 2;
}
