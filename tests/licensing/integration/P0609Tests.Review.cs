using System.Net;
using System.Net.Http.Json;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0609Tests
{
    [Fact]
    public async Task PreviewBindsProposedAmountToReviewedBalance()
    {
        await using var fixture = await StartAsync(database, Instant);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000L);
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var preview = await PreviewAsync(billing, package.Account, request, 4500L, "Preserve");
        await AssertCodeAsync(await ApproveAsync(billing, package.Account, request, Command(preview, 6000L, "Preserve")),
            HttpStatusCode.Conflict, "Refund.PreviewStale");
        Assert.Equal(0L, await fixture.CountAsync("refund_approvals"));
        var intended = await PreviewAsync(billing, package.Account, request, 6000L, "Preserve");
        var command = Command(intended, 6000L, "Preserve");
        await ReadAsync(await ApproveAsync(billing, package.Account, request, command), HttpStatusCode.Created);
        await ReadAsync(await ApproveAsync(billing, package.Account, request, command), HttpStatusCode.OK);
        Assert.Equal(1L, await fixture.CountAsync("refund_approvals"));
    }

    [Fact]
    public async Task FirstDeliveryAndReplayRequireCompleteCommand()
    {
        await using var fixture = await StartAsync(database, Instant);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000L);
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var preview = await PreviewAsync(billing, package.Account, request, 4500L, "EndAt", Boundary);
        var original = Command(preview, 4500L, "EndAt", Boundary);
        foreach (var expected in new[] { HttpStatusCode.Created, HttpStatusCode.OK })
        {
            foreach (var (field, value) in new (string, object?)[]
                { ("expectedVersion", null), ("confirmed", false), ("confirmed", null), ("effectiveAt", Boundary.AddTicks(1)) })
            {
                var changed = new Dictionary<string, object?>(original);
                if (value is null) changed.Remove(field);
                else changed[field] = value;
                using var response = await ApproveAsync(billing, package.Account, request, changed);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
            await ReadAsync(await ApproveAsync(billing, package.Account, request, original), expected);
        }
        Assert.Equal(1L, await fixture.CountAsync("refund_approvals"));
    }

    [Theory]
    [InlineData("EndAt", 0)]
    [InlineData("TermAdjustment", 7)]
    public async Task RestoredPurchaseUsesAdjustedAccessEnd(string treatment, int graceDays)
    {
        await using var fixture = await StartAsync(database, Instant);
        var (_, paid, independent, request) = await TwoSubscriptionsAsync(fixture);
        var original = await TermAsync(paid.Shops[0]);
        var untouched = (await TermAsync(independent.Shops[0])).GetRawText();
        var term = original.GetProperty("termId").GetGuid();
        var stale = await RenewalQuoteAsync(paid, term, "Month");
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var preview = await PreviewAsync(billing, paid.Account, request, 4500L, treatment, Boundary);
        await ReadAsync(await ApproveAsync(billing, paid.Account, request, Command(preview, 4500L, treatment, Boundary)),
            HttpStatusCode.Created);
        Assert.Equal(original.GetProperty("accessEndsAt").GetDateTimeOffset(),
            (await TermAsync(paid.Shops[0])).GetProperty("accessEndsAt").GetDateTimeOffset());
        await P0610Tests.PumpRefundsAsync(fixture);
        using var rejected = await paid.Payer.Client.PostAsJsonAsync(P0301Tests.AcceptPath(stale), P0301Tests.Accept(stale));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        var restoredAt = Boundary.AddDays(graceDays).AddSeconds(1);
        fixture.At(restoredAt);
        var quote = await AcceptAsync(paid, await RenewalQuoteAsync(paid, term, "Month"));
        Assert.Equal("RestoredAccess", quote.StartRule);
        Assert.Equal(restoredAt, quote.PreviewStartsAt);
        Assert.Equal(12000L, quote.AmountPaise);
        var attempt = await StartPaymentAsync(paid, quote);
        await P0302Fixture.CaptureAsync(paid.Payer.Client, quote, attempt);
        for (var delivery = 0; delivery < 2; delivery++)
            P0302Fixture.AssertStatus(await P0302Fixture.VerifyAsync(paid.Payer.Client, quote, attempt),
                "Verified", "Fulfilled", "Applying");
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments", $"predecessor_term_id = '{term:D}'"));
        Assert.Equal(restoredAt, (await TermAsync(paid.Shops[0])).GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(untouched, (await TermAsync(independent.Shops[0])).GetRawText());
    }

    [Fact]
    public async Task ReceiptWaiterSeesCommittedWinner()
    {
        await using var fixture = await StartAsync(database, Instant);
        var (package, first, confirmation, order) = await RequestedAsync(fixture, 8000L);
        var second = await RequestRefundAsync(package, order, confirmation, 7000L);
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await using var winner = await SqlScope.OpenAsync(fixture, package.Account);
        await using var contender = await SqlScope.OpenAsync(fixture, package.Account);
        var approval = await winner.ApproveAsync(first, 8000, 12000, 1);
        var waiting = contender.ApproveAsync(second, 7000, 12000, 1);
        Assert.True(await winner.BlocksAsync(contender));
        await winner.DecideAsync(approval, first);
        await winner.CommitAsync();
        var refused = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => waiting);
        Assert.Equal("23514", refused.SqlState);
        Assert.Equal(1L, await fixture.CountAsync("refund_approvals", "amount_paise = 8000 AND remaining_after_paise = 4000"));
        Assert.Equal(1L, await fixture.CountAsync("refund_access_decisions", $"approval_id = '{approval:D}'"));
        Assert.Equal(8000L, await fixture.ScalarAsync(
            $"SELECT public.refund_receipt_occupied_paise('{package.Account:D}', '{confirmation:D}')"));
    }
}
