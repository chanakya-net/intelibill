using Intelibill.Application.Common.Security;

namespace Intelibill.Application.Features.Shops.Commands.UpdateShopBankDetails;

public sealed record UpdateShopBankDetailsCommand(
    Guid UserId,
    Guid ShopId,
    string? BankName,
    string? AccountNumber,
    string? AccountType,
    string? IfscCode,
    string? AccountHolderName)
{
    public SessionClaims? Session { get; init; }
}
