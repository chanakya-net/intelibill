namespace InventoryAI.Infrastructure.Options;

public sealed class ExternalAuthOptions
{
    public const string SectionName = "ExternalAuth";

    public GoogleOptions Google { get; init; } = new();
    public MicrosoftOptions Microsoft { get; init; } = new();
    public FacebookOptions Facebook { get; init; } = new();
    public TwitterOptions Twitter { get; init; } = new();
    public AppleOptions Apple { get; init; } = new();

    public sealed class GoogleOptions
    {
        public string ClientId { get; init; } = string.Empty;
    }

    public sealed class MicrosoftOptions
    {
        // Use "common" to accept work/school + personal accounts, or a specific tenant GUID.
        public string TenantId { get; init; } = "common";
        public string ClientId { get; init; } = string.Empty;
    }

    public sealed class FacebookOptions
    {
        public string AppId { get; init; } = string.Empty;
        public string AppSecret { get; init; } = string.Empty;
    }

    public sealed class TwitterOptions
    {
        // No server-side secret needed — we validate the user's OAuth 2.0 access token
        // directly against the Twitter API v2 /users/me endpoint.
    }

    public sealed class AppleOptions
    {
        // The Service ID (bundle ID) registered in the Apple Developer portal.
        public string ClientId { get; init; } = string.Empty;
    }
}
