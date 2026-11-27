using System.Text;
using System.Text.Json;
using Intelibill.Infrastructure.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Billing;

public sealed class MerchantRefundRequestRelayTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
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
    public void MixedPagesRetainLegacyRequestsAndRequireCoherentOnlineOutcomes()
    {
        var legacy = Item();
        var confirmed = Item(legacy.AccountId) with { RequestedAt = legacy.RequestedAt.AddSeconds(1),
            Status = "Confirmed", ApprovedAmountPaise = 2000, AccessDisposition = "NeedsReview", ProviderState = "Confirmed" };
        Assert.True(MerchantBillingClient.IsRefundPageValid(new(legacy.AccountId, [legacy, confirmed], null), legacy.AccountId, null));
        foreach (var invalid in new[] { confirmed with { Status = "Failed" }, confirmed with { ApprovedAmountPaise = 4501 },
            confirmed with { AccessDisposition = "AwaitingConfirmation" }, confirmed with { ProviderState = null } })
            Assert.False(MerchantBillingClient.IsRefundRequestValid(invalid, legacy.AccountId));
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

    [Fact]
    public void DeserializedUpstreamNullItemAndMissingOrNullReasonAreRejectedWithoutThrowing()
    {
        var account = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var confirmationId = Guid.NewGuid();
        Assert.False(MerchantBillingClient.IsRefundRequestValid(null, account));
        Assert.False(MerchantBillingClient.IsRefundRequestValid(Item(account) with { Reason = null! }, account));

        var nullItemPage = JsonSerializer.Deserialize<RefundRequestPageV1>(
            $$"""{"schemaVersion":1,"accountId":"{{account:D}}","items":[null],"nextCursor":null}""", WebJson);
        Assert.False(MerchantBillingClient.IsRefundPageValid(nullItemPage, account, null));

        var nullReasonJson = $$"""
            {"schemaVersion":1,"refundRequestId":"{{requestId:D}}","accountId":"{{account:D}}","orderId":"{{orderId:D}}",
             "confirmationId":"{{confirmationId:D}}","status":"Requested","amountPaise":4500,"paymentAmountPaise":12000,
             "currency":"INR","reason":null,"requestedAt":"2028-01-31T04:30:00Z"}
            """;
        var omittedReasonJson = $$"""
            {"schemaVersion":1,"refundRequestId":"{{requestId:D}}","accountId":"{{account:D}}","orderId":"{{orderId:D}}",
             "confirmationId":"{{confirmationId:D}}","status":"Requested","amountPaise":4500,"paymentAmountPaise":12000,
             "currency":"INR","requestedAt":"2028-01-31T04:30:00Z"}
            """;

        Assert.False(MerchantBillingClient.IsRefundRequestValid(
            JsonSerializer.Deserialize<RefundRequestV1>(nullReasonJson, WebJson), account));
        Assert.False(MerchantBillingClient.IsRefundRequestValid(
            JsonSerializer.Deserialize<RefundRequestV1>(omittedReasonJson, WebJson), account));
        Assert.False(MerchantBillingClient.IsRefundPageValid(JsonSerializer.Deserialize<RefundRequestPageV1>(
            $$"""{"schemaVersion":1,"accountId":"{{account:D}}","items":[{{nullReasonJson}}],"nextCursor":null}""", WebJson), account, null));
        Assert.False(MerchantBillingClient.IsRefundPageValid(JsonSerializer.Deserialize<RefundRequestPageV1>(
            $$"""{"schemaVersion":1,"accountId":"{{account:D}}","items":[{{omittedReasonJson}}],"nextCursor":null}""", WebJson), account, null));
    }

    [Fact]
    public void CreateBodyLimitRejectsEscapedExpansionAboveMaxCreateBytesAndAllowsMaxMultilingualReason()
    {
        var oversizedReason = new string('अ', 700);
        var oversizedRequest = new CreateRefundRequestV1(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1000L, oversizedReason);
        Assert.True(Encoding.UTF8.GetByteCount(oversizedReason) < RefundRequestsV1.MaxCreateBytes);
        Assert.False(MerchantBillingClient.TrySerializeCreateRefundRequest(oversizedRequest, out var oversizedBody));
        Assert.True(oversizedBody.Length > RefundRequestsV1.MaxCreateBytes);

        var maxMultilingual = new CreateRefundRequestV1(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1000L,
            new string('अ', RefundRequestsV1.MaxReasonLength));
        Assert.True(MerchantBillingClient.TrySerializeCreateRefundRequest(maxMultilingual, out var validBody));
        Assert.True(validBody.Length <= RefundRequestsV1.MaxCreateBytes);
    }

    private static RefundRequestV1 Item(Guid? account = null) => new(
        Guid.NewGuid(), account ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        RefundRequestsV1.Requested, 4500L, 12000L, "INR", "Duplicate charge check",
        new DateTimeOffset(2028, 1, 31, 4, 30, 0, TimeSpan.Zero));
}
