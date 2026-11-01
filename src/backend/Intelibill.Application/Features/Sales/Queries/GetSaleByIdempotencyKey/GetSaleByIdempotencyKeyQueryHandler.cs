using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Queries.GetSaleByIdempotencyKey;

/// <summary>
/// Resolves a checkout's idempotency key to the sale it committed, within the caller's own
/// (shop, actor) scope — the same scope <c>RecordSale</c> replays under. A miss means no
/// committed sale is visible yet, not that an in-flight submission can be abandoned: the
/// client converges by re-sending the identical request.
/// </summary>
public sealed class GetSaleByIdempotencyKeyQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISaleRepository saleRepository)
{
    public async Task<ErrorOr<Guid>> Handle(
        GetSaleByIdempotencyKeyQuery query,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(query.UserId, cancellationToken);
        if (user is null)
            return Error.NotFound("User.NotFound", "User not found.");

        var shop = await shopRepository.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(query.UserId, query.ShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var normalizedKey = query.IdempotencyKey.Trim();
        if (normalizedKey.Length == 0)
            return Errors.Sale.IdempotencyKeyRequired;
        if (normalizedKey.Length > 128)
            return Errors.Sale.IdempotencyKeyTooLong;

        var sale = await saleRepository.GetByIdempotencyKeyAsync(
            query.ShopId,
            query.UserId,
            normalizedKey,
            cancellationToken);

        return sale is null ? Errors.Sale.IdempotencyKeyNotFound : sale.Id;
    }
}
