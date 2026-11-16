namespace Intelibill.Licensing.Domain.Entities;

/// <summary>The one standard trial for a stable merchant shop, retained after expiry.</summary>
public sealed record TrialHistory(Guid ShopId, Guid AccountId, Guid TermId, Guid EventId, Guid OperationKey,
    Guid ActorId, Guid MembershipId, long AuthorityVersion, Guid PolicyId, int PolicyRevision, string TermsVersion,
    int MemberCap, int AccountTrialCap, int ActiveMembers, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    DateTimeOffset AppliedAt, string ConsentDigest, bool DevelopmentFixture)
{
    public const string ComputationVersion = "shop-trial/1";
}
