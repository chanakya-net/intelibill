namespace Intelibill.Application.Features.Shops.Commands.UpdateShopBankDetails;

public sealed record UpdateShopBankDetailsCommand(
    Guid UserId,
    Guid ShopId,
    string? BankName,
    string? BankAccountNumber,
    string? BankAccountType,
    string? IfscCode,
    string? AccountHolderName);
