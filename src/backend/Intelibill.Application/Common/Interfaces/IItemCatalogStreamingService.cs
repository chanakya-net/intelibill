using ErrorOr;
using Intelibill.Application.Features.Items.DTOs;

namespace Intelibill.Application.Common.Interfaces;

public interface IItemCatalogStreamingService
{
    Task<ErrorOr<Success>> ValidateAccessAsync(
        Guid userId,
        Guid activeShopId,
        CancellationToken cancellationToken);

    IAsyncEnumerable<ItemCatalogEntryDto> StreamByShopAsync(
        Guid activeShopId,
        CancellationToken cancellationToken);
}
