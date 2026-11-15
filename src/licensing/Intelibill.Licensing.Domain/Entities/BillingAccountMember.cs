namespace Intelibill.Licensing.Domain.Entities;

public enum BillingAccountRole
{
    Owner = 1,
    Manager = 2,
}

public enum BillingAccountMemberStatus
{
    Active = 1,
    Removed = 2,
}

/// <summary>
/// Commercial membership of a merchant user in a billing account. It grants billing actions only and never
/// grants access to any merchant shop or its data.
/// </summary>
public sealed class BillingAccountMember
{
    private BillingAccountMember(Guid id, Guid accountId, Guid merchantUserId, BillingAccountRole role,
        BillingAccountMemberStatus status, int revision, DateTimeOffset createdAt)
    {
        Id = id;
        AccountId = accountId;
        MerchantUserId = merchantUserId;
        Role = role;
        Status = status;
        Revision = revision;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid MerchantUserId { get; private set; }
    public BillingAccountRole Role { get; private set; }
    public BillingAccountMemberStatus Status { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    internal static BillingAccountMember FirstOwner(Guid accountId, Guid merchantUserId, DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(createdAt), accountId, merchantUserId, BillingAccountRole.Owner,
            BillingAccountMemberStatus.Active, 1, createdAt);
}
