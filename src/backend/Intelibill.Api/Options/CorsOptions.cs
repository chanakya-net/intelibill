namespace Intelibill.Api.Options;

public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>
    /// Exact origins, scheme and host and port, no trailing slash. Empty denies
    /// every cross-origin browser request, which is the right default: the SSR
    /// web app proxies <c>/api</c> from its own origin, and the mobile client is
    /// not a browser, so neither needs an entry here.
    /// </summary>
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];
}
