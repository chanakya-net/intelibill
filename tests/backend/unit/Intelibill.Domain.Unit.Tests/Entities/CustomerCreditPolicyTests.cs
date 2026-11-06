using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class CustomerCreditPolicyTests
{
    [Fact]
    public void New_customers_default_to_legacy_advisory_without_terms()
    {
        var customer = Customer.Create(Guid.NewGuid(), "A", "+911234567890", null);

        Assert.Equal(CreditPolicyMode.LegacyAdvisory, customer.CreditPolicyMode);
        Assert.Null(customer.CreditTermsDays);
    }

    [Fact]
    public void Enforced_policy_needs_a_positive_limit()
    {
        var customer = Customer.Create(Guid.NewGuid(), "A", "+911234567890", null, creditLimit: 0m);

        var result = customer.SetCreditPolicy(CreditPolicyMode.Enforced, null);

        Assert.True(result.IsError);
        Assert.Equal("Customer.EnforcedCreditRequiresLimit", result.FirstError.Code);
        Assert.Equal(CreditPolicyMode.LegacyAdvisory, customer.CreditPolicyMode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(366)]
    public void Terms_must_be_within_a_year(int days)
    {
        var customer = Customer.Create(Guid.NewGuid(), "A", "+911234567890", null, creditLimit: 100m);

        Assert.Equal("Customer.CreditTermsDaysInvalid", customer.SetCreditPolicy(CreditPolicyMode.Enforced, days).FirstError.Code);
    }

    [Fact]
    public void Undefined_mode_is_refused()
    {
        var customer = Customer.Create(Guid.NewGuid(), "A", "+911234567890", null, creditLimit: 100m);

        Assert.Equal("Customer.CreditPolicyModeInvalid", customer.SetCreditPolicy((CreditPolicyMode)9, null).FirstError.Code);
    }

    [Fact]
    public void Due_date_is_the_ist_issue_date_plus_terms()
    {
        // 20:00 UTC on 31 Oct is 01:30 IST on 1 Nov: the business date is 1 Nov.
        var sale = Sale.Create(Guid.NewGuid(), "A/2627/000001", Guid.NewGuid(), "A", "+911234567890", PaymentMethod.Credit,
            new DateTimeOffset(2026, 10, 31, 20, 0, 0, TimeSpan.Zero), 0m, 100m, 100m, 0m, []);

        sale.ApplyCreditTerms(30);

        Assert.Equal(new DateOnly(2026, 12, 1), sale.DueDate);
    }

    [Fact]
    public void Fully_paid_sale_never_gets_a_due_date()
    {
        var sale = Sale.Create(Guid.NewGuid(), "A/2627/000001", Guid.NewGuid(), "A", "+911234567890", PaymentMethod.Cash,
            DateTimeOffset.UtcNow, 100m, 0m, 100m, 0m, []);

        sale.ApplyCreditTerms(30);

        Assert.Null(sale.DueDate);
    }
}
