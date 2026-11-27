using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class BankAccount : BaseEntity
{
    public Guid ShopId { get; private set; }
    public string BankName { get; private set; } = string.Empty;
    public string AccountNumber { get; private set; } = string.Empty;
    public BankAccountType? AccountType { get; private set; }
    public string? IfscCode { get; private set; }
    public string? AccountHolderName { get; private set; }

    private BankAccount() { }

    public static BankAccount Create(
        Guid shopId,
        string bankName,
        string accountNumber,
        BankAccountType? accountType,
        string? ifscCode,
        string? accountHolderName)
    {
        return new BankAccount
        {
            ShopId = shopId,
            BankName = bankName.Trim(),
            AccountNumber = accountNumber.Trim(),
            AccountType = accountType,
            IfscCode = ifscCode?.Trim(),
            AccountHolderName = accountHolderName?.Trim()
        };
    }

    public void Update(
        string bankName,
        string accountNumber,
        BankAccountType? accountType,
        string? ifscCode,
        string? accountHolderName)
    {
        BankName = bankName.Trim();
        AccountNumber = accountNumber.Trim();
        AccountType = accountType;
        IfscCode = ifscCode?.Trim();
        AccountHolderName = accountHolderName?.Trim();
    }
}
