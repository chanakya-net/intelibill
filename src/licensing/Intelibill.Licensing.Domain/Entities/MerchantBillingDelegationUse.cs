namespace Intelibill.Licensing.Domain.Entities;

public sealed record MerchantBillingDelegationUse(
    string Issuer, Guid ReplayId, Guid AccountId, Guid ActorId, string Action,
    Guid RequestId, string KeyId, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, DateTimeOffset ConsumedAt);
