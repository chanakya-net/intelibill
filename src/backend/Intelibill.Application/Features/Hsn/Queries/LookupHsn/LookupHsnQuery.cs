namespace Intelibill.Application.Features.Hsn.Queries.LookupHsn;

public sealed record LookupHsnQuery(
    string ProductName,
    Guid ActorUserId,
    Guid ActiveShopId);
