namespace Intelibill.Application.Features.Shops.Commands.AddShopBankAccount;

public sealed record AddShopBankAccountCommand(
    Guid UserId,
    Guid ShopId,
    string? BankName,
    string? AccountNumber,
    string? AccountType,
    string? IfscCode,
    string? AccountHolderName);
