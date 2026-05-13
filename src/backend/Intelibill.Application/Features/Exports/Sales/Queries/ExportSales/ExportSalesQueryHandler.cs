using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Wolverine.Attributes;

namespace Intelibill.Application.Features.Exports.Sales.Queries.ExportSales;

[WolverineHandler]
public sealed class ExportSalesQueryHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IShopRepository _shopRepository;

    public ExportSalesQueryHandler(
        IUserRepository userRepository,
        IShopRepository shopRepository)
    {
        _userRepository = userRepository;
        _shopRepository = shopRepository;
    }

    public async Task<ErrorOr<SalesExportResult>> Handle(
        ExportSalesQuery query,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(query.UserId, cancellationToken);
        if (user is null)
        {
            return Errors.General.NotFound(nameof(user), query.UserId);
        }

        var shop = await _shopRepository.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null)
        {
            return Errors.Shop.ShopNotFound;
        }

        var membership = await _shopRepository.GetMembershipAsync(
            query.UserId,
            query.ShopId,
            cancellationToken);
        if (membership is null)
        {
            return Errors.Shop.MembershipNotFound;
        }

        if (membership.Role == ShopRole.Staff)
        {
            return Errors.Export.UserIsNotOwnerOrManager;
        }

        // Placeholder: will dispatch to renderers in future slices
        return new SalesExportResult(
            Array.Empty<byte>(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"sales_export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx");
    }
}
