using System.Net;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0608Tests
{
    [Fact]
    public async Task ConcurrentRetryPreservesOneRequest()
    {
        await using var fixture = await StartAsync(database, RequestedInstant);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (purchase, _) = await PurchaseAsync(package, "Month");
        var confirmation = await ConfirmationIdAsync(package.Payer.Client, package.Account, purchase.OrderId);

        var key = Guid.NewGuid();
        var body = RequestBody(purchase.OrderId, confirmation, 6000L, "  Duplicate debit review  ", key);
        var responses = await Task.WhenAll(
            PostRefundAsync(package.Payer.Client, package.Account, body),
            PostRefundAsync(package.Payer.Client, package.Account, body));
        try
        {
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            using var firstDoc = JsonDocument.Parse(await responses[0].Content.ReadAsStringAsync());
            using var secondDoc = JsonDocument.Parse(await responses[1].Content.ReadAsStringAsync());
            var id = firstDoc.RootElement.GetProperty("refundRequestId").GetGuid();
            Assert.Equal(id, secondDoc.RootElement.GetProperty("refundRequestId").GetGuid());
            Assert.Equal("Duplicate debit review", firstDoc.RootElement.GetProperty("reason").GetString());
            Assert.Equal(1L, await fixture.CountAsync("refund_requests"));

            // Same key with changed amount or reason -> 409 OperationPayloadMismatch.
            using var changedAmount = await PostRefundAsync(package.Payer.Client, package.Account,
                RequestBody(purchase.OrderId, confirmation, 7000L, "Duplicate debit review", key));
            Assert.Equal(HttpStatusCode.Conflict, changedAmount.StatusCode);
            Assert.Equal("Billing.OperationPayloadMismatch", await P0206Fixture.CodeAsync(changedAmount));

            using var changedReason = await PostRefundAsync(package.Payer.Client, package.Account,
                RequestBody(purchase.OrderId, confirmation, 6000L, "Different reason text", key));
            Assert.Equal(HttpStatusCode.Conflict, changedReason.StatusCode);
            Assert.Equal("Billing.OperationPayloadMismatch", await P0206Fixture.CodeAsync(changedReason));

            // Clock advances -> replay preserves original requestedAt and id.
            fixture.At(RequestedInstant.AddMinutes(30));
            using var replay = await PostRefundAsync(package.Payer.Client, package.Account, body);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            using var replayDoc = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
            Assert.Equal(id, replayDoc.RootElement.GetProperty("refundRequestId").GetGuid());
            Assert.Equal(RequestedInstant, replayDoc.RootElement.GetProperty("requestedAt").GetDateTimeOffset());
            Assert.Equal(1L, await fixture.CountAsync("refund_requests"));
        }
        finally
        {
            foreach (var r in responses) r.Dispose();
        }
    }

    [Fact]
    public async Task AuthorizationPrecedesReplay()
    {
        await using var fixture = await StartAsync(database, RequestedInstant);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (purchase, _) = await PurchaseAsync(package, "Month");
        var confirmation = await ConfirmationIdAsync(package.Payer.Client, package.Account, purchase.OrderId);

        // Seed a live Billing Manager (role = 2, status = 1): managers may request refunds.
        var manager = package.Shops[1];
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            INSERT INTO public.billing_account_members (id, account_id, merchant_user_id, role, status, revision, created_at)
            VALUES ('{Guid.NewGuid():D}', '{package.Account:D}', '{manager.UserId:D}', 2, 1, 1, now())
            """);

        var key = Guid.NewGuid();
        var body = RequestBody(purchase.OrderId, confirmation, 3000L, "Manager submitted refund request", key);
        using var created = await PostRefundAsync(manager.Client, package.Account, body);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        // Another live member (the owner) cannot replay the manager's key as their own.
        using var otherActor = await PostRefundAsync(package.Payer.Client, package.Account, body);
        Assert.Equal(HttpStatusCode.Conflict, otherActor.StatusCode);
        Assert.Equal("Billing.OperationPayloadMismatch", await P0206Fixture.CodeAsync(otherActor));

        // Remove the manager, then replay the known operationKey -> 403 Forbidden (authority precedes replay).
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            UPDATE public.billing_account_members
            SET status = 2, revision = revision + 1
            WHERE account_id = '{package.Account:D}' AND merchant_user_id = '{manager.UserId:D}'
            """);
        using var removedReplay = await PostRefundAsync(manager.Client, package.Account, body);
        Assert.Equal(HttpStatusCode.Forbidden, removedReplay.StatusCode);
        Assert.Equal(1L, await fixture.CountAsync("refund_requests"));
    }
}
