namespace Intelibill.Application.Features.BankAccounts.Commands.AddBankAccount;

public sealed record AddBankAccountCommand(
    Guid OwnerUserId,
    string? BankName,
    string? AccountNumber,
    string? AccountType,
    string? IfscCode,
    string? AccountHolderName);
