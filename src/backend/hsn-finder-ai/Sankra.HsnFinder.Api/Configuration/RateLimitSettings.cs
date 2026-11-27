namespace HsnFinder.Api.Configuration;

public sealed class RateLimitSettings
{
    public int PermitLimit { get; set; }

    public int WindowSeconds { get; set; }
}
