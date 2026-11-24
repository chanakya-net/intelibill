using System.Net;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0609Tests
{
    [Fact]
    public async Task ReplayRequiresLiveBillingAndFreshMfa()
    {
        await using var fixture = await StartAsync(database, Instant);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000L);
        var catalogue = fixture.Checkout.Trial.Catalogue;
        var seed = await P0306Tests.EnrollBillingAsync(fixture.Checkout, P0306Tests.BillingEmail);
        using var one = await catalogue.SignInOperatorAsync(seed, P0306Tests.BillingEmail);
        using var two = await catalogue.SignInOperatorAsync(seed, P0306Tests.BillingEmail);

        // The same key raced on two sessions of one operator settles once; the other answer is the saved receipt.
        var preview = await PreviewAsync(one, package.Account, request, 4500L, "Preserve");
        var command = Command(preview, 4500L, "Preserve");
        var raced = await Task.WhenAll(ApproveAsync(one, package.Account, request, command),
            ApproveAsync(two, package.Account, request, command));
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Created], raced.Select(r => r.StatusCode).Order().ToArray());
        var saved = await raced[0].Content.ReadAsStringAsync();
        Assert.Equal(saved, await raced[1].Content.ReadAsStringAsync());
        Assert.Equal(1L, await fixture.CountAsync("refund_approvals"));

        // A lost response is recovered by its key, from another session of the same operator, byte for byte.
        var recoverPath = $"{RequestPath(package.Account, request)}/approvals/operations/{command["operationKey"]:D}";
        using var recovered = await two.SendAsync(HttpMethod.Get, recoverPath);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.True(recovered.Headers.CacheControl?.NoStore);
        Assert.Equal(saved, await recovered.Content.ReadAsStringAsync());

        // The exact replay still answers after the receipt moved on; anything else under that key is a mismatch.
        var replay = await ReadAsync(await ApproveAsync(two, package.Account, request, command), HttpStatusCode.OK);
        Assert.Equal(4500L, replay.GetProperty("approvedAmountPaise").GetInt64());
        await AssertCodeAsync(await ApproveAsync(one, package.Account, request,
            new Dictionary<string, object?>(command) { ["amountPaise"] = 4400L }), HttpStatusCode.Conflict, "Refund.ApprovalPayloadMismatch");
        await AssertCodeAsync(await ApproveAsync(one, package.Account, request,
            new Dictionary<string, object?>(command) { ["reason"] = "Another reason" }), HttpStatusCode.Conflict, "Refund.ApprovalPayloadMismatch");
        // A new key cannot decide the same request twice.
        await AssertCodeAsync(await ApproveAsync(one, package.Account, request,
            new Dictionary<string, object?>(command) { ["operationKey"] = Guid.NewGuid() }), HttpStatusCode.Conflict, "Refund.AlreadyDecided");
        await AssertCodeAsync(await TryPreviewAsync(one, package.Account, request, 1L, "Preserve"),
            HttpStatusCode.Conflict, "Refund.AlreadyDecided");
        Assert.Equal(1L, await fixture.CountAsync("refund_approvals"));
        Assert.Equal(1L, await fixture.CountAsync("refund_access_decisions"));
    }

    [Fact]
    public async Task OnlyTheApprovingBillingOperatorRecoversAnApproval()
    {
        await using var fixture = await StartAsync(database, Instant);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000L);
        var other = await P0209Fixture.CreateAccountAsync(package.Payer.Client, "Unrelated Account");
        var catalogue = fixture.Checkout.Trial.Catalogue;
        var all = await BillingSessionsAsync(fixture, P0306Tests.BillingEmail, "second.billing@example.test", "support.norefund@example.test");
        var (alpha, beta, support) = (all[0], all[1], all[2]);
        using var sessions = new Disposables(all);

        var preview = await PreviewAsync(alpha, package.Account, request, 4500L, "Preserve");
        var command = Command(preview, 4500L, "Preserve");
        var approvals = RequestPath(package.Account, request) + "/approvals";
        var recoverPath = $"{approvals}/operations/{command["operationKey"]:D}";

        // Without Billing nothing is previewed, approved or recovered, before or after the approval exists.
        await AssertCodeAsync(await TryPreviewAsync(support, package.Account, request, 4500L, "Preserve"),
            HttpStatusCode.Forbidden, "ManualPayment.AccessDenied");
        await AssertCodeAsync(await ApproveAsync(support, package.Account, request, command),
            HttpStatusCode.Forbidden, "ManualPayment.AccessDenied");
        // A wrong account or request is a scoped 404, never another account's facts.
        await AssertCodeAsync(await ApproveAsync(alpha, other, request, command), HttpStatusCode.NotFound, "Refund.RequestNotFound");
        await AssertCodeAsync(await ApproveAsync(alpha, package.Account, Guid.NewGuid(), command),
            HttpStatusCode.NotFound, "Refund.RequestNotFound");
        // A changed version or digest is a stale preview.
        await AssertCodeAsync(await ApproveAsync(alpha, package.Account, request,
            new Dictionary<string, object?>(command) { ["expectedVersion"] = 1L }), HttpStatusCode.Conflict, "Refund.PreviewStale");
        await AssertCodeAsync(await ApproveAsync(alpha, package.Account, request,
            new Dictionary<string, object?>(command) { ["impactDigest"] = new string('a', 64) }), HttpStatusCode.Conflict, "Refund.PreviewStale");
        // Browser guards: foreign origin and a missing CSRF proof never reach the handler.
        using var foreign = await P0212Fixture.PostAsync(alpha, approvals, command, "https://foreign.example");
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        using var noCsrf = await alpha.SendAsync(HttpMethod.Post, approvals, command);
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        Assert.Equal(0L, await fixture.CountAsync("refund_approvals"));

        await ReadAsync(await ApproveAsync(alpha, package.Account, request, command), HttpStatusCode.Created);
        // Another Billing operator cannot replay or recover the owner-bound operation.
        await AssertCodeAsync(await ApproveAsync(beta, package.Account, request, command),
            HttpStatusCode.Conflict, "Refund.ApprovalPayloadMismatch");
        await AssertCodeAsync(await beta.SendAsync(HttpMethod.Get, recoverPath), HttpStatusCode.NotFound, "Refund.OperationNotFound");
        await AssertCodeAsync(await support.SendAsync(HttpMethod.Get, recoverPath), HttpStatusCode.Forbidden, "ManualPayment.AccessDenied");
        await AssertCodeAsync(await alpha.SendAsync(HttpMethod.Get,
            $"{RequestPath(other, request)}/approvals/operations/{command["operationKey"]:D}"), HttpStatusCode.NotFound, "Refund.OperationNotFound");
        await AssertCodeAsync(await alpha.SendAsync(HttpMethod.Get, $"{approvals}/operations/{Guid.NewGuid():D}"),
            HttpStatusCode.NotFound, "Refund.OperationNotFound");

        // A signed-out session replays nothing, even with the exact saved command.
        var stale = alpha.Fork(catalogue.Api.CreateClient(new() { BaseAddress = new Uri(P0110CookieBrowser.Origin), HandleCookies = false }));
        var proof = await P0206Fixture.ProofAsync(stale);
        using var signedOut = await P0212Fixture.PostAsync(alpha, "/api/admin/auth/sign-out", new { });
        Assert.True(signedOut.IsSuccessStatusCode, await signedOut.Content.ReadAsStringAsync());
        using var ended = await stale.SendAsync(HttpMethod.Post, approvals, command, proof);
        Assert.Equal(HttpStatusCode.Unauthorized, ended.StatusCode);
        using var endedRecovery = await stale.SendAsync(HttpMethod.Get, recoverPath);
        Assert.Equal(HttpStatusCode.Unauthorized, endedRecovery.StatusCode);
        stale.Dispose();
        Assert.Equal(1L, await fixture.CountAsync("refund_approvals"));
    }

    [Fact]
    public async Task MfaOlderThanFiveMinutesCannotPreviewApproveOrRecover()
    {
        await using var fixture = await StartAsync(database, Instant);
        var (package, request, confirmation, order) = await RequestedAsync(fixture, 6000L);
        var second = await RequestRefundAsync(package, order, confirmation, 1000L);
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var clock = fixture.Checkout.Trial.Catalogue.Clock;
        var verified = (DateTime)(await fixture.ScalarAsync(
            "SELECT mfa_verified_at FROM public.admin_sessions ORDER BY mfa_verified_at DESC LIMIT 1"))!;
        var limit = new DateTimeOffset(DateTime.SpecifyKind(verified, DateTimeKind.Utc)).AddMinutes(5);
        var preview = await PreviewAsync(billing, package.Account, request, 4500L, "Preserve");
        var command = Command(preview, 4500L, "Preserve");

        // 4:59.999999 after MFA is still recent: the approval is created.
        clock.Advance(limit.AddTicks(-10) - clock.GetUtcNow());
        await ReadAsync(await ApproveAsync(billing, package.Account, request, command), HttpStatusCode.Created);
        var lastInstant = Command(await PreviewAsync(billing, package.Account, second, 1000L, "Preserve"), 1000L, "Preserve");

        // The MFA expires while the writer waits behind the receipt lock: nothing commits.
        Task<HttpResponseMessage> waiting;
        await using (var holder = await HoldReceiptAsync(fixture, confirmation))
        {
            waiting = ApproveAsync(billing, package.Account, second, lastInstant);
            Assert.True(await holder.WaitForBlockedAsync() >= 1, "The approval did not wait behind the receipt lock.");
            clock.Advance(TimeSpan.FromTicks(10));
        }
        await AssertCodeAsync(await waiting, HttpStatusCode.Forbidden, "ManualPayment.RecentMfaRequired");

        // Exactly 5:00: preview, a new approval, the exact replay and recovery are all refused.
        Assert.Equal(limit, clock.GetUtcNow());
        await AssertCodeAsync(await TryPreviewAsync(billing, package.Account, second, 1000L, "Preserve"),
            HttpStatusCode.Forbidden, "ManualPayment.RecentMfaRequired");
        await AssertCodeAsync(await ApproveAsync(billing, package.Account, request, command),
            HttpStatusCode.Forbidden, "ManualPayment.RecentMfaRequired");
        await AssertCodeAsync(await billing.SendAsync(HttpMethod.Get,
            $"{RequestPath(package.Account, request)}/approvals/operations/{command["operationKey"]:D}"),
            HttpStatusCode.Forbidden, "ManualPayment.RecentMfaRequired");
        // Read-only intake stays available under its existing policy.
        await ReadAsync(await billing.SendAsync(HttpMethod.Get, AdminPath(package.Account)), HttpStatusCode.OK);
        Assert.Equal(1L, await fixture.CountAsync("refund_approvals"));
        Assert.Equal(1L, await fixture.CountAsync("refund_access_decisions"));
    }

    [Fact]
    public async Task FailureBetweenAmountAndDecisionRollsBothBack()
    {
        await using var fixture = await StartAsync(database, Instant);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000L);
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var preview = await PreviewAsync(billing, package.Account, request, 4500L, "Preserve");
        var command = Command(preview, 4500L, "Preserve");

        // Test-only fault after the amount row is written and before the decision row exists.
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, """
            CREATE FUNCTION public.p0609_fail() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'P0609 injected failure' USING ERRCODE = '53400'; END $$;
            CREATE TRIGGER p0609_fail BEFORE INSERT ON public.refund_access_decisions
                FOR EACH ROW EXECUTE FUNCTION public.p0609_fail();
            """);
        using (var failed = await ApproveAsync(billing, package.Account, request, command))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal(0L, await fixture.CountAsync("refund_approvals"));
        Assert.Equal(0L, await fixture.CountAsync("refund_access_decisions"));
        await AssertCodeAsync(await billing.SendAsync(HttpMethod.Get,
            $"{RequestPath(package.Account, request)}/approvals/operations/{command["operationKey"]:D}"),
            HttpStatusCode.NotFound, "Refund.OperationNotFound");

        // Once the fault is gone the same frozen command, same key, creates exactly one approval.
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner,
            "DROP TRIGGER p0609_fail ON public.refund_access_decisions; DROP FUNCTION public.p0609_fail();");
        await ReadAsync(await ApproveAsync(billing, package.Account, request, command), HttpStatusCode.Created);
        Assert.Equal(1L, await fixture.CountAsync("refund_approvals"));
        Assert.Equal(1L, await fixture.CountAsync("refund_access_decisions"));
    }

    /// <summary>One shop, one verified 12,000-paise monthly purchase and one refund request against it.</summary>
    internal static async Task<(Package Package, Guid Request, Guid Confirmation, Guid Order)> RequestedAsync(
        P0308Fixture fixture, long paise)
    {
        var package = await fixture.PackageAsync(1, ["Replay Shop"]);
        var (purchase, _) = await PurchaseAsync(package, "Month");
        var confirmation = await P0608Tests.ConfirmationIdAsync(package.Payer.Client, package.Account, purchase.OrderId);
        return (package, await RequestRefundAsync(package, purchase.OrderId, confirmation, paise), confirmation,
            purchase.OrderId);
    }
}
