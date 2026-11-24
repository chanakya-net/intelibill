using Intelibill.Infrastructure.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Billing;

public sealed class MerchantRefundRequestRelayTests
{
    [Fact]
    public void RecognizedStatusesPassAndUnrecognizedStatusesAreRejected()
    {
        var item = Item();
        foreach (var status in RefundRequestsV1.States)
            Assert.True(MerchantBillingClient.IsRefundRequestValid(item with { Status = status }, item.AccountId));

        foreach (var invalid in new[] { "", "Approved", "Refunded", "Suspended", "requested" })
            Assert.False(MerchantBillingClient.IsRefundRequestValid(item with { Status = invalid }, item.AccountId));
    }

    [Fact]
    public void RequestMustStayScopedBoundedAndCoherent()
    {
        var item = Item();
        Assert.True(MerchantBillingClient.IsRefundRequestValid(item, item.AccountId));
        Assert.False(MerchantBillingClient.IsRefundRequestValid(item, Guid.NewGuid()));
        Assert.False(MerchantBillingClient.IsRefundRequestValid(item with { SchemaVersion = 2 }, item.AccountId));
        Assert.False(MerchantBillingClient.IsRefundRequestValid(item with { RefundRequestId = Guid.Empty }, item.AccountId));
        Assert.False(MerchantBillingClient.IsRefundRequestValid(item with { OrderId = Guid.Empty }, item.AccountId));
        Assert.False(MerchantBillingClient.IsRefundRequestValid(item with { ConfirmationId = Guid.Empty }, item.AccountId));
        Assert.False(MerchantBillingClient.IsRefundRequestValid(item with { AmountPaise = 0 }, item.AccountId));
        Assert.False(MerchantBillingClient.IsRefundRequestValid(item with { AmountPaise = item.PaymentAmountPaise + 1 }, item.AccountId));
        Assert.False(MerchantBillingClient.IsRefundRequestValid(item with { PaymentAmountPaise = 0 }, item.AccountId));
        Assert.False(MerchantBillingClient.IsRefundRequestValid(item with { Currency = "USD" }, item.AccountId));
        Assert.False(MerchantBillingClient.IsRefundRequestValid(item with { Reason = " leading space" }, item.AccountId));
        Assert.False(MerchantBillingClient.IsRefundRequestValid(item with { Reason = "bad\nline" }, item.AccountId));
        Assert.False(MerchantBillingClient.IsRefundRequestValid(item with { Reason = new string('a', 501) }, item.AccountId));
        Assert.False(MerchantBillingClient.IsRefundRequestValid(item with { RequestedAt = item.RequestedAt.AddTicks(1) }, item.AccountId));
    }

    [Fact]
    public void PageValidatesOrderingDuplicatesCursorAndAfterEcho()
    {
        var account = Guid.NewGuid();
        var at = new DateTimeOffset(2028, 1, 31, 4, 30, 0, TimeSpan.Zero);
        var first = Item(account) with { RefundRequestId = Guid.Parse("00000000-0000-4000-8000-000000000001"), RequestedAt = at };
        var second = Item(account) with { RefundRequestId = Guid.Parse("00000000-0000-4000-8000-000000000002"), RequestedAt = at };

        Assert.True(MerchantBillingClient.IsRefundPageValid(new(account, [first, second], null), account, null));
        Assert.False(MerchantBillingClient.IsRefundPageValid(new(account, [second, first], null), account, null));
        Assert.False(MerchantBillingClient.IsRefundPageValid(new(account, [first, first], null), account, null));
        Assert.False(MerchantBillingClient.IsRefundPageValid(new(account, [first], null), account, first.RefundRequestId));
        Assert.False(MerchantBillingClient.IsRefundPageValid(new(account, [first, second], second.RefundRequestId), account, null));

        var full = Enumerable.Range(0, RefundRequestsV1.PageSize).Select(i => Item(account) with
        {
            RefundRequestId = Guid.Parse($"00000000-0000-4000-8000-{i + 1:000000000000}"),
            RequestedAt = at.AddSeconds(i),
        }).ToArray();
        Assert.True(MerchantBillingClient.IsRefundPageValid(new(account, full, full[^1].RefundRequestId), account, null));
        Assert.False(MerchantBillingClient.IsRefundPageValid(new(account, full, full[0].RefundRequestId), account, null));
    }

    private static RefundRequestV1 Item(Guid? account = null) => new(
        Guid.NewGuid(), account ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        RefundRequestsV1.Requested, 4500L, 12000L, "INR", "Duplicate charge check",
        new DateTimeOffset(2028, 1, 31, 4, 30, 0, TimeSpan.Zero));
}
