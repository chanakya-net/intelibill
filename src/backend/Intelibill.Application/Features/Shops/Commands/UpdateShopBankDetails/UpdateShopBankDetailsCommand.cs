namespace Intelibill.Application.Features.Shops.Commands.UpdateShopBankDetails;

public sealed record UpdateShopBankDetailsCommand(
    Guid UserId,
    Guid ShopId,
    string? BankName,
    string? AccountNumber,
    string? AccountType,
    string? IfscCode,
    string? AccountHolderName);
