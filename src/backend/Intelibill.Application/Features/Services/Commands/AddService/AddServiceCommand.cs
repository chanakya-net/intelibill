using Intelibill.Application.Common.Security;

namespace Intelibill.Application.Features.Services.Commands.AddService;

public sealed record AddServiceCommand(
    SessionClaims Session,
    string Name,
    string? Description,
    decimal Price,
    string? HsnCode,
    decimal TaxRatePercent,
    bool TaxIncluded,
    bool IsActive)
{
    public Guid ActorUserId => Session.UserId;
    public Guid ActiveShopId => Session.ActiveShopId.GetValueOrDefault();
}
