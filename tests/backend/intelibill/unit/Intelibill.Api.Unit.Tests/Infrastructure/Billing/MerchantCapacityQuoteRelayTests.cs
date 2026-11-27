using Intelibill.Infrastructure.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Billing;

public sealed class MerchantCapacityQuoteRelayTests
{
    [Fact]
    public void NonpayableQuoteCannotCarryEvenAZeroAmount()
    {
        var quote = Quote() with { Outcome = "AtRenewalOrCourtesy", AmountPaise = null };
        Assert.True(IsValid(quote));
        Assert.False(IsValid(quote with { AmountPaise = 0 }));
        Assert.False(IsValid(quote with { AmountPaise = -1 }));
    }

    [Fact]
    public void PaidQuoteRequiresAPositiveAmountAndCoherentTargetAndDates()
    {
        var quote = Quote();
        Assert.True(IsValid(quote));
        Assert.False(IsValid(quote with { AmountPaise = null }));
        Assert.False(IsValid(quote with { AmountPaise = 201 }));
        Assert.False(IsValid(quote with { TargetPlanId = Guid.Empty }));
        Assert.False(IsValid(quote with { TargetPlanVersion = 0 }));
        Assert.False(IsValid(quote with { TargetMemberAllowance = 0, PriorMemberAllowance = 0 }));
        Assert.False(IsValid(quote with { PriceBasis = "Unknown" }));
        Assert.False(IsValid(quote with { CalendarVersion = "Unknown" }));
        Assert.False(IsValid(quote with { PaidThrough = quote.PaidThrough.AddTicks(1) }));
        Assert.False(IsValid(quote with { ValidUntil = quote.CreatedAt.AddSeconds(-1) }));
    }

    private static bool IsValid(CapacityChangeQuoteV1 quote) => MerchantBillingClient.IsCapacityQuoteValid(
        quote with { SnapshotHash = CapacityChangeQuotesV1.SnapshotHash(quote) }, quote.AccountId, quote.TermId);

    private static CapacityChangeQuoteV1 Quote()
    {
        var started = new DateTimeOffset(2028, 1, 31, 4, 30, 0, TimeSpan.Zero);
        var evaluated = started.AddDays(14);
        var end = started.AddMonths(1);
        return new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Payable", null, ["AcceptIncrease"],
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, "Fixture growth", "Month", 3, 4, 5, 5, 1,
            new string('a', 64), new string('b', 64), 12000, 12200, 200, 103, "INR", "TaxInclusiveFinal",
            started, end, evaluated, 2505600, 1296000, "2028-01-31T10:00:00", 0, "Asia/Kolkata", "proration/1",
            "calendar-month-year/1", false, evaluated, evaluated.AddMinutes(15), evaluated.AddMinutes(15), true, "");
    }
}
