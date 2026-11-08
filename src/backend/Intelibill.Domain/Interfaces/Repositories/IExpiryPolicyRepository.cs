using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IExpiryPolicyRepository
{
    Task InitializeAsync(Guid shopId, Guid actor, DateTimeOffset now, CancellationToken ct);
    Task<ExpiryPolicyRevision?> GetInitialRevisionAsync(Guid shopId, CancellationToken ct);
    Task<ShopExpiryState?> GetStateAsync(Guid shopId, CancellationToken ct);
    Task<bool> ItemExistsAsync(Guid shopId, Guid itemId, CancellationToken ct);
    Task<EffectiveExpiryPolicy> GetEffectiveAsync(Guid shopId, Guid itemId, DateTimeOffset? asOf, CancellationToken ct);
    Task<ExpiryPolicyRevision?> GetLatestRevisionAsync(Guid shopId, Guid? itemId, DateTimeOffset? asOf, CancellationToken ct);
    Task AddRevisionAsync(ExpiryPolicyRevision revision, CancellationToken ct);
    Task<ExpiryReadinessPage> GetReadinessAsync(Guid shopId, DateOnly businessDate, int page, int pageSize, CancellationToken ct);
}

public sealed record ExpiryReadinessLot(Guid BatchId, Guid ItemId, string ItemName, string BatchNumber,
    DateOnly? ExpiryDate, decimal PhysicalQuantity, decimal QuarantinedQuantity, bool MissingDate, bool Expired);
public sealed record ExpiryReadinessPage(IReadOnlyList<ExpiryReadinessLot> Items, int TotalLots,
    int MissingDateLots, int ExpiredLots, int QuarantinedLots, decimal PhysicalQuantity, decimal QuarantinedQuantity);
