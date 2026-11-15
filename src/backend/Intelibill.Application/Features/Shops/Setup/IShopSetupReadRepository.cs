using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Shops.Setup;

public sealed record ShopSetupMember(Shop Shop, ShopRole Role);
public sealed record ShopSetupFacts(bool HasVerifiedOwner, bool HasActiveGoods, bool HasActiveServices, bool HasBankDetails);
public sealed record ShopSetupGoodsCandidate(Item Item, InventoryBatch Batch, decimal? AggregateQuantity);

public interface IShopSetupReadRepository
{
    Task<ShopSetupMember?> GetMemberAsync(Guid shopId, Guid userId, CancellationToken ct);
    Task<ShopSetupFacts> GetFactsAsync(Guid shopId, CancellationToken ct);
    Task<IReadOnlyList<ShopSetupGoodsCandidate>> GetGoodsAsync(Guid shopId, int offset, int limit, CancellationToken ct);
    Task<IReadOnlyList<Service>> GetServicesAsync(Guid shopId, int offset, int limit, CancellationToken ct);
}
