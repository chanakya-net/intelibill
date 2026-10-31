namespace Intelibill.Infrastructure.Options;

public sealed class AuthSessionOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// The recovery epoch this deployment serves. Changing it refuses every session and refresh
    /// credential stamped with the previous value, which is what a database restore needs: the
    /// restored rows name the old epoch.
    /// </summary>
    public string RecoveryEpoch { get; init; } = "1";
}
