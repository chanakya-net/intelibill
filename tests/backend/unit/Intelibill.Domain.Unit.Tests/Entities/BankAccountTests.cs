using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class BankAccountTests
{
    [Fact]
    public void Create_SetsAllProperties()
    {
        var ownerId = Guid.NewGuid();

        var account = BankAccount.Create(
            ownerId,
            "  HDFC Bank  ",
            "  1234567890  ",
            BankAccountType.Savings,
            "  HDFC0001234  ",
            "  Ravi Kumar  ");

        Assert.Equal(ownerId, account.OwnerUserId);
        Assert.Equal("HDFC Bank", account.BankName);
        Assert.Equal("1234567890", account.AccountNumber);
        Assert.Equal(BankAccountType.Savings, account.AccountType);
        Assert.Equal("HDFC0001234", account.IfscCode);
        Assert.Equal("Ravi Kumar", account.AccountHolderName);
    }

    [Fact]
    public void Create_WithNullOptionalFields_LeavesThemNull()
    {
        var account = BankAccount.Create(
            Guid.NewGuid(),
            "SBI",
            "9876543210",
            null,
            null,
            null);

        Assert.Null(account.AccountType);
        Assert.Null(account.IfscCode);
        Assert.Null(account.AccountHolderName);
    }

    [Fact]
    public void Create_TrimsWhitespaceFromOptionalStringFields()
    {
        var account = BankAccount.Create(
            Guid.NewGuid(),
            "SBI",
            "9876",
            null,
            "  SBIN0001234  ",
            "  Meera  ");

        Assert.Equal("SBIN0001234", account.IfscCode);
        Assert.Equal("Meera", account.AccountHolderName);
    }

    [Fact]
    public void Create_SetsNewId()
    {
        var account = BankAccount.Create(Guid.NewGuid(), "Bank", "ACC", null, null, null);
        Assert.NotEqual(Guid.Empty, account.Id);
    }
}
