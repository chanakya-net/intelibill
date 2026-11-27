namespace Intelibill.Application.Features.Suppliers.Commands.AddSupplier;

using Intelibill.Application.Common.Security;

public sealed record AddSupplierCommand(
    SessionClaims Session,
    string Name,
    string? ContactPersonName,
    string? ContactPersonPhone,
    string Address,
    string City,
    string State,
    string Pin,
    bool IsActive,
    bool IsPreferred)
{
    public Guid ActorUserId => Session.UserId;
    public Guid ActiveShopId => Session.ActiveShopId.GetValueOrDefault();
}
