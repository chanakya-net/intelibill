using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Queries.GetSaleByReturnNumber;

public sealed class GetSaleByReturnNumberQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISaleReturnRepository saleReturnRepository)
{
    public async Task<ErrorOr<Guid>> Handle(
        GetSaleByReturnNumberQuery query,
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

        var saleReturn = await saleReturnRepository.GetByReturnNumberAsync(
            query.ShopId,
            query.ReturnNumber.Trim(),
            cancellationToken);

        return saleReturn is null
            ? Error.NotFound("SaleReturn.NotFound", $"Sale return '{query.ReturnNumber}' was not found.")
            : saleReturn.SaleId;
    }
}
