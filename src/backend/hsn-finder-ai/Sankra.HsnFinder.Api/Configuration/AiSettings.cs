namespace HsnFinder.Api.Configuration;

public enum AiApi
{
    Responses,
    ChatCompletions
}

public sealed class AiSettings
{
    public const string SectionName = "Ai";

    public string Endpoint { get; set; } = "https://api.openai.com/v1";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public AiApi Api { get; set; } = AiApi.Responses;

    public string? ReasoningEffort { get; set; }

    public string? Verbosity { get; set; }

    public AiWebSearchSettings WebSearch { get; set; } = new();
}

public sealed class AiWebSearchSettings
{
    public bool Enabled { get; set; }

    public string? Country { get; set; }

    public string? ContextSize { get; set; }
}
