namespace HsnFinder.Api.Configuration;

public sealed class AzureAiSettings
{
    public string Endpoint { get; set; } = string.Empty;

    public string AgentName { get; set; } = string.Empty;

    public string AgentVersion { get; set; } = string.Empty;
}
