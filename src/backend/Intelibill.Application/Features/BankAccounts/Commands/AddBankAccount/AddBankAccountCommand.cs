using Intelibill.Application.Common.Security;

namespace Intelibill.Application.Features.BankAccounts.Commands.AddBankAccount;

public sealed record AddBankAccountCommand(
    Guid ShopId,
    string? BankName,
    string? AccountNumber,
    string? AccountType,
    string? IfscCode,
    string? AccountHolderName)
{
    public SessionClaims? Session { get; init; }
}
