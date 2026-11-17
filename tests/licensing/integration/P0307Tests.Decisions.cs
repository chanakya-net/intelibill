using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0307Tests
{
    [Fact]
    public async Task DistinctCapturedPaymentsRetainOneFulfilment()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Payments:Razorpay:WebhookSecret"] = P0305Tests.Secret,
            ["Payments:Razorpay:WebhookAccountId"] = P0305Tests.Account
        };
        await using var rig = await P0304Tests.Rig.StartAsync(database, extraSettings: settings);
        var attempt = await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        rig.Fake.AddPayment(order, "captured");
        AssertStatus(await rig.VerifyAsync(attempt, null, null), "Verified", "Fulfilled", "Applying");
        var duplicate = rig.Fake.AddPayment(order, "captured");
        using var webhook = rig.Fixture.Hosts.Licensing.CreateClient();
        var body = P0305Tests.Body(rig.Quote.AmountPaise, rig.Fixture.CheckoutClock.Now, order, duplicate);
        foreach (var eventId in new[] { "duplicate", "duplicate", "duplicate_again" })
        {
            using var response = await P0305Tests.SendAsync(webhook, body, eventId);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        await P0305Tests.DrainAsync(rig.Fixture.Hosts.Licensing.Services, rig.Fixture.CheckoutClock);
        await P0302Tests.AssertSingleFulfilmentAsync(rig.Fixture, rig.Quote, 1);
        using var finance = await P0306Tests.SignInBillingAsync(rig.Fixture);
        var review = await SingleCaseAsync(finance, rig.Quote.AccountId);
        Assert.Equal("DuplicateFunds", review.GetProperty("classification").GetString());
    }

    [Fact]
    public async Task ReviewDecisionIsAuthorizedAuditedAndReplaySafe()
    {
        await using var rig = await P0304Tests.Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        rig.Fake.AddPayment(rig.Fake.OrderId(), "captured", amount: rig.Quote.AmountPaise + 1);
        await rig.VerifyAsync(attempt, null, null);
        using var finance = await P0306Tests.SignInBillingAsync(rig.Fixture);
        var account = rig.Quote.AccountId;
        var review = await SingleCaseAsync(finance, account);
        var path = $"{ReviewPath(account)}/{review.GetProperty("caseId").GetGuid():D}";
        var command = new { operationKey = Guid.NewGuid(), expectedVersion = 0L, action = "ReferForRefundReview",
            reason = "Observed overpayment on fixture statement", evidence = "STATEMENT-REFERENCE" };
        using var guest = rig.Fixture.Hosts.Licensing.CreateClient();
        using var anonymous = await guest.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var crossAccount = await finance.SendAsync(HttpMethod.Get, path.Replace(account.ToString("D"), Guid.NewGuid().ToString("D"), StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.NotFound, crossAccount.StatusCode);
        using var noCsrf = await finance.SendAsync(HttpMethod.Post, path + "/decisions", command);
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        using var first = await RaceDecisionsAsync(rig.Fixture, finance, path + "/decisions", command,
            command with { operationKey = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var saved = await first.Content.ReadFromJsonAsync<JsonElement>();
        var contract = ReviewContract();
        var expected = contract.GetProperty("decisions").EnumerateArray().Single(row =>
            row.GetProperty("action").GetString() == command.action);
        Assert.Equal(expected.GetProperty("disposition").GetString(), saved.GetProperty("disposition").GetString());
        Assert.Equal("no-store", first.Headers.CacheControl?.ToString());
        using var replay = await P0212Fixture.PostAsync(finance, path + "/decisions", command);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(saved.GetRawText(), (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
        using var stale = await P0212Fixture.PostAsync(finance, path + "/decisions", command with { operationKey = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var changed = await P0212Fixture.PostAsync(finance, path + "/decisions", command with { evidence = "OTHER-REFERENCE" });
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var owner = await rig.Fixture.Trial.Catalogue.SignInOwnerAsync();
        using var denied = await P0212Fixture.PostAsync(owner, path + "/decisions", command);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        rig.Fixture.Trial.Catalogue.Clock.Advance(TimeSpan.FromMinutes(4.5));
        using var mfa = await P0212Fixture.PostAsync(finance, path + "/decisions", command);
        Assert.Equal("ManualPayment.RecentMfaRequired", await P0206Fixture.CodeAsync(mfa));
        Assert.Equal(0L, await rig.Fixture.CountAsync("payment_fulfilments"));
        Assert.Equal(1L, await rig.Fixture.CountAsync("payment_review_decisions"));
    }

    private static async Task<HttpResponseMessage> RaceDecisionsAsync(P0302Fixture fixture, P0110CookieBrowser finance,
        string path, object firstCommand, object secondCommand)
    {
        await using var fence = await P0302Tests.FenceAsync(fixture, "payment_review_decisions");
        var first = P0212Fixture.PostAsync(finance, path, firstCommand);
        await P0302Tests.WaitForBlockedChainAsync(fixture, fence.Pid, 1);
        var second = P0212Fixture.PostAsync(finance, path, secondCommand);
        await P0302Tests.WaitForBlockedChainAsync(fixture, fence.Pid, 2);
        await fence.ReleaseAsync();
        using var refused = await second;
        Assert.Equal("PaymentReview.CaseVersionConflict", await P0206Fixture.CodeAsync(refused));
        return await first;
    }

    private static JsonElement ReviewContract() => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(
        Path.Combine(ProjectPaths.Root, "tests/contracts/licensing/payment-review-v1.json")));

    private static async Task<JsonElement> SingleCaseAsync(P0110CookieBrowser finance, Guid account)
    {
        using var response = await finance.SendAsync(HttpMethod.Get, ReviewPath(account));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
    }
}
