namespace Intelibill.Application.Features.Items.Queries.GetItems;

public sealed record GetItemsQuery(Guid UserId, Guid ActiveShopId);
