using Intelibill.Application.Common.Security;

namespace Intelibill.Application.Features.BankAccounts.Commands.DeleteBankAccount;

public sealed record DeleteBankAccountCommand(Guid Id, Guid ShopId)
{
    public SessionClaims? Session { get; init; }
}
