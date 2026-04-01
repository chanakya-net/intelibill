namespace Intelibill.Infrastructure.Options;

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
        public bool Enabled { get; init; }
        public string ClientId { get; init; } = string.Empty;
        public string ClientSecret { get; init; } = string.Empty;
        public string RedirectUri { get; init; } = string.Empty;
        public string Scope { get; init; } = "openid email profile";
        public string AuthorizationEndpoint { get; init; } = "https://accounts.google.com/o/oauth2/v2/auth";
        public string TokenEndpoint { get; init; } = "https://oauth2.googleapis.com/token";
    }

    public sealed class MicrosoftOptions
    {
        // Use "common" to accept work/school + personal accounts, or a specific tenant GUID.
        public string TenantId { get; init; } = "common";
        public string ClientId { get; init; } = string.Empty;
    }

    public sealed class FacebookOptions
    {
        public bool Enabled { get; init; }
        public string AppId { get; init; } = string.Empty;
        public string AppSecret { get; init; } = string.Empty;
        public string RedirectUri { get; init; } = string.Empty;
        public string Scope { get; init; } = "email";
        public string AuthorizationEndpoint { get; init; } = "https://www.facebook.com/v19.0/dialog/oauth";
        public string TokenEndpoint { get; init; } = "https://graph.facebook.com/v19.0/oauth/access_token";
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
