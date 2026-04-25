namespace Intelibill.Application.Features.BankAccounts.Commands.UpdateBankAccount;

public sealed record UpdateBankAccountCommand(
    Guid Id,
    Guid ShopId,
    string? BankName,
    string? AccountNumber,
    string? AccountType,
    string? IfscCode,
    string? AccountHolderName);
