namespace Intelibill.Licensing.Domain.Entities;

/// <summary>
/// A commercial billing account. It belongs to the merchant users who are its members, never to a shop,
/// email address, phone number or GSTIN, so nothing groups shops or logins into it implicitly.
/// </summary>
public sealed class BillingAccount
{
    private BillingAccount(Guid id, string name, Guid createdByMerchantUserId, Guid creationKey,
        byte[] creationRequestHash, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        CreatedByMerchantUserId = createdByMerchantUserId;
        CreationKey = creationKey;
        CreationRequestHash = creationRequestHash;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public Guid CreatedByMerchantUserId { get; private set; }
    public Guid CreationKey { get; private set; }
    public byte[] CreationRequestHash { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Create the account together with its single active owner: the creating merchant user.</summary>
    public static (BillingAccount Account, BillingAccountMember Owner) Create(Guid id, string name, Guid creator,
        Guid creationKey, byte[] creationRequestHash, DateTimeOffset createdAt)
    {
        var account = new BillingAccount(id, name, creator, creationKey, creationRequestHash, createdAt);
        return (account, BillingAccountMember.FirstOwner(id, creator, createdAt));
    }
}
