namespace Intelibill.Application.Features.SupplierLedger.Queries.GetSupplierEntries;

public sealed record GetSupplierEntriesQuery(Guid UserId, Guid ActiveShopId, Guid SupplierId);
