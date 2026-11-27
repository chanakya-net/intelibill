using System.Net;
using System.Net.Http.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0302Tests
{
    [Fact]
    public async Task BrowserSuccessWithoutCapturedFundsNeverGrantsATerm()
    {
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        await using var fixture = await P0302Fixture.StartAsync(database, clock);
        var (payer, _, _, quote) = await fixture.AcceptedQuoteAsync(2);
        var attempt = (await StartAsync(payer.Client, quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();

        var claimed = await VerifyAsync(payer.Client, quote, attempt, launcherOutcome: "Success");
        AssertStatus(claimed, "PendingVerification", "NotFulfilled", "NotApplicable");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, claimed.GetProperty("launch").ValueKind);
        AssertStatus(await StatusAsync(payer.Client, quote), "PendingVerification", "NotFulfilled", "NotApplicable");
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await fixture.CountAsync("payment_fulfilments"));
        Assert.Equal(0L, await fixture.CountAsync("subscription_term_facts"));
        Assert.Equal(1L, await fixture.CountAsync("payment_events", "kind = 'VerificationRequested'"));

        // Capture and verification after the quote window and after the plan retired still use the frozen order.
        clock.Now = quote.ExpiresAt.AddMinutes(30);
        await P0301Tests.RetirePlanAsync(fixture.Trial, quote);
        await CaptureAsync(payer.Client, quote, attempt);
        var verified = await VerifyAsync(payer.Client, quote, attempt);
        AssertStatus(verified, "Verified", "Fulfilled", "Applying");
        Assert.Equal(quote.AmountPaise, verified.GetProperty("amountPaise").GetInt64());
        await AssertSingleFulfilmentAsync(fixture, quote, 2);
    }

    [Fact]
    public async Task AttemptStartsOnlyInsideTheAcceptedQuoteWindow()
    {
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        await using var fixture = await P0302Fixture.StartAsync(database, clock);
        var (payer, _, _, quote) = await fixture.AcceptedQuoteAsync(1);

        clock.Now = quote.ExpiresAt;
        Assert.False((await StatusAsync(payer.Client, quote)).GetProperty("canStart").GetBoolean());
        using (var late = await payer.Client.PostAsJsonAsync(AttemptsPath(quote), Start(quote)))
        {
            Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
            Assert.Equal(CheckoutOrdersV1.Expired, await P0206Fixture.CodeAsync(late));
        }
        Assert.Equal(0L, await fixture.CountAsync("payment_attempts"));

        clock.Now = quote.ExpiresAt.AddMicroseconds(-1);
        var key = Guid.NewGuid();
        var started = await StartAsync(payer.Client, quote, HttpStatusCode.Created, key);
        var replay = await StartAsync(payer.Client, quote, HttpStatusCode.OK, key);
        Assert.Equal(started.GetProperty("attemptId").GetGuid(), replay.GetProperty("attemptId").GetGuid());

        using var changed = await payer.Client.PostAsJsonAsync(AttemptsPath(quote),
            new { operationKey = key, expectedRevision = 2, snapshotHash = quote.SnapshotHash });
        Assert.Equal("Billing.OperationPayloadMismatch", await P0206Fixture.CodeAsync(changed));
        using var second = await payer.Client.PostAsJsonAsync(AttemptsPath(quote), Start(quote));
        Assert.Equal(CheckoutPaymentsV1.AlreadyStarted, await P0206Fixture.CodeAsync(second));
        Assert.Equal(1L, await fixture.CountAsync("payment_attempts"));
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_sessions"));
    }

    [Fact]
    public async Task UnacceptedQuoteCannotStartPayment()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new P0201Clock { Now = P0210Fixture.Start });
        var (payer, account, _, accepted) = await fixture.AcceptedQuoteAsync(1);
        var plan = accepted.PlanVersionId;
        var other = await P0209Fixture.SignInAsync(fixture.Hosts, Intelibill.Domain.Enums.ShopRole.Owner);
        await P0213Fixture.LinkAsync(account, payer, other);
        var quote = await P0301Tests.CreateQuoteAsync(payer.Client, account,
            new(Guid.NewGuid(), plan, "Month", [other.ShopId], 0, 1));

        using var refused = await payer.Client.PostAsJsonAsync(AttemptsPath(quote), Start(quote));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(CheckoutPaymentsV1.NotAccepted, await P0206Fixture.CodeAsync(refused));
        Assert.False((await StatusAsync(payer.Client, quote)).GetProperty("canStart").GetBoolean());
        Assert.Equal(0L, await fixture.CountAsync("payment_attempts"));
    }
}
