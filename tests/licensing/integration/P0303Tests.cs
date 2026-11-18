using System.Net;
using System.Net.Http.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0303Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task LostCreateResponseReconcilesOriginalAttempt()
    {
        var probe = new P0302Tests.GatewayProbe { LoseCreateResponse = true, LookupNotFound = true };
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        await using var fixture = await P0302Fixture.StartAsync(database, clock, services: probe.Install);
        var (payer, account, _, quote) = await fixture.AcceptedQuoteAsync(1);
        probe.Database = fixture.Hosts.LicensingOwner;
        probe.Account = account;
        var key = Guid.NewGuid();
        var started = await StartAsync(payer.Client, quote, HttpStatusCode.Created, key);
        AssertStatus(started, "PendingVerification", "NotFulfilled", "NotApplicable");
        var attempt = started.GetProperty("attemptId").GetGuid();
        var replay = await StartAsync(payer.Client, quote, HttpStatusCode.OK, key);
        Assert.Equal(attempt, replay.GetProperty("attemptId").GetGuid());
        using var changed = await payer.Client.PostAsJsonAsync(AttemptsPath(quote),
            Start(quote with { Revision = quote.Revision + 1 }, key));
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal("Billing.OperationPayloadMismatch", await P0206Fixture.CodeAsync(changed));
        using var replacement = await payer.Client.PostAsJsonAsync(AttemptsPath(quote), Start(quote));
        Assert.Equal(HttpStatusCode.Conflict, replacement.StatusCode);

        AssertStatus(await VerifyAsync(payer.Client, quote, attempt), "PendingVerification", "NotFulfilled", "NotApplicable");
        Assert.Equal(1, probe.LookupCalls);
        Assert.Equal(0L, await fixture.CountAsync("payment_attempt_sessions"));
        var outsider = await P0209Fixture.SignInAsync(fixture.Hosts, Intelibill.Domain.Enums.ShopRole.Owner);
        using var denied = await outsider.Client.PostAsJsonAsync(VerifyPath(quote, attempt), new { launcherOutcome = "Success" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var deniedReplay = await outsider.Client.PostAsJsonAsync(AttemptsPath(quote), Start(quote, key));
        Assert.Equal(HttpStatusCode.Forbidden, deniedReplay.StatusCode);
        Assert.Equal(1, probe.LookupCalls);

        clock.Now = quote.ExpiresAt.AddMinutes(2);
        probe.LookupNotFound = false;
        await CaptureOriginalSessionAsync(fixture, attempt);
        AssertStatus(await VerifyAsync(payer.Client, quote, attempt), "Verified", "Fulfilled", "Applying");
        Assert.Equal(probe.OriginalReference, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.LicensingOwner,
            $"SELECT provider_order_reference FROM public.payment_attempt_sessions WHERE attempt_id = '{attempt:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_sessions"));
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, quote, 1);
    }
    [Fact]
    public async Task UnresolvedAttemptPreventsOverlappingReplacementPurchase()
    {
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        await using var fixture = await P0302Fixture.StartAsync(database, clock);
        var (payer, account, shops, quote) = await fixture.AcceptedQuoteAsync(2);
        var another = await P0209Fixture.SignInAsync(fixture.Hosts, Intelibill.Domain.Enums.ShopRole.Owner);
        await P0213Fixture.LinkAsync(account, payer, another);
        await fixture.Delivery.DrainAsync(source: true);
        await fixture.Delivery.DrainAsync(source: false);
        var overlapping = await OtherAcceptedQuoteAsync(payer, quote, shops[0].ShopId);
        var original = await OtherAcceptedQuoteAsync(payer, quote, shops.Select(s => s.ShopId).ToArray());
        var disjoint = await OtherAcceptedQuoteAsync(payer, quote, another.ShopId);
        var attempt = (await StartAsync(payer.Client, original, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
        // A proof-free recheck that finds the order unpaid leaves it payable; the unresolved attempt still blocks overlap below.
        AssertStatus(await VerifyAsync(payer.Client, original, attempt), "AwaitingPayment", "NotFulfilled", "NotApplicable");

        using var replacement = await payer.Client.PostAsJsonAsync(AttemptsPath(overlapping), Start(overlapping));
        Assert.Equal(HttpStatusCode.Conflict, replacement.StatusCode);
        Assert.Equal(CheckoutPaymentsV1.AlreadyStarted, await P0206Fixture.CodeAsync(replacement));
        await StartAsync(payer.Client, disjoint, HttpStatusCode.Created);
        Assert.Equal(2L, await fixture.CountAsync("development_gateway_sessions"));
    }

    internal static async Task<CheckoutQuoteV1> OtherAcceptedQuoteAsync(P0209Fixture.Merchant payer, CheckoutQuoteV1 quote,
        params Guid[] shops)
    {
        var created = await P0301Tests.CreateQuoteAsync(payer.Client, quote.AccountId,
            new(Guid.NewGuid(), quote.PlanVersionId, "Month", shops, 0, 1));
        using var result = await payer.Client.PostAsJsonAsync(P0301Tests.AcceptPath(created), P0301Tests.Accept(created));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        return (await result.Content.ReadFromJsonAsync<CheckoutQuoteV1>())!;
    }
}
