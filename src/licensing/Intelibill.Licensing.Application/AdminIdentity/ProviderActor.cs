namespace Intelibill.Licensing.Application.AdminIdentities;

/// <summary>
/// The authenticated provider principal as presented by a provider session. Its claims are
/// necessary but never sufficient: handlers re-check persisted authority before any write.
/// </summary>
public sealed record ProviderActor(Guid AdminId, DateTimeOffset MfaVerifiedAt)
{
    public const string InvitationCapability = "admin.invitations.manage";
    public static readonly TimeSpan RecentMfaWindow = TimeSpan.FromMinutes(5);

    public bool HasRecentMfa(DateTimeOffset now) =>
        MfaVerifiedAt <= now && now - MfaVerifiedAt < RecentMfaWindow;
}
